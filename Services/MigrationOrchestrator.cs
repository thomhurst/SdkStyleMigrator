using System.Xml.Linq;
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
    private readonly IBackupService _backupService;
    private readonly ILockService _lockService;
    private readonly IAuditService _auditService;
    private readonly MigrationOptions _options;

    public MigrationOrchestrator(
        ILogger<MigrationOrchestrator> logger,
        IProjectFileScanner projectFileScanner,
        IProjectParser projectParser,
        ISdkStyleProjectGenerator sdkStyleProjectGenerator,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IDirectoryBuildPropsGenerator directoryBuildPropsGenerator,
        ISolutionFileUpdater solutionFileUpdater,
        IBackupService backupService,
        ILockService lockService,
        IAuditService auditService,
        MigrationOptions options)
    {
        _logger = logger;
        _projectFileScanner = projectFileScanner;
        _projectParser = projectParser;
        _sdkStyleProjectGenerator = sdkStyleProjectGenerator;
        _assemblyInfoExtractor = assemblyInfoExtractor;
        _directoryBuildPropsGenerator = directoryBuildPropsGenerator;
        _solutionFileUpdater = solutionFileUpdater;
        _backupService = backupService;
        _lockService = lockService;
        _auditService = auditService;
        _options = options;
    }

    public async Task<MigrationReport> MigrateProjectsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var report = new MigrationReport
        {
            StartTime = DateTime.UtcNow
        };

        BackupSession? backupSession = null;
        var lockAcquired = false;

        try
        {
            // Try to acquire lock first
            if (!await _lockService.TryAcquireLockAsync(directoryPath, cancellationToken))
            {
                throw new InvalidOperationException("Could not acquire migration lock. Another migration may be in progress.");
            }
            lockAcquired = true;

            // Log migration start
            await _auditService.LogMigrationStartAsync(_options, cancellationToken);

            // Initialize backup if enabled
            if (_options.CreateBackup && !_options.DryRun)
            {
                backupSession = await _backupService.InitializeBackupAsync(directoryPath, cancellationToken);
                _logger.LogInformation("Backup initialized with session ID: {SessionId}", backupSession.SessionId);
            }

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
                        await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, cancellationToken);
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
                    
                    await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, cancellationToken);
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
            await _auditService.LogErrorAsync("MigrationOrchestrator.MigrateProjectsAsync", ex, cancellationToken);
            throw;
        }
        finally
        {
            report.EndTime = DateTime.UtcNow;
            LogReport(report);

            // Log migration end
            await _auditService.LogMigrationEndAsync(report, cancellationToken);

            // Finalize backup session
            if (backupSession != null)
            {
                await _backupService.FinalizeBackupAsync(backupSession, cancellationToken);
            }

            // Release lock
            if (lockAcquired)
            {
                await _lockService.ReleaseLockAsync(cancellationToken);
            }
        }

        return report;
    }
    
    private async Task ProcessProjectAsync(
        string projectFile, 
        string progress, 
        System.Collections.Concurrent.ConcurrentDictionary<string, AssemblyProperties> projectAssemblyProperties,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> projectMappings,
        MigrationReport report,
        BackupSession? backupSession,
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

            var outputPath = await GenerateOutputPathAsync(projectFile, cancellationToken);

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
                await RemoveAssemblyInfoFilesAsync(projectDir, backupSession, cancellationToken);
                await HandleAppConfigFileAsync(projectDir, result, cancellationToken);

                // Audit the file modification
                var fileInfo = new FileInfo(outputPath);
                await _auditService.LogFileModificationAsync(new FileModificationAudit
                {
                    FilePath = outputPath,
                    BeforeHash = await FileHashCalculator.CalculateHashAsync(projectFile, cancellationToken),
                    AfterHash = await FileHashCalculator.CalculateHashAsync(outputPath, cancellationToken),
                    BeforeSize = new FileInfo(projectFile).Length,
                    AfterSize = fileInfo.Length,
                    ModificationType = "SDK-style migration"
                }, cancellationToken);
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

    private async Task RemoveAssemblyInfoFilesAsync(string projectDirectory, BackupSession? backupSession, CancellationToken cancellationToken)
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
                        var beforeHash = await FileHashCalculator.CalculateHashAsync(file, cancellationToken);
                        var fileSize = new FileInfo(file).Length;

                        if (_options.CreateBackup && backupSession != null)
                        {
                            await _backupService.BackupFileAsync(backupSession, file, cancellationToken);
                        }
                        
                        File.Delete(file);

                        // Audit the file deletion
                        await _auditService.LogFileDeletionAsync(new FileDeletionAudit
                        {
                            FilePath = file,
                            BeforeHash = beforeHash,
                            FileSize = fileSize,
                            DeletionReason = "AssemblyInfo auto-generated by SDK"
                        }, cancellationToken);
                        
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
    }
    
    private async Task HandleAppConfigFileAsync(string projectDirectory, MigrationResult result, CancellationToken cancellationToken)
    {
        var appConfigPath = Path.Combine(projectDirectory, "app.config");
        if (!File.Exists(appConfigPath))
        {
            appConfigPath = Path.Combine(projectDirectory, "App.config");
            if (!File.Exists(appConfigPath))
                return;
        }
        
        try
        {
            var configContent = await File.ReadAllTextAsync(appConfigPath, cancellationToken);
            var doc = System.Xml.Linq.XDocument.Parse(configContent);
            
            var configuration = doc.Root;
            if (configuration == null || configuration.Name != "configuration")
                return;
                
            var runtime = configuration.Element("runtime");
            var assemblyBinding = runtime?.Element(XName.Get("assemblyBinding", "urn:schemas-microsoft-com:asm.v1"));
            
            // Remove assemblyBinding section if it exists
            if (assemblyBinding != null)
            {
                assemblyBinding.Remove();
                result.RemovedElements.Add("Assembly binding redirects from app.config");
                _logger.LogInformation("Removed assembly binding redirects from {File}", appConfigPath);
            }
            
            // Check if app.config has any other content
            var hasOtherContent = configuration.Elements()
                .Any(e => e.Name != "runtime" || 
                         (e.Name == "runtime" && e.Elements().Any()));
            
            if (!hasOtherContent)
            {
                // App.config only contained binding redirects, so we can remove it
                if (_options.CreateBackup)
                {
                    var backupPath = $"{appConfigPath}.legacy";
                    File.Copy(appConfigPath, backupPath, overwrite: true);
                }
                
                File.Delete(appConfigPath);
                result.RemovedElements.Add($"App.config file (contained only binding redirects)");
                _logger.LogInformation("Removed {File} as it only contained binding redirects", appConfigPath);
            }
            else
            {
                // Save the modified app.config without binding redirects
                await File.WriteAllTextAsync(appConfigPath, doc.ToString(), cancellationToken);
                _logger.LogInformation("Updated {File} - removed binding redirects", appConfigPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process app.config file: {File}", appConfigPath);
        }
    }

    private async Task<string> GenerateOutputPathAsync(string projectFile, CancellationToken cancellationToken)
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
            if (File.Exists(projectFile))
            {
                var backupSession = await _backupService.GetCurrentSessionAsync();
                if (backupSession != null)
                {
                    await _backupService.BackupFileAsync(backupSession, projectFile, cancellationToken);
                    _logger.LogDebug("Created backup for {ProjectFile}", projectFile);
                }
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