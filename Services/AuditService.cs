using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class AuditService : IAuditService
{
    private readonly ILogger<AuditService> _logger;
    private readonly string _auditLogPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    
    public AuditService(ILogger<AuditService> logger, MigrationOptions options)
    {
        _logger = logger;
        _auditLogPath = Path.Combine(
            options.OutputDirectory ?? options.DirectoryPath,
            $"sdkmigrator_audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl"
        );
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false // Use JSON Lines format for easier parsing
        };
    }
    
    public async Task LogMigrationStartAsync(MigrationOptions options, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "MigrationStart",
            Timestamp = DateTime.UtcNow,
            User = new
            {
                Name = Environment.UserName,
                Domain = Environment.UserDomainName,
                Machine = Environment.MachineName
            },
            Tool = new
            {
                Name = "SdkMigrator",
                Version = GetToolVersion(),
                ProcessId = Environment.ProcessId
            },
            Parameters = new
            {
                options.DirectoryPath,
                options.OutputDirectory,
                options.TargetFramework,
                options.DryRun,
                options.CreateBackup,
                options.MaxDegreeOfParallelism
            }
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    public async Task LogFileModificationAsync(FileModificationAudit audit, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "FileModification",
            audit.Timestamp,
            audit.FilePath,
            audit.ModificationType,
            Before = new
            {
                Hash = audit.BeforeHash,
                Size = audit.BeforeSize
            },
            After = new
            {
                Hash = audit.AfterHash,
                Size = audit.AfterSize
            },
            User = Environment.UserName,
            ProcessId = Environment.ProcessId
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    public async Task LogFileCreationAsync(FileCreationAudit audit, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "FileCreation",
            audit.Timestamp,
            audit.FilePath,
            audit.CreationType,
            File = new
            {
                Hash = audit.FileHash,
                Size = audit.FileSize
            },
            User = Environment.UserName,
            ProcessId = Environment.ProcessId
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    public async Task LogFileDeletionAsync(FileDeletionAudit audit, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "FileDeletion",
            audit.Timestamp,
            audit.FilePath,
            audit.DeletionReason,
            Before = new
            {
                Hash = audit.BeforeHash,
                Size = audit.FileSize
            },
            User = Environment.UserName,
            ProcessId = Environment.ProcessId
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    public async Task LogMigrationEndAsync(MigrationReport report, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "MigrationEnd",
            Timestamp = DateTime.UtcNow,
            Summary = new
            {
                report.TotalProjectsFound,
                report.TotalProjectsMigrated,
                report.TotalProjectsFailed,
                Duration = report.Duration.TotalSeconds,
                Success = report.TotalProjectsFailed == 0
            },
            User = Environment.UserName,
            ProcessId = Environment.ProcessId
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    public async Task LogErrorAsync(string context, Exception exception, CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            EventType = "Error",
            Timestamp = DateTime.UtcNow,
            Context = context,
            Error = new
            {
                Type = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace
            },
            User = Environment.UserName,
            ProcessId = Environment.ProcessId
        };
        
        await WriteAuditEntryAsync(entry, cancellationToken);
    }
    
    private async Task WriteAuditEntryAsync(object entry, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            await File.AppendAllTextAsync(_auditLogPath, json + Environment.NewLine, cancellationToken);
            
            // Also log to structured logging
            _logger.LogInformation("Audit: {AuditEntry}", json);
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    private string GetToolVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }
}

public static class FileHashCalculator
{
    public static async Task<string> CalculateHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }
        
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hash);
    }
}