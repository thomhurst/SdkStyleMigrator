using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class PackageVersionConflictResolver : IPackageVersionConflictResolver
{
    private readonly ILogger<PackageVersionConflictResolver> _logger;
    private readonly INuGetPackageResolver _nugetResolver;

    public PackageVersionConflictResolver(
        ILogger<PackageVersionConflictResolver> logger,
        INuGetPackageResolver nugetResolver)
    {
        _logger = logger;
        _nugetResolver = nugetResolver;
    }

    public async Task<PackageVersionResolution> ResolveConflictsAsync(
        List<PackageVersionConflict> conflicts,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var resolution = new PackageVersionResolution();

        foreach (var conflict in conflicts)
        {
            _logger.LogInformation("Resolving version conflict for package {PackageId}: {Versions}",
                conflict.PackageId, string.Join(", ", conflict.RequestedVersions.Select(v => v.Version)));

            var resolvedVersion = strategy switch
            {
                ConflictResolutionStrategy.UseHighest => await ResolveUsingHighestVersion(conflict, cancellationToken),
                ConflictResolutionStrategy.UseLowest => ResolveUsingLowestVersion(conflict),
                ConflictResolutionStrategy.UseLatestStable => await ResolveUsingLatestStable(conflict, cancellationToken),
                ConflictResolutionStrategy.UseMostCommon => ResolveUsingMostCommon(conflict),
                ConflictResolutionStrategy.Interactive => await ResolveInteractively(conflict, cancellationToken),
                _ => await ResolveUsingHighestVersion(conflict, cancellationToken)
            };

            resolution.ResolvedVersions[conflict.PackageId] = resolvedVersion;
            
            // Track which projects need version updates
            foreach (var request in conflict.RequestedVersions)
            {
                if (request.Version != resolvedVersion)
                {
                    resolution.ProjectsNeedingUpdate.Add(new ProjectVersionUpdate
                    {
                        ProjectPath = request.ProjectPath,
                        PackageId = conflict.PackageId,
                        OldVersion = request.Version,
                        NewVersion = resolvedVersion
                    });
                }
            }

            _logger.LogInformation("Resolved {PackageId} to version {Version} using {Strategy} strategy",
                conflict.PackageId, resolvedVersion, strategy);
        }

        return resolution;
    }

    public List<PackageVersionConflict> DetectConflicts(Dictionary<string, List<ProjectPackageReference>> packagesByProject)
    {
        var conflicts = new List<PackageVersionConflict>();
        var packageVersions = new Dictionary<string, List<ProjectPackageVersion>>(StringComparer.OrdinalIgnoreCase);

        // Group by package ID and collect all versions
        foreach (var (projectPath, packages) in packagesByProject)
        {
            foreach (var package in packages)
            {
                if (!packageVersions.ContainsKey(package.PackageId))
                {
                    packageVersions[package.PackageId] = new List<ProjectPackageVersion>();
                }

                packageVersions[package.PackageId].Add(new ProjectPackageVersion
                {
                    ProjectPath = projectPath,
                    Version = package.Version ?? "*",
                    IsTransitive = package.IsTransitive
                });
            }
        }

        // Find conflicts (packages with multiple versions)
        foreach (var (packageId, versions) in packageVersions)
        {
            var uniqueVersions = versions
                .Where(v => !v.IsTransitive) // Only consider direct references
                .GroupBy(v => v.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueVersions.Count > 1)
            {
                conflicts.Add(new PackageVersionConflict
                {
                    PackageId = packageId,
                    RequestedVersions = versions.Where(v => !v.IsTransitive).ToList()
                });
            }
        }

        return conflicts;
    }

    private async Task<string> ResolveUsingHighestVersion(PackageVersionConflict conflict, CancellationToken cancellationToken)
    {
        var versions = conflict.RequestedVersions
            .Select(v => v.Version)
            .Where(v => !string.IsNullOrEmpty(v) && v != "*")
            .ToList();

        if (!versions.Any())
        {
            // If all versions are wildcards, get latest from NuGet
            return await _nugetResolver.GetLatestStableVersionAsync(conflict.PackageId, cancellationToken) ?? "*";
        }

        // Parse versions and find highest
        var nugetVersions = new List<(string original, NuGetVersion? parsed)>();
        foreach (var version in versions)
        {
            if (NuGetVersion.TryParse(version, out var parsed))
            {
                nugetVersions.Add((version, parsed));
            }
            else
            {
                _logger.LogWarning("Could not parse version {Version} for package {PackageId}", 
                    version, conflict.PackageId);
                nugetVersions.Add((version, null));
            }
        }

        // Get highest parsed version
        var highest = nugetVersions
            .Where(v => v.parsed != null)
            .OrderByDescending(v => v.parsed)
            .FirstOrDefault();

        if (highest.parsed != null)
        {
            return highest.original;
        }

        // Fallback to string comparison if parsing failed
        return versions.OrderByDescending(v => v).First();
    }

    private string ResolveUsingLowestVersion(PackageVersionConflict conflict)
    {
        var versions = conflict.RequestedVersions
            .Select(v => v.Version)
            .Where(v => !string.IsNullOrEmpty(v) && v != "*")
            .ToList();

        if (!versions.Any())
        {
            return "*";
        }

        // Parse versions and find lowest
        var nugetVersions = new List<(string original, NuGetVersion? parsed)>();
        foreach (var version in versions)
        {
            if (NuGetVersion.TryParse(version, out var parsed))
            {
                nugetVersions.Add((version, parsed));
            }
            else
            {
                nugetVersions.Add((version, null));
            }
        }

        // Get lowest parsed version
        var lowest = nugetVersions
            .Where(v => v.parsed != null)
            .OrderBy(v => v.parsed)
            .FirstOrDefault();

        if (lowest.parsed != null)
        {
            return lowest.original;
        }

        // Fallback to string comparison if parsing failed
        return versions.OrderBy(v => v).First();
    }

    private async Task<string> ResolveUsingLatestStable(PackageVersionConflict conflict, CancellationToken cancellationToken)
    {
        var latestVersion = await _nugetResolver.GetLatestStableVersionAsync(conflict.PackageId, cancellationToken);
        
        if (latestVersion != null)
        {
            _logger.LogInformation("Found latest stable version {Version} for {PackageId}", 
                latestVersion, conflict.PackageId);
            return latestVersion;
        }

        // Fallback to highest existing version
        _logger.LogWarning("Could not fetch latest stable version for {PackageId}, using highest existing",
            conflict.PackageId);
        return await ResolveUsingHighestVersion(conflict, cancellationToken);
    }

    private string ResolveUsingMostCommon(PackageVersionConflict conflict)
    {
        // Group by version and count occurrences
        var versionCounts = conflict.RequestedVersions
            .Where(v => !string.IsNullOrEmpty(v.Version) && v.Version != "*")
            .GroupBy(v => v.Version, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Version = g.Key, Count = g.Count() })
            .OrderByDescending(v => v.Count)
            .ThenByDescending(v => v.Version) // Tie-breaker: use higher version
            .ToList();

        if (versionCounts.Any())
        {
            var mostCommon = versionCounts.First();
            _logger.LogInformation("Most common version for {PackageId} is {Version} (used in {Count} projects)",
                conflict.PackageId, mostCommon.Version, mostCommon.Count);
            return mostCommon.Version;
        }

        return "*";
    }

    private async Task<string> ResolveInteractively(PackageVersionConflict conflict, CancellationToken cancellationToken)
    {
        // In a real implementation, this would prompt the user
        // For now, we'll just use the highest version strategy
        _logger.LogWarning("Interactive resolution not implemented, falling back to highest version strategy");
        return await ResolveUsingHighestVersion(conflict, cancellationToken);
    }

    public void ApplyResolution(
        PackageVersionResolution resolution,
        Dictionary<string, List<ProjectPackageReference>> projectPackages)
    {
        foreach (var update in resolution.ProjectsNeedingUpdate)
        {
            if (projectPackages.TryGetValue(update.ProjectPath, out var packages))
            {
                var package = packages.FirstOrDefault(p => 
                    p.PackageId.Equals(update.PackageId, StringComparison.OrdinalIgnoreCase));
                    
                if (package != null)
                {
                    _logger.LogInformation("Updating {PackageId} in {Project} from {OldVersion} to {NewVersion}",
                        update.PackageId, Path.GetFileName(update.ProjectPath), 
                        update.OldVersion, update.NewVersion);
                    package.Version = update.NewVersion;
                }
            }
        }
    }
}