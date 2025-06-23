using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class LockService : ILockService, IDisposable
{
    private readonly ILogger<LockService> _logger;
    private const string LockFileName = ".sdkmigrator.lock";
    private FileStream? _lockFileStream;
    private string? _lockFilePath;

    public LockService(ILogger<LockService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TryAcquireLockAsync(string directory, CancellationToken cancellationToken = default)
    {
        _lockFilePath = Path.Combine(directory, LockFileName);

        try
        {
            // First check if there's an existing lock
            var existingLock = await GetLockInfoAsync(directory, cancellationToken);
            if (existingLock != null && !existingLock.IsStale)
            {
                _logger.LogWarning("Migration already in progress by process {ProcessId} ({ProcessName}) started at {StartTime}",
                    existingLock.ProcessId, existingLock.ProcessName, existingLock.AcquiredAt);
                return false;
            }

            // If there's a stale lock, try to clean it
            if (existingLock?.IsStale == true)
            {
                _logger.LogInformation("Found stale lock from process {ProcessId}, attempting cleanup", existingLock.ProcessId);
                await CleanStaleLockAsync(directory, cancellationToken);
            }

            // Try to create the lock file with exclusive access
            _lockFileStream = new FileStream(_lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

            // Write lock information
            var lockInfo = new LockInfo
            {
                ProcessId = Environment.ProcessId,
                ProcessName = Process.GetCurrentProcess().ProcessName,
                AcquiredAt = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName
            };

            var json = JsonSerializer.Serialize(lockInfo, new JsonSerializerOptions { WriteIndented = true });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            await _lockFileStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            await _lockFileStream.FlushAsync(cancellationToken);

            _logger.LogInformation("Lock acquired successfully for process {ProcessId}", lockInfo.ProcessId);
            return true;
        }
        catch (IOException ex) when (ex.HResult == -2147024864) // ERROR_SHARING_VIOLATION
        {
            _logger.LogWarning("Cannot acquire lock, file is in use by another process");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire lock");
            return false;
        }
    }

    public async Task ReleaseLockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_lockFileStream != null)
            {
                await _lockFileStream.DisposeAsync();
                _lockFileStream = null;
            }

            if (!string.IsNullOrEmpty(_lockFilePath) && File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
                _logger.LogInformation("Lock released and file deleted");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release lock");
        }
    }

    public async Task<LockInfo?> GetLockInfoAsync(string directory, CancellationToken cancellationToken = default)
    {
        var lockFilePath = Path.Combine(directory, LockFileName);

        if (!File.Exists(lockFilePath))
        {
            return null;
        }

        try
        {
            // Try to read the lock file
            var json = await File.ReadAllTextAsync(lockFilePath, cancellationToken);
            var lockInfo = JsonSerializer.Deserialize<LockInfo>(json);

            if (lockInfo != null)
            {
                // Check if the process is still running
                lockInfo.IsStale = !IsProcessRunning(lockInfo.ProcessId);

                // Also consider it stale if it's been held for more than 24 hours
                var age = DateTime.UtcNow - lockInfo.AcquiredAt;
                if (age.TotalHours > 24)
                {
                    lockInfo.IsStale = true;
                }
            }

            return lockInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read lock file information");
            return null;
        }
    }

    public async Task<bool> CleanStaleLockAsync(string directory, CancellationToken cancellationToken = default)
    {
        var lockInfo = await GetLockInfoAsync(directory, cancellationToken);

        if (lockInfo == null)
        {
            return true; // No lock to clean
        }

        if (!lockInfo.IsStale)
        {
            _logger.LogWarning("Cannot clean lock held by active process {ProcessId}", lockInfo.ProcessId);
            return false;
        }

        try
        {
            var lockFilePath = Path.Combine(directory, LockFileName);
            File.Delete(lockFilePath);
            _logger.LogInformation("Cleaned stale lock from process {ProcessId}", lockInfo.ProcessId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean stale lock");
            return false;
        }
    }

    private bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if process {ProcessId} is running", processId);
            // Assume it's running to be safe
            return true;
        }
    }

    public void Dispose()
    {
        ReleaseLockAsync().GetAwaiter().GetResult();
    }
}