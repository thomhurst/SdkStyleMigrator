using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Services;
using SdkMigrator.ViewModels;
using SdkMigrator.Views;

namespace SdkMigrator;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        Console.WriteLine("App.Initialize called");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("App.OnFrameworkInitializationCompleted called");
        
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Show(); // Ensure window is shown
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Views
        services.AddSingleton<MainWindow>();
        
        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MigrationViewModel>();
        services.AddTransient<RollbackViewModel>();
        services.AddTransient<AnalysisViewModel>();
        services.AddTransient<CleanDepsViewModel>();
        services.AddTransient<CleanCpmViewModel>();
        
        // Register Lazy<T> for ViewModels
        services.AddTransient<Lazy<MigrationViewModel>>(provider => 
            new Lazy<MigrationViewModel>(() => provider.GetRequiredService<MigrationViewModel>()));
        services.AddTransient<Lazy<RollbackViewModel>>(provider => 
            new Lazy<RollbackViewModel>(() => provider.GetRequiredService<RollbackViewModel>()));
        services.AddTransient<Lazy<AnalysisViewModel>>(provider => 
            new Lazy<AnalysisViewModel>(() => provider.GetRequiredService<AnalysisViewModel>()));
        services.AddTransient<Lazy<CleanDepsViewModel>>(provider => 
            new Lazy<CleanDepsViewModel>(() => provider.GetRequiredService<CleanDepsViewModel>()));
        services.AddTransient<Lazy<CleanCpmViewModel>>(provider => 
            new Lazy<CleanCpmViewModel>(() => provider.GetRequiredService<CleanCpmViewModel>()));
        
        // UI Services
        // Use SimpleDialogService for now to avoid crashes
        services.AddSingleton<IDialogService, SimpleDialogService>();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core Services
        services.AddSingleton<ISdkStyleProjectGenerator, CleanSdkStyleProjectGenerator>();
        services.AddSingleton<IDirectoryBuildPropsReader, DirectoryBuildPropsReader>();
        services.AddSingleton<ITestProjectHandler, TestProjectHandler>();
        services.AddSingleton<IDesignerFileHandler, DesignerFileHandler>();
        services.AddSingleton<IPackageVersionConflictResolver, PackageVersionConflictResolver>();
        services.AddSingleton<IProjectFileScanner, ProjectFileScanner>();
        services.AddSingleton<ProjectParser>();
        services.AddSingleton<IProjectParser>(provider => provider.GetRequiredService<ProjectParser>());
        services.AddSingleton<IPackageReferenceMigrator, PackageReferenceMigrator>();
        services.AddSingleton<ITransitiveDependencyDetector, TransitiveDependencyDetector>();
        services.AddSingleton<IAssemblyInfoExtractor, AssemblyInfoExtractor>();
        services.AddSingleton<IDirectoryBuildPropsGenerator>(provider =>
            new DirectoryBuildPropsGenerator(
                provider.GetRequiredService<ILogger<DirectoryBuildPropsGenerator>>(),
                provider.GetRequiredService<IAuditService>(),
                MigrationOptions.Default));
        services.AddSingleton<ISolutionFileUpdater>(provider =>
            new SolutionFileUpdater(
                provider.GetRequiredService<ILogger<SolutionFileUpdater>>(),
                provider.GetRequiredService<IAuditService>(),
                provider.GetRequiredService<IBackupService>(),
                MigrationOptions.Default));
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ILockService, LockService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<ILocalPackageFilesCleaner, LocalPackageFilesCleaner>();
        services.AddSingleton<CpmVersionResolver>();
        services.AddSingleton<CpmPackageClassifier>();
        services.AddSingleton<ExistingCpmDetector>();
        services.AddSingleton<IImportScanner, ImportScanner>();
        services.AddSingleton<ITargetScanner, TargetScanner>();
        services.AddSingleton<IUserInteractionService, UiUserInteractionService>();
        services.AddSingleton<ICentralPackageManagementGenerator, CentralPackageManagementGenerator>();
        services.AddSingleton<IPostMigrationValidator, PostMigrationValidator>();
        services.AddSingleton<IMSBuildArtifactDetector, MSBuildArtifactDetector>();

        // Edge case detectors
        services.AddSingleton<ProjectTypeDetector>();
        services.AddSingleton<IBuildEventMigrator, BuildEventMigrator>();
        services.AddSingleton<INativeDependencyHandler, NativeDependencyHandler>();
        services.AddSingleton<ServiceReferenceDetector>();

        // Analysis and migration services
        services.AddSingleton<CustomTargetAnalyzer>();
        services.AddSingleton<IMigrationAnalyzer, MigrationAnalyzer>();
        services.AddSingleton<IConfigurationFileGenerator, ConfigurationFileGenerator>();

        // NuGet services
        services.AddSingleton<INuGetPackageResolver, NuGetPackageResolver>();
        services.AddSingleton<IAssemblyReferenceConverter, AssemblyReferenceConverter>();
        services.AddSingleton<PackageAssemblyResolver>();

        // Package caching (conditional)
        services.AddSingleton<IPackageVersionCache, MemoryPackageVersionCache>();

        // Register orchestrator with factory
        services.AddScoped<IMigrationOrchestrator>(provider =>
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
            var options = MigrationOptions.Default;
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
                options,
                packageCache);
        });
    }
}