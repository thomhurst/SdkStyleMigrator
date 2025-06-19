using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ProjectParser : IProjectParser, IDisposable
{
    private readonly ILogger<ProjectParser> _logger;
    private readonly ProjectCollection _projectCollection;

    public ProjectParser(ILogger<ProjectParser> logger)
    {
        _logger = logger;
        _projectCollection = new ProjectCollection();
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
            var existingProject = _projectCollection.LoadedProjects.FirstOrDefault(p => 
                string.Equals(p.FullPath, projectPath, StringComparison.OrdinalIgnoreCase));
                
            if (existingProject != null)
            {
                _projectCollection.UnloadProject(existingProject);
            }
            
            var project = new Project(projectPath, null, null, _projectCollection);
            
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

        var hasSdkAttribute = !string.IsNullOrEmpty(project.Xml.Sdk);
        
        _logger.LogDebug("Project {ProjectPath} - SDK attribute: '{Sdk}'", project.FullPath, project.Xml.Sdk ?? "(null)");
        
        if (hasSdkAttribute)
        {
            _logger.LogDebug("Project {ProjectPath} has SDK attribute, is SDK-style", project.FullPath);
            return false;
        }
        
        var hasProjectGuid = project.Properties.Any(p => p.Name == "ProjectGuid");
        var hasToolsVersion = !string.IsNullOrEmpty(project.Xml.ToolsVersion);
        var hasExplicitImports = project.Xml.Imports.Any(import => 
            import.Project.Contains("Microsoft.CSharp.targets") ||
            import.Project.Contains("Microsoft.VisualBasic.targets") ||
            import.Project.Contains("Microsoft.Common.props"));

        var isLegacy = hasProjectGuid || hasToolsVersion || hasExplicitImports;
        
        _logger.LogDebug("Project {ProjectPath} - HasProjectGuid: {HasGuid}, HasToolsVersion: {HasTools}, HasExplicitImports: {HasImports}, IsLegacy: {IsLegacy}", 
            project.FullPath, hasProjectGuid, hasToolsVersion, hasExplicitImports, isLegacy);

        return isLegacy;
    }
    
    public void Dispose()
    {
        _projectCollection?.Dispose();
        _logger.LogDebug("ProjectCollection disposed");
    }
}