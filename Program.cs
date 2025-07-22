using System.CommandLine;
using System.CommandLine.Invocation;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.X11;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging.Core;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Services;

namespace SdkMigrator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        InitializeMSBuild();
        
        // If no arguments provided, launch UI mode
        if (args.Length == 0)
        {
            Console.WriteLine("Starting SdkMigrator in UI mode...");
            Console.WriteLine($"Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            Console.WriteLine($"Current directory: {Environment.CurrentDirectory}");
            
            try
            {
                // Add global exception handlers for UI mode
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
                    if (e.ExceptionObject is Exception ex)
                    {
                        Console.Error.WriteLine($"Exception type: {ex.GetType().FullName}");
                        Console.Error.WriteLine($"Message: {ex.Message}");
                        Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                };

                TaskScheduler.UnobservedTaskException += (sender, e) =>
                {
                    Console.Error.WriteLine($"Unobserved task exception: {e.Exception}");
                    e.SetObserved();
                };

                return BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to start Avalonia app: {ex.Message}");
                Console.Error.WriteLine($"Exception type: {ex.GetType().FullName}");
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                return 1;
            }
        }

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

        var cpmVersionStrategyOption = new Option<string>(
            aliases: new[] { "--cpm-version-strategy" },
            getDefaultValue: () => "UseHighest",
            description: "CPM version resolution strategy (UseHighest|UseLowest|UseLatestStable|UseMostCommon|SemanticCompatible|FrameworkCompatible)");

        var cpmPreferStableOption = new Option<bool>(
            aliases: new[] { "--cpm-prefer-stable" },
            getDefaultValue: () => true,
            description: "Prefer stable versions over prereleases in CPM resolution");

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

        var disableCacheOption = new Option<bool>(
            aliases: new[] { "--no-cache" },
            description: "Disable package version caching");

        var cacheTTLOption = new Option<int?>(
            aliases: new[] { "--cache-ttl" },
            description: "Cache time-to-live in minutes (default: 60)");

        var interactiveImportsOption = new Option<bool>(
            aliases: new[] { "--interactive-imports", "-ii" },
            description: "Enable interactive import selection during migration");
            
        var interactiveTargetsOption = new Option<bool>(
            aliases: new[] { "--interactive-targets", "-it" },
            description: "Enable interactive target selection during migration");

        // Add migrate command as the default behavior
        rootCommand.AddArgument(directoryArgument);
        rootCommand.AddOption(dryRunOption);
        rootCommand.AddOption(outputDirectoryOption);
        rootCommand.AddOption(targetFrameworkOption);
        rootCommand.AddOption(targetFrameworksOption);
        rootCommand.AddOption(centralPackageManagementOption);
        rootCommand.AddOption(cpmVersionStrategyOption);
        rootCommand.AddOption(cpmPreferStableOption);
        rootCommand.AddOption(forceOption);
        rootCommand.AddOption(noBackupOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(offlineOption);
        rootCommand.AddOption(nugetConfigOption);
        rootCommand.AddOption(disableCacheOption);
        rootCommand.AddOption(cacheTTLOption);
        rootCommand.AddOption(interactiveImportsOption);
        rootCommand.AddOption(interactiveTargetsOption);

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

        // Clean-sln command - Clean solution files
        var cleanSlnCommand = new Command("clean-sln", "Clean and fix common issues in solution files");
        var cleanSlnPathArgument = new Argument<string>(
            name: "solution",
            description: "Path to the .sln file to clean");
        var fixMissingOption = new Option<bool>(
            aliases: new[] { "--fix-missing", "-m" },
            description: "Remove references to non-existent projects");
        var removeDuplicatesOption = new Option<bool>(
            aliases: new[] { "--remove-duplicates", "-d" },
            description: "Remove duplicate project entries");
        var fixConfigsOption = new Option<bool>(
            aliases: new[] { "--fix-configs", "-c" },
            description: "Fix orphaned configurations and add missing ones");
        var removeSccOption = new Option<bool>(
            aliases: new[] { "--remove-scc", "-s" },
            description: "Remove source control bindings");
        var removeEmptyOption = new Option<bool>(
            aliases: new[] { "--remove-empty", "-e" },
            description: "Remove empty solution folders");
        var removeNonStandardOption = new Option<bool>(
            aliases: new[] { "--remove-non-standard", "-n" },
            description: "Remove non-standard project types (.vcxproj, .sqlproj, etc.)");
        var fixAllOption = new Option<bool>(
            aliases: new[] { "--fix-all", "-a" },
            description: "Apply all safe fixes (excludes --remove-non-standard)");
        var cleanSlnDryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Preview changes without modifying files");
        var cleanSlnBackupOption = new Option<bool>(
            aliases: new[] { "--backup", "-b" },
            getDefaultValue: () => true,
            description: "Create backup before modifying");

        cleanSlnCommand.AddArgument(cleanSlnPathArgument);
        cleanSlnCommand.AddOption(fixMissingOption);
        cleanSlnCommand.AddOption(removeDuplicatesOption);
        cleanSlnCommand.AddOption(fixConfigsOption);
        cleanSlnCommand.AddOption(removeSccOption);
        cleanSlnCommand.AddOption(removeEmptyOption);
        cleanSlnCommand.AddOption(removeNonStandardOption);
        cleanSlnCommand.AddOption(fixAllOption);
        cleanSlnCommand.AddOption(cleanSlnDryRunOption);
        cleanSlnCommand.AddOption(cleanSlnBackupOption);
        cleanSlnCommand.AddOption(logLevelOption);

        cleanSlnCommand.SetHandler(async (InvocationContext context) =>
        {
            var solutionPath = context.ParseResult.GetValueForArgument(cleanSlnPathArgument);
            var fixMissing = context.ParseResult.GetValueForOption(fixMissingOption);
            var removeDuplicates = context.ParseResult.GetValueForOption(removeDuplicatesOption);
            var fixConfigs = context.ParseResult.GetValueForOption(fixConfigsOption);
            var removeScc = context.ParseResult.GetValueForOption(removeSccOption);
            var removeEmpty = context.ParseResult.GetValueForOption(removeEmptyOption);
            var removeNonStandard = context.ParseResult.GetValueForOption(removeNonStandardOption);
            var fixAll = context.ParseResult.GetValueForOption(fixAllOption);
            var dryRun = context.ParseResult.GetValueForOption(cleanSlnDryRunOption);
            var backup = context.ParseResult.GetValueForOption(cleanSlnBackupOption);
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information";

            var cleanOptions = new SolutionCleanOptions
            {
                FixMissingProjects = fixMissing || fixAll,
                RemoveDuplicates = removeDuplicates || fixAll,
                FixConfigurations = fixConfigs || fixAll,
                RemoveSourceControlBindings = removeScc || fixAll,
                RemoveEmptyFolders = removeEmpty || fixAll,
                RemoveNonStandardProjects = removeNonStandard,
                FixAll = fixAll,
                DryRun = dryRun,
                CreateBackup = backup
            };

            // If no specific options provided, default to fix-all
            if (!fixMissing && !removeDuplicates && !fixConfigs && !removeScc &&
                !removeEmpty && !removeNonStandard && !fixAll)
            {
                cleanOptions.FixAll = true;
                cleanOptions.ApplyFixAll();
            }

            var exitCode = await RunCleanSln(solutionPath, cleanOptions, logLevel);
            context.ExitCode = exitCode;
        });

        rootCommand.AddCommand(cleanSlnCommand);

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
                CpmOptions = new CpmVersionResolutionOptions
                {
                    Strategy = ParseCpmStrategy(context.ParseResult.GetValueForOption(cpmVersionStrategyOption)!),
                    PreferStableVersions = context.ParseResult.GetValueForOption(cpmPreferStableOption)
                },
                Force = context.ParseResult.GetValueForOption(forceOption),
                CreateBackup = !context.ParseResult.GetValueForOption(noBackupOption),
                MaxDegreeOfParallelism = context.ParseResult.GetValueForOption(parallelOption) ?? 1,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information",
                UseOfflineMode = context.ParseResult.GetValueForOption(offlineOption),
                NuGetConfigPath = context.ParseResult.GetValueForOption(nugetConfigOption),
                DisableCache = context.ParseResult.GetValueForOption(disableCacheOption),
                CacheTTLMinutes = context.ParseResult.GetValueForOption(cacheTTLOption),
                InteractiveImportSelection = context.ParseResult.GetValueForOption(interactiveImportsOption),
                InteractiveTargetSelection = context.ParseResult.GetValueForOption(interactiveTargetsOption)
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
legacy MSBuild project files (.*proj) and migrates
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

        // Register the clean SDK-style project generator
        services.AddSingleton<ISdkStyleProjectGenerator, CleanSdkStyleProjectGenerator>();
        services.AddSingleton<IDirectoryBuildPropsReader, DirectoryBuildPropsReader>();
        services.AddSingleton<ITestProjectHandler, TestProjectHandler>();
        services.AddSingleton<IDesignerFileHandler, DesignerFileHandler>();
        services.AddSingleton<IPackageVersionConflictResolver, PackageVersionConflictResolver>();

        services.AddSingleton<IProjectFileScanner, ProjectFileScanner>();
        services.AddSingleton<ProjectParser>();
        services.AddSingleton<IProjectParser>(provider => provider.GetRequiredService<ProjectParser>());
        services.AddSingleton<IPackageReferenceMigrator, PackageReferenceMigrator>();
        // Register as singleton to maintain caches across multiple operations
        services.AddSingleton<NuGetTransitiveDependencyDetector>();
        services.AddSingleton<ITransitiveDependencyDetector>(provider =>
        {
            var innerDetector = provider.GetRequiredService<NuGetTransitiveDependencyDetector>();
            var cache = provider.GetRequiredService<IPackageVersionCache>();
            var logger = provider.GetRequiredService<ILogger<CachedNuGetTransitiveDependencyDetector>>();
            return new CachedNuGetTransitiveDependencyDetector(innerDetector, cache, logger);
        });

        // Register package caching
        services.Configure<PackageCacheOptions>(opt =>
        {
            opt.EnableCaching = !options.DisableCache;
            opt.CacheTTLMinutes = options.CacheTTLMinutes ?? 60;
        });
        services.AddSingleton<IPackageVersionCache, MemoryPackageVersionCache>();

        // Register package resolver based on offline mode
        if (options.UseOfflineMode)
        {
            services.AddSingleton<INuGetPackageResolver, OfflinePackageResolver>();
        }
        else
        {
            // Register the actual resolver
            services.AddSingleton<NuGetPackageResolver>();

            // Register the cached decorator
            services.AddSingleton<INuGetPackageResolver>(provider =>
            {
                var innerResolver = provider.GetRequiredService<NuGetPackageResolver>();
                var cache = provider.GetRequiredService<IPackageVersionCache>();
                var logger = provider.GetRequiredService<ILogger<CachedNuGetPackageResolver>>();
                return new CachedNuGetPackageResolver(innerResolver, cache, logger);
            });
        }

        services.AddSingleton<INuSpecExtractor, NuSpecExtractor>();
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
        services.AddSingleton<CpmVersionResolver>();
        services.AddSingleton<CpmPackageClassifier>();
        services.AddSingleton<ExistingCpmDetector>();
        services.AddSingleton<IImportScanner, ImportScanner>();
        services.AddSingleton<ITargetScanner, TargetScanner>();
        services.AddSingleton<IUserInteractionService, ConsoleUserInteractionService>();
        services.AddSingleton<ICentralPackageManagementGenerator, CentralPackageManagementGenerator>();
        services.AddSingleton<IPostMigrationValidator, PostMigrationValidator>();
        services.AddSingleton<IMSBuildArtifactDetector, MSBuildArtifactDetector>();

        // Edge case detectors
        services.AddSingleton<ProjectTypeDetector>();
        services.AddSingleton<IBuildEventMigrator, BuildEventMigrator>();
        services.AddSingleton<INativeDependencyHandler, NativeDependencyHandler>();
        services.AddSingleton<ServiceReferenceDetector>();
        services.AddSingleton<IWebProjectHandler, WebProjectHandler>();

        // New analysis and migration services
        services.AddSingleton<CustomTargetAnalyzer>();
        services.AddSingleton<EntityFrameworkMigrationHandler>();
        services.AddSingleton<T4TemplateHandler>();
        services.AddSingleton<IMigrationAnalyzer, MigrationAnalyzer>();
        services.AddSingleton<PackageAssemblyResolver>();
        services.AddSingleton<NuGetAssetsResolver>();
        services.AddSingleton<IAssemblyReferenceConverter, AssemblyReferenceConverter>();
        services.AddSingleton<IConfigurationFileGenerator, ConfigurationFileGenerator>();

        services.AddSingleton<IMigrationOrchestrator>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<MigrationOrchestrator>>();
            var projectFileScanner = provider.GetRequiredService<IProjectFileScanner>();
            var projectParser = provider.GetRequiredService<IProjectParser>();
            var sdkStyleProjectGenerator = provider.GetRequiredService<ISdkStyleProjectGenerator>();
            var assemblyInfoExtractor = provider.GetRequiredService<IAssemblyInfoExtractor>();
            var directoryBuildPropsGenerator = provider.GetRequiredService<IDirectoryBuildPropsGenerator>();
            var solutionFileUpdater = provider.GetRequiredService<ISolutionFileUpdater>();
            var backupService = provider.GetRequiredService<IBackupService>();
            var lockService = provider.GetRequiredService<ILockService>();
            var auditService = provider.GetRequiredService<IAuditService>();
            var localPackageFilesCleaner = provider.GetRequiredService<ILocalPackageFilesCleaner>();
            var centralPackageManagementGenerator = provider.GetRequiredService<ICentralPackageManagementGenerator>();
            var postMigrationValidator = provider.GetRequiredService<IPostMigrationValidator>();
            var migrationAnalyzer = provider.GetRequiredService<IMigrationAnalyzer>();
            var configurationFileGenerator = provider.GetRequiredService<IConfigurationFileGenerator>();
            var importScanner = provider.GetRequiredService<IImportScanner>();
            var userInteractionService = provider.GetRequiredService<IUserInteractionService>();
            var options = provider.GetRequiredService<MigrationOptions>();
            var packageCache = provider.GetService<IPackageVersionCache>();

            return new MigrationOrchestrator(
                logger,
                projectFileScanner,
                projectParser,
                sdkStyleProjectGenerator,
                assemblyInfoExtractor,
                directoryBuildPropsGenerator,
                solutionFileUpdater,
                backupService,
                lockService,
                auditService,
                localPackageFilesCleaner,
                centralPackageManagementGenerator,
                postMigrationValidator,
                migrationAnalyzer,
                provider.GetRequiredService<IPackageVersionConflictResolver>(),
                configurationFileGenerator,
                importScanner,
                provider.GetRequiredService<ITargetScanner>(),
                userInteractionService,
                provider.GetRequiredService<IWebProjectHandler>(),
                options,
                packageCache);
        });
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

            // Process projects in parallel if requested
            if (options.MaxDegreeOfParallelism > 1)
            {
                logger.LogInformation("Processing projects in parallel with max degree of parallelism: {MaxDegree}", options.MaxDegreeOfParallelism);

                var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
                var processedCount = 0;
                var lockObj = new object();

                var tasks = sdkStyleProjects.Select(async projectPath =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        logger.LogInformation("Processing: {Project}", Path.GetFileName(projectPath));

                        var result = await CleanProjectDependenciesAsync(
                            projectPath,
                            transitiveDepsService,
                            backupService,
                            options,
                            logger);

                        lock (lockObj)
                        {
                            processedCount++;
                            if (result.Success)
                            {
                                totalRemoved += result.RemovedCount;
                                if (result.RemovedCount > 0)
                                {
                                    logger.LogInformation("Removed {Count} transitive dependencies from {Project}",
                                        result.RemovedCount, Path.GetFileName(projectPath));
                                }
                            }
                            else
                            {
                                failedProjects++;
                                logger.LogError("Failed to process {Project}: {Error}",
                                    Path.GetFileName(projectPath), result.Error);
                            }

                            logger.LogInformation("Progress: {Processed}/{Total} projects processed",
                                processedCount, sdkStyleProjects.Count);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                await Task.WhenAll(tasks);
            }
            else
            {
                // Sequential processing (original code)
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

    static async Task<int> RunCleanSln(string solutionPath, SolutionCleanOptions options, string logLevel)
    {
        var services = new ServiceCollection();

        // Configure basic services
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Enum.Parse<LogLevel>(logLevel));
        });

        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ISolutionCleaner, SolutionCleaner>();

        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var solutionCleaner = serviceProvider.GetRequiredService<ISolutionCleaner>();

        try
        {
            if (!File.Exists(solutionPath))
            {
                logger.LogError("Solution file not found: {SolutionPath}", solutionPath);
                return 1;
            }

            if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("File is not a solution file: {SolutionPath}", solutionPath);
                return 1;
            }

            solutionPath = Path.GetFullPath(solutionPath);

            if (options.DryRun)
            {
                logger.LogWarning("DRY RUN MODE - No files will be modified");
            }

            var result = await solutionCleaner.CleanSolutionAsync(solutionPath, options);

            if (result.Success)
            {
                if (result.HasChanges)
                {
                    Console.WriteLine();
                    Console.WriteLine("Solution cleanup completed successfully!");
                    Console.WriteLine($"Solution: {result.SolutionPath}");

                    if (result.BackupPath != null)
                    {
                        Console.WriteLine($"Backup created: {result.BackupPath}");
                    }

                    Console.WriteLine();
                    Console.WriteLine("Changes made:");

                    if (result.ProjectsRemoved > 0)
                        Console.WriteLine($"  - Removed {result.ProjectsRemoved} missing/non-standard project(s)");

                    if (result.DuplicatesRemoved > 0)
                        Console.WriteLine($"  - Removed {result.DuplicatesRemoved} duplicate project(s)");

                    if (result.ConfigurationsFixed > 0)
                        Console.WriteLine($"  - Fixed {result.ConfigurationsFixed} orphaned configuration(s)");

                    if (result.ConfigurationsAdded > 0)
                        Console.WriteLine($"  - Added {result.ConfigurationsAdded} missing configuration(s)");

                    if (result.EmptyFoldersRemoved > 0)
                        Console.WriteLine($"  - Removed {result.EmptyFoldersRemoved} empty solution folder(s)");

                    if (result.SourceControlBindingsRemoved > 0)
                        Console.WriteLine($"  - Removed {result.SourceControlBindingsRemoved} source control binding(s)");
                }
                else
                {
                    Console.WriteLine("No changes needed - solution file is already clean.");
                }

                if (result.UnfixableIssues.Any())
                {
                    Console.WriteLine();
                    Console.WriteLine("Issues requiring manual intervention:");
                    foreach (var issue in result.UnfixableIssues)
                    {
                        Console.WriteLine($"  - [{issue.Type}] {issue.Description}");
                        if (!string.IsNullOrEmpty(issue.Details))
                        {
                            Console.WriteLine($"    {issue.Details}");
                        }
                    }
                }

                if (result.Warnings.Any())
                {
                    Console.WriteLine();
                    Console.WriteLine("Warnings:");
                    foreach (var warning in result.Warnings)
                    {
                        Console.WriteLine($"  - {warning}");
                    }
                }

                return 0;
            }
            else
            {
                logger.LogError("Solution cleanup failed");

                if (result.Errors.Any())
                {
                    foreach (var error in result.Errors)
                    {
                        logger.LogError(error);
                    }
                }

                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during solution cleanup");
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

            // Collect all packages (direct and transitive) from referenced projects
            var projectDepPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedProjectPackages = new Dictionary<string, List<Models.PackageReference>>();

            foreach (var projRef in projectRefs)
            {
                try
                {
                    var projDoc = XDocument.Load(projRef);
                    var depPackages = projDoc.Descendants("PackageReference")
                        .Select(pr => new Models.PackageReference
                        {
                            PackageId = pr.Attribute("Include")!.Value,
                            Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? "*",
                            IsTransitive = false
                        })
                        .Where(p => !string.IsNullOrEmpty(p.PackageId))
                        .ToList();

                    referencedProjectPackages[projRef] = depPackages;

                    foreach (var pkg in depPackages)
                    {
                        projectDepPackages.Add(pkg.PackageId);
                        logger.LogDebug("Package '{Package}' is directly used by project reference: {Project}", pkg.PackageId, Path.GetFileName(projRef));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze project reference: {Project}", projRef);
                }
            }

            // Detect transitive dependencies within the current project
            var projectDirectory = Path.GetDirectoryName(projectPath);
            var analyzedPackages = await transitiveDepsService.DetectTransitiveDependenciesAsync(packages, projectDirectory, CancellationToken.None);

            // Now analyze transitive dependencies from referenced projects
            var allTransitiveFromRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in referencedProjectPackages)
            {
                var refProjPath = kvp.Key;
                var refProjPackages = kvp.Value;

                if (refProjPackages.Any())
                {
                    logger.LogDebug("Analyzing transitive dependencies from referenced project: {Project}", Path.GetFileName(refProjPath));
                    var refProjDirectory = Path.GetDirectoryName(refProjPath);
                    var refAnalyzed = await transitiveDepsService.DetectTransitiveDependenciesAsync(refProjPackages, refProjDirectory, CancellationToken.None);

                    // Get all dependencies (direct and transitive) from this project
                    foreach (var refPkg in refAnalyzed)
                    {
                        allTransitiveFromRefs.Add(refPkg.PackageId);

                        // Also try to get transitive dependencies of each package
                        if (transitiveDepsService is NuGetTransitiveDependencyDetector nugetDetector)
                        {
                            try
                            {
                                var deps = await nugetDetector.GetPackageDependenciesAsync(
                                    refPkg.PackageId,
                                    refPkg.Version ?? "*",
                                    NuGet.Frameworks.NuGetFramework.AnyFramework,
                                    CancellationToken.None);

                                foreach (var dep in deps)
                                {
                                    allTransitiveFromRefs.Add(dep.Id);
                                    logger.LogDebug("Package '{Package}' is transitively provided by '{Parent}' in project '{Project}'",
                                        dep.Id, refPkg.PackageId, Path.GetFileName(refProjPath));
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogDebug(ex, "Failed to get dependencies for package {Package}", refPkg.PackageId);
                            }
                        }
                    }
                }
            }

            // Find packages that can be removed:
            // 1. Packages marked as transitive within the current project
            // 2. Packages that are provided (directly or transitively) by referenced projects
            var removablePackages = new List<Models.PackageReference>();

            foreach (var package in analyzedPackages)
            {
                bool shouldRemove = false;
                string reason = "";

                // Check if it's transitive within current project
                if (package.IsTransitive)
                {
                    shouldRemove = true;
                    reason = "transitive dependency within current project";
                }
                // Check if it's provided by referenced projects
                else if (allTransitiveFromRefs.Contains(package.PackageId))
                {
                    shouldRemove = true;
                    reason = "provided by referenced project";
                }

                if (shouldRemove)
                {
                    // Don't remove if it's essential
                    var essentialPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Microsoft.NET.Test.Sdk",
                        "xunit.runner.visualstudio",
                        "NUnit3TestAdapter",
                        "MSTest.TestAdapter",
                        "coverlet.collector"
                    };

                    if (essentialPackages.Contains(package.PackageId))
                    {
                        logger.LogDebug("Keeping {Package} as it's an essential package", package.PackageId);
                    }
                    else
                    {
                        removablePackages.Add(package);
                        logger.LogInformation("Package '{Package}' can be removed: {Reason}", package.PackageId, reason);
                    }
                }
            }

            var transitiveDeps = removablePackages;

            if (!transitiveDeps.Any())
            {
                logger.LogInformation("  No removable transitive dependencies found");
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

    static CpmVersionResolutionStrategy ParseCpmStrategy(string strategy)
    {
        return strategy?.ToLowerInvariant() switch
        {
            "usehighest" => CpmVersionResolutionStrategy.UseHighest,
            "uselowest" => CpmVersionResolutionStrategy.UseLowest,
            "uselateststable" => CpmVersionResolutionStrategy.UseLatestStable,
            "usemostcommon" => CpmVersionResolutionStrategy.UseMostCommon,
            "semanticcompatible" => CpmVersionResolutionStrategy.SemanticCompatible,
            "frameworkcompatible" => CpmVersionResolutionStrategy.FrameworkCompatible,
            _ => CpmVersionResolutionStrategy.UseHighest
        };
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
    
    static AppBuilder BuildAvaloniaApp()
    {
        // Force software rendering in WSL
        if (OperatingSystem.IsLinux())
        {
            Environment.SetEnvironmentVariable("AVALONIA_FORCE_SOFTWARE_RENDERING", "1");
            Console.WriteLine("Running on Linux - forcing software rendering for WSL");
        }
        
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // Add platform-specific configuration for Linux/WSL
        if (OperatingSystem.IsLinux())
        {
            builder = builder.With(new X11PlatformOptions
            {
                EnableMultiTouch = false,
                UseDBusMenu = false
            });
        }

        return builder;
    }
}