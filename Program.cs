using System.CommandLine;
using System.CommandLine.Invocation;
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
            
        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Force migration without prompts");
            
        var noBackupOption = new Option<bool>(
            aliases: new[] { "--no-backup" },
            description: "Skip creating backup files");
            
        var parallelOption = new Option<int?>(
            aliases: new[] { "--parallel", "-p" },
            description: "Enable parallel processing (n = max threads)");
            
        var logLevelOption = new Option<string>(
            aliases: new[] { "--log-level", "-l" },
            getDefaultValue: () => "Information",
            description: "Set log level (Trace|Debug|Information|Warning|Error)");
            
        rootCommand.AddArgument(directoryArgument);
        rootCommand.AddOption(dryRunOption);
        rootCommand.AddOption(outputDirectoryOption);
        rootCommand.AddOption(targetFrameworkOption);
        rootCommand.AddOption(forceOption);
        rootCommand.AddOption(noBackupOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(logLevelOption);
        
        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var options = new MigrationOptions
            {
                DirectoryPath = context.ParseResult.GetValueForArgument(directoryArgument),
                DryRun = context.ParseResult.GetValueForOption(dryRunOption),
                OutputDirectory = context.ParseResult.GetValueForOption(outputDirectoryOption),
                TargetFramework = context.ParseResult.GetValueForOption(targetFrameworkOption),
                Force = context.ParseResult.GetValueForOption(forceOption),
                NoBackup = context.ParseResult.GetValueForOption(noBackupOption),
                MaxDegreeOfParallelism = context.ParseResult.GetValueForOption(parallelOption) ?? 1,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "Information"
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
- Create backup files with .legacy extension
- Maintain feature parity with the original project

Examples:
  SdkMigrator ./src
  SdkMigrator ./src --dry-run
  SdkMigrator ./src -o ./src-migrated -t net8.0
  SdkMigrator ./src --parallel 4 --log-level Debug";
        
        return await rootCommand.InvokeAsync(args);
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
        services.AddSingleton<ISdkStyleProjectGenerator, SdkStyleProjectGenerator>();
        services.AddSingleton<IAssemblyInfoExtractor, AssemblyInfoExtractor>();
        services.AddSingleton<IDirectoryBuildPropsGenerator, DirectoryBuildPropsGenerator>();
        services.AddSingleton<ISolutionFileUpdater, SolutionFileUpdater>();
        services.AddSingleton<IMigrationOrchestrator, MigrationOrchestrator>();
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