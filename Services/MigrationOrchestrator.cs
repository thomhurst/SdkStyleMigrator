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
    private readonly IPackageVersionConflictResolver _packageVersionConflictResolver;
    private readonly IConfigurationFileGenerator _configurationFileGenerator;
    private readonly IImportScanner _importScanner;
    private readonly ITargetScanner _targetScanner;
    private readonly IUserInteractionService _userInteractionService;
    private readonly IWebProjectHandler _webProjectHandler;
    private readonly IPackageVersionCache? _packageCache;
    private ImportScanResult? _importScanResult;
    private TargetScanResult? _targetScanResult;

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
        IPackageVersionConflictResolver packageVersionConflictResolver,
        IConfigurationFileGenerator configurationFileGenerator,
        IImportScanner importScanner,
        ITargetScanner targetScanner,
        IUserInteractionService userInteractionService,
        IWebProjectHandler webProjectHandler,
        IPackageVersionCache? packageCache = null)
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
        _packageVersionConflictResolver = packageVersionConflictResolver;
        _configurationFileGenerator = configurationFileGenerator;
        _importScanner = importScanner;
        _targetScanner = targetScanner;
        _userInteractionService = userInteractionService;
        _webProjectHandler = webProjectHandler;
        _packageCache = packageCache;
    }

    
    public async Task<MigrationReport> MigrateProjectsAsync(string directoryPath, MigrationOptions options, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CPM Debug: MigrateProjectsAsync called with EnableCentralPackageManagement = {EnableCpm}", options.EnableCentralPackageManagement);
        
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
            await _auditService.LogMigrationStartAsync(options, cancellationToken);

            // Initialize backup if enabled
            if (options.CreateBackup && !options.DryRun)
            {
                backupSession = await _backupService.InitializeBackupAsync(directoryPath, cancellationToken);
                _logger.LogInformation("Backup initialized with session ID: {SessionId}", backupSession.SessionId);
            }

            _logger.LogInformation("Starting migration process for directory: {DirectoryPath}", directoryPath);

            // Run pre-migration analysis if not in force mode
            if (!options.Force && !options.DryRun)
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
            
            // Apply project type filters
            if (options.ProjectTypeFilters != null)
            {
                var filteredProjects = await FilterProjectsByTypeAsync(projectFilesList, options.ProjectTypeFilters, cancellationToken);
                var excludedCount = projectFilesList.Count - filteredProjects.Count;
                
                if (excludedCount > 0)
                {
                    _logger.LogInformation("Excluded {Count} projects based on type filters", excludedCount);
                    projectFilesList = filteredProjects;
                    report.TotalProjectsFound = projectFilesList.Count;
                }
            }

            // Scan imports if interactive mode is enabled
            _logger.LogInformation("Checking interactive import selection - Enabled: {InteractiveImportSelection}, ProjectCount: {ProjectCount}", 
                options.InteractiveImportSelection, projectFilesList.Count);
            
            if (options.InteractiveImportSelection && projectFilesList.Count > 0)
            {
                _logger.LogInformation("Scanning project imports for interactive selection...");
                
                // Scan imports by reading project XML directly (to avoid missing imports due to validation errors)  
                _importScanResult = await _importScanner.ScanProjectFileImportsAsync(projectFilesList, cancellationToken);
                
                _logger.LogInformation("Import scan result - HasCustomImports: {HasCustomImports}, TotalImports: {TotalImports}", 
                    _importScanResult.HasCustomImports, _importScanResult.TotalImports);
                
                if (_importScanResult.HasCustomImports)
                {
                    _logger.LogInformation("About to call SelectImportsAsync");
                    _importScanResult = await _userInteractionService.SelectImportsAsync(
                        _importScanResult, 
                        options.ImportOptions, 
                        cancellationToken);
                    
                    _logger.LogInformation("Import selection complete. {SelectedCount}/{TotalCount} imports will be kept",
                        _importScanResult.SelectedImports, _importScanResult.TotalImports);
                }
                else
                {
                    _logger.LogInformation("No custom imports found to select");
                }
            }

            // Scan targets if interactive mode is enabled
            _logger.LogInformation("Checking interactive target selection - Enabled: {InteractiveTargetSelection}, ProjectCount: {ProjectCount}", 
                options.InteractiveTargetSelection, projectFilesList.Count);
            
            if (options.InteractiveTargetSelection && projectFilesList.Count > 0)
            {
                _logger.LogInformation("Scanning project targets for interactive selection...");
                
                // Scan targets by reading project XML directly (to avoid missing targets due to validation errors)
                _targetScanResult = await _targetScanner.ScanProjectFileTargetsAsync(projectFilesList, cancellationToken);
                
                _logger.LogInformation("Target scan result - HasCustomTargets: {HasCustomTargets}, TotalTargets: {TotalTargets}", 
                    _targetScanResult.HasCustomTargets, _targetScanResult.TotalTargets);
                
                if (_targetScanResult.HasCustomTargets)
                {
                    _logger.LogInformation("About to call SelectTargetsAsync");
                    _targetScanResult = await _userInteractionService.SelectTargetsAsync(
                        _targetScanResult, 
                        options.TargetOptions, 
                        cancellationToken);
                    
                    _logger.LogInformation("Target selection complete. {SelectedCount}/{TotalCount} targets will be kept",
                        _targetScanResult.SelectedTargets, _targetScanResult.TotalTargets);
                }
                else
                {
                    _logger.LogInformation("No custom targets found to select");
                }
            }

            var projectAssemblyProperties = new System.Collections.Concurrent.ConcurrentDictionary<string, AssemblyProperties>();
            var projectMappings = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var projectIndex = 0;
            var totalProjects = projectFilesList.Count;

            if (options.MaxDegreeOfParallelism > 1)
            {
                _logger.LogInformation("Processing projects in parallel with max degree of parallelism: {MaxDegree}", options.MaxDegreeOfParallelism);

                var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
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
                        await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, projectCleanupInfo, options, cancellationToken);
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

                    await ProcessProjectAsync(projectFile, progress, projectAssemblyProperties, projectMappings, report, backupSession, projectCleanupInfo, options, cancellationToken);
                }
            }

            if (projectAssemblyProperties.Any())
            {
                var outputDir = !string.IsNullOrEmpty(options.OutputDirectory)
                    ? options.OutputDirectory
                    : directoryPath;

                await _directoryBuildPropsGenerator.GenerateDirectoryBuildPropsAsync(
                    outputDir, projectAssemblyProperties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), cancellationToken);
            }

            // Detect and resolve package version conflicts across all projects
            if (report.Results.Any(r => r.Success))
            {
                await ResolvePackageVersionConflictsAsync(report, options, cancellationToken);
            }

            // Generate Central Package Management configuration if enabled
            _logger.LogInformation("CPM Debug: EnableCentralPackageManagement = {EnableCPM}, SuccessfulResults = {SuccessCount}", 
                options.EnableCentralPackageManagement, report.Results.Count(r => r.Success));
            
            if (options.EnableCentralPackageManagement && report.Results.Any(r => r.Success))
            {
                _logger.LogInformation("Generating Central Package Management configuration...");

                var outputDir = !string.IsNullOrEmpty(options.OutputDirectory)
                    ? options.OutputDirectory
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
                    if (!options.DryRun)
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
            if (!options.DryRun && projectCleanupInfo.Any())
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

                    // Additional cleanup: packages.config files
                    try
                    {
                        var packagesConfigCleaned = await _localPackageFilesCleaner.CleanPackagesConfigAsync(
                            projectDir,
                            true, // Migration was successful if we reached this point
                            cancellationToken);

                        if (packagesConfigCleaned)
                        {
                            _logger.LogDebug("Successfully cleaned packages.config in {ProjectDir}", projectDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean packages.config for {ProjectDir}", projectDir);
                        cleanupErrors.Add($"{projectDir} (packages.config): {ex.Message}");
                    }

                    // Additional cleanup: legacy project artifacts
                    try
                    {
                        var artifactCleanupResult = await _localPackageFilesCleaner.CleanLegacyProjectArtifactsAsync(
                            projectDir,
                            true, // Assume AssemblyInfo was migrated (could be enhanced to track this per project)
                            cancellationToken);

                        if (artifactCleanupResult.Success && artifactCleanupResult.CleanedFiles.Any())
                        {
                            totalCleanedFiles += artifactCleanupResult.CleanedFiles.Count;
                            totalBytesFreed += artifactCleanupResult.TotalBytesFreed;
                            _logger.LogDebug("Cleaned {Count} legacy artifacts in {ProjectDir}",
                                artifactCleanupResult.CleanedFiles.Count, projectDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean legacy artifacts for {ProjectDir}", projectDir);
                        cleanupErrors.Add($"{projectDir} (artifacts): {ex.Message}");
                    }

                    // Additional cleanup: configuration transformation files
                    try
                    {
                        var configCleanupResult = await _localPackageFilesCleaner.CleanConfigTransformationFilesAsync(
                            projectDir,
                            cancellationToken);

                        if (configCleanupResult.Success && configCleanupResult.CleanedFiles.Any())
                        {
                            totalCleanedFiles += configCleanupResult.CleanedFiles.Count;
                            totalBytesFreed += configCleanupResult.TotalBytesFreed;
                            _logger.LogDebug("Cleaned {Count} config transformation files in {ProjectDir}",
                                configCleanupResult.CleanedFiles.Count, projectDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean config transformations for {ProjectDir}", projectDir);
                        cleanupErrors.Add($"{projectDir} (config): {ex.Message}");
                    }
                }

                if (totalCleanedFiles > 0)
                {
                    _logger.LogInformation("Total cleanup: Cleaned {Count} files (packages, configs, legacy artifacts), freed {Size:N0} bytes",
                        totalCleanedFiles, totalBytesFreed);
                }

                if (cleanupErrors.Any())
                {
                    _logger.LogWarning("Post-migration cleanup encountered {Count} errors", cleanupErrors.Count);
                    foreach (var error in cleanupErrors.Take(10)) // Limit error output
                    {
                        _logger.LogWarning("  - {Error}", error);
                    }
                }
            }

            // Clean packages folder if all projects have been migrated
            if (report.TotalProjectsMigrated > 0 && report.TotalProjectsFailed == 0 && !options.DryRun)
            {
                _logger.LogInformation("All projects migrated successfully. Checking if packages folder can be cleaned...");
                var packagesCleanResult = await _localPackageFilesCleaner.CleanPackagesFolderAsync(directoryPath, cancellationToken);
                if (packagesCleanResult)
                {
                    _logger.LogInformation("Successfully cleaned packages folder");
                }

                // Clean solution-level GlobalAssemblyInfo files
                await CleanSolutionLevelAssemblyInfoFilesAsync(directoryPath, backupSession, options, cancellationToken);
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
            if (report.Results.Any(r => r.Success) && !options.DryRun)
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
            LogReport(report, options);

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

            // Log cache statistics if available
            if (_packageCache != null && !options.DisableCache)
            {
                var stats = _packageCache.GetStatistics();
                _logger.LogInformation(
                    "Package cache statistics - Total entries: {TotalEntries}, Hit rate: {HitRate:F1}%, " +
                    "Version hits: {VersionHits}, Resolution hits: {ResolutionHits}, Dependency hits: {DependencyHits}",
                    stats.TotalEntries, stats.HitRate, stats.VersionCacheHits,
                    stats.ResolutionCacheHits, stats.DependencyCacheHits);
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
        MigrationOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedProject = await _projectParser.ParseProjectAsync(projectFile, cancellationToken);
            var project = parsedProject.Project;

            if (!_projectParser.IsLegacyProject(project))
            {
                _logger.LogInformation("{Progress} Skipping {ProjectPath} - already SDK-style", progress, projectFile);

                // Add to report as successful but skipped
                var skippedResult = new MigrationResult
                {
                    ProjectPath = projectFile,
                    OutputPath = projectFile,
                    Success = true,
                    Warnings = { "Project is already in SDK-style format - no migration needed" }
                };

                lock (report)
                {
                    report.Results.Add(skippedResult);
                    report.TotalProjectsMigrated++; // Count as migrated since it's already in the desired format
                }

                return;
            }

            _logger.LogInformation("{Progress} Processing {ProjectPath}", progress, projectFile);

            var projectDir = Path.GetDirectoryName(projectFile)!;
            
            // Perform web project analysis
            WebMigrationAnalysis? webAnalysis = null;
            try
            {
                webAnalysis = await _webProjectHandler.AnalyzeWebProjectAsync(projectFile, cancellationToken);
                if (webAnalysis.IsWebProject)
                {
                    _logger.LogInformation("{Progress} Web project detected: {ProjectType} with {PatternCount} patterns", 
                        progress, webAnalysis.ProjectType, webAnalysis.DetectedPatterns.Count);
                    
                    if (webAnalysis.Complexity.OverallComplexity == MigrationRiskLevel.High)
                    {
                        _logger.LogWarning("{Progress} High complexity web project - estimated migration time: {EstimatedTime}", 
                            progress, webAnalysis.Complexity.EstimatedMigrationTime);
                    }
                    
                    // Log web-specific recommendations
                    foreach (var recommendation in webAnalysis.Recommendations.Take(3))
                    {
                        _logger.LogInformation("{Progress} Web migration note: {Title} - {Description}", 
                            progress, recommendation.Title, recommendation.Description);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Progress} Error during web project analysis: {Error}", progress, ex.Message);
            }
            
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

            var outputPath = await GenerateOutputPathAsync(projectFile, options, cancellationToken);

            // Set import and target scan results if available
            _sdkStyleProjectGenerator.SetImportScanResult(_importScanResult);
            _sdkStyleProjectGenerator.SetTargetScanResult(_targetScanResult);
            _sdkStyleProjectGenerator.SetCentralPackageManagementEnabled(options.EnableCentralPackageManagement);
            
            var result = await _sdkStyleProjectGenerator.GenerateSdkStyleProjectAsync(
                project, outputPath, cancellationToken);

            // Add web analysis to result if available
            if (webAnalysis != null)
            {
                result.WebProjectAnalysis = webAnalysis;
                
                // Add web-specific warnings based on complexity
                if (webAnalysis.IsWebProject && webAnalysis.Complexity.OverallComplexity == MigrationRiskLevel.High)
                {
                    result.Warnings.Add($"High complexity web project detected - manual review recommended for {webAnalysis.DetectedPatterns.Count} web patterns");
                }
                
                // Add specific pattern warnings
                foreach (var pattern in webAnalysis.DetectedPatterns.Where(p => p.Risk == MigrationRiskLevel.High))
                {
                    result.Warnings.Add($"High-risk web pattern: {pattern.Description} - {string.Join(", ", pattern.MigrationNotes.Take(2))}");
                }
            }

            if (parsedProject.LoadedWithDefensiveParsing)
            {
                result.LoadedWithDefensiveParsing = true;
                result.Warnings.Add("Project was loaded with defensive parsing due to invalid imports. Some imports were removed automatically.");
                foreach (var removedImport in parsedProject.RemovedImports)
                {
                    result.Warnings.Add($"Removed import: {removedImport}");
                }
            }

            if (result.Success && !options.DryRun)
            {
                await RemoveAssemblyInfoFilesAsync(projectDir, backupSession, options, cancellationToken);
                await HandleAppConfigFileAsync(projectDir, result, options, cancellationToken);
                await HandleNuSpecFileAsync(projectDir, result, backupSession, options, cancellationToken);

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
                // Always add successful migrations for packages.config cleanup
                projectCleanupInfo.Add((projectDir, result.MigratedPackages, result.ConvertedHintPaths));
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

    private async Task RemoveAssemblyInfoFilesAsync(string projectDirectory, BackupSession? backupSession, MigrationOptions options, CancellationToken cancellationToken)
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
                    if (!options.DryRun)
                    {
                        var beforeHash = await FileHashCalculator.CalculateHashAsync(file, cancellationToken);
                        var fileSize = new FileInfo(file).Length;

                        if (options.CreateBackup && backupSession != null)
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
                            options.CreateBackup ? $" (backup: {file}.legacy)" : "");
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

    private async Task ResolvePackageVersionConflictsAsync(MigrationReport report, MigrationOptions options, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for package version conflicts across projects...");
        
        // Collect all packages from successful migrations
        var packagesByProject = new Dictionary<string, List<ProjectPackageReference>>();
        
        foreach (var result in report.Results.Where(r => r.Success))
        {
            if (!string.IsNullOrEmpty(result.OutputPath))
            {
                var projectPackages = new List<ProjectPackageReference>();
                
                // Add migrated packages
                foreach (var package in result.MigratedPackages)
                {
                    projectPackages.Add(new ProjectPackageReference
                    {
                        ProjectPath = result.OutputPath,
                        PackageId = package.PackageId,
                        Version = package.Version,
                        IsTransitive = package.IsTransitive
                    });
                }
                
                packagesByProject[result.OutputPath] = projectPackages;
            }
        }
        
        if (!packagesByProject.Any())
        {
            return;
        }
        
        // Detect conflicts
        var conflicts = _packageVersionConflictResolver.DetectConflicts(packagesByProject);
        
        if (!conflicts.Any())
        {
            _logger.LogInformation("No package version conflicts detected");
            return;
        }
        
        _logger.LogWarning("Found {Count} package version conflicts", conflicts.Count);
        
        foreach (var conflict in conflicts)
        {
            var versions = string.Join(", ", conflict.RequestedVersions.Select(v => v.Version).Distinct());
            _logger.LogWarning("Package {PackageId} has conflicting versions: {Versions}", 
                conflict.PackageId, versions);
        }
        
        // Resolve conflicts using configured strategy
        var strategy = options.EnableCentralPackageManagement 
            ? ConflictResolutionStrategy.UseHighest  // For CPM, use highest version
            : ConflictResolutionStrategy.UseMostCommon; // Otherwise, use most common
            
        var resolution = await _packageVersionConflictResolver.ResolveConflictsAsync(
            conflicts, strategy, cancellationToken);
        
        if (resolution.ProjectsNeedingUpdate.Any())
        {
            _logger.LogInformation("Resolved {Count} package version conflicts", 
                resolution.ProjectsNeedingUpdate.Count);
            
            // Update the migration results with resolved versions
            foreach (var update in resolution.ProjectsNeedingUpdate)
            {
                var result = report.Results.FirstOrDefault(r => 
                    r.OutputPath?.Equals(update.ProjectPath, StringComparison.OrdinalIgnoreCase) == true);
                    
                if (result != null)
                {
                    var package = result.MigratedPackages.FirstOrDefault(p => 
                        p.PackageId.Equals(update.PackageId, StringComparison.OrdinalIgnoreCase));
                        
                    if (package != null)
                    {
                        package.Version = update.NewVersion;
                        result.Warnings.Add($"Updated {update.PackageId} version from {update.OldVersion} to {update.NewVersion} to resolve conflict");
                    }
                }
            }
            
            // If not in dry run mode, update the actual project files
            if (!options.DryRun)
            {
                await UpdateProjectFilesWithResolvedVersionsAsync(resolution, cancellationToken);
            }
        }
    }

    private async Task UpdateProjectFilesWithResolvedVersionsAsync(
        PackageVersionResolution resolution, 
        CancellationToken cancellationToken)
    {
        foreach (var update in resolution.ProjectsNeedingUpdate)
        {
            try
            {
                var doc = XDocument.Load(update.ProjectPath);
                var packageRefs = doc.Descendants("PackageReference")
                    .Where(e => e.Attribute("Include")?.Value.Equals(update.PackageId, 
                        StringComparison.OrdinalIgnoreCase) == true);
                
                foreach (var packageRef in packageRefs)
                {
                    var versionAttr = packageRef.Attribute("Version");
                    if (versionAttr != null)
                    {
                        versionAttr.Value = update.NewVersion;
                    }
                    else
                    {
                        packageRef.Add(new XAttribute("Version", update.NewVersion));
                    }
                }
                
                doc.Save(update.ProjectPath);
                _logger.LogInformation("Updated {PackageId} to {Version} in {Project}",
                    update.PackageId, update.NewVersion, Path.GetFileName(update.ProjectPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update package version in {Project}", update.ProjectPath);
            }
        }
    }

    private async Task CleanSolutionLevelAssemblyInfoFilesAsync(string solutionDirectory, BackupSession? backupSession, MigrationOptions options, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning solution-level AssemblyInfo files...");

        // GlobalAssemblyInfo and SharedAssemblyInfo patterns specifically
        var solutionLevelPatterns = new[]
        {
            "GlobalAssemblyInfo.cs",
            "GlobalAssemblyInfo.vb",
            "SharedAssemblyInfo.cs",
            "SharedAssemblyInfo.vb",
            "CommonAssemblyInfo.cs",
            "CommonAssemblyInfo.vb",
            "SolutionInfo.cs",
            "SolutionInfo.vb"
        };

        foreach (var pattern in solutionLevelPatterns)
        {
            // Search in solution root and common directories
            var searchPaths = new[]
            {
                solutionDirectory,
                Path.Combine(solutionDirectory, "src"),
                Path.Combine(solutionDirectory, "source"),
                Path.Combine(solutionDirectory, "Solution Items"),
                Path.Combine(solutionDirectory, "SolutionItems"),
                Path.Combine(solutionDirectory, "Common"),
                Path.Combine(solutionDirectory, "Shared")
            };

            foreach (var searchPath in searchPaths.Where(Directory.Exists))
            {
                try
                {
                    var files = Directory.GetFiles(searchPath, pattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        try
                        {
                            // Check if this file is still referenced by any project
                            if (await IsAssemblyInfoStillReferencedAsync(file, solutionDirectory, cancellationToken))
                            {
                                _logger.LogWarning("GlobalAssemblyInfo file {File} is still referenced by projects, skipping removal", file);
                                continue;
                            }

                            if (!options.DryRun)
                            {
                                var beforeHash = await FileHashCalculator.CalculateHashAsync(file, cancellationToken);
                                var fileSize = new FileInfo(file).Length;

                                if (options.CreateBackup && backupSession != null)
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
                                    DeletionReason = "Solution-level AssemblyInfo no longer needed with SDK-style projects"
                                }, cancellationToken);

                                _logger.LogInformation("Removed solution-level AssemblyInfo file: {File}", file);
                            }
                            else
                            {
                                _logger.LogInformation("[DRY RUN] Would remove solution-level AssemblyInfo file: {File}", file);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to remove solution-level AssemblyInfo file: {File}", file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to search for AssemblyInfo files in: {Path}", searchPath);
                }
            }
        }
    }

    private async Task<bool> IsAssemblyInfoStillReferencedAsync(string assemblyInfoFile, string solutionDirectory, CancellationToken cancellationToken)
    {
        // Check if any .csproj files still reference this assembly info file
        var projectFiles = await _projectFileScanner.ScanForProjectFilesAsync(solutionDirectory, cancellationToken);
        var relativePath = Path.GetRelativePath(solutionDirectory, assemblyInfoFile);

        foreach (var projectFile in projectFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(projectFile, cancellationToken);
                // Check for both Compile Include and Link references
                if (content.Contains(assemblyInfoFile, StringComparison.OrdinalIgnoreCase) ||
                    content.Contains(relativePath, StringComparison.OrdinalIgnoreCase) ||
                    content.Contains(Path.GetFileName(assemblyInfoFile), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check project {Project} for AssemblyInfo references", projectFile);
            }
        }

        return false;
    }

    private async Task HandleAppConfigFileAsync(string projectDirectory, MigrationResult result, MigrationOptions options, CancellationToken cancellationToken)
    {
        var appConfigPath = Path.Combine(projectDirectory, "app.config");
        if (!File.Exists(appConfigPath))
        {
            appConfigPath = Path.Combine(projectDirectory, "App.config");
            if (!File.Exists(appConfigPath))
            {
                // Also check for web.config
                var webConfigPath = Path.Combine(projectDirectory, "web.config");
                if (!File.Exists(webConfigPath))
                {
                    webConfigPath = Path.Combine(projectDirectory, "Web.config");
                    if (!File.Exists(webConfigPath))
                        return;
                }
                appConfigPath = webConfigPath;
            }
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

            // Check if app.config has any other meaningful content
            var hasOtherContent = configuration.Elements()
                .Any(e => e.Name != "runtime" ||
                         (e.Name == "runtime" && e.Elements().Any()));

            // Generate appsettings.json and migration code if there's meaningful content
            var targetFramework = result.TargetFrameworks?.FirstOrDefault() ?? "net472";
            bool generatedAppSettings = false;
            bool generatedMigrationCode = false;

            if (hasOtherContent)
            {
                // Try to generate appsettings.json from the configuration
                generatedAppSettings = await _configurationFileGenerator.GenerateAppSettingsFromConfigAsync(
                    appConfigPath, projectDirectory, cancellationToken);

                // Generate migration code examples
                generatedMigrationCode = await _configurationFileGenerator.GenerateStartupMigrationCodeAsync(
                    projectDirectory, targetFramework, cancellationToken);

                if (generatedAppSettings)
                {
                    result.GeneratedFiles.Add("appsettings.json");
                    result.Warnings.Add("Generated appsettings.json from configuration file. Review and update as needed.");
                }

                if (generatedMigrationCode)
                {
                    result.GeneratedFiles.Add("ConfigurationMigration.cs");
                    result.Warnings.Add("Generated configuration migration code. Review ConfigurationMigration.cs for integration steps.");
                }
            }

            if (!hasOtherContent)
            {
                // App.config only contained binding redirects, so we can remove it
                if (options.CreateBackup)
                {
                    var backupPath = $"{appConfigPath}.legacy";
                    File.Copy(appConfigPath, backupPath, overwrite: true);
                }

                File.Delete(appConfigPath);
                result.RemovedElements.Add($"Configuration file (contained only binding redirects)");
                _logger.LogInformation("Removed {File} as it only contained binding redirects", appConfigPath);

                // Also remove any references to app.config from the project file
                await RemoveAppConfigFromProjectFileAsync(projectDirectory, appConfigPath, cancellationToken);
            }
            else if (targetFramework.StartsWith("net5") || targetFramework.StartsWith("net6") ||
                     targetFramework.StartsWith("net7") || targetFramework.StartsWith("net8") ||
                     targetFramework.Contains("netcore"))
            {
                // For .NET Core/5+ projects, recommend removing app.config in favor of appsettings.json
                if (generatedAppSettings)
                {
                    result.Warnings.Add($"Consider removing {Path.GetFileName(appConfigPath)} in favor of appsettings.json for .NET {targetFramework.Replace("net", "")}+ projects.");
                }
                else
                {
                    // Save the modified app.config without binding redirects
                    await File.WriteAllTextAsync(appConfigPath, doc.ToString(), cancellationToken);
                    _logger.LogInformation("Updated {File} - removed binding redirects", appConfigPath);
                }
            }
            else
            {
                // For .NET Framework projects, keep the modified app.config
                await File.WriteAllTextAsync(appConfigPath, doc.ToString(), cancellationToken);
                _logger.LogInformation("Updated {File} - removed binding redirects", appConfigPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process configuration file: {File}", appConfigPath);
        }
    }

    private async Task RemoveAppConfigFromProjectFileAsync(string projectDirectory, string appConfigPath, CancellationToken cancellationToken)
    {
        try
        {
            // Find the project file
            var projectFiles = Directory.GetFiles(projectDirectory, "*.csproj")
                .Concat(Directory.GetFiles(projectDirectory, "*.vbproj"))
                .Concat(Directory.GetFiles(projectDirectory, "*.fsproj"))
                .ToList();

            foreach (var projectFile in projectFiles)
            {
                var projectContent = await File.ReadAllTextAsync(projectFile, cancellationToken);
                var projectDoc = System.Xml.Linq.XDocument.Parse(projectContent);

                bool modified = false;
                var appConfigFileName = Path.GetFileName(appConfigPath);

                // Remove any references to app.config from the project file
                var itemsToRemove = projectDoc.Descendants()
                    .Where(e => e.Attribute("Include")?.Value == appConfigFileName ||
                               e.Attribute("Include")?.Value == $".\\{appConfigFileName}" ||
                               e.Attribute("Include")?.Value?.EndsWith($"\\{appConfigFileName}") == true)
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    _logger.LogDebug("Removing reference to {AppConfig} from project file {ProjectFile}",
                        appConfigFileName, projectFile);
                    item.Remove();
                    modified = true;
                }

                // Remove empty ItemGroup elements
                var emptyItemGroups = projectDoc.Descendants("ItemGroup")
                    .Where(ig => !ig.HasElements)
                    .ToList();

                foreach (var emptyGroup in emptyItemGroups)
                {
                    emptyGroup.Remove();
                    modified = true;
                }

                if (modified)
                {
                    await File.WriteAllTextAsync(projectFile, projectDoc.ToString(), cancellationToken);
                    _logger.LogInformation("Removed references to {AppConfig} from project file {ProjectFile}",
                        appConfigFileName, projectFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove app.config references from project file in {Directory}", projectDirectory);
        }
    }

    private async Task<string> GenerateOutputPathAsync(string projectFile, MigrationOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.OutputDirectory))
        {
            var relativePath = Path.GetRelativePath(options.DirectoryPath, projectFile);
            var outputPath = Path.Combine(options.OutputDirectory, relativePath);

            if (!options.DryRun)
            {
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
            }

            return outputPath;
        }

        if (options.CreateBackup && !options.DryRun)
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

    private async Task HandleNuSpecFileAsync(string projectDirectory, MigrationResult result, BackupSession? backupSession, MigrationOptions options, CancellationToken cancellationToken)
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

                        if (options.CreateBackup && backupSession != null)
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

    private void LogReport(MigrationReport report, MigrationOptions options)
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

        if (!options.DryRun)
        {
            var reportPath = Path.Combine(Path.GetDirectoryName(report.Results.FirstOrDefault()?.ProjectPath ?? ".") ?? ".",
                $"migration-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            WriteDetailedReport(report, reportPath, options);
            _logger.LogInformation("");
            _logger.LogInformation("Detailed migration report written to: {Path}", reportPath);
        }
    }

    public async Task<MigrationAnalysis> AnalyzeProjectsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting pre-migration analysis for directory: {DirectoryPath}", directoryPath);
        return await _migrationAnalyzer.AnalyzeProjectsAsync(directoryPath, cancellationToken);
    }

    private void WriteDetailedReport(MigrationReport report, string reportPath, MigrationOptions options)
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
            var targetFramework = options.TargetFramework ?? "net8.0";
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

    private async Task<List<string>> FilterProjectsByTypeAsync(List<string> projectFiles, ProjectTypeFilters filters, CancellationToken cancellationToken)
    {
        var filteredProjects = new List<string>();
        
        foreach (var projectFile in projectFiles)
        {
            try
            {
                var projectType = await DetermineProjectTypeAsync(projectFile, cancellationToken);
                
                bool includeProject = projectType switch
                {
                    ProjectType.WinForms => filters.IncludeWinForms,
                    ProjectType.Wpf => filters.IncludeWpf,
                    ProjectType.Web => filters.IncludeWeb,
                    ProjectType.Test => filters.IncludeTest,
                    ProjectType.Console => filters.IncludeConsole,
                    ProjectType.ClassLibrary => filters.IncludeClassLibrary,
                    _ => true // Include unknown project types by default
                };
                
                if (includeProject)
                {
                    filteredProjects.Add(projectFile);
                }
                else
                {
                    _logger.LogDebug("Excluding project {Project} of type {Type} based on filters", 
                        Path.GetFileName(projectFile), projectType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine project type for {Project}, including by default", 
                    Path.GetFileName(projectFile));
                filteredProjects.Add(projectFile);
            }
        }
        
        return filteredProjects;
    }
    
    private async Task<ProjectType> DetermineProjectTypeAsync(string projectFile, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(projectFile, cancellationToken);
        var projectName = Path.GetFileNameWithoutExtension(projectFile).ToLowerInvariant();
        
        // Check for test projects
        if (projectName.Contains("test") || projectName.Contains("spec") || 
            content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("mstest", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectType.Test;
        }
        
        // Check for WinForms
        if (content.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<UseWindowsForms>true</UseWindowsForms>", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectType.WinForms;
        }
        
        // Check for WPF
        if (content.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectType.Wpf;
        }
        
        // Check for Web projects
        if (content.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase) || // Web Application GUID
            content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("System.Web", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectType.Web;
        }
        
        // Check for Console applications
        if (content.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectType.Console;
        }
        
        // Default to class library
        return ProjectType.ClassLibrary;
    }
    
    private enum ProjectType
    {
        ClassLibrary,
        Console,
        WinForms,
        Wpf,
        Web,
        Test
    }
}