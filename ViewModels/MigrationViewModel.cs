using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Views;

namespace SdkMigrator.ViewModels;

public class MigrationViewModel : ViewModelBase
{
    private readonly ILogger<MigrationViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    private string _directoryPath = string.Empty;
    private string? _outputDirectory;
    private string? _targetFramework;
    private string? _targetFrameworks;
    private bool _enableCpm;
    private string _cpmVersionStrategy = "UseHighest";
    private bool _cpmPreferStable = true;
    private bool _dryRun;
    private bool _createBackup = true;
    private bool _force;
    private int _parallelism = 1;
    private string _logLevel = "Information";
    private bool _offline;
    private string? _nugetConfig;
    private bool _disableCache;
    private int? _cacheTtl;
    private bool _interactiveImports;
    private bool _interactiveTargets;
    private bool _isRunning;
    private double _progress;
    private string _statusMessage = "Ready to migrate";
    private ObservableCollection<string> _logMessages = new();

    public string DirectoryPath
    {
        get => _directoryPath;
        set => this.RaiseAndSetIfChanged(ref _directoryPath, value);
    }

    public string? OutputDirectory
    {
        get => _outputDirectory;
        set => this.RaiseAndSetIfChanged(ref _outputDirectory, value);
    }

    public string? TargetFramework
    {
        get => _targetFramework;
        set => this.RaiseAndSetIfChanged(ref _targetFramework, value);
    }

    public string? TargetFrameworks
    {
        get => _targetFrameworks;
        set => this.RaiseAndSetIfChanged(ref _targetFrameworks, value);
    }

    public bool EnableCpm
    {
        get => _enableCpm;
        set => this.RaiseAndSetIfChanged(ref _enableCpm, value);
    }

    public string CpmVersionStrategy
    {
        get => _cpmVersionStrategy;
        set => this.RaiseAndSetIfChanged(ref _cpmVersionStrategy, value);
    }

    public bool CpmPreferStable
    {
        get => _cpmPreferStable;
        set => this.RaiseAndSetIfChanged(ref _cpmPreferStable, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => this.RaiseAndSetIfChanged(ref _dryRun, value);
    }

    public bool CreateBackup
    {
        get => _createBackup;
        set => this.RaiseAndSetIfChanged(ref _createBackup, value);
    }

    public bool Force
    {
        get => _force;
        set => this.RaiseAndSetIfChanged(ref _force, value);
    }

    public int Parallelism
    {
        get => _parallelism;
        set => this.RaiseAndSetIfChanged(ref _parallelism, value);
    }

    public string LogLevel
    {
        get => _logLevel;
        set => this.RaiseAndSetIfChanged(ref _logLevel, value);
    }

    public bool Offline
    {
        get => _offline;
        set => this.RaiseAndSetIfChanged(ref _offline, value);
    }

    public string? NugetConfig
    {
        get => _nugetConfig;
        set => this.RaiseAndSetIfChanged(ref _nugetConfig, value);
    }

    public bool DisableCache
    {
        get => _disableCache;
        set => this.RaiseAndSetIfChanged(ref _disableCache, value);
    }

    public int? CacheTtl
    {
        get => _cacheTtl;
        set => this.RaiseAndSetIfChanged(ref _cacheTtl, value);
    }

    public bool InteractiveImports
    {
        get => _interactiveImports;
        set => this.RaiseAndSetIfChanged(ref _interactiveImports, value);
    }

    public bool InteractiveTargets
    {
        get => _interactiveTargets;
        set => this.RaiseAndSetIfChanged(ref _interactiveTargets, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ObservableCollection<string> LogMessages => _logMessages;

    public ObservableCollection<string> CpmVersionStrategies { get; } = new()
    {
        "UseHighest",
        "UseLowest",
        "UseLatestStable",
        "UseMostCommon",
        "SemanticCompatible",
        "FrameworkCompatible"
    };

    public ObservableCollection<string> LogLevels { get; } = new()
    {
        "Trace",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Critical"
    };

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand BrowseOutputDirectoryCommand { get; }
    public ICommand BrowseNugetConfigCommand { get; }
    public ICommand RunMigrationCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public MigrationViewModel(ILogger<MigrationViewModel> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            (dir, running) => !string.IsNullOrWhiteSpace(dir) && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        BrowseOutputDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseOutputDirectoryAsync);
        BrowseNugetConfigCommand = ReactiveCommand.CreateFromTask(BrowseNugetConfigAsync);
        RunMigrationCommand = ReactiveCommand.CreateFromTask(RunMigrationAsync, canRun);
        ClearLogsCommand = ReactiveCommand.Create(() => LogMessages.Clear());
    }

    private async Task BrowseDirectoryAsync()
    {
        var topLevel = App.Services?.GetService<MainWindow>();
        
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Solution Directory",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            DirectoryPath = result[0].Path.LocalPath;
        }
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        var topLevel = App.Services?.GetService<MainWindow>();
        
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Directory",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            OutputDirectory = result[0].Path.LocalPath;
        }
    }

    private async Task BrowseNugetConfigAsync()
    {
        var topLevel = App.Services?.GetService<MainWindow>();
        
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select NuGet Config File",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("NuGet Config") { Patterns = new[] { "*.config" } },
                new("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (result.Count > 0)
        {
            NugetConfig = result[0].Path.LocalPath;
        }
    }

    private async Task RunMigrationAsync()
    {
        try
        {
            IsRunning = true;
            Progress = 0;
            StatusMessage = "Starting migration...";
            LogMessages.Clear();

            var options = new MigrationOptions
            {
                DirectoryPath = DirectoryPath,
                OutputDirectory = OutputDirectory,
                TargetFramework = TargetFramework,
                TargetFrameworks = TargetFrameworks?.Split(';', StringSplitOptions.RemoveEmptyEntries),
                EnableCentralPackageManagement = EnableCpm,
                CpmOptions = new CpmVersionResolutionOptions
                {
                    Strategy = Enum.Parse<CpmVersionResolutionStrategy>(CpmVersionStrategy),
                    PreferStableVersions = CpmPreferStable
                },
                DryRun = DryRun,
                CreateBackup = CreateBackup,
                Force = Force,
                MaxDegreeOfParallelism = Parallelism,
                LogLevel = LogLevel,
                UseOfflineMode = Offline,
                NuGetConfigPath = NugetConfig,
                DisableCache = DisableCache,
                CacheTTLMinutes = CacheTtl,
                InteractiveImportSelection = InteractiveImports,
                InteractiveTargetSelection = InteractiveTargets
            };

            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IMigrationOrchestrator>();

            var cts = new CancellationTokenSource();
            var report = await orchestrator.MigrateProjectsAsync(options.DirectoryPath, cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Progress = 100;
                StatusMessage = $"Migration completed. {report.TotalProjectsMigrated} projects migrated, {report.TotalProjectsFailed} failed.";
                
                if (report.TotalProjectsFailed > 0)
                {
                    LogMessages.Add($"⚠ {report.TotalProjectsFailed} project(s) failed to migrate");
                }

                foreach (var result in report.Results.Where(r => r.Warnings.Any()))
                {
                    LogMessages.Add($"⚠ {result.ProjectPath}:");
                    foreach (var warning in result.Warnings)
                    {
                        LogMessages.Add($"  - {warning}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Migration failed: {ex.Message}";
                LogMessages.Add($"❌ Error: {ex.Message}");
            });
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AddLogMessage(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
    }
}