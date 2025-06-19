using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ISolutionFileUpdater
{
    Task<SolutionUpdateResult> UpdateSolutionFilesAsync(
        string rootDirectory, 
        Dictionary<string, string> projectMappings, 
        CancellationToken cancellationToken = default);
}