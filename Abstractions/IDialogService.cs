using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace SdkMigrator.Abstractions;

public interface IDialogService
{
    Task<string?> OpenFolderDialogAsync(string title);
    Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? fileTypes = null);
    Task ShowErrorAlertAsync(string title, string message);
    Task TestDialogSystemAsync();
}