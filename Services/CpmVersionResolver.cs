using Microsoft.Extensions.Logging;
using SdkMigrator.Models;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace SdkMigrator.Services;

public class CpmVersionResolver
{
    private readonly ILogger<CpmVersionResolver> _logger;

    public CpmVersionResolver(ILogger<CpmVersionResolver> logger)
    {
        _logger = logger;
    }

    public CpmVersionResolution ResolveVersionConflict(
        string packageId,
        List<string> versions,
        List<string> targetFrameworks,
        CpmVersionResolutionOptions options)
    {
        var resolution = new CpmVersionResolution
        {
            PackageId = packageId,
            OriginalVersions = versions,
            TargetFrameworks = targetFrameworks
        };

        try
        {
            // Apply custom override if specified
            if (options.PackageVersionOverrides.TryGetValue(packageId, out var overrideVersion))
            {
                resolution.ResolvedVersion = overrideVersion;
                resolution.ResolutionReason = $"Custom override to {overrideVersion}";
                resolution.Strategy = "Override";
                return resolution;
            }

            // Parse versions to NuGet versions for better handling
            var nugetVersions = ParseVersions(versions);
            if (!nugetVersions.Any())
            {
                resolution.ResolvedVersion = versions.First();
                resolution.ResolutionReason = "Could not parse versions, using first available";
                resolution.Strategy = "Fallback";
                return resolution;
            }

            // Filter stable versions if preferred
            var candidateVersions = options.PreferStableVersions
                ? GetStableVersions(nugetVersions) ?? nugetVersions
                : nugetVersions;

            // Apply resolution strategy
            var resolvedNuGetVersion = options.Strategy switch
            {
                CpmVersionResolutionStrategy.UseHighest => ResolveHighest(candidateVersions),
                CpmVersionResolutionStrategy.UseLowest => ResolveLowest(candidateVersions),
                CpmVersionResolutionStrategy.UseLatestStable => ResolveLatestStable(candidateVersions),
                CpmVersionResolutionStrategy.UseMostCommon => ResolveMostCommon(candidateVersions),
                CpmVersionResolutionStrategy.SemanticCompatible => ResolveSemanticCompatible(candidateVersions),
                CpmVersionResolutionStrategy.FrameworkCompatible => ResolveFrameworkCompatible(candidateVersions, targetFrameworks),
                _ => ResolveHighest(candidateVersions)
            };

            resolution.ResolvedVersion = resolvedNuGetVersion.ToString();
            resolution.Strategy = options.Strategy.ToString();
            resolution.ResolutionReason = GenerateResolutionReason(options.Strategy, versions, resolution.ResolvedVersion);

            // Add compatibility warnings
            if (options.CheckFrameworkCompatibility)
            {
                CheckFrameworkCompatibility(packageId, resolvedNuGetVersion, targetFrameworks, resolution);
            }

            if (options.UseSemanticVersioning)
            {
                CheckSemanticCompatibility(packageId, nugetVersions, resolvedNuGetVersion, resolution);
            }

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving version conflict for package {PackageId}, using first version", packageId);
            resolution.ResolvedVersion = versions.First();
            resolution.ResolutionReason = $"Error during resolution: {ex.Message}";
            resolution.Strategy = "Error";
            resolution.HasWarnings = true;
            resolution.Warnings.Add($"Version resolution failed, using {versions.First()}");
        }

        return resolution;
    }

    private List<NuGetVersion> ParseVersions(List<string> versions)
    {
        var parsedVersions = new List<NuGetVersion>();
        
        foreach (var version in versions)
        {
            if (NuGetVersion.TryParse(version, out var nugetVersion))
            {
                parsedVersions.Add(nugetVersion);
            }
            else
            {
                _logger.LogDebug("Could not parse version {Version} as NuGet version", version);
            }
        }

        return parsedVersions.OrderByDescending(v => v).ToList();
    }

    private List<NuGetVersion>? GetStableVersions(List<NuGetVersion> versions)
    {
        var stableVersions = versions.Where(v => !v.IsPrerelease).ToList();
        return stableVersions.Any() ? stableVersions : null;
    }

    private NuGetVersion ResolveHighest(List<NuGetVersion> versions)
    {
        return versions.OrderByDescending(v => v).First();
    }

    private NuGetVersion ResolveLowest(List<NuGetVersion> versions)
    {
        return versions.OrderBy(v => v).First();
    }

    private NuGetVersion ResolveLatestStable(List<NuGetVersion> versions)
    {
        var stableVersions = versions.Where(v => !v.IsPrerelease).ToList();
        return stableVersions.Any() ? stableVersions.OrderByDescending(v => v).First() : versions.OrderByDescending(v => v).First();
    }

    private NuGetVersion ResolveMostCommon(List<NuGetVersion> versions)
    {
        var versionCounts = versions.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var maxCount = versionCounts.Values.Max();
        var mostCommonVersions = versionCounts.Where(kvp => kvp.Value == maxCount).Select(kvp => kvp.Key).ToList();
        
        // If there's a tie, pick the highest version among the most common
        return mostCommonVersions.OrderByDescending(v => v).First();
    }

    private NuGetVersion ResolveSemanticCompatible(List<NuGetVersion> versions)
    {
        // Group by major.minor and prefer the highest patch version within each group
        var versionGroups = versions
            .GroupBy(v => new { v.Major, v.Minor })
            .OrderByDescending(g => g.Key.Major)
            .ThenByDescending(g => g.Key.Minor)
            .ToList();

        // Take the highest major.minor group and the highest patch within it
        var selectedGroup = versionGroups.First();
        return selectedGroup.OrderByDescending(v => v).First();
    }

    private NuGetVersion ResolveFrameworkCompatible(List<NuGetVersion> versions, List<string> targetFrameworks)
    {
        // This is a simplified implementation - in a real scenario, you'd want to check
        // package compatibility with target frameworks using NuGet APIs
        // For now, we'll use the highest version and add a warning if needed
        
        var resolved = ResolveHighest(versions);
        _logger.LogDebug("Framework compatibility check not fully implemented, using highest version {Version}", resolved);
        return resolved;
    }

    private string GenerateResolutionReason(CpmVersionResolutionStrategy strategy, List<string> originalVersions, string resolvedVersion)
    {
        return strategy switch
        {
            CpmVersionResolutionStrategy.UseHighest => $"Selected highest version from: {string.Join(", ", originalVersions)}",
            CpmVersionResolutionStrategy.UseLowest => $"Selected lowest version from: {string.Join(", ", originalVersions)}",
            CpmVersionResolutionStrategy.UseLatestStable => $"Selected latest stable version from: {string.Join(", ", originalVersions)}",
            CpmVersionResolutionStrategy.UseMostCommon => $"Selected most common version from: {string.Join(", ", originalVersions)}",
            CpmVersionResolutionStrategy.SemanticCompatible => $"Selected semantically compatible version from: {string.Join(", ", originalVersions)}",
            CpmVersionResolutionStrategy.FrameworkCompatible => $"Selected framework compatible version from: {string.Join(", ", originalVersions)}",
            _ => $"Selected version {resolvedVersion} using {strategy} strategy"
        };
    }

    private void CheckFrameworkCompatibility(string packageId, NuGetVersion resolvedVersion, List<string> targetFrameworks, CpmVersionResolution resolution)
    {
        // This would require NuGet package metadata APIs to properly implement
        // For now, we'll add basic warnings for common scenarios
        
        var hasOldFrameworkTargets = targetFrameworks.Any(tf => tf.StartsWith("net4", StringComparison.OrdinalIgnoreCase));
        var hasNewFrameworkTargets = targetFrameworks.Any(tf => 
            tf.StartsWith("net5", StringComparison.OrdinalIgnoreCase) ||
            tf.StartsWith("net6", StringComparison.OrdinalIgnoreCase) ||
            tf.StartsWith("net7", StringComparison.OrdinalIgnoreCase) ||
            tf.StartsWith("net8", StringComparison.OrdinalIgnoreCase) ||
            tf.Contains("netcore", StringComparison.OrdinalIgnoreCase));

        if (hasOldFrameworkTargets && hasNewFrameworkTargets)
        {
            resolution.HasWarnings = true;
            resolution.Warnings.Add($"Package {packageId} version {resolvedVersion} selected for multi-targeting project. Verify compatibility with both .NET Framework and .NET Core/.NET targets.");
        }
    }

    private void CheckSemanticCompatibility(string packageId, List<NuGetVersion> originalVersions, NuGetVersion resolvedVersion, CpmVersionResolution resolution)
    {
        var hasBreakingChanges = originalVersions.Any(v => 
            v.Major != resolvedVersion.Major && 
            v < resolvedVersion);

        if (hasBreakingChanges)
        {
            resolution.HasWarnings = true;
            resolution.Warnings.Add($"Package {packageId} version {resolvedVersion} may introduce breaking changes from lower major versions in the conflict set.");
        }

        var significantVersionSpread = originalVersions.Any(v => 
            Math.Abs(v.Major - resolvedVersion.Major) > 1 ||
            (v.Major == resolvedVersion.Major && Math.Abs(v.Minor - resolvedVersion.Minor) > 3));

        if (significantVersionSpread)
        {
            resolution.HasWarnings = true;
            resolution.Warnings.Add($"Package {packageId} has significant version differences. Review if version {resolvedVersion} is compatible with all project requirements.");
        }
    }
}

public class CpmVersionResolution
{
    public string PackageId { get; set; } = string.Empty;
    public List<string> OriginalVersions { get; set; } = new();
    public string ResolvedVersion { get; set; } = string.Empty;
    public string ResolutionReason { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<string> TargetFrameworks { get; set; } = new();
    public bool HasWarnings { get; set; }
    public List<string> Warnings { get; set; } = new();
}