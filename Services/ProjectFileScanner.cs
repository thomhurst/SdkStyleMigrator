using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ProjectFileScanner : IProjectFileScanner
{
    private readonly ILogger<ProjectFileScanner> _logger;
    private readonly string[] _projectFileExtensions = { "*.csproj", "*.vbproj", "*.fsproj" };
    private readonly string[] _webSiteProjectIndicators = { "App_Code", "App_Data", "App_GlobalResources", "App_LocalResources" };
    private readonly HashSet<string> _excludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vcxproj", // C++ projects
        ".sqlproj", // SQL Server projects
        ".wixproj", // WiX installer projects
        ".shproj", // Shared projects
        ".pyproj",  // Python projects
        ".njsproj", // Node.js projects
        ".jsproj",  // JavaScript projects
        ".dbproj",  // Database projects
        ".deployproj", // Deployment projects
        ".modelproj", // Modeling projects
        ".nativeproj", // Native projects
    };

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
                           !f.Contains("/bin/", StringComparison.OrdinalIgnoreCase) && // Skip bin directories (Linux)
                           !_excludedExtensions.Contains(Path.GetExtension(f))) // Skip non-standard project types
                .ToArray();
            
            if (files.Length > 0)
            {
                projectFiles.AddRange(files);
            }

            _logger.LogDebug("Found {Count} {Extension} files", files.Length, extension);
        }

        // Check for non-standard project files that were skipped
        var allProjectFiles = Directory.GetFiles(directoryPath, "*.*proj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) &&
                       !f.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                       !f.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                       !f.Contains("/bin/", StringComparison.OrdinalIgnoreCase));

        var skippedProjects = allProjectFiles
            .Where(f => _excludedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (skippedProjects.Any())
        {
            _logger.LogWarning("Skipped {Count} non-standard project files:", skippedProjects.Count);
            foreach (var skipped in skippedProjects.GroupBy(f => Path.GetExtension(f).ToLowerInvariant()))
            {
                _logger.LogWarning("  - {Count} {Extension} files", skipped.Count(), skipped.Key);
            }
        }

        _logger.LogInformation("Found total of {Count} .NET project files eligible for migration", projectFiles.Count);

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