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
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

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
            // Ensure the entire operation runs on the UI thread
            // If not on UI thread, dispatch this method to it once
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFolderDialogAsync(title));
            }

            // Now guaranteed to be on UI thread - proceed directly
            var topLevel = GetMainTopLevel();
            if (topLevel == null)
            {
                throw new InvalidOperationException("Could not find main window for dialog");
            }

            // Ensure TopLevel is loaded
            await EnsureTopLevelLoaded(topLevel);

            // Check StorageProvider availability
            if (topLevel.StorageProvider == null)
            {
                throw new InvalidOperationException(
                    "StorageProvider is not available. This may occur on systems missing native dialog dependencies.");
            }

            // Create folder picker options
            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            // Call StorageProvider directly - we're already on UI thread
            // ConfigureAwait(true) ensures continuation returns to UI thread
            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options).ConfigureAwait(true);
            
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
            
            // Show visible error alert to user
            var errorTitle = "Browse Folder Failed";
            var errorMessage = $"Failed to open folder browser: {ex.GetType().Name}\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Environment: {Environment.GetEnvironmentVariable("DISPLAY") ?? "No DISPLAY"} | " +
                             $"WSL: {Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "No"}\n\n" +
                             $"Check console for detailed diagnostics.";
            
            try
            {
                await ShowErrorAlertAsync(errorTitle, errorMessage);
            }
            catch (Exception alertEx)
            {
                Console.WriteLine($"Failed to show error alert: {alertEx.Message}");
            }
            
            return null;
        }
    }

    public async Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? fileTypes = null)
    {
        try
        {
            // Ensure the entire operation runs on the UI thread
            // If not on UI thread, dispatch this method to it once
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => OpenFileDialogAsync(title, fileTypes));
            }

            // Now guaranteed to be on UI thread - proceed directly
            var topLevel = GetMainTopLevel();
            if (topLevel == null)
            {
                throw new InvalidOperationException("Could not find main window for dialog");
            }

            // Ensure TopLevel is loaded
            await EnsureTopLevelLoaded(topLevel);

            // Check StorageProvider availability
            if (topLevel.StorageProvider == null)
            {
                throw new InvalidOperationException(
                    "StorageProvider is not available. This may occur on systems missing native dialog dependencies.");
            }

            // Create file picker options
            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            if (fileTypes != null)
            {
                options.FileTypeFilter = fileTypes;
            }

            // Call StorageProvider directly - we're already on UI thread
            // ConfigureAwait(true) ensures continuation returns to UI thread
            var result = await topLevel.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
            
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
            
            // Show visible error alert to user
            var errorTitle = "Browse File Failed";
            var errorMessage = $"Failed to open file browser: {ex.GetType().Name}\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Environment: {Environment.GetEnvironmentVariable("DISPLAY") ?? "No DISPLAY"} | " +
                             $"WSL: {Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? "No"}\n\n" +
                             $"Check console for detailed diagnostics.";
            
            try
            {
                await ShowErrorAlertAsync(errorTitle, errorMessage);
            }
            catch (Exception alertEx)
            {
                Console.WriteLine($"Failed to show error alert: {alertEx.Message}");
            }
            
            return null;
        }
    }

    private TopLevel? GetMainTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private async Task EnsureTopLevelLoaded(TopLevel topLevel)
    {
        // If TopLevel is not loaded, wait a short time for it to initialize
        if (topLevel is Window window && !window.IsLoaded)
        {
            await Task.Delay(50);
        }
    }

    public async Task ShowErrorAlertAsync(string title, string message)
    {
        Console.WriteLine($"=== ShowErrorAlertAsync ENTRY ===");
        Console.WriteLine($"Title: {title}");
        Console.WriteLine($"Message length: {message.Length}");
        Console.WriteLine($"Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
        Console.WriteLine($"Is UI Thread: {Dispatcher.UIThread.CheckAccess()}");
        
        try
        {
            Console.WriteLine("Step 1: Checking UI thread...");
            
            // Ensure we're on the UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Console.WriteLine("Not on UI thread, invoking...");
                await Dispatcher.UIThread.InvokeAsync(() => ShowErrorAlertAsync(title, message));
                Console.WriteLine("UI thread invocation completed");
                return;
            }

            Console.WriteLine("Step 2: Getting main window...");
            
            // Get the main TopLevel for parent
            var topLevel = GetMainTopLevel();
            Console.WriteLine($"Main TopLevel: {topLevel != null}");
            if (topLevel != null)
            {
                if (topLevel is Window window)
                {
                    Console.WriteLine($"Window state: Loaded={window.IsLoaded}, Visible={window.IsVisible}");
                    Console.WriteLine($"Window title: {window.Title}");
                    Console.WriteLine($"Window size: {window.Width}x{window.Height}");
                }
                else
                {
                    Console.WriteLine($"TopLevel type: {topLevel.GetType().Name}");
                }
            }
            
            Console.WriteLine("Step 3: Pre-MessageBox system validation...");
            
            // Test if we can access the MessageBox system at all
            try
            {
                Console.WriteLine("Step 3a: Testing MessageBoxManager access...");
                var testAccess = typeof(MessageBoxManager);
                Console.WriteLine($"✅ MessageBoxManager type: {testAccess.FullName}");
                
                Console.WriteLine("Step 3b: Testing enum access...");
                var testButton = ButtonEnum.Ok;
                var testIcon = Icon.Error;
                Console.WriteLine($"✅ ButtonEnum.Ok: {testButton}");
                Console.WriteLine($"✅ Icon.Error: {testIcon}");
            }
            catch (Exception preTestEx)
            {
                Console.WriteLine($"❌ MessageBox system pre-test failed: {preTestEx}");
                throw new InvalidOperationException("MessageBox system is not accessible", preTestEx);
            }
            
            Console.WriteLine("Step 4: Creating message box...");
            
            // CRITICAL: Add try-catch around MessageBox creation
            try
            {
                Console.WriteLine("Step 4a: Creating message box with parameters...");
                Console.WriteLine($"Title length: {title.Length}");
                Console.WriteLine($"Message length: {message.Length}");
                
                // Add process monitoring - capture PID and memory before MessageBox creation
                var process = System.Diagnostics.Process.GetCurrentProcess();
                Console.WriteLine($"Process state before MessageBox: PID={process.Id}, Memory={process.WorkingSet64 / 1024 / 1024}MB");
                
                var messageBox = MessageBoxManager.GetMessageBoxStandard(
                    title,
                    message,
                    ButtonEnum.Ok,
                    Icon.Error);
                Console.WriteLine("✅ Message box created successfully");
                
                Console.WriteLine("Step 4b: Message box type validation...");
                Console.WriteLine($"MessageBox type: {messageBox.GetType().FullName}");
                
                Console.WriteLine("Step 5: Showing message box...");
                
                // Pre-show validation
                Console.WriteLine("Step 5a: Pre-show validation...");
                Console.WriteLine($"Process still alive: PID={System.Diagnostics.Process.GetCurrentProcess().Id}");
                Console.WriteLine($"UI thread access: {Dispatcher.UIThread.CheckAccess()}");
                
                if (topLevel is Window window)
                {
                    Console.WriteLine("Step 5b: Showing as window dialog...");
                    Console.WriteLine("About to call ShowWindowDialogAsync...");
                    
                    // Monitor the actual ShowWindowDialogAsync call
                    var showTask = messageBox.ShowWindowDialogAsync(window);
                    Console.WriteLine("ShowWindowDialogAsync call initiated");
                    
                    var result = await showTask;
                    Console.WriteLine($"✅ Window dialog completed with result: {result}");
                }
                else
                {
                    Console.WriteLine("Step 5b: Showing as standalone dialog...");
                    Console.WriteLine("About to call ShowAsync...");
                    
                    // Monitor the actual ShowAsync call  
                    var showTask = messageBox.ShowAsync();
                    Console.WriteLine("ShowAsync call initiated");
                    
                    var result = await showTask;
                    Console.WriteLine($"✅ Standalone dialog completed with result: {result}");
                }
            }
            catch (Exception showEx)
            {
                Console.WriteLine($"❌ CRITICAL: Failed during MessageBox operations");
                Console.WriteLine($"Exception type: {showEx.GetType().FullName}");
                Console.WriteLine($"Exception message: {showEx.Message}");
                Console.WriteLine($"Exception source: {showEx.Source}");
                Console.WriteLine($"HResult: {showEx.HResult:X8}");
                Console.WriteLine($"Stack trace: {showEx.StackTrace}");
                
                if (showEx.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {showEx.InnerException.GetType().FullName}: {showEx.InnerException.Message}");
                    Console.WriteLine($"Inner stack: {showEx.InnerException.StackTrace}");
                }
                
                // Try to check if process is still alive after exception
                try
                {
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    Console.WriteLine($"Process still alive after exception: PID={currentProcess.Id}");
                }
                catch (Exception procEx)
                {
                    Console.WriteLine($"Cannot check process state: {procEx.Message}");
                }
                
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== CRITICAL ERROR IN ShowErrorAlertAsync ===");
            Console.WriteLine($"Exception Type: {ex.GetType().FullName}");
            Console.WriteLine($"Exception Message: {ex.Message}");
            Console.WriteLine($"Exception Source: {ex.Source}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            
            Console.WriteLine($"Original error - {title}: {message}");
            Console.WriteLine("===============================================");
            
            // Don't rethrow - this could cause cascade failures
        }
        finally
        {
            Console.WriteLine("=== ShowErrorAlertAsync COMPLETE ===");
        }
    }


}