using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ISolutionCleaner
{
    /// <summary>
    /// Cleans and fixes common issues in solution files
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file</param>
    /// <param name="options">Options controlling which fixes to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing summary of changes made</returns>
    Task<SolutionCleanResult> CleanSolutionAsync(
        string solutionPath,
        SolutionCleanOptions options,
        CancellationToken cancellationToken = default);
}