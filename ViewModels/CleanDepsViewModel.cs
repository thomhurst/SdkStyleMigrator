using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;
using SdkMigrator.Views;

namespace SdkMigrator.ViewModels;

public class CleanDepsViewModel : ViewModelBase
{
    private readonly ILogger<CleanDepsViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    private string _directoryPath = string.Empty;
    private bool _dryRun;
    private bool _isRunning;
    private string _statusMessage = "Ready to clean dependencies";
    private ObservableCollection<string> _logMessages = new();
    private int _totalRemoved;

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

    public int TotalRemoved
    {
        get => _totalRemoved;
        set => this.RaiseAndSetIfChanged(ref _totalRemoved, value);
    }

    public ObservableCollection<string> LogMessages => _logMessages;

    public ICommand BrowseDirectoryCommand { get; }
    public ICommand RunCleanupCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public CleanDepsViewModel(ILogger<CleanDepsViewModel> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            (dir, running) => !string.IsNullOrWhiteSpace(dir) && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync);
        RunCleanupCommand = ReactiveCommand.CreateFromTask(RunCleanupAsync, canRun);
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

    private async Task RunCleanupAsync()
    {
        try
        {
            IsRunning = true;
            TotalRemoved = 0;
            StatusMessage = "Scanning for transitive dependencies...";
            LogMessages.Clear();

            using var scope = _serviceProvider.CreateScope();
            var projectScanner = scope.ServiceProvider.GetRequiredService<IProjectFileScanner>();
            var transitiveDepsDetector = scope.ServiceProvider.GetRequiredService<ITransitiveDependencyDetector>();
            
            var cts = new CancellationTokenSource();
            var projectFiles = await projectScanner.ScanForProjectFilesAsync(DirectoryPath, cts.Token);
            var sdkStyleProjects = projectFiles.Where(IsSdkStyleProject).ToList();

            if (!sdkStyleProjects.Any())
            {
                StatusMessage = "No SDK-style projects found";
                return;
            }

            AddLogMessage($"Found {sdkStyleProjects.Count} SDK-style projects");

            foreach (var projectFile in sdkStyleProjects)
            {
                try
                {
                    AddLogMessage($"Processing {Path.GetFileName(projectFile)}...");
                    
                    // Read project file and extract package references
                    var projectDoc = XDocument.Load(projectFile);
                    var packageRefs = projectDoc.Descendants("PackageReference")
                        .Select(pr => new Models.PackageReference
                        {
                            PackageId = pr.Attribute("Include")?.Value ?? string.Empty,
                            Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? string.Empty
                        })
                        .Where(pr => !string.IsNullOrWhiteSpace(pr.PackageId))
                        .ToList();

                    if (!packageRefs.Any())
                    {
                        AddLogMessage("  No package references found");
                        continue;
                    }

                    // Detect transitive dependencies
                    var transitivePackages = await transitiveDepsDetector.DetectTransitiveDependenciesAsync(
                        packageRefs, 
                        cts.Token);

                    var removedCount = 0;
                    if (transitivePackages.Any())
                    {
                        if (!DryRun)
                        {
                            // Remove transitive packages from project file
                            foreach (var transitivePkg in transitivePackages)
                            {
                                var elements = projectDoc.Descendants("PackageReference")
                                    .Where(pr => pr.Attribute("Include")?.Value == transitivePkg.PackageId)
                                    .ToList();
                                
                                foreach (var element in elements)
                                {
                                    element.Remove();
                                    removedCount++;
                                    TotalRemoved++;
                                    AddLogMessage($"  - Removed: {transitivePkg.PackageId}");
                                }
                            }

                            if (removedCount > 0)
                            {
                                projectDoc.Save(projectFile);
                            }
                        }
                        else
                        {
                            foreach (var transitivePkg in transitivePackages)
                            {
                                removedCount++;
                                TotalRemoved++;
                                AddLogMessage($"  - Would remove: {transitivePkg.PackageId}");
                            }
                        }
                    }

                    if (removedCount == 0)
                    {
                        AddLogMessage("  No transitive dependencies found");
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage($"  ❌ Error: {ex.Message}");
                }
            }

            StatusMessage = $"Cleanup complete. Removed {TotalRemoved} transitive dependencies.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed");
            StatusMessage = $"Cleanup failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool IsSdkStyleProject(string projectPath)
    {
        try
        {
            var content = File.ReadAllText(projectPath);
            return content.Contains("<Project Sdk=", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void AddLogMessage(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
    }
}