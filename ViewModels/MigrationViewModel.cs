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
using ReactiveUI.Validation.Extensions;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.ViewModels;

public class MigrationViewModel : ViewModelBase
{
    private readonly ILogger<MigrationViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const int MaxLogMessages = 1000;
    private readonly IDialogService _dialogService;
    
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

    public MigrationViewModel(ILogger<MigrationViewModel> logger, IServiceProvider serviceProvider, IDialogService dialogService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;

        // Set up validation rules
        this.ValidationRule(
            viewModel => viewModel.DirectoryPath,
            path => !string.IsNullOrWhiteSpace(path),
            "Solution directory is required");
            
        this.ValidationRule(
            viewModel => viewModel.DirectoryPath,
            path => string.IsNullOrWhiteSpace(path) || Directory.Exists(path),
            "Directory must exist");
            
        this.ValidationRule(
            viewModel => viewModel.OutputDirectory,
            path => string.IsNullOrWhiteSpace(path) || Directory.Exists(path),
            "Output directory must exist if specified");
            
        this.ValidationRule(
            viewModel => viewModel.TargetFramework,
            framework => string.IsNullOrWhiteSpace(framework) || IsValidFramework(framework),
            "Invalid target framework format");

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            x => x.ValidationContext.IsValid,
            (dir, running, isValid) => !string.IsNullOrWhiteSpace(dir) && !running && isValid);

        // Create commands with exception handling
        var browseCmd = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        browseCmd.ThrownExceptions.Subscribe(ex =>
        {
            Console.WriteLine($"BrowseDirectoryCommand exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            StatusMessage = $"Error: {ex.Message}";
        });
        BrowseDirectoryCommand = browseCmd;
        
        var browseOutputCmd = ReactiveCommand.CreateFromTask(BrowseOutputDirectoryAsync);
        browseOutputCmd.ThrownExceptions.Subscribe(ex =>
        {
            Console.WriteLine($"BrowseOutputDirectoryCommand exception: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        });
        BrowseOutputDirectoryCommand = browseOutputCmd;
        
        var browseNugetCmd = ReactiveCommand.CreateFromTask(BrowseNugetConfigAsync);
        browseNugetCmd.ThrownExceptions.Subscribe(ex =>
        {
            Console.WriteLine($"BrowseNugetConfigCommand exception: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        });
        BrowseNugetConfigCommand = browseNugetCmd;
        
        RunMigrationCommand = ReactiveCommand.CreateFromTask(RunMigrationAsync, canRun);
        ClearLogsCommand = ReactiveCommand.Create(() => LogMessages.Clear());
    }

    private async Task BrowseDirectoryAsync()
    {
        try
        {
            // Add debug logging
            Console.WriteLine("BrowseDirectoryAsync called");
            Console.WriteLine($"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            Console.WriteLine($"Is UI Thread: {Dispatcher.UIThread.CheckAccess()}");
            
            var result = await _dialogService.OpenFolderDialogAsync("Select Solution Directory");
            Console.WriteLine($"Dialog result: {result ?? "null"}");
            
            if (result != null)
            {
                DirectoryPath = result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BrowseDirectoryAsync error: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            
            // Log error to user
            _logger.LogError(ex, "Failed to open folder browser dialog");
            AddLogMessage($"Error opening folder browser: {ex.Message}");
            AddLogMessage("This may be due to missing GTK dependencies on Linux/WSL");
            AddLogMessage("Try: sudo apt-get install libgtk-3-0 zenity");
            
            // Show a manual input fallback
            StatusMessage = "Browser failed - please type the path manually";
        }
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        try
        {
            var result = await _dialogService.OpenFolderDialogAsync("Select Output Directory");
            if (result != null)
            {
                OutputDirectory = result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder browser dialog for output directory");
            AddLogMessage($"Error opening folder browser: {ex.Message}");
            StatusMessage = "Browser failed - please type the output path manually";
        }
    }

    private async Task BrowseNugetConfigAsync()
    {
        try
        {
            var fileTypes = new[]
            {
                new FilePickerFileType("NuGet Config") { Patterns = new[] { "*.config" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            };
            
            var result = await _dialogService.OpenFileDialogAsync("Select NuGet Config File", fileTypes);
            if (result != null)
            {
                NugetConfig = result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file browser dialog for NuGet config");
            AddLogMessage($"Error opening file browser: {ex.Message}");
            StatusMessage = "Browser failed - please type the NuGet config path manually";
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
                    AddLogMessage($"⚠ {report.TotalProjectsFailed} project(s) failed to migrate");
                }

                foreach (var result in report.Results.Where(r => r.Warnings.Any()))
                {
                    AddLogMessage($"⚠ {result.ProjectPath}:");
                    foreach (var warning in result.Warnings)
                    {
                        AddLogMessage($"  - {warning}");
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
                AddLogMessage($"❌ Error: {ex.Message}");
            });
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AddLogMessage(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => 
        {
            LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            
            // Limit log messages to prevent unbounded growth
            while (LogMessages.Count > MaxLogMessages)
            {
                LogMessages.RemoveAt(0);
            }
        });
    }
    
    private static bool IsValidFramework(string framework)
    {
        // Basic validation for .NET framework monikers
        return System.Text.RegularExpressions.Regex.IsMatch(
            framework, 
            @"^(net\d+\.\d+|net\d+|netstandard\d+\.\d+|netcoreapp\d+\.\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}