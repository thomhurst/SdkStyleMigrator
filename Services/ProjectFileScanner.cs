using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ProjectFileScanner : IProjectFileScanner
{
    private readonly ILogger<ProjectFileScanner> _logger;
    private readonly string[] _projectFileExtensions = { "*.csproj", "*.vbproj", "*.fsproj" };

    public ProjectFileScanner(ILogger<ProjectFileScanner> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<string>> ScanForProjectFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        _logger.LogInformation("Scanning for project files in {DirectoryPath}", directoryPath);

        var projectFiles = new List<string>();

        foreach (var extension in _projectFileExtensions)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var files = Directory.GetFiles(directoryPath, extension, SearchOption.AllDirectories)
                .Where(f => !f.Contains(".legacy.") && // Skip backup files
                           !f.Contains("_sdkmigrator_backup_") && // Skip backup directories
                           !f.Contains(".sdkmigrator.lock") && // Skip lock files
                           !f.EndsWith(".legacy.csproj", StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith(".legacy.vbproj", StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith(".legacy.fsproj", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            projectFiles.AddRange(files);
            
            _logger.LogDebug("Found {Count} {Extension} files", files.Length, extension);
        }

        _logger.LogInformation("Found total of {Count} project files", projectFiles.Count);

        return Task.FromResult<IEnumerable<string>>(projectFiles.OrderBy(f => f));
    }
}