using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IMigrationOrchestrator
{
    Task<MigrationReport> MigrateProjectsAsync(string directoryPath, CancellationToken cancellationToken = default);
}