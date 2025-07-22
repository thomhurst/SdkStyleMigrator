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
using SdkMigrator.Models;
using SdkMigrator.Views;

namespace SdkMigrator.ViewModels;

public class AnalysisViewModel : ViewModelBase
{
    private readonly ILogger<AnalysisViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    private string _directoryPath = string.Empty;
    private bool _isRunning;
    private string _statusMessage = "Ready to analyze";
    private MigrationAnalysis? _analysisReport;
    private ObservableCollection<ProjectAnalysisViewModel> _projectAnalyses = new();

    public string DirectoryPath
    {
        get => _directoryPath;
        set => this.RaiseAndSetIfChanged(ref _directoryPath, value);
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

    public MigrationAnalysis? AnalysisReport
    {
        get => _analysisReport;
        set => this.RaiseAndSetIfChanged(ref _analysisReport, value);
    }

    public ObservableCollection<ProjectAnalysisViewModel> ProjectAnalyses => _projectAnalyses;

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand RunAnalysisCommand { get; }

    public AnalysisViewModel(ILogger<AnalysisViewModel> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            (dir, running) => !string.IsNullOrWhiteSpace(dir) && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        RunAnalysisCommand = ReactiveCommand.CreateFromTask(RunAnalysisAsync, canRun);
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

    private async Task RunAnalysisAsync()
    {
        try
        {
            IsRunning = true;
            StatusMessage = "Analyzing projects...";

            using var scope = _serviceProvider.CreateScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<IMigrationAnalyzer>();
            
            var cts = new CancellationTokenSource();
            var report = await analyzer.AnalyzeProjectsAsync(DirectoryPath, cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AnalysisReport = report;
                ProjectAnalyses.Clear();
                
                foreach (var project in report.ProjectAnalyses)
                {
                    ProjectAnalyses.Add(new ProjectAnalysisViewModel(project));
                }
                
                StatusMessage = $"Analysis complete. Risk: {report.OverallRisk}, Effort: {report.EstimatedManualEffortHours}h";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed");
            StatusMessage = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public class ProjectAnalysisViewModel : ViewModelBase
{
    private readonly ProjectAnalysis _analysis;

    public string ProjectName => _analysis.ProjectName;
    public string ProjectType => _analysis.ProjectType.ToString();
    public MigrationRiskLevel RiskLevel => _analysis.RiskLevel;
    public bool CanMigrate => _analysis.CanMigrate;
    public int IssueCount => _analysis.Issues.Count;
    public string RiskColor => RiskLevel switch
    {
        MigrationRiskLevel.Low => "#10B981",
        MigrationRiskLevel.Medium => "#F59E0B",
        MigrationRiskLevel.High => "#EF4444",
        _ => "#6B7280"
    };

    public ObservableCollection<MigrationIssue> Issues { get; }

    public ProjectAnalysisViewModel(ProjectAnalysis analysis)
    {
        _analysis = analysis;
        Issues = new ObservableCollection<MigrationIssue>(_analysis.Issues);
    }
}