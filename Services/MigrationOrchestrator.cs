using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class MigrationOrchestrator : IMigrationOrchestrator
{
    private readonly ILogger<MigrationOrchestrator> _logger;
    private readonly IProjectFileScanner _projectFileScanner;
    private readonly IProjectParser _projectParser;
    private readonly ISdkStyleProjectGenerator _sdkStyleProjectGenerator;

    public MigrationOrchestrator(
        ILogger<MigrationOrchestrator> logger,
        IProjectFileScanner projectFileScanner,
        IProjectParser projectParser,
        ISdkStyleProjectGenerator sdkStyleProjectGenerator)
    {
        _logger = logger;
        _projectFileScanner = projectFileScanner;
        _projectParser = projectParser;
        _sdkStyleProjectGenerator = sdkStyleProjectGenerator;
    }

    public async Task<MigrationReport> MigrateProjectsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var report = new MigrationReport
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting migration process for directory: {DirectoryPath}", directoryPath);

            // Scan for project files
            var projectFiles = await _projectFileScanner.ScanForProjectFilesAsync(directoryPath, cancellationToken);
            var projectFilesList = projectFiles.ToList();
            report.TotalProjectsFound = projectFilesList.Count;

            _logger.LogInformation("Found {Count} project files to process", projectFilesList.Count);

            foreach (var projectFile in projectFilesList)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Migration cancelled by user");
                    break;
                }

                try
                {
                    // Parse the project
                    var project = await _projectParser.ParseProjectAsync(projectFile, cancellationToken);

                    // Check if it's a legacy project
                    if (!_projectParser.IsLegacyProject(project))
                    {
                        _logger.LogInformation("Skipping {ProjectPath} - already SDK-style", projectFile);
                        continue;
                    }

                    // Generate output path
                    var outputPath = GenerateOutputPath(projectFile);

                    // Migrate to SDK-style
                    var result = await _sdkStyleProjectGenerator.GenerateSdkStyleProjectAsync(
                        project, outputPath, cancellationToken);

                    report.Results.Add(result);

                    if (result.Success)
                    {
                        report.TotalProjectsMigrated++;
                        _logger.LogInformation("Successfully migrated {ProjectPath}", projectFile);
                    }
                    else
                    {
                        report.TotalProjectsFailed++;
                        _logger.LogError("Failed to migrate {ProjectPath}", projectFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing project {ProjectPath}", projectFile);
                    
                    var result = new MigrationResult
                    {
                        ProjectPath = projectFile,
                        Success = false,
                        Errors = { ex.Message }
                    };
                    
                    report.Results.Add(result);
                    report.TotalProjectsFailed++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during migration process");
            throw;
        }
        finally
        {
            report.EndTime = DateTime.UtcNow;
            LogReport(report);
        }

        return report;
    }

    private string GenerateOutputPath(string projectFile)
    {
        // Create a backup of the original and use the same filename
        var directory = Path.GetDirectoryName(projectFile)!;
        var filename = Path.GetFileName(projectFile);
        var backupPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(filename)}.legacy{Path.GetExtension(filename)}");
        
        // Backup the original file
        if (File.Exists(projectFile))
        {
            File.Copy(projectFile, backupPath, overwrite: true);
            _logger.LogDebug("Created backup at {BackupPath}", backupPath);
        }

        return projectFile;
    }

    private void LogReport(MigrationReport report)
    {
        _logger.LogInformation("Migration Report:");
        _logger.LogInformation("  Duration: {Duration}", report.Duration);
        _logger.LogInformation("  Total projects found: {Count}", report.TotalProjectsFound);
        _logger.LogInformation("  Successfully migrated: {Count}", report.TotalProjectsMigrated);
        _logger.LogInformation("  Failed: {Count}", report.TotalProjectsFailed);

        if (report.TotalProjectsFailed > 0)
        {
            _logger.LogWarning("Failed projects:");
            foreach (var failed in report.Results.Where(r => !r.Success))
            {
                _logger.LogWarning("  - {ProjectPath}: {Errors}", 
                    failed.ProjectPath, 
                    string.Join(", ", failed.Errors));
            }
        }
    }
}