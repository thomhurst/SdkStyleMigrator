using System.CommandLine;
using System.CommandLine.Invocation;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Services;

namespace SdkMigrator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        InitializeMSBuild();
        
        var rootCommand = new RootCommand("SDK Migrator - Migrate legacy MSBuild project files to SDK-style format");
        
        var directoryArgument = new Argument<string>(
            name: "directory",
            description: "The directory to scan for project files");
        
        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run", "-d" },
            description: "Preview changes without modifying files");
            
        var outputDirectoryOption = new Option<string?>(
            aliases: new[] { "--output-directory", "-o" },
            description: "Output directory for migrated projects");
            
        var targetFrameworkOption = new Option<string?>(
            aliases: new[] { "--target-framework", "-t" },
            description: "Override target framework (e.g., net8.0)");
            
        var targetFrameworksOption = new Option<string[]?>(
            aliases: new[] { "--target-frameworks", "-tf" },
            description: "Multi-targeting frameworks (e.g., net8.0 net472)")
        {
            AllowMultipleArgumentsPerToken = true
        };
            
        var centralPackageManagementOption = new Option<bool>(
            aliases: new[] { "--central-package-management", "-cpm" },
            description: "Enable Central Package Management (Directory.Packages.props)");
            
        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Force migration without prompts");
            
        var noBackupOption = new Option<bool>(
            aliases: new[] { "--no-backup" },
            description: "Skip creating backup files (not recommended)");
            
        var parallelOption = new Option<int?>(
            aliases: new[] { "--parallel", "-p" },
            description: "Enable parallel processing (n = max threads)");
            
        var logLevelOption = new Option<string>(
            aliases: new[] { "--log-level", "-l" },
            getDefaultValue: () => "Information",
            description: "Set log level (Trace|Debug|Information|Warning|Error)");
            
        var offlineOption = new Option<bool>(
            aliases: new[] { "--offline" },
            description: "Use hardcoded package versions instead of querying NuGet (for offline scenarios)");
            
        var nugetConfigOption = new Option<string?>(
            aliases: new[] { "--nuget-config", "-n" },
            description: "Path to a specific NuGet.config file to use for package sources");
            
        // Add migrate command as the default behavior
        rootCommand.AddArgument(directoryArgument);
        rootCommand.AddOption(dryRunOption);
        rootCommand.AddOption(outputDirectoryOption);
        rootCommand.AddOption(targetFrameworkOption);
        rootCommand.AddOption(targetFrameworksOption);
        rootCommand.AddOption(centralPackageManagementOption);
        rootCommand.AddOption(forceOption);
        rootCommand.AddOption(noBackupOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(offlineOption);
        rootCommand.AddOption(nugetConfigOption);
        
        // Rollback command
        var rollbackCommand = new Command("rollback", "Rollback a previous migration using backup session");
        var sessionIdOption = new Option<string?>(
            aliases: new[] { "--session-id", "-s" },
            description: "Backup session ID to rollback (defaults to latest)");
        var rollbackDirectoryArgument = new Argument<string>(
            name: "directory",
            description: "The directory containing the backup to rollback");
        
        rollbackCommand.AddArgument(rollbackDirectoryArgument);
        rollbackCommand.AddOption(sessionIdOption);
        rollbackCommand.AddOption(logLevelOption);
        
        rollbackCommand.SetHandler(async (InvocationContext context) =>
        {
            var directory = context.ParseResult.GetValueForArgument(rollbackDirectoryArgument);
            var sessionId = context.ParseResult.GetValueForOption(sessionIdOption);
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information";
            
            var options = new MigrationOptions
            {
                DirectoryPath = Path.GetFullPath(directory),
                LogLevel = logLevel
            };
            
            var exitCode = await RunRollback(options, sessionId);
            context.ExitCode = exitCode;
        });
        
        rootCommand.AddCommand(rollbackCommand);
        
        // Analyze command
        var analyzeCommand = new Command("analyze", "Analyze projects for migration readiness without making changes");
        var analyzeDirectoryArgument = new Argument<string>(
            name: "directory",
            description: "The directory to analyze for migration readiness");
        
        analyzeCommand.AddArgument(analyzeDirectoryArgument);
        analyzeCommand.AddOption(logLevelOption);
        
        analyzeCommand.SetHandler(async (InvocationContext context) =>
        {
            var directory = context.ParseResult.GetValueForArgument(analyzeDirectoryArgument);
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information";
            
            var options = new MigrationOptions
            {
                DirectoryPath = Path.GetFullPath(directory),
                LogLevel = logLevel
            };
            
            var exitCode = await RunAnalysis(options);
            context.ExitCode = exitCode;
        });
        
        rootCommand.AddCommand(analyzeCommand);
        
        // Clean-deps command - Remove transitive dependencies
        var cleanDepsCommand = new Command("clean-deps", "Remove transitive package dependencies from SDK-style projects");
        var cleanDepsDirectoryArgument = new Argument<string>(
            name: "directory",
            description: "The directory to scan for project files");
        var cleanDepsBackupOption = new Option<bool>(
            aliases: new[] { "--backup", "-b" },
            getDefaultValue: () => true,
            description: "Create backup files before modifying");
        var cleanDepsDryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run", "-d" },
            description: "Preview changes without modifying files");
        
        cleanDepsCommand.AddArgument(cleanDepsDirectoryArgument);
        cleanDepsCommand.AddOption(cleanDepsBackupOption);
        cleanDepsCommand.AddOption(cleanDepsDryRunOption);
        cleanDepsCommand.AddOption(logLevelOption);
        cleanDepsCommand.AddOption(parallelOption);
        cleanDepsCommand.AddOption(offlineOption);
        cleanDepsCommand.AddOption(nugetConfigOption);
        
        cleanDepsCommand.SetHandler(async (InvocationContext context) =>
        {
            var directory = context.ParseResult.GetValueForArgument(cleanDepsDirectoryArgument);
            var backup = context.ParseResult.GetValueForOption(cleanDepsBackupOption);
            var dryRun = context.ParseResult.GetValueForOption(cleanDepsDryRunOption);
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information";
            var parallel = context.ParseResult.GetValueForOption(parallelOption) ?? 1;
            var offline = context.ParseResult.GetValueForOption(offlineOption);
            var nugetConfig = context.ParseResult.GetValueForOption(nugetConfigOption);
            
            var options = new MigrationOptions
            {
                DirectoryPath = Path.GetFullPath(directory),
                DryRun = dryRun,
                CreateBackup = backup,
                LogLevel = logLevel,
                MaxDegreeOfParallelism = parallel == 0 ? Environment.ProcessorCount : parallel,
                UseOfflineMode = offline,
                NuGetConfigPath = nugetConfig
            };
            
            var exitCode = await RunCleanDeps(options);
            context.ExitCode = exitCode;
        });
        
        rootCommand.AddCommand(cleanDepsCommand);
        
        // Clean-cpm command - Clean unused packages from Central Package Management
        var cleanCpmCommand = new Command("clean-cpm", "Remove unused packages from Directory.Packages.props");
        var cleanCpmDirectoryArgument = new Argument<string>(
            name: "directory",
            description: "The directory containing Directory.Packages.props");
        var cleanCpmDryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run", "-d" },
            description: "Preview changes without modifying files");
        
        cleanCpmCommand.AddArgument(cleanCpmDirectoryArgument);
        cleanCpmCommand.AddOption(cleanCpmDryRunOption);
        cleanCpmCommand.AddOption(logLevelOption);
        
        cleanCpmCommand.SetHandler(async (InvocationContext context) =>
        {
            var directory = context.ParseResult.GetValueForArgument(cleanCpmDirectoryArgument);
            var dryRun = context.ParseResult.GetValueForOption(cleanCpmDryRunOption);
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information";
            
            var options = new MigrationOptions
            {
                DirectoryPath = Path.GetFullPath(directory),
                DryRun = dryRun,
                LogLevel = logLevel
            };
            
            var exitCode = await RunCleanCpm(options);
            context.ExitCode = exitCode;
        });
        
        rootCommand.AddCommand(cleanCpmCommand);
        
        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var options = new MigrationOptions
            {
                DirectoryPath = context.ParseResult.GetValueForArgument(directoryArgument),
                DryRun = context.ParseResult.GetValueForOption(dryRunOption),
                OutputDirectory = context.ParseResult.GetValueForOption(outputDirectoryOption),
                TargetFramework = context.ParseResult.GetValueForOption(targetFrameworkOption),
                TargetFrameworks = context.ParseResult.GetValueForOption(targetFrameworksOption),
                EnableCentralPackageManagement = context.ParseResult.GetValueForOption(centralPackageManagementOption),
                Force = context.ParseResult.GetValueForOption(forceOption),
                CreateBackup = !context.ParseResult.GetValueForOption(noBackupOption),
                MaxDegreeOfParallelism = context.ParseResult.GetValueForOption(parallelOption) ?? 1,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information",
                UseOfflineMode = context.ParseResult.GetValueForOption(offlineOption),
                NuGetConfigPath = context.ParseResult.GetValueForOption(nugetConfigOption)
            };
            
            options.DirectoryPath = Path.GetFullPath(options.DirectoryPath);
            
            if (!Directory.Exists(options.DirectoryPath))
            {
                Console.Error.WriteLine($"Error: Directory '{options.DirectoryPath}' does not exist.");
                context.ExitCode = 1;
                return;
            }
            
            if (options.OutputDirectory != null)
            {
                options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            }
            
            if (options.NuGetConfigPath != null)
            {
                options.NuGetConfigPath = Path.GetFullPath(options.NuGetConfigPath);
                if (!File.Exists(options.NuGetConfigPath))
                {
                    Console.Error.WriteLine($"Error: NuGet config file '{options.NuGetConfigPath}' does not exist.");
                    context.ExitCode = 1;
                    return;
                }
            }
            
            if (options.MaxDegreeOfParallelism == 0)
            {
                options.MaxDegreeOfParallelism = Environment.ProcessorCount;
            }
            
            var exitCode = await RunMigration(options);
            context.ExitCode = exitCode;
        });
        
        rootCommand.Description = @"SDK Migrator - Migrate legacy MSBuild project files to SDK-style format

This tool scans the specified directory and all subdirectories for
legacy MSBuild project files (.csproj, .vbproj, .fsproj) and migrates
them to the new SDK-style format.

The tool will:
- Remove unnecessary legacy properties and imports
- Convert packages.config to PackageReference
- Detect and remove transitive package dependencies
- Extract assembly properties to Directory.Build.props
- Remove AssemblyInfo files and enable SDK auto-generation
- Create centralized backup with manifest for safe rollback
- Maintain feature parity with the original project

Commands:
  migrate (default)  Migrate legacy projects to SDK-style format
  analyze           Analyze projects for migration readiness
  rollback          Rollback a previous migration using backup session
  clean-deps        Remove transitive package dependencies from SDK-style projects
  clean-cpm         Remove unused packages from Directory.Packages.props

Examples:
  SdkMigrator ./src
  SdkMigrator ./src --dry-run
  SdkMigrator ./src -o ./src-migrated -t net8.0
  SdkMigrator ./src --parallel 4 --log-level Debug
  SdkMigrator ./src --nuget-config ./custom-nuget.config
  SdkMigrator analyze ./src
  SdkMigrator rollback ./src
  SdkMigrator rollback ./src --session-id 20250119_120000
  SdkMigrator clean-deps ./src --dry-run
  SdkMigrator clean-cpm ./src";
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task<int> RunAnalysis(MigrationOptions options)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, options);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var analyzer = serviceProvider.GetRequiredService<IMigrationAnalyzer>();
        
        try
        {
            logger.LogInformation("Starting migration analysis for directory: {Directory}", options.DirectoryPath);
            
            var analysis = await analyzer.AnalyzeProjectsAsync(options.DirectoryPath, CancellationToken.None);
            
            // Display analysis results
            Console.WriteLine();
            Console.WriteLine("Migration Analysis Report");
            Console.WriteLine("========================");
            Console.WriteLine($"Directory: {analysis.DirectoryPath}");
            Console.WriteLine($"Projects found: {analysis.ProjectAnalyses.Count}");
            Console.WriteLine($"Overall risk: {analysis.OverallRisk}");
            Console.WriteLine($"Estimated manual effort: {analysis.EstimatedManualEffortHours} hours");
            Console.WriteLine($"Can proceed automatically: {(analysis.CanProceedAutomatically ? "Yes" : "No")}");
            Console.WriteLine();
            
            // Show project-level details
            foreach (var project in analysis.ProjectAnalyses)
            {
                Console.WriteLine($"Project: {project.ProjectName}");
                Console.WriteLine($"  Type: {project.ProjectType}");
                Console.WriteLine($"  Risk: {project.RiskLevel}");
                Console.WriteLine($"  Can migrate: {(project.CanMigrate ? "Yes" : "No")}");
                
                if (project.Issues.Any())
                {
                    Console.WriteLine("  Issues:");
                    foreach (var issue in project.Issues.OrderByDescending(i => i.Severity))
                    {
                        var severityIcon = issue.Severity switch
                        {
                            MigrationIssueSeverity.Critical => "[CRITICAL]",
                            MigrationIssueSeverity.Error => "[ERROR]",
                            MigrationIssueSeverity.Warning => "[WARNING]",
                            _ => "[INFO]"
                        };
                        Console.WriteLine($"    {severityIcon} {issue.Description}");
                    }
                }
                
                if (project.CustomTargets.Any(t => !t.CanAutoMigrate))
                {
                    Console.WriteLine($"  Custom targets requiring review: {project.CustomTargets.Count(t => !t.CanAutoMigrate)}");
                }
                
                Console.WriteLine();
            }
            
            // Show global recommendations
            if (analysis.GlobalRecommendations.Any())
            {
                Console.WriteLine("Recommendations:");
                foreach (var rec in analysis.GlobalRecommendations)
                {
                    Console.WriteLine($"  - {rec}");
                }
            }
            
            // Generate detailed report file
            var reportPath = Path.Combine(options.DirectoryPath, $"migration-analysis-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            await WriteAnalysisReportAsync(analysis, reportPath);
            Console.WriteLine();
            Console.WriteLine($"Detailed analysis report written to: {reportPath}");
            
            // Return appropriate exit code
            if (!analysis.CanProceedAutomatically)
                return 1;
            if (analysis.OverallRisk >= MigrationRiskLevel.High)
                return 2;
            
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during analysis");
            return 1;
        }
    }
    
    static async Task WriteAnalysisReportAsync(MigrationAnalysis analysis, string reportPath)
    {
        using var writer = new StreamWriter(reportPath);
        
        await writer.WriteLineAsync("SDK Migration Analysis Report");
        await writer.WriteLineAsync($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync("=".PadRight(80, '='));
        await writer.WriteLineAsync();
        
        await writer.WriteLineAsync($"Directory: {analysis.DirectoryPath}");
        await writer.WriteLineAsync($"Projects analyzed: {analysis.ProjectAnalyses.Count}");
        await writer.WriteLineAsync($"Overall risk level: {analysis.OverallRisk}");
        await writer.WriteLineAsync($"Estimated manual effort: {analysis.EstimatedManualEffortHours} hours");
        await writer.WriteLineAsync($"Can proceed with automatic migration: {analysis.CanProceedAutomatically}");
        await writer.WriteLineAsync();
        
        foreach (var project in analysis.ProjectAnalyses)
        {
            await writer.WriteLineAsync($"Project: {project.ProjectPath}");
            await writer.WriteLineAsync($"  Name: {project.ProjectName}");
            await writer.WriteLineAsync($"  Type: {project.ProjectType}");
            await writer.WriteLineAsync($"  Target Framework: {project.CurrentTargetFramework}");
            await writer.WriteLineAsync($"  Risk Level: {project.RiskLevel}");
            await writer.WriteLineAsync($"  Can Migrate: {project.CanMigrate}");
            await writer.WriteLineAsync($"  Estimated Effort: {project.EstimatedManualEffortHours} hours");
            
            if (project.Issues.Any())
            {
                await writer.WriteLineAsync("  Issues:");
                foreach (var issue in project.Issues.OrderByDescending(i => i.Severity))
                {
                    await writer.WriteLineAsync($"    [{issue.Severity}] {issue.Category}: {issue.Description}");
                    if (!string.IsNullOrEmpty(issue.Resolution))
                    {
                        await writer.WriteLineAsync($"      Resolution: {issue.Resolution}");
                    }
                }
            }
            
            if (project.CustomTargets.Any())
            {
                await writer.WriteLineAsync($"  Custom MSBuild Targets ({project.CustomTargets.Count}):");
                foreach (var target in project.CustomTargets)
                {
                    await writer.WriteLineAsync($"    - {target.TargetName} (Complexity: {target.Complexity}, Auto-migrate: {target.CanAutoMigrate})");
                    if (!target.CanAutoMigrate && !string.IsNullOrEmpty(target.ManualMigrationGuidance))
                    {
                        await writer.WriteLineAsync($"      Guidance: {target.ManualMigrationGuidance}");
                    }
                }
            }
            
            if (project.ManualStepsRequired.Any())
            {
                await writer.WriteLineAsync("  Manual Steps Required:");
                foreach (var step in project.ManualStepsRequired)
                {
                    await writer.WriteLineAsync($"    - {step}");
                }
            }
            
            await writer.WriteLineAsync();
        }
        
        if (analysis.GlobalRecommendations.Any())
        {
            await writer.WriteLineAsync("Global Recommendations:");
            foreach (var rec in analysis.GlobalRecommendations)
            {
                await writer.WriteLineAsync($"  - {rec}");
            }
        }
    }
    
    static async Task<int> RunRollback(MigrationOptions options, string? sessionId)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, options);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var backupService = serviceProvider.GetRequiredService<IBackupService>();
        var auditService = serviceProvider.GetRequiredService<IAuditService>();
        
        try
        {
            logger.LogInformation("Starting rollback process for directory: {Directory}", options.DirectoryPath);
            
            BackupSession backupSession;
            if (string.IsNullOrEmpty(sessionId))
            {
                // Find the latest backup session
                var sessions = await backupService.ListBackupsAsync(options.DirectoryPath);
                var sessionsList = sessions.ToList();
                if (!sessionsList.Any())
                {
                    logger.LogError("No backup sessions found in {Directory}", options.DirectoryPath);
                    return 1;
                }
                
                backupSession = sessionsList.OrderByDescending(s => s.StartTime).First();
                logger.LogInformation("Using latest backup session: {SessionId} from {Timestamp}", 
                    backupSession.SessionId, backupSession.StartTime);
            }
            else
            {
                // Load specific session
                var session = await backupService.GetBackupSessionAsync(options.DirectoryPath, sessionId);
                if (session == null)
                {
                    logger.LogError("Backup session {SessionId} not found", sessionId);
                    return 1;
                }
                backupSession = session;
            }
            
            // Log rollback start
            await auditService.LogMigrationStartAsync(new MigrationOptions 
            { 
                DirectoryPath = options.DirectoryPath,
                LogLevel = options.LogLevel,
                // Mark as rollback operation
                DryRun = false,
                CreateBackup = false
            }, CancellationToken.None);
            
            logger.LogInformation("Rolling back {Count} files from session {SessionId}",
                backupSession.BackedUpFiles.Count, backupSession.SessionId);
            
            var rollbackResult = await backupService.RollbackAsync(backupSession.BackupDirectory, CancellationToken.None);
            var success = rollbackResult.Success;
            
            if (success)
            {
                logger.LogInformation("Rollback completed successfully");
                
                // Log successful rollback
                await auditService.LogMigrationEndAsync(new MigrationReport
                {
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    TotalProjectsFound = backupSession.BackedUpFiles.Count,
                    TotalProjectsMigrated = backupSession.BackedUpFiles.Count,
                    TotalProjectsFailed = 0
                }, CancellationToken.None);
                
                return 0;
            }
            else
            {
                logger.LogError("Rollback failed");
                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during rollback");
            await auditService.LogErrorAsync("Rollback", ex, CancellationToken.None);
            return 1;
        }
    }
    
    static async Task<int> RunMigration(MigrationOptions options)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, options);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (options.DryRun)
            {
                logger.LogWarning("DRY RUN MODE - No files will be modified");
                Console.WriteLine();
            }
            
            if (!options.DryRun && options.CreateBackup && string.IsNullOrEmpty(options.OutputDirectory))
            {
                logger.LogInformation("Backup files will be created with .legacy extension");
            }
            else if (!options.DryRun && !options.CreateBackup && string.IsNullOrEmpty(options.OutputDirectory))
            {
                logger.LogWarning("WARNING: Backup creation is disabled. Original files will be overwritten!");
            }
            
            logger.LogInformation("SDK Migrator - Starting migration process");
            
            var orchestrator = serviceProvider.GetRequiredService<IMigrationOrchestrator>();
            var cancellationTokenSource = new CancellationTokenSource();
            
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                logger.LogWarning("Cancellation requested...");
            };

            var report = await orchestrator.MigrateProjectsAsync(options.DirectoryPath, cancellationTokenSource.Token);
            
            Console.WriteLine();
            Console.WriteLine("Migration Summary:");
            Console.WriteLine($"  Total projects found: {report.TotalProjectsFound}");
            Console.WriteLine($"  Successfully migrated: {report.TotalProjectsMigrated}");
            Console.WriteLine($"  Failed: {report.TotalProjectsFailed}");
            Console.WriteLine($"  Duration: {report.Duration:mm\\:ss}");

            // Enhanced exit codes for dry-run mode
            if (options.DryRun)
            {
                // Check for errors that would cause failure
                if (report.Results.Any(r => r.Errors.Any()))
                {
                    Console.WriteLine();
                    Console.WriteLine("DRY RUN: Migration would FAIL due to errors");
                    return 1;
                }
                
                // Check for warnings that need review
                if (report.Results.Any(r => r.Warnings.Any()))
                {
                    Console.WriteLine();
                    Console.WriteLine("DRY RUN: Migration would succeed with WARNINGS requiring review");
                    return 2;
                }
                
                Console.WriteLine();
                Console.WriteLine("DRY RUN: Migration would succeed without issues");
                return 0;
            }

            return report.TotalProjectsFailed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during migration");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    static void ConfigureServices(IServiceCollection services, MigrationOptions options)
    {
        services.AddSingleton(options);
        
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            
            var logLevel = options.LogLevel.ToLower() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "information" => LogLevel.Information,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" => LogLevel.Critical,
                _ => LogLevel.Information
            };
            
            builder.SetMinimumLevel(logLevel);
        });

        services.AddSingleton<IProjectFileScanner, ProjectFileScanner>();
        services.AddSingleton<ProjectParser>();
        services.AddSingleton<IProjectParser>(provider => provider.GetRequiredService<ProjectParser>());
        services.AddSingleton<IPackageReferenceMigrator, PackageReferenceMigrator>();
        services.AddSingleton<ITransitiveDependencyDetector, NuGetTransitiveDependencyDetector>();
        
        // Register package resolver based on offline mode
        if (options.UseOfflineMode)
        {
            services.AddSingleton<INuGetPackageResolver, OfflinePackageResolver>();
        }
        else
        {
            services.AddSingleton<INuGetPackageResolver, NuGetPackageResolver>();
        }
        
        services.AddSingleton<INuSpecExtractor, NuSpecExtractor>();
        services.AddSingleton<ISdkStyleProjectGenerator, SdkStyleProjectGenerator>();
        services.AddSingleton<IAssemblyInfoExtractor, AssemblyInfoExtractor>();
        services.AddSingleton<IDirectoryBuildPropsGenerator>(provider => 
            new DirectoryBuildPropsGenerator(
                provider.GetRequiredService<ILogger<DirectoryBuildPropsGenerator>>(),
                provider.GetRequiredService<IAuditService>(),
                provider.GetRequiredService<MigrationOptions>()));
        services.AddSingleton<ISolutionFileUpdater>(provider => 
            new SolutionFileUpdater(
                provider.GetRequiredService<ILogger<SolutionFileUpdater>>(),
                provider.GetRequiredService<IAuditService>(),
                provider.GetRequiredService<IBackupService>(),
                provider.GetRequiredService<MigrationOptions>()));
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ILockService, LockService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<ILocalPackageFilesCleaner, LocalPackageFilesCleaner>();
        services.AddSingleton<ICentralPackageManagementGenerator, CentralPackageManagementGenerator>();
        services.AddSingleton<IPostMigrationValidator, PostMigrationValidator>();
        
        // Edge case detectors
        services.AddSingleton<ProjectTypeDetector>();
        services.AddSingleton<BuildEventMigrator>();
        services.AddSingleton<NativeDependencyHandler>();
        services.AddSingleton<ServiceReferenceDetector>();
        
        // New analysis and migration services
        services.AddSingleton<CustomTargetAnalyzer>();
        services.AddSingleton<EntityFrameworkMigrationHandler>();
        services.AddSingleton<T4TemplateHandler>();
        services.AddSingleton<IMigrationAnalyzer, MigrationAnalyzer>();
        services.AddSingleton<PackageAssemblyResolver>();
        services.AddSingleton<NuGetAssetsResolver>();
        
        services.AddSingleton<IMigrationOrchestrator, MigrationOrchestrator>();
    }
    
    static async Task<int> RunCleanDeps(MigrationOptions options)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, options);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var transitiveDepsService = serviceProvider.GetRequiredService<ITransitiveDependencyDetector>();
        var projectScanner = serviceProvider.GetRequiredService<IProjectFileScanner>();
        var backupService = serviceProvider.GetRequiredService<IBackupService>();
        
        try
        {
            if (options.DryRun)
            {
                logger.LogWarning("DRY RUN MODE - No files will be modified");
            }
            
            logger.LogInformation("Starting transitive dependency cleanup for directory: {Directory}", options.DirectoryPath);
            
            var projectFiles = await projectScanner.ScanForProjectFilesAsync(options.DirectoryPath, CancellationToken.None);
            var sdkStyleProjects = projectFiles.Where(p => IsSdkStyleProject(p)).ToList();
            
            if (!sdkStyleProjects.Any())
            {
                logger.LogWarning("No SDK-style projects found in {Directory}", options.DirectoryPath);
                return 0;
            }
            
            logger.LogInformation("Found {Count} SDK-style projects to process", sdkStyleProjects.Count);
            
            var totalRemoved = 0;
            var failedProjects = 0;
            
            foreach (var projectPath in sdkStyleProjects)
            {
                try
                {
                    logger.LogInformation("Processing: {Project}", Path.GetFileName(projectPath));
                    
                    var result = await CleanProjectDependenciesAsync(
                        projectPath, 
                        transitiveDepsService, 
                        backupService,
                        options,
                        logger);
                        
                    if (result.Success)
                    {
                        totalRemoved += result.RemovedCount;
                        if (result.RemovedCount > 0)
                        {
                            logger.LogInformation("  Removed {Count} transitive dependencies", result.RemovedCount);
                        }
                        else
                        {
                            logger.LogInformation("  No transitive dependencies found");
                        }
                    }
                    else
                    {
                        failedProjects++;
                        logger.LogError("  Failed: {Error}", result.Error);
                    }
                }
                catch (Exception ex)
                {
                    failedProjects++;
                    logger.LogError(ex, "Error processing {Project}", projectPath);
                }
            }
            
            Console.WriteLine();
            Console.WriteLine("Transitive Dependency Cleanup Summary:");
            Console.WriteLine($"  Projects processed: {sdkStyleProjects.Count}");
            Console.WriteLine($"  Total dependencies removed: {totalRemoved}");
            Console.WriteLine($"  Failed projects: {failedProjects}");
            
            return failedProjects > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during transitive dependency cleanup");
            return 1;
        }
    }
    
    static async Task<int> RunCleanCpm(MigrationOptions options)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, options);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var cpmGenerator = serviceProvider.GetRequiredService<ICentralPackageManagementGenerator>();
        
        try
        {
            if (options.DryRun)
            {
                logger.LogWarning("DRY RUN MODE - No files will be modified");
            }
            
            logger.LogInformation("Starting Central Package Management cleanup for directory: {Directory}", options.DirectoryPath);
            
            var result = await cpmGenerator.CleanUnusedPackagesAsync(options.DirectoryPath, options.DryRun, CancellationToken.None);
            
            if (result.Success)
            {
                if (result.RemovedPackages.Any())
                {
                    Console.WriteLine();
                    Console.WriteLine($"Removed {result.RemovedPackages.Count} unused packages:");
                    foreach (var package in result.RemovedPackages.OrderBy(p => p))
                    {
                        Console.WriteLine($"  - {package}");
                    }
                }
                else
                {
                    Console.WriteLine("No unused packages found.");
                }
                
                return 0;
            }
            else
            {
                logger.LogError("Failed to clean Central Package Management: {Error}", result.Error);
                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Central Package Management cleanup");
            return 1;
        }
    }
    
    static bool IsSdkStyleProject(string projectPath)
    {
        try
        {
            var content = File.ReadAllText(projectPath);
            return content.Contains("<Project Sdk=", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    static async Task<CleanDepsResult> CleanProjectDependenciesAsync(
        string projectPath,
        ITransitiveDependencyDetector transitiveDepsService,
        IBackupService backupService,
        MigrationOptions options,
        ILogger logger)
    {
        try
        {
            // Load project XML
            var doc = XDocument.Load(projectPath);
            var root = doc.Root;
            if (root == null)
                return new CleanDepsResult { Success = false, Error = "Invalid project file" };
            
            // Find all PackageReference elements
            var packageRefs = root.Descendants("PackageReference")
                .Where(pr => pr.Attribute("Include") != null)
                .ToList();
                
            if (!packageRefs.Any())
                return new CleanDepsResult { Success = true, RemovedCount = 0 };
            
            // Convert to PackageReference models
            var packages = packageRefs.Select(pr => new Models.PackageReference
            {
                PackageId = pr.Attribute("Include")!.Value,
                Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? "*",
                IsTransitive = false
            }).ToList();
            
            // Get project references to check their package dependencies
            var projectRefs = root.Descendants("ProjectReference")
                .Select(pr => pr.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, path!)))
                .Where(fullPath => File.Exists(fullPath))
                .ToList();
            
            // Collect packages that are directly referenced by project dependencies
            var projectDepPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var projRef in projectRefs)
            {
                try
                {
                    var projDoc = XDocument.Load(projRef);
                    var depPackages = projDoc.Descendants("PackageReference")
                        .Select(pr => pr.Attribute("Include")?.Value)
                        .Where(id => !string.IsNullOrEmpty(id));
                    
                    foreach (var pkg in depPackages)
                    {
                        projectDepPackages.Add(pkg!);
                        logger.LogDebug("Package '{Package}' is used by project reference: {Project}", pkg, Path.GetFileName(projRef));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze project reference: {Project}", projRef);
                }
            }
            
            // Detect transitive dependencies
            var projectDirectory = Path.GetDirectoryName(projectPath);
            var analyzedPackages = await transitiveDepsService.DetectTransitiveDependenciesAsync(packages, projectDirectory, CancellationToken.None);
            
            // Filter out packages that are transitive but also directly used by project references
            var transitiveDeps = analyzedPackages
                .Where(p => p.IsTransitive)
                .Where(p => !projectDepPackages.Contains(p.PackageId))
                .ToList();
            
            if (!transitiveDeps.Any())
            {
                logger.LogInformation("  No removable transitive dependencies found");
                if (analyzedPackages.Any(p => p.IsTransitive && projectDepPackages.Contains(p.PackageId)))
                {
                    var kept = analyzedPackages.Count(p => p.IsTransitive && projectDepPackages.Contains(p.PackageId));
                    logger.LogInformation("  {Count} transitive dependencies kept because they're used by project references", kept);
                }
                return new CleanDepsResult { Success = true, RemovedCount = 0 };
            }
            
            if (!options.DryRun && options.CreateBackup)
            {
                // Create simple backup
                var backupPath = projectPath + ".backup";
                File.Copy(projectPath, backupPath, overwrite: true);
                logger.LogDebug("Created backup: {BackupPath}", backupPath);
            }
            
            // Remove transitive dependencies from XML
            var removedCount = 0;
            foreach (var transitiveDep in transitiveDeps)
            {
                var elementsToRemove = packageRefs
                    .Where(pr => pr.Attribute("Include")?.Value.Equals(transitiveDep.PackageId, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                    
                foreach (var element in elementsToRemove)
                {
                    logger.LogDebug("  Removing transitive dependency: {Package}", transitiveDep.PackageId);
                    element.Remove();
                    removedCount++;
                }
            }
            
            // Clean up empty ItemGroups
            var emptyItemGroups = root.Descendants("ItemGroup")
                .Where(ig => !ig.HasElements && !ig.HasAttributes)
                .ToList();
            foreach (var ig in emptyItemGroups)
            {
                ig.Remove();
            }
            
            if (!options.DryRun && removedCount > 0)
            {
                // Save the modified project file without XML declaration
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineChars = Environment.NewLine,
                    NewLineHandling = NewLineHandling.Replace
                };
                
                using (var writer = XmlWriter.Create(projectPath, settings))
                {
                    doc.Save(writer);
                }
            }
            
            return new CleanDepsResult { Success = true, RemovedCount = removedCount };
        }
        catch (Exception ex)
        {
            return new CleanDepsResult { Success = false, Error = ex.Message };
        }
    }
    
    class CleanDepsResult
    {
        public bool Success { get; set; }
        public int RemovedCount { get; set; }
        public string? Error { get; set; }
    }
    
    static void InitializeMSBuild()
    {
        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            
            if (instances.Length == 0)
            {
                Console.Error.WriteLine("No MSBuild instances found. Please ensure .NET SDK is installed.");
                Environment.Exit(1);
            }
            
            var instance = instances.OrderByDescending(x => x.Version).First();
            Console.WriteLine($"Using MSBuild from: {instance.MSBuildPath}");
            
            MSBuildLocator.RegisterInstance(instance);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize MSBuild: {ex.Message}");
            Environment.Exit(1);
        }
    }
}