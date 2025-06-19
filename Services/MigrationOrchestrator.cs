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
    private readonly ISolutionFileUpdater _solutionFileUpdater;
    private readonly MigrationOptions _options;

    public MigrationOrchestrator(
        ILogger<MigrationOrchestrator> logger,
        IProjectFileScanner projectFileScanner,
        IProjectParser projectParser,
        ISdkStyleProjectGenerator sdkStyleProjectGenerator,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IDirectoryBuildPropsGenerator directoryBuildPropsGenerator,
        ISolutionFileUpdater solutionFileUpdater,
        MigrationOptions options)
    {
        _logger = logger;
        _projectFileScanner = projectFileScanner;
        _projectParser = projectParser;
        _sdkStyleProjectGenerator = sdkStyleProjectGenerator;
        _assemblyInfoExtractor = assemblyInfoExtractor;
        _directoryBuildPropsGenerator = directoryBuildPropsGenerator;
        _solutionFileUpdater = solutionFileUpdater;
        _options = options;
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

            var projectFiles = await _projectFileScanner.ScanForProjectFilesAsync(directoryPath, cancellationToken);
            var projectFilesList = projectFiles.ToList();
            report.TotalProjectsFound = projectFilesList.Count;

            _logger.LogInformation("Found {Count} project files to process", projectFilesList.Count);

            var projectAssemblyProperties = new System.Collections.Concurrent.ConcurrentDictionary<string, AssemblyProperties>();
            var projectMappings = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var projectIndex = 0;
            var totalProjects = projectFilesList.Count;
            
            if (_options.MaxDegreeOfParallelism > 1)
            {
                _logger.LogInformation("Processing projects in parallel with max degree of parallelism: {MaxDegree}", _options.MaxDegreeOfParallelism);
                
                var semaphore = new SemaphoreSlim(_options.MaxDegreeOfParallelism);
                var processedCount = 0;
                var lockObj = new object();
                
                var migrationTasks = projectFilesList.Select(async projectFile =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogWarning("Migration cancelled by user");
                            return;
                        }
                        
                        int currentIndex;
                        lock (lockObj)
                        {
                            currentIndex = ++processedCount;
                        }
                        
                        var progress = $"[{currentIndex}/{totalProjects}]";
                        await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();
                
                await Task.WhenAll(migrationTasks);
            }
            else
            {
                foreach (var projectFile in projectFilesList)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Migration cancelled by user");
                        break;
                    }

                    projectIndex++;
                    var progress = $"[{projectIndex}/{projectFilesList.Count}]";
                    
                    await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, cancellationToken);
                }
            }
            
            if (projectAssemblyProperties.Any())
            {
                var outputDir = !string.IsNullOrEmpty(_options.OutputDirectory) 
                    ? _options.OutputDirectory 
                    : directoryPath;
                    
                await _directoryBuildPropsGenerator.GenerateDirectoryBuildPropsAsync(
                    outputDir, projectAssemblyProperties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), cancellationToken);
            }
            
            if (projectMappings.Any())
            {
                _logger.LogInformation("Updating solution files with new project paths");
                var solutionResult = await _solutionFileUpdater.UpdateSolutionFilesAsync(
                    directoryPath, 
                    projectMappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), 
                    cancellationToken);
                    
                if (!solutionResult.Success)
                {
                    foreach (var error in solutionResult.Errors)
                    {
                        _logger.LogError("Solution update error: {Error}", error);
                    }
                }
                else if (solutionResult.UpdatedProjects.Any())
                {
                    _logger.LogInformation("Updated {Count} project references in solution files", 
                        solutionResult.UpdatedProjects.Count);
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
    
    private async Task ProcessProjectAsync(
        string projectFile, 
        string progress, 
        System.Collections.Concurrent.ConcurrentDictionary<string, AssemblyProperties> projectAssemblyProperties,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> projectMappings,
        MigrationReport report,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedProject = await _projectParser.ParseProjectAsync(projectFile, cancellationToken);
            var project = parsedProject.Project;

            if (!_projectParser.IsLegacyProject(project))
            {
                _logger.LogInformation("{Progress} Skipping {ProjectPath} - already SDK-style", progress, projectFile);
                return;
            }
            
            _logger.LogInformation("{Progress} Processing {ProjectPath}", progress, projectFile);

            var projectDir = Path.GetDirectoryName(projectFile)!;
            var assemblyProps = await _assemblyInfoExtractor.ExtractAssemblyPropertiesAsync(projectDir, cancellationToken);
            var projectProps = await _assemblyInfoExtractor.ExtractFromProjectAsync(project, cancellationToken);
            
            foreach (var prop in typeof(AssemblyProperties).GetProperties())
            {
                var projectValue = prop.GetValue(projectProps);
                if (projectValue != null && (projectValue is not string str || !string.IsNullOrEmpty(str)))
                {
                    prop.SetValue(assemblyProps, projectValue);
                }
            }
            
            projectAssemblyProperties[projectFile] = assemblyProps;

            var outputPath = GenerateOutputPath(projectFile);

            var result = await _sdkStyleProjectGenerator.GenerateSdkStyleProjectAsync(
                project, outputPath, cancellationToken);

            if (parsedProject.LoadedWithDefensiveParsing)
            {
                result.LoadedWithDefensiveParsing = true;
                result.Warnings.Add("Project was loaded with defensive parsing due to invalid imports. Some imports were removed automatically.");
                foreach (var removedImport in parsedProject.RemovedImports)
                {
                    result.Warnings.Add($"Removed import: {removedImport}");
                }
            }

            if (result.Success && !_options.DryRun)
            {
                await RemoveAssemblyInfoFilesAsync(projectDir, cancellationToken);
            }
            
            if (result.Success && outputPath != projectFile)
            {
                projectMappings[projectFile] = outputPath;
            }

            lock (report)
            {
                report.Results.Add(result);

                if (result.Success)
                {
                    report.TotalProjectsMigrated++;
                    _logger.LogInformation("{Progress} Successfully migrated {ProjectPath}", progress, projectFile);
                }
                else
                {
                    report.TotalProjectsFailed++;
                    _logger.LogError("{Progress} Failed to migrate {ProjectPath}", progress, projectFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Progress} Error processing project {ProjectPath}", progress, projectFile);
            
            var result = new MigrationResult
            {
                ProjectPath = projectFile,
                Success = false,
                Errors = { ex.Message }
            };
            
            lock (report)
            {
                report.Results.Add(result);
                report.TotalProjectsFailed++;
            }
        }
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
                    if (!_options.DryRun)
                    {
                        if (_options.CreateBackup)
                        {
                            var backupPath = $"{file}.legacy";
                            File.Copy(file, backupPath, overwrite: true);
                        }
                        File.Delete(file);
                        
                        _logger.LogInformation("Removed AssemblyInfo file: {File}{BackupInfo}", 
                            file, 
                            _options.CreateBackup ? $" (backup: {file}.legacy)" : "");
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would remove AssemblyInfo file: {File}", file);
                    }
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
        if (!string.IsNullOrEmpty(_options.OutputDirectory))
        {
            var relativePath = Path.GetRelativePath(_options.DirectoryPath, projectFile);
            var outputPath = Path.Combine(_options.OutputDirectory, relativePath);
            
            if (!_options.DryRun)
            {
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
            }
            
            return outputPath;
        }
        
        if (_options.CreateBackup && !_options.DryRun)
        {
            var directory = Path.GetDirectoryName(projectFile)!;
            var filename = Path.GetFileName(projectFile);
            var backupPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(filename)}.legacy{Path.GetExtension(filename)}");
            
            if (File.Exists(projectFile))
            {
                File.Copy(projectFile, backupPath, overwrite: true);
                _logger.LogDebug("Created backup at {BackupPath}", backupPath);
            }
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
        
        var totalRemovedElements = report.Results.Sum(r => r.RemovedElements.Count);
        if (totalRemovedElements > 0)
        {
            _logger.LogInformation("");
            _logger.LogInformation("Total legacy elements removed: {Count}", totalRemovedElements);
        }
        
        if (!_options.DryRun)
        {
            var reportPath = Path.Combine(Path.GetDirectoryName(report.Results.FirstOrDefault()?.ProjectPath ?? ".") ?? ".", 
                $"migration-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            WriteDetailedReport(report, reportPath);
            _logger.LogInformation("");
            _logger.LogInformation("Detailed migration report written to: {Path}", reportPath);
        }
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