using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.ViewModels;
using SdkMigrator.Views;

namespace SdkMigrator.Services;

public class UiUserInteractionService : IUserInteractionService
{
    private readonly ILogger<UiUserInteractionService> _logger;

    public UiUserInteractionService(ILogger<UiUserInteractionService> logger)
    {
        _logger = logger;
        _logger.LogInformation("UiUserInteractionService created");
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    public async Task<ImportScanResult> SelectImportsAsync(
        ImportScanResult scanResult, 
        ImportSelectionOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SelectImportsAsync called - InteractiveMode: {InteractiveMode}, HasCustomImports: {HasCustomImports}, TotalImports: {TotalImports}", 
            options.InteractiveMode, scanResult.HasCustomImports, scanResult.TotalImports);
            
        if (!options.InteractiveMode || !scanResult.HasCustomImports)
        {
            _logger.LogInformation("Skipping import selection - InteractiveMode: {InteractiveMode}, HasCustomImports: {HasCustomImports}", 
                options.InteractiveMode, scanResult.HasCustomImports);
            return scanResult;
        }

        // Create and show dialog on UI thread
        _logger.LogInformation("About to show import selection dialog");
        var result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = GetMainWindow();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for import selection dialog");
                return scanResult;
            }

            _logger.LogInformation("Creating import selection dialog");
            var dialog = new ImportSelectionDialog
            {
                DataContext = new ImportSelectionViewModel(scanResult)
            };

            _logger.LogInformation("Showing import selection dialog");
            await dialog.ShowDialog(window);
            
            var vm = dialog.DataContext as ImportSelectionViewModel;
            return vm?.ScanResult ?? scanResult;
        });

        return result;
    }

    public async Task<TargetScanResult> SelectTargetsAsync(
        TargetScanResult scanResult,
        TargetSelectionOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SelectTargetsAsync called - InteractiveMode: {InteractiveMode}, HasCustomTargets: {HasCustomTargets}, TotalTargets: {TotalTargets}", 
            options.InteractiveMode, scanResult.HasCustomTargets, scanResult.TotalTargets);
            
        if (!options.InteractiveMode || !scanResult.HasCustomTargets)
        {
            _logger.LogInformation("Skipping target selection - InteractiveMode: {InteractiveMode}, HasCustomTargets: {HasCustomTargets}", 
                options.InteractiveMode, scanResult.HasCustomTargets);
            return scanResult;
        }

        // Create and show dialog on UI thread
        _logger.LogInformation("About to show target selection dialog");
        var result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = GetMainWindow();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for target selection dialog");
                return scanResult;
            }

            _logger.LogInformation("Creating target selection dialog");
            var dialog = new TargetSelectionDialog
            {
                DataContext = new TargetSelectionViewModel(scanResult)
            };

            _logger.LogInformation("Showing target selection dialog");
            await dialog.ShowDialog(window);
            
            var vm = dialog.DataContext as TargetSelectionViewModel;
            return vm?.ScanResult ?? scanResult;
        });

        return result;
    }

    public async Task<bool> AskYesNoAsync(string question, bool defaultValue = true)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = GetMainWindow();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for yes/no dialog");
                return defaultValue;
            }

            // For now, return default value - we can implement a custom dialog later
            _logger.LogInformation($"Yes/No prompt: {question} - returning default: {defaultValue}");
            return defaultValue;
        });
    }

    public async Task<int> SelectFromListAsync(string prompt, List<string> options)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = GetMainWindow();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for selection dialog");
                return 0;
            }

            var dialog = new ListSelectionDialog
            {
                DataContext = new ListSelectionViewModel(prompt, options)
            };

            await dialog.ShowDialog(window);
            
            var vm = dialog.DataContext as ListSelectionViewModel;
            return vm?.SelectedIndex ?? 0;
        });
    }

    public void ShowInformation(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logger.LogInformation(message);
        });
    }

    public void ShowWarning(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logger.LogWarning(message);
        });
    }
}