namespace SdkMigrator.Abstractions;

public interface IProjectFileScanner
{
    Task<IEnumerable<string>> ScanForProjectFilesAsync(string directoryPath, CancellationToken cancellationToken = default);
}