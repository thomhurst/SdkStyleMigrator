using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ILocalPackageFilesCleaner
{
    /// <summary>
    /// Finds and removes local package files (DLLs, XMLs, PDBs, etc.) that are now replaced by PackageReference
    /// </summary>
    Task<LocalPackageCleanupResult> CleanLocalPackageFilesAsync(
        string projectDirectory,
        List<PackageReference> packageReferences,
        List<string> hintPaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up packages folders that are no longer needed
    /// </summary>
    Task<bool> CleanPackagesFolderAsync(
        string solutionDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes packages.config files after successful migration to PackageReference
    /// </summary>
    Task<bool> CleanPackagesConfigAsync(
        string projectDirectory,
        bool migrationSuccessful,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up legacy project artifacts like .user files, AssemblyInfo.cs (when migrated), etc.
    /// </summary>
    Task<LocalPackageCleanupResult> CleanLegacyProjectArtifactsAsync(
        string projectDirectory,
        bool assemblyInfoMigrated,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up configuration transformation files that may be obsolete after migration
    /// </summary>
    Task<LocalPackageCleanupResult> CleanConfigTransformationFilesAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default);
}