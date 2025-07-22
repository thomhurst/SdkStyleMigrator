using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.ViewModels;

public class RollbackViewModel : ViewModelBase
{
    private readonly ILogger<RollbackViewModel> _logger;
    private readonly IBackupService _backupService;
    private readonly IDialogService _dialogService;
    
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

    public RollbackViewModel(ILogger<RollbackViewModel> logger, IBackupService backupService, IDialogService dialogService)
    {
        _logger = logger;
        _backupService = backupService;
        _dialogService = dialogService;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.SelectedSession,
            x => x.IsRunning,
            (dir, session, running) => !string.IsNullOrWhiteSpace(dir) && session != null && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync, outputScheduler: RxApp.MainThreadScheduler);
        RefreshSessionsCommand = ReactiveCommand.CreateFromTask(LoadBackupSessionsAsync);
        RunRollbackCommand = ReactiveCommand.CreateFromTask(RunRollbackAsync, canRun);
    }

    private async Task BrowseDirectoryAsync()
    {
        var result = await _dialogService.OpenFolderDialogAsync("Select Directory with Backups");
        if (result != null)
        {
            DirectoryPath = result;
        }
    }

    private async Task LoadBackupSessionsAsync()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        try
        {
            
            var sessions = await _backupService.ListBackupsAsync(DirectoryPath);
            
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

            
            var cts = new CancellationTokenSource();
            await _backupService.RollbackAsync(SelectedSession.BackupPath, cts.Token);

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