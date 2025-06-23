using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private const string BackupDirectoryPrefix = "_sdkmigrator_backup_";
    private const string ManifestFileName = "manifest.json";
    private const string GitIgnoreContent = "*\n";
    private BackupSession? _currentSession;

    public BackupService(ILogger<BackupService> logger)
    {
        _logger = logger;
    }

    public async Task<BackupSession> InitializeBackupAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;
        var sessionId = timestamp.ToString("yyyyMMdd_HHmmss");
        var backupDirName = $"{BackupDirectoryPrefix}{sessionId}";
        var backupDirectory = Path.Combine(rootDirectory, backupDirName);

        _logger.LogInformation("Initializing backup session {SessionId} at {BackupDirectory}", sessionId, backupDirectory);

        // Create backup directory
        Directory.CreateDirectory(backupDirectory);

        // Create .gitignore to prevent accidental commits
        var gitIgnorePath = Path.Combine(backupDirectory, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, GitIgnoreContent, cancellationToken);

        var session = new BackupSession
        {
            SessionId = sessionId,
            StartTime = timestamp,
            RootDirectory = rootDirectory,
            BackupDirectory = backupDirectory,
            ToolVersion = GetToolVersion(),
            UserName = Environment.UserName,
            MachineName = Environment.MachineName
        };

        _currentSession = session;
        return session;
    }

    public async Task BackupFileAsync(BackupSession session, string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File {FilePath} does not exist, skipping backup", filePath);
            return;
        }

        var relativePath = Path.GetRelativePath(session.RootDirectory, filePath);
        var backupPath = Path.Combine(session.BackupDirectory, relativePath);

        // Create directory structure in backup
        var backupDir = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        // Calculate hash before copying
        var hash = await CalculateFileHashAsync(filePath, cancellationToken);

        // Copy file to backup
        await CopyFileAsync(filePath, backupPath, cancellationToken);

        var fileInfo = new FileInfo(filePath);
        var backupInfo = new BackupFileInfo
        {
            OriginalPath = filePath,
            BackupPath = backupPath,
            OriginalHash = hash,
            BackupTime = DateTime.UtcNow,
            FileSize = fileInfo.Length
        };

        session.BackedUpFiles.Add(backupInfo);
        _logger.LogDebug("Backed up {FilePath} to {BackupPath} (hash: {Hash})", filePath, backupPath, hash);
    }

    public async Task FinalizeBackupAsync(BackupSession session, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(session.BackupDirectory, ManifestFileName);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(session, options);
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);

        _logger.LogInformation("Backup session {SessionId} finalized with {FileCount} files backed up",
            session.SessionId, session.BackedUpFiles.Count);
    }

    public async Task<RollbackResult> RollbackAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var result = new RollbackResult();

        try
        {
            var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                result.Errors.Add($"Manifest file not found at {manifestPath}");
                return result;
            }

            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var session = JsonSerializer.Deserialize<BackupSession>(json);

            if (session == null)
            {
                result.Errors.Add("Failed to deserialize backup manifest");
                return result;
            }

            result.BackupSession = session;

            _logger.LogInformation("Starting rollback from backup session {SessionId}", session.SessionId);

            // First, verify all backup files exist and match their hashes
            foreach (var fileInfo in session.BackedUpFiles)
            {
                if (!File.Exists(fileInfo.BackupPath))
                {
                    result.Errors.Add($"Backup file missing: {fileInfo.BackupPath}");
                    continue;
                }

                var backupHash = await CalculateFileHashAsync(fileInfo.BackupPath, cancellationToken);
                if (backupHash != fileInfo.OriginalHash)
                {
                    result.Errors.Add($"Backup file hash mismatch for {fileInfo.BackupPath}. Expected: {fileInfo.OriginalHash}, Got: {backupHash}");
                }
            }

            if (result.Errors.Any())
            {
                _logger.LogError("Rollback aborted due to backup integrity issues");
                return result;
            }

            // Perform the rollback
            foreach (var fileInfo in session.BackedUpFiles)
            {
                try
                {
                    // If the original file was deleted (exists in backup but not in original location)
                    // we need to restore it. Otherwise, we're replacing a modified file.

                    var originalDir = Path.GetDirectoryName(fileInfo.OriginalPath);
                    if (!string.IsNullOrEmpty(originalDir) && !Directory.Exists(originalDir))
                    {
                        Directory.CreateDirectory(originalDir);
                    }

                    await CopyFileAsync(fileInfo.BackupPath, fileInfo.OriginalPath, cancellationToken);
                    result.RestoredFiles.Add(fileInfo.OriginalPath);

                    _logger.LogDebug("Restored {FilePath} from backup", fileInfo.OriginalPath);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to restore {fileInfo.OriginalPath}: {ex.Message}";
                    result.Errors.Add(error);
                    _logger.LogError(ex, "Failed to restore file {FilePath}", fileInfo.OriginalPath);
                }
            }

            // Handle files that were created during migration (not in backup)
            // This requires tracking in the migration process, which we'll add later

            result.Success = !result.Errors.Any();

            _logger.LogInformation("Rollback completed. Restored: {RestoredCount}, Errors: {ErrorCount}",
                result.RestoredFiles.Count, result.Errors.Count);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Rollback failed: {ex.Message}");
            _logger.LogError(ex, "Rollback failed");
        }

        return result;
    }

    public async Task<IEnumerable<BackupSession>> ListBackupsAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var backups = new List<BackupSession>();

        if (!Directory.Exists(rootDirectory))
        {
            return backups;
        }

        var backupDirs = Directory.GetDirectories(rootDirectory, $"{BackupDirectoryPrefix}*");

        foreach (var backupDir in backupDirs)
        {
            try
            {
                var manifestPath = Path.Combine(backupDir, ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                    var session = JsonSerializer.Deserialize<BackupSession>(json);
                    if (session != null)
                    {
                        backups.Add(session);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read backup manifest from {BackupDir}", backupDir);
            }
        }

        return backups.OrderByDescending(b => b.StartTime);
    }

    private async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hash);
    }

    private async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB buffer

        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        await sourceStream.CopyToAsync(destinationStream, bufferSize, cancellationToken);
    }

    private string GetToolVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    public Task<BackupSession?> GetCurrentSessionAsync()
    {
        return Task.FromResult(_currentSession);
    }

    public async Task<BackupSession?> GetBackupSessionAsync(string rootDirectory, string sessionId, CancellationToken cancellationToken = default)
    {
        var backupDirName = $"{BackupDirectoryPrefix}{sessionId}";
        var backupDirectory = Path.Combine(rootDirectory, backupDirName);

        if (!Directory.Exists(backupDirectory))
        {
            return null;
        }

        var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            return JsonSerializer.Deserialize<BackupSession>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read backup manifest for session {SessionId}", sessionId);
            return null;
        }
    }
}