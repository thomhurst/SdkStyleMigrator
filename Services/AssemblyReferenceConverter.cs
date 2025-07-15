using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class AssemblyReferenceConverter : IAssemblyReferenceConverter
{
    private readonly ILogger<AssemblyReferenceConverter> _logger;
    private readonly PackageAssemblyResolver _packageAssemblyResolver;
    private readonly INuGetPackageResolver _nugetResolver;

    // Standard .NET Framework references that should not be converted to packages
    private static readonly HashSet<string> BuiltInFrameworkAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "mscorlib",
        "System",
        "System.Core",
        "System.Data",
        "System.Data.DataSetExtensions",
        "System.Deployment",
        "System.Design",
        "System.DirectoryServices",
        "System.Drawing",
        "System.Drawing.Design",
        "System.EnterpriseServices",
        "System.Management",
        "System.Messaging",
        "System.Runtime.Remoting",
        "System.Runtime.Serialization",
        "System.Runtime.Serialization.Formatters.Soap",
        "System.Security",
        "System.ServiceModel",
        "System.ServiceModel.Web",
        "System.ServiceProcess",
        "System.Transactions",
        "System.Web",
        "System.Web.Extensions",
        "System.Web.Extensions.Design",
        "System.Web.Mobile",
        "System.Web.RegularExpressions",
        "System.Web.Services",
        "System.Windows.Forms",
        "System.Xml",
        "System.Xml.Linq",
        "System.ComponentModel.Composition",
        "System.ComponentModel.DataAnnotations",
        "System.Net",
        "System.Net.Http",
        "System.Numerics",
        "System.IO.Compression",
        "System.IO.Compression.FileSystem",
        "System.Runtime.Caching",
        "System.Runtime.DurableInstancing",
        "System.ServiceModel.Activation",
        "System.ServiceModel.Activities",
        "System.ServiceModel.Channels",
        "System.ServiceModel.Discovery",
        "System.ServiceModel.Routing",
        "System.Speech",
        "System.Threading.Tasks.Dataflow",
        "System.Web.Abstractions",
        "System.Configuration",
        "System.Configuration.Install",
        "System.IdentityModel",
        "System.IdentityModel.Selectors",
        "System.Activities",
        "System.Activities.Core.Presentation",
        "System.Activities.DurableInstancing",
        "System.Activities.Presentation",
        "System.Data.Entity",
        "System.Data.Entity.Design",
        "System.Data.Linq",
        "System.Data.OracleClient",
        "System.Data.Services",
        "System.Data.Services.Client",
        "System.Data.Services.Design",
        "System.Data.SqlXml",
        "System.Device",
        "System.Net.Http.WebRequest",
        "System.Runtime.Serialization.Formatters",
        "System.Web.ApplicationServices",
        "System.Web.DataVisualization",
        "System.Web.DynamicData",
        "System.Web.Entity",
        "System.Web.Entity.Design",
        "System.Web.Routing",
        "System.Windows",
        "System.Workflow.Activities",
        "System.Workflow.ComponentModel",
        "System.Workflow.Runtime",
        "System.WorkflowServices",
        "System.Xaml",
        "Microsoft.Build",
        "Microsoft.Build.Engine",
        "Microsoft.Build.Framework",
        "Microsoft.Build.Tasks.Core",
        "Microsoft.Build.Utilities.Core",
        "Microsoft.CSharp",
        "Microsoft.JScript",
        "Microsoft.VisualBasic",
        "Microsoft.VisualBasic.Compatibility",
        "Microsoft.VisualBasic.Compatibility.Data",
        "Microsoft.VisualC",
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "PresentationFramework.Aero",
        "PresentationFramework.Classic",
        "PresentationFramework.Luna",
        "PresentationFramework.Royale",
        "ReachFramework",
        "System.Printing",
        "UIAutomationClient",
        "UIAutomationClientsideProviders",
        "UIAutomationProvider",
        "UIAutomationTypes",
        "WindowsFormsIntegration"
    };

    public AssemblyReferenceConverter(
        ILogger<AssemblyReferenceConverter> logger,
        PackageAssemblyResolver packageAssemblyResolver,
        INuGetPackageResolver nugetResolver)
    {
        _logger = logger;
        _packageAssemblyResolver = packageAssemblyResolver;
        _nugetResolver = nugetResolver;
    }

    public async Task<ReferenceConversionResult> ConvertReferencesAsync(
        Project legacyProject,
        string targetFramework,
        IEnumerable<PackageReference> existingPackages,
        CancellationToken cancellationToken = default)
    {
        var result = new ReferenceConversionResult();
        var detectedPackageReferences = new HashSet<PackageReference>(new PackageReferenceComparer());

        // Get all references
        var allReferences = legacyProject.Items
            .Where(i => i.ItemType == "Reference")
            .ToList();

        // Separate references with HintPath (package references) and without (framework/GAC references)
        var packageReferences = allReferences.Where(i => i.HasMetadata("HintPath")).ToList();
        var frameworkReferences = allReferences.Where(i => !i.HasMetadata("HintPath")).ToList();

        _logger.LogDebug("Found {PackageCount} package references and {FrameworkCount} framework references for target framework {TargetFramework}",
            packageReferences.Count, frameworkReferences.Count, targetFramework);

        // Filter out references that are already covered by existing packages
        var filteredPackageReferences = packageReferences
            .Where(referenceItem => !IsReferenceCoveredByExistingPackage(referenceItem, existingPackages))
            .ToList();

        _logger.LogDebug("Filtered {OriginalCount} package references down to {FilteredCount} after removing references covered by existing packages",
            packageReferences.Count, filteredPackageReferences.Count);

        // Process package references (those with HintPath)
        foreach (var referenceItem in filteredPackageReferences)
        {
            var assemblyIdentity = AssemblyIdentity.Parse(referenceItem.EvaluatedInclude);

            // First, try to find a package using our framework-aware PackageAssemblyResolver
            var packageFromResolver = await FindPackageUsingFrameworkAwareResolver(assemblyIdentity, targetFramework, cancellationToken);
            if (packageFromResolver != null)
            {
                // Try to match the version if specified in the original reference
                if (!string.IsNullOrEmpty(assemblyIdentity.Version))
                {
                    packageFromResolver.Version = await TryMatchVersion(packageFromResolver.PackageId, assemblyIdentity.Version, cancellationToken)
                        ?? packageFromResolver.Version;
                }

                detectedPackageReferences.Add(packageFromResolver);
                _logger.LogInformation("Framework-aware resolver: Converted assembly reference '{Assembly}' to PackageReference '{PackageId}' version '{Version}' for TFM '{TargetFramework}'",
                    assemblyIdentity.Name, packageFromResolver.PackageId, packageFromResolver.Version, targetFramework);
                continue;
            }

            // Fallback to the general NuGet resolver
            var resolvedPackage = await _nugetResolver.ResolveAssemblyToPackageAsync(assemblyIdentity.Name, targetFramework, cancellationToken);
            if (resolvedPackage != null)
            {
                // Validate package-assembly match using IncludedAssemblies and public key token
                var isValidMatch = ValidatePackageAssemblyMatch(resolvedPackage, assemblyIdentity, targetFramework, cancellationToken);

                if (!isValidMatch)
                {
                    result.Warnings.Add($"Assembly '{assemblyIdentity.Name}' does not match the package '{resolvedPackage.PackageId}'. Keeping as local reference.");
                    result.UnconvertedReferences.Add(UnconvertedReference.FromProjectItem(referenceItem,
                        "Assembly-package validation failed"));
                    continue;
                }

                // Try to match the version
                var version = resolvedPackage.Version;
                if (!string.IsNullOrEmpty(assemblyIdentity.Version))
                {
                    version = await TryMatchVersion(resolvedPackage.PackageId, assemblyIdentity.Version, cancellationToken)
                        ?? resolvedPackage.Version;

                    if (version != assemblyIdentity.Version)
                    {
                        result.Warnings.Add($"Assembly '{assemblyIdentity.Name}' version '{assemblyIdentity.Version}' " +
                            $"converted to package '{resolvedPackage.PackageId}' version '{version}'");
                    }
                }

                detectedPackageReferences.Add(new PackageReference
                {
                    PackageId = resolvedPackage.PackageId,
                    Version = version ?? "*"
                });
                _logger.LogInformation("NuGet resolver: Converted assembly reference '{Assembly}' to PackageReference '{PackageId}' version '{Version}' for TFM '{TargetFramework}'",
                    assemblyIdentity.Name, resolvedPackage.PackageId, version, targetFramework);
            }
            else
            {
                _logger.LogDebug("Assembly reference '{Assembly}' did not resolve to a known NuGet package for TFM '{TargetFramework}'. Preserving as local reference.",
                    assemblyIdentity.Name, targetFramework);
                result.UnconvertedReferences.Add(UnconvertedReference.FromProjectItem(referenceItem,
                    "No matching NuGet package found"));
            }
        }

        // Process framework references only for .NET Core/.NET 5+ targets
        // For .NET Framework targets, these remain as implicit references
        var isNetFrameworkTarget = targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase) ||
                                   targetFramework.Equals("net35", StringComparison.OrdinalIgnoreCase) ||
                                   targetFramework.Equals("net20", StringComparison.OrdinalIgnoreCase);

        if (!isNetFrameworkTarget)
        {
            foreach (var referenceItem in frameworkReferences)
            {
                var assemblyIdentity = AssemblyIdentity.Parse(referenceItem.EvaluatedInclude);

                // Skip built-in framework assemblies that are available in all .NET versions
                if (IsBuiltInFrameworkAssembly(assemblyIdentity.Name))
                {
                    _logger.LogDebug("Skipping built-in framework reference '{Assembly}' for TFM '{TargetFramework}'",
                        assemblyIdentity.Name, targetFramework);
                    continue;
                }

                // Try to find a package for framework assemblies that need explicit packages in .NET Core/.NET 5+
                var packageFromResolver = await FindPackageUsingFrameworkAwareResolver(assemblyIdentity, targetFramework, cancellationToken);
                if (packageFromResolver != null)
                {
                    detectedPackageReferences.Add(packageFromResolver);
                    _logger.LogInformation("Framework reference '{Assembly}' requires package '{PackageId}' for TFM '{TargetFramework}'",
                        assemblyIdentity.Name, packageFromResolver.PackageId, targetFramework);
                }
                else
                {
                    _logger.LogDebug("Framework reference '{Assembly}' does not require a package for TFM '{TargetFramework}'",
                        assemblyIdentity.Name, targetFramework);
                }
            }
        }
        else
        {
            // For .NET Framework targets, preserve ALL framework references (both built-in and non-built-in)
            // as they are all valid and available in the .NET Framework
            foreach (var referenceItem in frameworkReferences)
            {
                result.UnconvertedReferences.Add(UnconvertedReference.FromProjectItem(referenceItem,
                    "Framework reference for .NET Framework target"));
            }

            _logger.LogDebug("Target framework {TargetFramework} is .NET Framework - preserving all framework references", targetFramework);
        }

        result.PackageReferences = detectedPackageReferences.ToList();

        _logger.LogInformation("Converted {ConvertedCount} assembly references to package references and preserved {UnconvertedCount} references for target framework {TargetFramework}",
            result.PackageReferences.Count, result.UnconvertedReferences.Count, targetFramework);

        return result;
    }

    /// <summary>
    /// Extracts the simple assembly name from a reference string (removes version, culture, etc.)
    /// </summary>
    private static string ExtractAssemblyName(string referenceInclude)
    {
        // Handle cases like "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
        var commaIndex = referenceInclude.IndexOf(',');
        return commaIndex > 0 ? referenceInclude.Substring(0, commaIndex).Trim() : referenceInclude.Trim();
    }

    /// <summary>
    /// Checks if an assembly is a built-in .NET Framework assembly that should not be converted to a package reference.
    /// </summary>
    private static bool IsBuiltInFrameworkAssembly(string assemblyName)
    {
        return BuiltInFrameworkAssemblies.Contains(assemblyName);
    }

    /// <summary>
    /// Uses our framework-aware PackageAssemblyResolver to find packages that provide a specific assembly for the target framework.
    /// This performs a reverse lookup through the well-known mappings.
    /// </summary>
    private async Task<PackageReference?> FindPackageUsingFrameworkAwareResolver(
        AssemblyIdentity assemblyIdentity,
        string targetFramework,
        CancellationToken cancellationToken)
    {
        // We need to search through our framework-aware mappings to find which package provides this assembly
        // for the given target framework. This is essentially a reverse lookup.

        // Since PackageAssemblyResolver doesn't expose this reverse lookup directly, we'll iterate through
        // the well-known mappings ourselves. In a future enhancement, we could add a reverse lookup method
        // to PackageAssemblyResolver.

        foreach (var packageMapping in _packageAssemblyResolver.GetWellKnownMappings())
        {
            foreach (var frameworkEntry in packageMapping.Value)
            {
                if (IsFrameworkCompatible(targetFramework, frameworkEntry.Key))
                {
                    if (frameworkEntry.Value.Contains(assemblyIdentity.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        // Found a match - this package provides the assembly for this framework
                        var version = await _nugetResolver.GetLatestStableVersionAsync(packageMapping.Key, cancellationToken);
                        return new PackageReference
                        {
                            PackageId = packageMapping.Key,
                            Version = version ?? "*"
                        };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Validates that a NuGet package contains the expected assembly by checking the IncludedAssemblies
    /// from the PackageResolutionResult and optionally validating the public key token.
    /// </summary>
    private bool ValidatePackageAssemblyMatch(
        PackageResolutionResult packageResult,
        AssemblyIdentity assemblyIdentity,
        string targetFramework,
        CancellationToken cancellationToken)
    {
        // Primary validation: Check if assembly is in IncludedAssemblies
        if (packageResult.IncludedAssemblies.Contains(assemblyIdentity.Name, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Assembly '{Assembly}' found in package '{Package}' IncludedAssemblies",
                assemblyIdentity.Name, packageResult.PackageId);
            return true;
        }

        // Secondary validation: Public key token check for additional security
        if (!string.IsNullOrEmpty(assemblyIdentity.PublicKeyToken))
        {
            var wellKnownTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Newtonsoft.Json"] = "30ad4fe6b2a6aeed",
                ["System.Data.SqlClient"] = "b03f5f7f11d50a3a",
                ["Microsoft.EntityFrameworkCore"] = "adb9793829ddae60",
                ["EntityFramework"] = "b77a5c561934e089"
            };

            if (wellKnownTokens.TryGetValue(packageResult.PackageId, out var expectedToken))
            {
                var tokenMatch = string.Equals(expectedToken, assemblyIdentity.PublicKeyToken, StringComparison.OrdinalIgnoreCase);
                if (tokenMatch)
                {
                    _logger.LogDebug("Public key token validation passed for package '{Package}' and assembly '{Assembly}'",
                        packageResult.PackageId, assemblyIdentity.Name);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Public key token mismatch for package '{Package}' and assembly '{Assembly}'. " +
                        "Expected '{Expected}', got '{Actual}'",
                        packageResult.PackageId, assemblyIdentity.Name, expectedToken, assemblyIdentity.PublicKeyToken);
                    return false;
                }
            }
        }

        // If no IncludedAssemblies data and no public key token validation available, 
        // allow conversion but log a warning
        _logger.LogWarning("Cannot validate assembly '{Assembly}' for package '{Package}'. " +
            "No IncludedAssemblies data and no public key token validation available. Allowing conversion.",
            assemblyIdentity.Name, packageResult.PackageId);

        return true; // Allow conversion when we can't validate but have a resolved package
    }

    /// <summary>
    /// Attempts to find a package version that matches the assembly version.
    /// </summary>
    private async Task<string?> TryMatchVersion(
        string packageId,
        string assemblyVersion,
        CancellationToken cancellationToken)
    {
        // For now, return the assembly version directly
        // In a full implementation, this would query NuGet to find available versions
        // and pick the closest match

        _logger.LogDebug("Attempting to match assembly version '{AssemblyVersion}' for package '{PackageId}'",
            assemblyVersion, packageId);

        // Common version mappings (assembly version -> package version)
        var versionMappings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Newtonsoft.Json"] = new Dictionary<string, string>
            {
                ["11.0.0.0"] = "11.0.2",
                ["12.0.0.0"] = "12.0.3",
                ["13.0.0.0"] = "13.0.3"
            }
        };

        if (versionMappings.TryGetValue(packageId, out var mappings) &&
            mappings.TryGetValue(assemblyVersion, out var packageVersion))
        {
            return packageVersion;
        }

        // Try to use assembly version directly (remove .0 suffix if present)
        var simplifiedVersion = assemblyVersion.TrimEnd(".0".ToCharArray());
        if (simplifiedVersion.Count(c => c == '.') == 2) // Ensure we have major.minor.patch
        {
            return simplifiedVersion;
        }

        return null;
    }

    /// <summary>
    /// Framework compatibility check - duplicated from PackageAssemblyResolver for now.
    /// TODO: Consider extracting this to a shared utility class.
    /// </summary>
    private static bool IsFrameworkCompatible(string targetFrameworkMoniker, string pattern)
    {
        if (string.IsNullOrEmpty(targetFrameworkMoniker) || string.IsNullOrEmpty(pattern))
            return false;

        if (pattern.Equals("*", StringComparison.OrdinalIgnoreCase))
            return true;

        if (targetFrameworkMoniker.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        var lowerTarget = targetFrameworkMoniker.ToLowerInvariant();
        var lowerPattern = pattern.ToLowerInvariant();

        return lowerPattern switch
        {
            "netframework" => lowerTarget.StartsWith("net4") || lowerTarget == "net35" || lowerTarget == "net20",
            "netcoreapp" => lowerTarget.StartsWith("netcoreapp"),
            "net" => lowerTarget.StartsWith("net") &&
                     !lowerTarget.StartsWith("net4") &&
                     !lowerTarget.StartsWith("netcoreapp") &&
                     !lowerTarget.StartsWith("netframework"),
            _ => false
        };
    }

    /// <summary>
    /// Checks if a Reference element is already covered by an existing package from packages.config.
    /// This prevents duplicate processing of references that are already handled by PackageReferenceMigrator.
    /// </summary>
    private bool IsReferenceCoveredByExistingPackage(Microsoft.Build.Evaluation.ProjectItem referenceItem, IEnumerable<PackageReference> existingPackages)
    {
        if (!referenceItem.HasMetadata("HintPath"))
            return false;

        var hintPath = referenceItem.GetMetadataValue("HintPath");
        if (string.IsNullOrEmpty(hintPath) || !hintPath.Contains("packages"))
            return false;

        // Extract package folder from HintPath (e.g., "packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll")
        var parts = hintPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var packagesIndex = Array.FindIndex(parts, p => p.Equals("packages", StringComparison.OrdinalIgnoreCase));

        if (packagesIndex >= 0 && packagesIndex < parts.Length - 1)
        {
            var packageFolder = parts[packagesIndex + 1];

            // Check if any existing package matches this folder
            foreach (var existingPackage in existingPackages)
            {
                // Try different package folder naming patterns
                var possibleFolders = new[]
                {
                    $"{existingPackage.PackageId}.{existingPackage.Version}",
                    $"{existingPackage.PackageId}.{existingPackage.Version ?? ""}".TrimEnd('.'),
                    existingPackage.PackageId // Some packages might not have version in folder name
                };

                if (possibleFolders.Any(folder => packageFolder.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("Reference '{Assembly}' is covered by existing package '{Package}' - skipping conversion",
                        referenceItem.EvaluatedInclude, existingPackage.PackageId);
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Comparer for PackageReference to enable de-duplication based on PackageId.
/// </summary>
public class PackageReferenceComparer : IEqualityComparer<PackageReference>
{
    public bool Equals(PackageReference? x, PackageReference? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.PackageId.Equals(y.PackageId, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(PackageReference obj)
    {
        return obj.PackageId.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }
}