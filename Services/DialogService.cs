using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SdkMigrator.Abstractions;
using SdkMigrator.Views;

namespace SdkMigrator.Services;

public class DialogService : IDialogService
{
    public DialogService()
    {
    }
    
    public async Task<string?> OpenFolderDialogAsync(string title)
    {
        try
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFolderDialogAsync(title));
            }

            // Get the main window
            var window = GetMainWindow();
            if (window == null)
            {
                throw new InvalidOperationException("Could not find main window for dialog");
            }

            // Ensure window is loaded
            await EnsureWindowLoaded(window);

            // Check StorageProvider availability
            if (window.StorageProvider == null)
            {
                throw new InvalidOperationException(
                    "StorageProvider is not available. This may occur on Linux systems missing GTK dependencies. " +
                    "Try installing: sudo apt-get install libgtk-3-0 libgtk-3-dev");
            }

            // Open folder picker
            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            var result = await window.StorageProvider.OpenFolderPickerAsync(options);
            return result?.FirstOrDefault()?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DialogService Error: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"DialogService Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"DialogService Inner: {ex.InnerException.Message}");
            }
            return null;
        }
    }

    public async Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? fileTypes = null)
    {
        try
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFileDialogAsync(title, fileTypes));
            }

            // Get the main window
            var window = GetMainWindow();
            if (window == null)
            {
                throw new InvalidOperationException("Could not find main window for dialog");
            }

            // Ensure window is loaded
            await EnsureWindowLoaded(window);

            // Check StorageProvider availability
            if (window.StorageProvider == null)
            {
                throw new InvalidOperationException(
                    "StorageProvider is not available. This may occur on Linux systems missing GTK dependencies. " +
                    "Try installing: sudo apt-get install libgtk-3-0 libgtk-3-dev");
            }

            // Open file picker
            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            if (fileTypes != null)
            {
                options.FileTypeFilter = fileTypes;
            }

            var result = await window.StorageProvider.OpenFilePickerAsync(options);
            return result?.FirstOrDefault()?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DialogService Error: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"DialogService Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"DialogService Inner: {ex.InnerException.Message}");
            }
            return null;
        }
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private async Task EnsureWindowLoaded(Window window)
    {
        // If window is not loaded, wait a short time for it to initialize
        if (!window.IsLoaded)
        {
            await Task.Delay(50);
        }
    }

}