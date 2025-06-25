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

        var legacyReferences = legacyProject.Items
            .Where(i => i.ItemType == "Reference")
            .Where(i => !i.HasMetadata("HintPath")) // Skip GAC/framework references that don't have hint paths to packages
            .ToList();

        _logger.LogDebug("Found {Count} legacy assembly references to analyze for target framework {TargetFramework}", 
            legacyReferences.Count, targetFramework);

        foreach (var referenceItem in legacyReferences)
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
                _logger.LogDebug("Assembly reference '{Assembly}' did not resolve to a known NuGet package for TFM '{TargetFramework}'. It might be built-in, a local DLL, or require manual handling.", 
                    assemblyName, targetFramework);
            }
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