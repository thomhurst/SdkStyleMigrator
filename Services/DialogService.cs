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
        // Perform pre-flight environment checks on initialization
        PerformEnvironmentPreflightChecks();
    }

    private void PerformEnvironmentPreflightChecks()
    {
        Console.WriteLine("=== DIALOG SERVICE PRE-FLIGHT CHECKS ===");
        
        // Check critical environment variables
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var wslDistro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
        var xdgDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var xdgSession = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        
        Console.WriteLine($"Environment Check Results:");
        Console.WriteLine($"  DISPLAY: {display ?? "⚠️  Not set"}");
        Console.WriteLine($"  WSL_DISTRO_NAME: {wslDistro ?? "Not WSL"}");
        Console.WriteLine($"  XDG_CURRENT_DESKTOP: {xdgDesktop ?? "⚠️  Not set"}");
        Console.WriteLine($"  XDG_SESSION_TYPE: {xdgSession ?? "⚠️  Not set"}");
        Console.WriteLine($"  WAYLAND_DISPLAY: {waylandDisplay ?? "Not set"}");
        
        // WSL-specific checks
        if (!string.IsNullOrEmpty(wslDistro))
        {
            Console.WriteLine($"WSL Environment Detected:");
            Console.WriteLine($"  Distribution: {wslDistro}");
            if (string.IsNullOrEmpty(display))
            {
                Console.WriteLine("  ❌ CRITICAL: DISPLAY variable not set in WSL");
                Console.WriteLine("  💡 Try: export DISPLAY=:0.0 or install VcXsrv/X410");
            }
            else
            {
                Console.WriteLine($"  ✅ DISPLAY variable is set: {display}");
            }
        }
        
        // Check if we're running in a graphical environment
        bool hasGraphicalEnvironment = !string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(waylandDisplay);
        Console.WriteLine($"Graphical Environment: {(hasGraphicalEnvironment ? "✅ Available" : "❌ Not detected")}");
        
        // Check X11/Wayland availability
        if (!string.IsNullOrEmpty(display))
        {
            Console.WriteLine($"X11 Display Server: ✅ Available ({display})");
        }
        else if (!string.IsNullOrEmpty(waylandDisplay))
        {
            Console.WriteLine($"Wayland Display Server: ✅ Available ({waylandDisplay})");
        }
        else
        {
            Console.WriteLine("Display Server: ❌ Neither X11 nor Wayland detected");
        }
        
        // Platform-specific checks
        if (OperatingSystem.IsLinux())
        {
            Console.WriteLine("Linux Platform Checks:");
            Console.WriteLine($"  Force Software Rendering: {Environment.GetEnvironmentVariable("AVALONIA_FORCE_SOFTWARE_RENDERING") ?? "Not set"}");
            Console.WriteLine($"  LibGL Software: {Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE") ?? "Not set"}");
            
            // Check for common dialog dependencies
            Console.WriteLine("Recommended Dependencies:");
            Console.WriteLine("  - libgtk-3-0 (GTK3 runtime)");
            Console.WriteLine("  - libgtk-3-dev (GTK3 development)");
            Console.WriteLine("  - xdg-desktop-portal (native dialogs)");
            Console.WriteLine("  - X server or Wayland compositor");
        }
        
        Console.WriteLine("==========================================");
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
            // Enhanced diagnostics: Comprehensive exception and environment logging
            Console.WriteLine("=== DIALOG SERVICE CRASH DIAGNOSTICS ===");
            Console.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Method: OpenFolderDialogAsync");
            Console.WriteLine($"Title: {title}");
            Console.WriteLine($"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            Console.WriteLine($"Is UI Thread: {Dispatcher.UIThread.CheckAccess()}");
            
            // Exception details
            Console.WriteLine($"Exception Type: {ex.GetType().FullName}");
            Console.WriteLine($"Exception Message: {ex.Message}");
            Console.WriteLine($"Exception Source: {ex.Source}");
            Console.WriteLine($"Exception TargetSite: {ex.TargetSite}");
            Console.WriteLine($"Exception HResult: {ex.HResult:X8}");
            
            // Stack trace
            Console.WriteLine($"Stack Trace:");
            Console.WriteLine(ex.StackTrace);
            
            // Inner exception details
            var innerEx = ex.InnerException;
            int innerCount = 0;
            while (innerEx != null && innerCount < 5)
            {
                innerCount++;
                Console.WriteLine($"Inner Exception #{innerCount}:");
                Console.WriteLine($"  Type: {innerEx.GetType().FullName}");
                Console.WriteLine($"  Message: {innerEx.Message}");
                Console.WriteLine($"  Source: {innerEx.Source}");
                Console.WriteLine($"  Stack: {innerEx.StackTrace}");
                innerEx = innerEx.InnerException;
            }
            
            // Environment diagnostics
            Console.WriteLine("Environment Variables:");
            Console.WriteLine($"  DISPLAY: {Environment.GetEnvironmentVariable("DISPLAY") ?? "Not set"}");
            Console.WriteLine($"  WSL_DISTRO_NAME: {Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "Not set"}");
            Console.WriteLine($"  XDG_CURRENT_DESKTOP: {Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "Not set"}");
            Console.WriteLine($"  XDG_SESSION_TYPE: {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "Not set"}");
            Console.WriteLine($"  WAYLAND_DISPLAY: {Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "Not set"}");
            Console.WriteLine($"  LIBGL_ALWAYS_SOFTWARE: {Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE") ?? "Not set"}");
            Console.WriteLine($"  AVALONIA_FORCE_SOFTWARE_RENDERING: {Environment.GetEnvironmentVariable("AVALONIA_FORCE_SOFTWARE_RENDERING") ?? "Not set"}");
            
            // System information
            Console.WriteLine("System Information:");
            Console.WriteLine($"  OS: {Environment.OSVersion}");
            Console.WriteLine($"  Runtime: {Environment.Version}");
            Console.WriteLine($"  Process: {Process.GetCurrentProcess().ProcessName} (PID: {Process.GetCurrentProcess().Id})");
            Console.WriteLine($"  Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"  User: {Environment.UserName}");
            Console.WriteLine($"  Machine: {Environment.MachineName}");
            
            // Application state
            var app = Application.Current;
            Console.WriteLine($"Application State:");
            Console.WriteLine($"  Application Current: {app != null}");
            if (app != null)
            {
                Console.WriteLine($"  Application Name: {app.Name}");
                Console.WriteLine($"  Lifetime Type: {app.ApplicationLifetime?.GetType().Name ?? "null"}");
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Console.WriteLine($"  Main Window: {desktop.MainWindow != null}");
                    if (desktop.MainWindow != null)
                    {
                        Console.WriteLine($"  Window Title: {desktop.MainWindow.Title}");
                        Console.WriteLine($"  Window IsLoaded: {desktop.MainWindow.IsLoaded}");
                        Console.WriteLine($"  Window IsVisible: {desktop.MainWindow.IsVisible}");
                        Console.WriteLine($"  StorageProvider: {desktop.MainWindow.StorageProvider != null}");
                    }
                }
            }
            Console.WriteLine("========================================");
            
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
            // Enhanced diagnostics: Comprehensive exception and environment logging
            Console.WriteLine("=== DIALOG SERVICE CRASH DIAGNOSTICS ===");
            Console.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Method: OpenFileDialogAsync");
            Console.WriteLine($"Title: {title}");
            Console.WriteLine($"File Types: {(fileTypes != null ? string.Join(", ", fileTypes.Select(ft => ft.Name)) : "None")}");
            Console.WriteLine($"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            Console.WriteLine($"Is UI Thread: {Dispatcher.UIThread.CheckAccess()}");
            
            // Exception details
            Console.WriteLine($"Exception Type: {ex.GetType().FullName}");
            Console.WriteLine($"Exception Message: {ex.Message}");
            Console.WriteLine($"Exception Source: {ex.Source}");
            Console.WriteLine($"Exception TargetSite: {ex.TargetSite}");
            Console.WriteLine($"Exception HResult: {ex.HResult:X8}");
            
            // Stack trace
            Console.WriteLine($"Stack Trace:");
            Console.WriteLine(ex.StackTrace);
            
            // Inner exception details
            var innerEx = ex.InnerException;
            int innerCount = 0;
            while (innerEx != null && innerCount < 5)
            {
                innerCount++;
                Console.WriteLine($"Inner Exception #{innerCount}:");
                Console.WriteLine($"  Type: {innerEx.GetType().FullName}");
                Console.WriteLine($"  Message: {innerEx.Message}");
                Console.WriteLine($"  Source: {innerEx.Source}");
                Console.WriteLine($"  Stack: {innerEx.StackTrace}");
                innerEx = innerEx.InnerException;
            }
            
            // Environment diagnostics
            Console.WriteLine("Environment Variables:");
            Console.WriteLine($"  DISPLAY: {Environment.GetEnvironmentVariable("DISPLAY") ?? "Not set"}");
            Console.WriteLine($"  WSL_DISTRO_NAME: {Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "Not set"}");
            Console.WriteLine($"  XDG_CURRENT_DESKTOP: {Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "Not set"}");
            Console.WriteLine($"  XDG_SESSION_TYPE: {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "Not set"}");
            Console.WriteLine($"  WAYLAND_DISPLAY: {Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "Not set"}");
            Console.WriteLine($"  LIBGL_ALWAYS_SOFTWARE: {Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE") ?? "Not set"}");
            Console.WriteLine($"  AVALONIA_FORCE_SOFTWARE_RENDERING: {Environment.GetEnvironmentVariable("AVALONIA_FORCE_SOFTWARE_RENDERING") ?? "Not set"}");
            
            // System information
            Console.WriteLine("System Information:");
            Console.WriteLine($"  OS: {Environment.OSVersion}");
            Console.WriteLine($"  Runtime: {Environment.Version}");
            Console.WriteLine($"  Process: {Process.GetCurrentProcess().ProcessName} (PID: {Process.GetCurrentProcess().Id})");
            Console.WriteLine($"  Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"  User: {Environment.UserName}");
            Console.WriteLine($"  Machine: {Environment.MachineName}");
            
            // Application state
            var app = Application.Current;
            Console.WriteLine($"Application State:");
            Console.WriteLine($"  Application Current: {app != null}");
            if (app != null)
            {
                Console.WriteLine($"  Application Name: {app.Name}");
                Console.WriteLine($"  Lifetime Type: {app.ApplicationLifetime?.GetType().Name ?? "null"}");
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Console.WriteLine($"  Main Window: {desktop.MainWindow != null}");
                    if (desktop.MainWindow != null)
                    {
                        Console.WriteLine($"  Window Title: {desktop.MainWindow.Title}");
                        Console.WriteLine($"  Window IsLoaded: {desktop.MainWindow.IsLoaded}");
                        Console.WriteLine($"  Window IsVisible: {desktop.MainWindow.IsVisible}");
                        Console.WriteLine($"  StorageProvider: {desktop.MainWindow.StorageProvider != null}");
                    }
                }
            }
            Console.WriteLine("========================================");
            
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