using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;

namespace SdkMigrator.Services;

public class MigrationOrchestrator : IMigrationOrchestrator
{
    private readonly ILogger<MigrationOrchestrator> _logger;
    private readonly IProjectFileScanner _projectFileScanner;
    private readonly IProjectParser _projectParser;
    private readonly ISdkStyleProjectGenerator _sdkStyleProjectGenerator;
    private readonly IAssemblyInfoExtractor _assemblyInfoExtractor;
    private readonly IDirectoryBuildPropsGenerator _directoryBuildPropsGenerator;

    public MigrationOrchestrator(
        ILogger<MigrationOrchestrator> logger,
        IProjectFileScanner projectFileScanner,
        IProjectParser projectParser,
        ISdkStyleProjectGenerator sdkStyleProjectGenerator,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IDirectoryBuildPropsGenerator directoryBuildPropsGenerator)
    {
        _logger = logger;
        _projectFileScanner = projectFileScanner;
        _projectParser = projectParser;
        _sdkStyleProjectGenerator = sdkStyleProjectGenerator;
        _assemblyInfoExtractor = assemblyInfoExtractor;
        _directoryBuildPropsGenerator = directoryBuildPropsGenerator;
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

            // Collect assembly properties from all projects
            var projectAssemblyProperties = new Dictionary<string, AssemblyProperties>();

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

                    // Extract assembly properties from AssemblyInfo files and project
                    var projectDir = Path.GetDirectoryName(projectFile)!;
                    var assemblyProps = await _assemblyInfoExtractor.ExtractAssemblyPropertiesAsync(projectDir, cancellationToken);
                    var projectProps = await _assemblyInfoExtractor.ExtractFromProjectAsync(project, cancellationToken);
                    
                    // Merge properties (project properties take precedence)
                    foreach (var prop in typeof(AssemblyProperties).GetProperties())
                    {
                        var projectValue = prop.GetValue(projectProps);
                        if (projectValue != null && (projectValue is not string str || !string.IsNullOrEmpty(str)))
                        {
                            prop.SetValue(assemblyProps, projectValue);
                        }
                    }
                    
                    projectAssemblyProperties[projectFile] = assemblyProps;

                    // Generate output path
                    var outputPath = GenerateOutputPath(projectFile);

                    // Migrate to SDK-style
                    var result = await _sdkStyleProjectGenerator.GenerateSdkStyleProjectAsync(
                        project, outputPath, cancellationToken);

                    // Remove AssemblyInfo files after successful migration
                    if (result.Success)
                    {
                        await RemoveAssemblyInfoFilesAsync(projectDir, cancellationToken);
                    }

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
            
            // Generate Directory.Build.props with common assembly properties
            if (projectAssemblyProperties.Any())
            {
                await _directoryBuildPropsGenerator.GenerateDirectoryBuildPropsAsync(
                    directoryPath, projectAssemblyProperties, cancellationToken);
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

    private Task RemoveAssemblyInfoFilesAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        foreach (var pattern in LegacyProjectElements.AssemblyInfoFilePatterns)
        {
            var files = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                try
                {
                    // Create backup before deletion
                    var backupPath = $"{file}.backup";
                    File.Copy(file, backupPath, overwrite: true);
                    File.Delete(file);
                    
                    _logger.LogInformation("Removed AssemblyInfo file: {File} (backup: {BackupPath})", file, backupPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove AssemblyInfo file: {File}", file);
                }
            }
        }
        
        return Task.CompletedTask;
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
        
        // Log warnings that need manual review
        var projectsWithWarnings = report.Results.Where(r => r.Warnings.Any()).ToList();
        if (projectsWithWarnings.Any())
        {
            _logger.LogWarning("");
            _logger.LogWarning("Projects with warnings that need manual review:");
            foreach (var project in projectsWithWarnings)
            {
                _logger.LogWarning("  {ProjectPath}:", project.ProjectPath);
                foreach (var warning in project.Warnings)
                {
                    _logger.LogWarning("    - {Warning}", warning);
                }
            }
        }
        
        // Summary of items removed
        var totalRemovedElements = report.Results.Sum(r => r.RemovedElements.Count);
        if (totalRemovedElements > 0)
        {
            _logger.LogInformation("");
            _logger.LogInformation("Total legacy elements removed: {Count}", totalRemovedElements);
        }
        
        // Write detailed report to file
        var reportPath = Path.Combine(Path.GetDirectoryName(report.Results.FirstOrDefault()?.ProjectPath ?? ".") ?? ".", 
            $"migration-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
        WriteDetailedReport(report, reportPath);
        _logger.LogInformation("");
        _logger.LogInformation("Detailed migration report written to: {Path}", reportPath);
    }
    
    private void WriteDetailedReport(MigrationReport report, string reportPath)
    {
        using var writer = new StreamWriter(reportPath);
        
        writer.WriteLine("SDK Migration Report");
        writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"Duration: {report.Duration}");
        writer.WriteLine();
        
        writer.WriteLine("Summary:");
        writer.WriteLine($"  Total projects found: {report.TotalProjectsFound}");
        writer.WriteLine($"  Successfully migrated: {report.TotalProjectsMigrated}");
        writer.WriteLine($"  Failed: {report.TotalProjectsFailed}");
        writer.WriteLine();
        
        foreach (var result in report.Results)
        {
            writer.WriteLine($"Project: {result.ProjectPath}");
            writer.WriteLine($"  Status: {(result.Success ? "Success" : "Failed")}");
            
            if (result.Errors.Any())
            {
                writer.WriteLine("  Errors:");
                foreach (var error in result.Errors)
                {
                    writer.WriteLine($"    - {error}");
                }
            }
            
            if (result.Warnings.Any())
            {
                writer.WriteLine("  Warnings (require manual review):");
                foreach (var warning in result.Warnings)
                {
                    writer.WriteLine($"    - {warning}");
                }
            }
            
            if (result.RemovedElements.Any())
            {
                writer.WriteLine("  Removed elements:");
                foreach (var element in result.RemovedElements)
                {
                    writer.WriteLine($"    - {element}");
                }
            }
            
            if (result.MigratedPackages.Any())
            {
                writer.WriteLine("  Migrated packages:");
                foreach (var package in result.MigratedPackages)
                {
                    writer.WriteLine($"    - {package.PackageId} {package.Version}");
                }
            }
            
            writer.WriteLine();
        }
    }
}