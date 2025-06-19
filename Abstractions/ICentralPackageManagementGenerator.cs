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
        
    /// <summary>
    /// Removes unused packages from Directory.Packages.props
    /// </summary>
    Task<CleanCpmResult> CleanUnusedPackagesAsync(
        string directoryPath,
        bool dryRun,
        CancellationToken cancellationToken = default);
}

public class CleanCpmResult
{
    public bool Success { get; set; }
    public List<string> RemovedPackages { get; set; } = new();
    public string? Error { get; set; }
}