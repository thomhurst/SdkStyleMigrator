using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IAuditService
{
    /// <summary>
    /// Logs the start of a migration operation
    /// </summary>
    Task LogMigrationStartAsync(MigrationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a file modification with before and after hashes
    /// </summary>
    Task LogFileModificationAsync(FileModificationAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a file creation
    /// </summary>
    Task LogFileCreationAsync(FileCreationAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a file deletion
    /// </summary>
    Task LogFileDeletionAsync(FileDeletionAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs the end of a migration operation
    /// </summary>
    Task LogMigrationEndAsync(MigrationReport report, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an error during migration
    /// </summary>
    Task LogErrorAsync(string context, Exception exception, CancellationToken cancellationToken = default);
}

public class FileModificationAudit
{
    public string FilePath { get; set; } = string.Empty;
    public string BeforeHash { get; set; } = string.Empty;
    public string AfterHash { get; set; } = string.Empty;
    public long BeforeSize { get; set; }
    public long AfterSize { get; set; }
    public string ModificationType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class FileCreationAudit
{
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string CreationType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class FileDeletionAudit
{
    public string FilePath { get; set; } = string.Empty;
    public string BeforeHash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string DeletionReason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}