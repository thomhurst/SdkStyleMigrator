using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ProjectFileScanner : IProjectFileScanner
{
    private readonly ILogger<ProjectFileScanner> _logger;
    private readonly string[] _projectFileExtensions = { "*.*proj" };
    private readonly string[] _webSiteProjectIndicators = { "App_Code", "App_Data", "App_GlobalResources", "App_LocalResources" };

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
                           !Path.GetFileName(f).Contains(".legacy.") && // Skip any legacy backup file
                           !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) && // Skip obj directories
                           !f.Contains("/obj/", StringComparison.OrdinalIgnoreCase) && // Skip obj directories (Linux)
                           !f.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) && // Skip bin directories
                           !f.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) // Skip bin directories (Linux)
                .ToArray();
            projectFiles.AddRange(files);

            _logger.LogDebug("Found {Count} {Extension} files", files.Length, extension);
        }

        _logger.LogInformation("Found total of {Count} project files", projectFiles.Count);

        // Check for Web Site Projects
        CheckForWebSiteProjects(directoryPath);

        return Task.FromResult<IEnumerable<string>>(projectFiles.OrderBy(f => f));
    }

    private void CheckForWebSiteProjects(string directoryPath)
    {
        try
        {
            var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);

            foreach (var dir in directories)
            {
                // Check if this directory looks like a Web Site Project
                bool hasWebSiteIndicators = _webSiteProjectIndicators.Any(indicator =>
                    Directory.Exists(Path.Combine(dir, indicator)));

                if (hasWebSiteIndicators)
                {
                    // Check if there's NO project file in this directory
                    var projectFileInDir = Directory.GetFiles(dir, "*.*proj", SearchOption.TopDirectoryOnly)
                        .Any(f => !f.Contains(".legacy.") && !Path.GetFileName(f).Contains(".legacy."));

                    if (!projectFileInDir)
                    {
                        _logger.LogWarning("Found Web Site Project at '{Directory}'. Web Site Projects cannot be migrated to SDK-style format. " +
                            "Consider converting to a Web Application Project first.", dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for Web Site Projects");
        }
    }
}