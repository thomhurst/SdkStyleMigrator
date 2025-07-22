using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;

namespace SdkMigrator.ViewModels;

public class CleanCpmViewModel : ViewModelBase
{
    private readonly ILogger<CleanCpmViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    
    private string _directoryPath = string.Empty;
    private bool _dryRun;
    private bool _isRunning;
    private string _statusMessage = "Ready to clean CPM";
    private ObservableCollection<string> _removedPackages = new();

    public string DirectoryPath
    {
        get => _directoryPath;
        set => this.RaiseAndSetIfChanged(ref _directoryPath, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => this.RaiseAndSetIfChanged(ref _dryRun, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ObservableCollection<string> RemovedPackages => _removedPackages;

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand RunCleanupCommand { get; }

    public CleanCpmViewModel(ILogger<CleanCpmViewModel> logger, IServiceProvider serviceProvider, IDialogService dialogService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            (dir, running) => !string.IsNullOrWhiteSpace(dir) && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        RunCleanupCommand = ReactiveCommand.CreateFromTask(RunCleanupAsync, canRun);
    }

    private async Task BrowseDirectoryAsync()
    {
        var result = await _dialogService.OpenFolderDialogAsync("Select Solution Directory");
        if (result != null)
        {
            DirectoryPath = result;
        }
    }

    private async Task RunCleanupAsync()
    {
        try
        {
            IsRunning = true;
            StatusMessage = "Scanning for unused packages...";
            RemovedPackages.Clear();

            using var scope = _serviceProvider.CreateScope();
            var cpmGenerator = scope.ServiceProvider.GetRequiredService<ICentralPackageManagementGenerator>();
            
            var cts = new CancellationTokenSource();
            var result = await cpmGenerator.CleanUnusedPackagesAsync(DirectoryPath, DryRun, cts.Token);

            if (result.Success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var package in result.RemovedPackages.OrderBy(p => p))
                    {
                        RemovedPackages.Add(package);
                    }
                    
                    StatusMessage = result.RemovedPackages.Any() 
                        ? $"Removed {result.RemovedPackages.Count} unused packages" 
                        : "No unused packages found";
                });
            }
            else
            {
                StatusMessage = $"Cleanup failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CPM cleanup failed");
            StatusMessage = $"Cleanup failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}