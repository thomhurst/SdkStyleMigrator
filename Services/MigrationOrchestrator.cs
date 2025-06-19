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
    private readonly ILocalPackageFilesCleaner _localPackageFilesCleaner;
    private readonly ICentralPackageManagementGenerator _centralPackageManagementGenerator;
    private readonly IPostMigrationValidator _postMigrationValidator;
    private readonly IMigrationAnalyzer _migrationAnalyzer;
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
        ILocalPackageFilesCleaner localPackageFilesCleaner,
        ICentralPackageManagementGenerator centralPackageManagementGenerator,
        IPostMigrationValidator postMigrationValidator,
        IMigrationAnalyzer migrationAnalyzer,
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
        _localPackageFilesCleaner = localPackageFilesCleaner;
        _centralPackageManagementGenerator = centralPackageManagementGenerator;
        _postMigrationValidator = postMigrationValidator;
        _migrationAnalyzer = migrationAnalyzer;
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
        
        // Track cleanup information for each project
        var projectCleanupInfo = new System.Collections.Concurrent.ConcurrentBag<(string ProjectDir, List<PackageReference> Packages, List<string> HintPaths)>();

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

            // Run pre-migration analysis if not in force mode
            if (!_options.Force && !_options.DryRun)
            {
                _logger.LogInformation("Running pre-migration analysis...");
                var analysis = await _migrationAnalyzer.AnalyzeProjectsAsync(directoryPath, cancellationToken);
                
                if (!analysis.CanProceedAutomatically)
                {
                    _logger.LogError("Pre-migration analysis found critical issues that prevent automatic migration");
                    _logger.LogError("Overall risk level: {Risk}", analysis.OverallRisk);
                    _logger.LogError("Estimated manual effort: {Hours} hours", analysis.EstimatedManualEffortHours);
                    
                    foreach (var project in analysis.ProjectAnalyses.Where(p => !p.CanMigrate))
                    {
                        _logger.LogError("Project {Project} cannot be migrated: {Issues}", 
                            project.ProjectName, 
                            string.Join(", ", project.Issues.Where(i => i.BlocksMigration).Select(i => i.Description)));
                    }
                    
                    throw new InvalidOperationException("Migration cannot proceed due to critical issues. Use --force to override or fix the issues first.");
                }
                
                if (analysis.OverallRisk >= MigrationRiskLevel.High)
                {
                    _logger.LogWarning("Pre-migration analysis indicates HIGH RISK migration");
                    _logger.LogWarning("Consider reviewing the analysis report before proceeding");
                }
            }

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
                        await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, projectCleanupInfo, cancellationToken);
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
                    
                    await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, projectCleanupInfo, cancellationToken);
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
            
            // Generate Central Package Management configuration if enabled
            if (_options.EnableCentralPackageManagement && report.Results.Any(r => r.Success))
            {
                _logger.LogInformation("Generating Central Package Management configuration...");
                
                var outputDir = !string.IsNullOrEmpty(_options.OutputDirectory) 
                    ? _options.OutputDirectory 
                    : directoryPath;
                    
                var cpmResult = await _centralPackageManagementGenerator.GenerateDirectoryPackagesPropsAsync(
                    outputDir, 
                    report.Results.Where(r => r.Success), 
                    cancellationToken);
                    
                if (cpmResult.Success)
                {
                    _logger.LogInformation("Created Directory.Packages.props with {Count} packages", cpmResult.PackageCount);
                    
                    if (cpmResult.VersionConflicts.Any())
                    {
                        _logger.LogWarning("Resolved {Count} package version conflicts", cpmResult.VersionConflicts.Count);
                    }
                    
                    // Remove versions from project files
                    if (!_options.DryRun)
                    {
                        var migratedProjectFiles = report.Results
                            .Where(r => r.Success)
                            .Select(r => r.OutputPath)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Cast<string>()
                            .ToList();
                            
                        await _centralPackageManagementGenerator.RemoveVersionsFromProjectsAsync(
                            migratedProjectFiles, 
                            cancellationToken);
                    }
                }
                else
                {
                    foreach (var error in cpmResult.Errors)
                    {
                        _logger.LogError("Central Package Management error: {Error}", error);
                    }
                }
            }
            
            // Clean up local package files after all projects are migrated (to avoid file lock issues)
            if (!_options.DryRun && projectCleanupInfo.Any())
            {
                _logger.LogInformation("Starting cleanup of local package files for all migrated projects...");
                
                var totalCleanedFiles = 0;
                var totalBytesFreed = 0L;
                var cleanupErrors = new List<string>();
                
                foreach (var (projectDir, packages, hintPaths) in projectCleanupInfo)
                {
                    try
                    {
                        var cleanupResult = await _localPackageFilesCleaner.CleanLocalPackageFilesAsync(
                            projectDir,
                            packages,
                            hintPaths,
                            cancellationToken);
                        
                        if (cleanupResult.Success)
                        {
                            totalCleanedFiles += cleanupResult.CleanedFiles.Count;
                            totalBytesFreed += cleanupResult.TotalBytesFreed;
                            
                            if (cleanupResult.CleanedFiles.Any())
                            {
                                _logger.LogInformation("Cleaned {Count} files in {ProjectDir}, freed {Size:N0} bytes", 
                                    cleanupResult.CleanedFiles.Count, projectDir, cleanupResult.TotalBytesFreed);
                            }
                        }
                        else
                        {
                            cleanupErrors.AddRange(cleanupResult.Errors);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean package files for {ProjectDir}", projectDir);
                        cleanupErrors.Add($"{projectDir}: {ex.Message}");
                    }
                }
                
                if (totalCleanedFiles > 0)
                {
                    _logger.LogInformation("Total package cleanup: Cleaned {Count} files, freed {Size:N0} bytes", 
                        totalCleanedFiles, totalBytesFreed);
                }
                
                if (cleanupErrors.Any())
                {
                    _logger.LogWarning("Package cleanup encountered {Count} errors", cleanupErrors.Count);
                    foreach (var error in cleanupErrors.Take(10)) // Limit error output
                    {
                        _logger.LogWarning("  - {Error}", error);
                    }
                }
            }
            
            // Clean packages folder if all projects have been migrated
            if (report.TotalProjectsMigrated > 0 && report.TotalProjectsFailed == 0 && !_options.DryRun)
            {
                _logger.LogInformation("All projects migrated successfully. Checking if packages folder can be cleaned...");
                var packagesCleanResult = await _localPackageFilesCleaner.CleanPackagesFolderAsync(directoryPath, cancellationToken);
                if (packagesCleanResult)
                {
                    _logger.LogInformation("Successfully cleaned packages folder");
                }
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
            
            // Run post-migration validation
            if (report.Results.Any(r => r.Success) && !_options.DryRun)
            {
                _logger.LogInformation("Running post-migration validation...");
                
                var validationReport = await _postMigrationValidator.ValidateSolutionAsync(
                    directoryPath,
                    report.Results,
                    cancellationToken);
                    
                if (validationReport.ProjectsWithIssues > 0)
                {
                    _logger.LogWarning("Post-migration validation found issues in {Count} projects", 
                        validationReport.ProjectsWithIssues);
                        
                    // Add validation issues to the migration report warnings
                    foreach (var projectResult in validationReport.ProjectResults.Where(r => r.Issues.Any()))
                    {
                        var migrationResult = report.Results.FirstOrDefault(r => 
                            r.OutputPath == projectResult.ProjectPath || 
                            r.ProjectPath == projectResult.ProjectPath);
                            
                        if (migrationResult != null)
                        {
                            foreach (var issue in projectResult.Issues.Where(i => 
                                i.Severity == ValidationSeverity.Warning || 
                                i.Severity == ValidationSeverity.Error))
                            {
                                migrationResult.Warnings.Add($"[Validation] {issue.Message}");
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Post-migration validation completed successfully for all projects");
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
        System.Collections.Concurrent.ConcurrentBag<(string ProjectDir, List<PackageReference> Packages, List<string> HintPaths)> projectCleanupInfo,
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
                await HandleNuSpecFileAsync(projectDir, result, backupSession, cancellationToken);

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

                // Store cleanup information for later processing
                // Files will be cleaned after all projects are migrated to avoid file lock issues
                if (result.ConvertedHintPaths.Any() || result.MigratedPackages.Any())
                {
                    projectCleanupInfo.Add((projectDir, result.MigratedPackages, result.ConvertedHintPaths));
                }
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
    
    private async Task HandleNuSpecFileAsync(string projectDirectory, MigrationResult result, BackupSession? backupSession, CancellationToken cancellationToken)
    {
        // Check if the result indicates a nuspec was migrated
        var nuspecEntry = result.RemovedElements.FirstOrDefault(e => e.StartsWith("NuSpec file:"));
        if (nuspecEntry != null)
        {
            // Extract the filename from the entry
            var startIndex = "NuSpec file: ".Length;
            var endIndex = nuspecEntry.IndexOf(" (metadata migrated");
            if (endIndex > startIndex)
            {
                var nuspecFileName = nuspecEntry.Substring(startIndex, endIndex - startIndex);
                
                // Search for the nuspec file in common locations
                var searchPaths = new[]
                {
                    Path.Combine(projectDirectory, nuspecFileName),
                    Path.Combine(projectDirectory, "..", nuspecFileName),
                    Path.Combine(projectDirectory, "nuget", nuspecFileName),
                    Path.Combine(projectDirectory, "..", "nuget", nuspecFileName)
                };
                
                foreach (var searchPath in searchPaths)
                {
                    if (File.Exists(searchPath))
                    {
                        var beforeHash = await FileHashCalculator.CalculateHashAsync(searchPath, cancellationToken);
                        var fileSize = new FileInfo(searchPath).Length;
                        
                        if (_options.CreateBackup && backupSession != null)
                        {
                            await _backupService.BackupFileAsync(backupSession, searchPath, cancellationToken);
                        }
                        
                        File.Delete(searchPath);
                        
                        await _auditService.LogFileDeletionAsync(new FileDeletionAudit
                        {
                            FilePath = searchPath,
                            BeforeHash = beforeHash,
                            FileSize = fileSize,
                            DeletionReason = "NuSpec metadata migrated to project file"
                        }, cancellationToken);
                        
                        _logger.LogInformation("Removed NuSpec file: {File} (metadata migrated to project file)", searchPath);
                        break;
                    }
                }
            }
        }
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
    
    public async Task<MigrationAnalysis> AnalyzeProjectsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting pre-migration analysis for directory: {DirectoryPath}", directoryPath);
        return await _migrationAnalyzer.AnalyzeProjectsAsync(directoryPath, cancellationToken);
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
        
        // Add configuration migration guidance section
        var configLogger = new Microsoft.Extensions.Logging.LoggerFactory()
            .CreateLogger<ConfigurationMigrationAnalyzer>();
        var configAnalyzer = new ConfigurationMigrationAnalyzer(configLogger);
        var projectsWithConfig = new List<ConfigurationMigrationGuidance>();
        
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
        
        // Write configuration migration guidance if any
        foreach (var result in report.Results.Where(r => r.Success))
        {
            var targetFramework = _options.TargetFramework ?? "net8.0";
            var configGuidance = configAnalyzer.AnalyzeConfiguration(result.OutputPath ?? result.ProjectPath, targetFramework);
            
            if (configGuidance.Issues.Any())
            {
                projectsWithConfig.Add(configGuidance);
            }
        }
        
        if (projectsWithConfig.Any())
        {
            writer.WriteLine();
            writer.WriteLine("Configuration Migration Guidance:");
            writer.WriteLine("================================");
            
            foreach (var guidance in projectsWithConfig)
            {
                writer.WriteLine();
                writer.WriteLine($"Project: {guidance.ProjectPath}");
                writer.WriteLine($"Config Type: {guidance.ConfigType}");
                
                foreach (var issue in guidance.Issues)
                {
                    writer.WriteLine();
                    writer.WriteLine($"  Issue: {issue.Issue}");
                    writer.WriteLine($"  Section: {issue.Section}");
                    writer.WriteLine("  Migration Steps:");
                    foreach (var step in issue.MigrationSteps)
                    {
                        writer.WriteLine($"    - {step}");
                    }
                    
                    if (!string.IsNullOrEmpty(issue.CodeExample))
                    {
                        writer.WriteLine("  Example:");
                        foreach (var line in issue.CodeExample.Split('\n'))
                        {
                            writer.WriteLine($"    {line}");
                        }
                    }
                }
            }
        }
    }
}