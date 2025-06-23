using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IBackupService
{
    /// <summary>
    /// Initializes a new backup session and creates the backup directory
    /// </summary>
    /// <returns>The backup session information</returns>
    Task<BackupSession> InitializeBackupAsync(string rootDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backs up a file before modification
    /// </summary>
    Task BackupFileAsync(BackupSession session, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes the backup session and writes the manifest
    /// </summary>
    Task FinalizeBackupAsync(BackupSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back all changes from a backup session
    /// </summary>
    Task<RollbackResult> RollbackAsync(string backupDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available backup sessions in a directory
    /// </summary>
    Task<IEnumerable<BackupSession>> ListBackupsAsync(string rootDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current active backup session
    /// </summary>
    Task<BackupSession?> GetCurrentSessionAsync();

    /// <summary>
    /// Gets a specific backup session by ID
    /// </summary>
    Task<BackupSession?> GetBackupSessionAsync(string rootDirectory, string sessionId, CancellationToken cancellationToken = default);
}