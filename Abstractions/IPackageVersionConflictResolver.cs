using SdkMigrator.Models;
using SdkMigrator.Services;

namespace SdkMigrator.Abstractions;

public interface IPackageVersionConflictResolver
{
    /// <summary>
    /// Detects package version conflicts across projects
    /// </summary>
    List<PackageVersionConflict> DetectConflicts(Dictionary<string, List<ProjectPackageReference>> packagesByProject);

    /// <summary>
    /// Resolves package version conflicts using the specified strategy
    /// </summary>
    Task<PackageVersionResolution> ResolveConflictsAsync(
        List<PackageVersionConflict> conflicts,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the resolved versions to the project packages
    /// </summary>
    void ApplyResolution(
        PackageVersionResolution resolution,
        Dictionary<string, List<ProjectPackageReference>> projectPackages);
}