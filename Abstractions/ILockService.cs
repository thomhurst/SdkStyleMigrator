namespace SdkMigrator.Abstractions;

public interface ILockService
{
    /// <summary>
    /// Attempts to acquire a lock for the migration process
    /// </summary>
    /// <returns>True if lock was acquired, false if another process holds the lock</returns>
    Task<bool> TryAcquireLockAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the lock held by this process
    /// </summary>
    Task ReleaseLockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a lock exists and provides information about it
    /// </summary>
    Task<LockInfo?> GetLockInfoAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to clean up stale locks
    /// </summary>
    Task<bool> CleanStaleLockAsync(string directory, CancellationToken cancellationToken = default);
}

public class LockInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime AcquiredAt { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsStale { get; set; }
}