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
        
        var options = ParseArguments(args);
        
        if (options == null)
        {
            return 1;
        }

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
    
    static MigrationOptions? ParseArguments(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            return null;
        }

        var options = new MigrationOptions();
        var positionalArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            if (arg.StartsWith("--") || arg.StartsWith("-"))
            {
                switch (arg.ToLower())
                {
                    case "--dry-run":
                    case "-d":
                        options.DryRun = true;
                        break;
                        
                    case "--output-directory":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            options.OutputDirectory = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: {arg} requires a value");
                            return null;
                        }
                        break;
                        
                    case "--target-framework":
                    case "-t":
                        if (i + 1 < args.Length)
                        {
                            options.TargetFramework = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: {arg} requires a value");
                            return null;
                        }
                        break;
                        
                    case "--no-backup":
                        options.NoBackup = true;
                        break;
                        
                    case "--force":
                    case "-f":
                        options.Force = true;
                        break;
                        
                    case "--parallel":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parallelism))
                        {
                            options.MaxDegreeOfParallelism = parallelism;
                            i++;
                        }
                        else
                        {
                            options.MaxDegreeOfParallelism = Environment.ProcessorCount;
                        }
                        break;
                        
                    case "--log-level":
                    case "-l":
                        if (i + 1 < args.Length)
                        {
                            options.LogLevel = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: {arg} requires a value");
                            return null;
                        }
                        break;
                        
                    default:
                        Console.Error.WriteLine($"Error: Unknown option '{arg}'");
                        ShowHelp();
                        return null;
                }
            }
            else
            {
                positionalArgs.Add(arg);
            }
        }

        if (positionalArgs.Count == 0)
        {
            Console.Error.WriteLine("Error: Directory path is required");
            ShowHelp();
            return null;
        }

        options.DirectoryPath = positionalArgs[0];
        
        if (!Directory.Exists(options.DirectoryPath))
        {
            Console.Error.WriteLine($"Error: Directory '{options.DirectoryPath}' does not exist.");
            return null;
        }

        options.DirectoryPath = Path.GetFullPath(options.DirectoryPath);
        
        if (options.OutputDirectory != null)
        {
            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
        }

        return options;
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
        services.AddSingleton<ITransitiveDependencyDetector, TransitiveDependencyDetector>();
        services.AddSingleton<ISdkStyleProjectGenerator, SdkStyleProjectGenerator>();
        services.AddSingleton<IAssemblyInfoExtractor, AssemblyInfoExtractor>();
        services.AddSingleton<IDirectoryBuildPropsGenerator, DirectoryBuildPropsGenerator>();
        services.AddSingleton<IMigrationOrchestrator, MigrationOrchestrator>();
    }

    static void ShowHelp()
    {
        Console.WriteLine("SDK Migrator - Migrate legacy MSBuild project files to SDK-style format");
        Console.WriteLine();
        Console.WriteLine("Usage: SdkMigrator <directory> [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <directory>              The directory to scan for project files");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help               Show this help message");
        Console.WriteLine("  -d, --dry-run            Preview changes without modifying files");
        Console.WriteLine("  -o, --output-directory   Output directory for migrated projects");
        Console.WriteLine("  -t, --target-framework   Override target framework (e.g., net8.0)");
        Console.WriteLine("  -f, --force              Force migration without prompts");
        Console.WriteLine("  --no-backup              Skip creating backup files");
        Console.WriteLine("  -p, --parallel [n]       Enable parallel processing (n = max threads)");
        Console.WriteLine("  -l, --log-level          Set log level (Trace|Debug|Information|Warning|Error)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  SdkMigrator ./src");
        Console.WriteLine("  SdkMigrator ./src --dry-run");
        Console.WriteLine("  SdkMigrator ./src -o ./src-migrated -t net8.0");
        Console.WriteLine("  SdkMigrator ./src --parallel 4 --log-level Debug");
        Console.WriteLine();
        Console.WriteLine("Description:");
        Console.WriteLine("  This tool scans the specified directory and all subdirectories for");
        Console.WriteLine("  legacy MSBuild project files (.csproj, .vbproj, .fsproj) and migrates");
        Console.WriteLine("  them to the new SDK-style format.");
        Console.WriteLine();
        Console.WriteLine("  The tool will:");
        Console.WriteLine("  - Remove unnecessary legacy properties and imports");
        Console.WriteLine("  - Convert packages.config to PackageReference");
        Console.WriteLine("  - Detect and remove transitive package dependencies");
        Console.WriteLine("  - Extract assembly properties to Directory.Build.props");
        Console.WriteLine("  - Remove AssemblyInfo files and enable SDK auto-generation");
        Console.WriteLine("  - Create backup files with .legacy extension");
        Console.WriteLine("  - Maintain feature parity with the original project");
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
