using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;

namespace SdkMigrator.ViewModels;

public class CleanDepsViewModel : ViewModelBase
{
    private readonly ILogger<CleanDepsViewModel> _logger;
    private readonly IProjectFileScanner _projectScanner;
    private readonly ITransitiveDependencyDetector _transitiveDepsDetector;
    private const int MaxLogMessages = 1000;
    private readonly IDialogService _dialogService;
    
    private string _directoryPath = string.Empty;
    private bool _dryRun;
    private bool _conservativeMode = true;
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

    public bool ConservativeMode
    {
        get => _conservativeMode;
        set => this.RaiseAndSetIfChanged(ref _conservativeMode, value);
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

    public CleanDepsViewModel(ILogger<CleanDepsViewModel> logger, IProjectFileScanner projectScanner, ITransitiveDependencyDetector transitiveDepsDetector, IDialogService dialogService)
    {
        _logger = logger;
        _projectScanner = projectScanner;
        _transitiveDepsDetector = transitiveDepsDetector;
        _dialogService = dialogService;

        var canRun = this.WhenAnyValue(
            x => x.DirectoryPath,
            x => x.IsRunning,
            (dir, running) => !string.IsNullOrWhiteSpace(dir) && !running);

        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(BrowseDirectoryAsync, outputScheduler: RxApp.MainThreadScheduler);
        RunCleanupCommand = ReactiveCommand.CreateFromTask(RunCleanupAsync, canRun);
        ClearLogsCommand = ReactiveCommand.Create(() => LogMessages.Clear());
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
            TotalRemoved = 0;
            StatusMessage = "Scanning for transitive dependencies...";
            LogMessages.Clear();

            
            var cts = new CancellationTokenSource();
            var projectFiles = await _projectScanner.ScanForProjectFilesAsync(DirectoryPath, cts.Token);
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
                    var root = projectDoc.Root;
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

                    // Get project references to check their package dependencies
                    var projectRefs = root.Descendants("ProjectReference")
                        .Select(pr => pr.Attribute("Include")?.Value)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Select(path => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, path!)))
                        .Where(fullPath => File.Exists(fullPath))
                        .ToList();

                    // Collect packages from referenced projects with their versions
                    var referencedProjectPackages = new Dictionary<string, (string ProjectPath, string Version)>();

                    foreach (var projRef in projectRefs)
                    {
                        try
                        {
                            var projDoc = XDocument.Load(projRef);
                            var depPackages = projDoc.Descendants("PackageReference")
                                .Select(pr => new
                                {
                                    PackageId = pr.Attribute("Include")?.Value,
                                    Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value
                                })
                                .Where(p => !string.IsNullOrEmpty(p.PackageId) && !string.IsNullOrEmpty(p.Version))
                                .ToList();

                            foreach (var pkg in depPackages)
                            {
                                if (!referencedProjectPackages.ContainsKey(pkg.PackageId!) || 
                                    ConservativeMode) // In conservative mode, don't overwrite if already found
                                {
                                    referencedProjectPackages[pkg.PackageId!] = (projRef, pkg.Version!);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLogMessage($"  Warning: Failed to analyze project reference: {Path.GetFileName(projRef)}");
                        }
                    }

                    // Detect transitive dependencies
                    var projectDirectory = Path.GetDirectoryName(projectFile);
                    var transitivePackages = await _transitiveDepsDetector.DetectTransitiveDependenciesAsync(
                        packageRefs, 
                        projectDirectory,
                        cts.Token);

                    // Filter packages based on project references
                    var packagesToRemove = new List<Models.PackageReference>();
                    
                    foreach (var package in packageRefs.Where(p => p.IsTransitive))
                    {
                        var shouldRemove = true;
                        var reason = "transitive dependency";

                        // Check if this package is provided by a project reference
                        if (referencedProjectPackages.TryGetValue(package.PackageId, out var refInfo))
                        {
                            if (ConservativeMode)
                            {
                                // In conservative mode, only remove if versions match exactly
                                if (string.Equals(package.Version, refInfo.Version, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldRemove = true;
                                    reason = $"provided by referenced project '{Path.GetFileName(refInfo.ProjectPath)}' with same version ({refInfo.Version})";
                                }
                                else
                                {
                                    shouldRemove = false;
                                    AddLogMessage($"  - Keeping {package.PackageId} ({package.Version}) - referenced project has different version ({refInfo.Version})");
                                }
                            }
                            else
                            {
                                // In aggressive mode, remove regardless of version
                                shouldRemove = true;
                                reason = $"provided by referenced project '{Path.GetFileName(refInfo.ProjectPath)}'";
                            }
                        }

                        if (shouldRemove)
                        {
                            packagesToRemove.Add(package);
                            package.IsTransitive = true;
                            package.TransitiveReason = reason;
                        }
                    }

                    var removedCount = 0;
                    if (packagesToRemove.Any())
                    {
                        if (!DryRun)
                        {
                            // Remove transitive packages from project file
                            foreach (var transitivePkg in packagesToRemove)
                            {
                                var elements = projectDoc.Descendants("PackageReference")
                                    .Where(pr => pr.Attribute("Include")?.Value == transitivePkg.PackageId)
                                    .ToList();
                                
                                foreach (var element in elements)
                                {
                                    element.Remove();
                                    removedCount++;
                                    TotalRemoved++;
                                    AddLogMessage($"  - Removed: {transitivePkg.PackageId} ({transitivePkg.TransitiveReason})");
                                }
                            }

                            if (removedCount > 0)
                            {
                                projectDoc.Save(projectFile);
                            }
                        }
                        else
                        {
                            foreach (var transitivePkg in packagesToRemove)
                            {
                                removedCount++;
                                TotalRemoved++;
                                AddLogMessage($"  - Would remove: {transitivePkg.PackageId} ({transitivePkg.TransitiveReason})");
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
}