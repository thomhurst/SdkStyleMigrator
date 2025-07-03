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

    public async Task<IEnumerable<PackageReference>> ConvertReferencesAsync(
        Project legacyProject,
        string targetFramework,
        CancellationToken cancellationToken = default)
    {
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

        // Process package references (those with HintPath)
        foreach (var referenceItem in packageReferences)
        {
            var assemblyName = ExtractAssemblyName(referenceItem.EvaluatedInclude);

            // First, try to find a package using our framework-aware PackageAssemblyResolver
            var packageFromResolver = await FindPackageUsingFrameworkAwareResolver(assemblyName, targetFramework, cancellationToken);
            if (packageFromResolver != null)
            {
                detectedPackageReferences.Add(packageFromResolver);
                _logger.LogInformation("Framework-aware resolver: Converted assembly reference '{Assembly}' to PackageReference '{PackageId}' for TFM '{TargetFramework}'",
                    assemblyName, packageFromResolver.PackageId, targetFramework);
                continue;
            }

            // Fallback to the general NuGet resolver
            var resolvedPackage = await _nugetResolver.ResolveAssemblyToPackageAsync(assemblyName, targetFramework, cancellationToken);
            if (resolvedPackage != null)
            {
                detectedPackageReferences.Add(new PackageReference
                {
                    PackageId = resolvedPackage.PackageId,
                    Version = resolvedPackage.Version ?? "*"
                });
                _logger.LogInformation("NuGet resolver: Converted assembly reference '{Assembly}' to PackageReference '{PackageId}' for TFM '{TargetFramework}'",
                    assemblyName, resolvedPackage.PackageId, targetFramework);
            }
            else
            {
                _logger.LogDebug("Assembly reference '{Assembly}' did not resolve to a known NuGet package for TFM '{TargetFramework}'. It might be a local DLL or require manual handling.",
                    assemblyName, targetFramework);
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
                var assemblyName = ExtractAssemblyName(referenceItem.EvaluatedInclude);

                // Skip built-in framework assemblies that are available in all .NET versions
                if (IsBuiltInFrameworkAssembly(assemblyName))
                {
                    _logger.LogDebug("Skipping built-in framework reference '{Assembly}' for TFM '{TargetFramework}'",
                        assemblyName, targetFramework);
                    continue;
                }

                // Try to find a package for framework assemblies that need explicit packages in .NET Core/.NET 5+
                var packageFromResolver = await FindPackageUsingFrameworkAwareResolver(assemblyName, targetFramework, cancellationToken);
                if (packageFromResolver != null)
                {
                    detectedPackageReferences.Add(packageFromResolver);
                    _logger.LogInformation("Framework reference '{Assembly}' requires package '{PackageId}' for TFM '{TargetFramework}'",
                        assemblyName, packageFromResolver.PackageId, targetFramework);
                }
                else
                {
                    _logger.LogDebug("Framework reference '{Assembly}' does not require a package for TFM '{TargetFramework}'",
                        assemblyName, targetFramework);
                }
            }
        }
        else
        {
            _logger.LogDebug("Target framework {TargetFramework} is .NET Framework - skipping framework reference conversion", targetFramework);
        }

        _logger.LogInformation("Converted {Count} assembly references to package references for target framework {TargetFramework}",
            detectedPackageReferences.Count, targetFramework);

        return detectedPackageReferences;
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
        string assemblyName,
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
                    if (frameworkEntry.Value.Contains(assemblyName, StringComparer.OrdinalIgnoreCase))
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