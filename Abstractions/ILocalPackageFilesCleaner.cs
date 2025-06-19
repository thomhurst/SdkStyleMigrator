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
}