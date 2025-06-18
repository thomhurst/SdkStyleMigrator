using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Services;

namespace SdkMigrator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var directoryPath = args[0];
        
        if (!Directory.Exists(directoryPath))
        {
            Console.Error.WriteLine($"Error: Directory '{directoryPath}' does not exist.");
            return 1;
        }

        // Convert to absolute path
        directoryPath = Path.GetFullPath(directoryPath);

        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        
        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("SDK Migrator - Starting migration process");
            
            var orchestrator = serviceProvider.GetRequiredService<IMigrationOrchestrator>();
            var cancellationTokenSource = new CancellationTokenSource();
            
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                logger.LogWarning("Cancellation requested...");
            };

            var report = await orchestrator.MigrateProjectsAsync(directoryPath, cancellationTokenSource.Token);
            
            // Display summary
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

    static void ConfigureServices(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register services
        services.AddSingleton<IProjectFileScanner, ProjectFileScanner>();
        services.AddSingleton<IProjectParser, ProjectParser>();
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
        Console.WriteLine("Usage: SdkMigrator <directory>");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <directory>    The directory to scan for project files");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help    Show this help message");
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
}
