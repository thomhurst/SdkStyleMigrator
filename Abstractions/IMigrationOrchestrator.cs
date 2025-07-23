using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IMigrationOrchestrator
{
    Task<MigrationReport> MigrateProjectsAsync(string directoryPath, MigrationOptions options, CancellationToken cancellationToken = default);
    Task<MigrationAnalysis> AnalyzeProjectsAsync(string directoryPath, CancellationToken cancellationToken = default);
}