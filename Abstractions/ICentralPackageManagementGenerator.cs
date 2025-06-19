using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ICentralPackageManagementGenerator
{
    /// <summary>
    /// Generates a Directory.Packages.props file for Central Package Management
    /// </summary>
    Task<CentralPackageManagementResult> GenerateDirectoryPackagesPropsAsync(
        string solutionDirectory,
        IEnumerable<MigrationResult> migrationResults,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Removes Version attributes from PackageReference items in project files
    /// </summary>
    Task<bool> RemoveVersionsFromProjectsAsync(
        IEnumerable<string> projectFiles,
        CancellationToken cancellationToken = default);
}