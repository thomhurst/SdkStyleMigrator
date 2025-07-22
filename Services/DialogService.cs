using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class DialogService : IDialogService
{
    public async Task<string?> OpenFolderDialogAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    public async Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? fileTypes = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

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

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}