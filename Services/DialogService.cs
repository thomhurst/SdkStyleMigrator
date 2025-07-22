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
            Console.WriteLine($"DialogService.OpenFolderDialogAsync called with title: {title}");
            Console.WriteLine($"Application.Current is null: {Application.Current == null}");
            
            // Always run on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Console.WriteLine("DialogService: Not on UI thread, invoking on UI thread");
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFolderDialogAsync(title));
            }

            Console.WriteLine("DialogService: Running on UI thread");

            // Get the window
            Window? window = null;
            
            // Method 1: Try to get from application lifetime
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                window = desktop.MainWindow;
                Console.WriteLine($"DialogService: Got window from ApplicationLifetime: {window != null}");
            }
            
            // Method 2: If that fails, try DI
            if (window == null)
            {
                window = App.Services?.GetService<MainWindow>();
                Console.WriteLine($"DialogService: Got window from DI: {window != null}");
            }
            
            if (window == null)
            {
                Console.WriteLine("DialogService: Could not get window from any source");
                return null;
            }

            Console.WriteLine($"DialogService: Got window type: {window.GetType().Name}");
            Console.WriteLine($"DialogService: Window IsLoaded: {window.IsLoaded}");
            Console.WriteLine($"DialogService: Window IsVisible: {window.IsVisible}");
            Console.WriteLine($"DialogService: Window DataContext type: {window.DataContext?.GetType().Name ?? "null"}");

            // Wait for window to be fully loaded if needed
            if (!window.IsLoaded)
            {
                Console.WriteLine("DialogService: Window not loaded, waiting...");
                var tcs = new TaskCompletionSource<bool>();
                void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
                {
                    window.Loaded -= OnLoaded;
                    tcs.SetResult(true);
                }
                window.Loaded += OnLoaded;
                
                // Set a timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                cts.Token.Register(() => tcs.TrySetCanceled());
                
                try
                {
                    await tcs.Task;
                    Console.WriteLine("DialogService: Window loaded successfully");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("DialogService: Timeout waiting for window to load");
                    return null;
                }
            }

            // Create a simple test to see if the issue is with StorageProvider
            if (window.StorageProvider == null)
            {
                Console.WriteLine("DialogService: StorageProvider is null!");
                Console.WriteLine("DialogService: This often happens on Linux/WSL when native dialog dependencies are missing");
                Console.WriteLine("DialogService: Consider installing: sudo apt-get install libgtk-3-0");
                return null;
            }

            Console.WriteLine("DialogService: StorageProvider is available");
            Console.WriteLine($"DialogService: StorageProvider type: {window.StorageProvider.GetType().FullName}");

            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            Console.WriteLine("DialogService: About to open folder picker...");
            var result = await window.StorageProvider.OpenFolderPickerAsync(options);
            Console.WriteLine($"DialogService: Folder picker returned {result?.Count ?? 0} results");
            
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
            // Always run on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFileDialogAsync(title, fileTypes));
            }

            // Get the window
            Window? window = null;
            
            // Method 1: Try to get from application lifetime
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                window = desktop.MainWindow;
            }
            
            // Method 2: If that fails, try DI
            if (window == null)
            {
                window = App.Services?.GetService<MainWindow>();
            }
            
            if (window == null)
            {
                Console.WriteLine("DialogService: Could not get window from any source");
                return null;
            }

            Console.WriteLine($"DialogService: Got window type: {window.GetType().Name}");
            Console.WriteLine($"DialogService: Window IsLoaded: {window.IsLoaded}");
            Console.WriteLine($"DialogService: Window IsVisible: {window.IsVisible}");

            // Create a simple test to see if the issue is with StorageProvider
            if (window.StorageProvider == null)
            {
                Console.WriteLine("DialogService: StorageProvider is null!");
                return null;
            }

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            if (fileTypes != null)
            {
                options.FileTypeFilter = fileTypes;
            }

            Console.WriteLine("DialogService: About to open file picker...");
            var result = await window.StorageProvider.OpenFilePickerAsync(options);
            Console.WriteLine($"DialogService: File picker returned {result?.Count ?? 0} results");
            
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

}