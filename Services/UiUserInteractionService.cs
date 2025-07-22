using System.Reactive.Linq;
using Avalonia.Controls;
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
    }

    public async Task<ImportScanResult> SelectImportsAsync(
        ImportScanResult scanResult, 
        ImportSelectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.InteractiveMode || !scanResult.HasCustomImports)
        {
            return scanResult;
        }

        // Create and show dialog on UI thread
        var result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = App.Services?.GetService<MainWindow>();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for import selection dialog");
                return scanResult;
            }

            var dialog = new ImportSelectionDialog
            {
                DataContext = new ImportSelectionViewModel(scanResult)
            };

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
        if (!options.InteractiveMode || !scanResult.HasCustomTargets)
        {
            return scanResult;
        }

        // Create and show dialog on UI thread
        var result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = App.Services?.GetService<MainWindow>();
            
            if (window == null)
            {
                _logger.LogWarning("Could not get window for target selection dialog");
                return scanResult;
            }

            var dialog = new TargetSelectionDialog
            {
                DataContext = new TargetSelectionViewModel(scanResult)
            };

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
            var window = App.Services?.GetService<MainWindow>();
            
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
            var window = App.Services?.GetService<MainWindow>();
            
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