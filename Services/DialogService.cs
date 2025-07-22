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
    public async Task<string?> OpenFolderDialogAsync(string title)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = GetTopLevel();
            if (topLevel == null)
            {
                Debug.WriteLine("DialogService: Could not get TopLevel window");
                return null;
            }

            try
            {
                var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false
                });

                return result.Count > 0 ? result[0].Path.LocalPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening folder dialog: {ex}");
                return null;
            }
        });
    }

    public async Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? fileTypes = null)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return null;

            try
            {
                var options = new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false
                };

                if (fileTypes != null)
                {
                    options.FileTypeFilter = fileTypes;
                }

                var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
                return result.Count > 0 ? result[0].Path.LocalPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening file dialog: {ex}");
                return null;
            }
        });
    }

    private static TopLevel? GetTopLevel()
    {
        // Get from application lifetime - this is the standard Avalonia approach
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        
        return null;
    }
}