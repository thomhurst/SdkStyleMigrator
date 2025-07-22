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

public class RollbackViewModel : ViewModelBase
{
    private readonly ILogger<RollbackViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    private string _directoryPath = string.Empty;
    private bool _isRunning;
    private string _statusMessage = "Ready to rollback";
    private ObservableCollection<BackupSessionViewModel> _backupSessions = new();
    private BackupSessionViewModel? _selectedSession;

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _directoryPath, value);
            _ = LoadBackupSessionsAsync();
        }
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

    public ObservableCollection<BackupSessionViewModel> BackupSessions => _backupSessions;

    public BackupSessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set => this.RaiseAndSetIfChanged(ref _selectedSession, value);
    }

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand RefreshSessionsCommand { get; }
    public ICommand RunRollbackCommand { get; }

    public RollbackViewModel(ILogger<RollbackViewModel> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.SelectedSession,
            x => x.IsRunning,
            (dir, session, running) => !string.IsNullOrWhiteSpace(dir) && session != null && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        RefreshSessionsCommand = ReactiveCommand.CreateFromTask(LoadBackupSessionsAsync);
        RunRollbackCommand = ReactiveCommand.CreateFromTask(RunRollbackAsync, canRun);
    }

    private async Task BrowseDirectoryAsync()
    {
        var topLevel = App.Services?.GetService<MainWindow>();
        
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Directory with Backups",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            DirectoryPath = result[0].Path.LocalPath;
        }
    }

    private async Task LoadBackupSessionsAsync()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            
            var sessions = await backupService.ListBackupsAsync(DirectoryPath);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BackupSessions.Clear();
                foreach (var session in sessions.OrderByDescending(s => s.StartTime))
                {
                    BackupSessions.Add(new BackupSessionViewModel(session));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load backup sessions");
            StatusMessage = $"Failed to load backups: {ex.Message}";
        }
    }

    private async Task RunRollbackAsync()
    {
        if (SelectedSession == null)
            return;

        try
        {
            IsRunning = true;
            StatusMessage = "Running rollback...";

            using var scope = _serviceProvider.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            
            var cts = new CancellationTokenSource();
            await backupService.RollbackAsync(SelectedSession.BackupPath, cts.Token);

            StatusMessage = "Rollback completed successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed");
            StatusMessage = $"Rollback failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public class BackupSessionViewModel : ViewModelBase
{
    private readonly BackupSession _session;

    public string SessionId => _session.SessionId;
    public DateTime StartTime => _session.StartTime;
    public string BackupPath => _session.BackupDirectory;
    public int FileCount => _session.BackedUpFiles.Count;
    public string DisplayName => $"{StartTime:yyyy-MM-dd HH:mm:ss} - {FileCount} files";

    public BackupSessionViewModel(BackupSession session)
    {
        _session = session;
    }
}