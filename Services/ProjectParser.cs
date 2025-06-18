using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ProjectParser : IProjectParser
{
    private readonly ILogger<ProjectParser> _logger;
    private static bool _msBuildInitialized = false;
    private static readonly object _initLock = new();

    public ProjectParser(ILogger<ProjectParser> logger)
    {
        _logger = logger;
        EnsureMSBuildInitialized();
    }

    private void EnsureMSBuildInitialized()
    {
        lock (_initLock)
        {
            if (!_msBuildInitialized)
            {
                MSBuildLocator.RegisterDefaults();
                _msBuildInitialized = true;
                _logger.LogDebug("MSBuild initialized");
            }
        }
    }

    public Task<Project> ParseProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: {projectPath}");
        }

        _logger.LogInformation("Parsing project: {ProjectPath}", projectPath);

        try
        {
            var projectCollection = new ProjectCollection();
            var project = new Project(projectPath, null, null, projectCollection);
            
            _logger.LogDebug("Successfully parsed project: {ProjectPath}", projectPath);
            return Task.FromResult(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project: {ProjectPath}", projectPath);
            throw;
        }
    }

    public bool IsLegacyProject(Project project)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));

        // SDK-style projects have the Sdk attribute
        var hasSdkAttribute = project.Xml.Sdk != null;
        
        // Legacy projects typically have these imports
        var hasLegacyImports = project.Imports.Any(import => 
            import.ImportedProject.FullPath.Contains("Microsoft.CSharp.targets") ||
            import.ImportedProject.FullPath.Contains("Microsoft.VisualBasic.targets") ||
            import.ImportedProject.FullPath.Contains("Microsoft.Common.props"));

        // Legacy projects often have ProjectGuid
        var hasProjectGuid = project.Properties.Any(p => p.Name == "ProjectGuid");

        var isLegacy = !hasSdkAttribute && (hasLegacyImports || hasProjectGuid);
        
        _logger.LogDebug("Project {ProjectPath} is {ProjectType}", 
            project.FullPath, 
            isLegacy ? "legacy" : "SDK-style");

        return isLegacy;
    }
}