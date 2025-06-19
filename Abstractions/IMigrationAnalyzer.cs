using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IMigrationAnalyzer
{
    Task<MigrationAnalysis> AnalyzeProjectsAsync(string directoryPath, CancellationToken cancellationToken = default);
    Task<ProjectAnalysis> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default);
}