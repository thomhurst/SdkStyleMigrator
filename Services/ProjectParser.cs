using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Xml.Linq;

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


    public Task<ParsedProject> ParseProjectAsync(string projectPath, CancellationToken cancellationToken = default)
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
            
            try
            {
                var project = new Project(projectPath, null, null, _projectCollection);
                _logger.LogDebug("Successfully parsed project: {ProjectPath}", projectPath);
                return Task.FromResult(new ParsedProject 
                { 
                    Project = project,
                    LoadedWithDefensiveParsing = false
                });
            }
            catch (InvalidProjectFileException ipfe) when (
                ipfe.Message.Contains("imported project") || 
                ipfe.Message.Contains("was not found") ||
                ipfe.Message.Contains("MSBuildExtensionsPath") ||
                ipfe.Message.Contains("VisualStudio") ||
                ipfe.Message.Contains("VSToolsPath"))
            {
                _logger.LogWarning("Project has invalid imports, attempting to load with imports removed: {ProjectPath}", projectPath);
                return LoadProjectWithoutInvalidImports(projectPath, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project: {ProjectPath}", projectPath);
            
            if (ex is InvalidProjectFileException && (
                ex.Message.Contains("imported project") || 
                ex.Message.Contains("was not found") ||
                ex.Message.Contains("MSBuildExtensionsPath") ||
                ex.Message.Contains("VisualStudio") ||
                ex.Message.Contains("VSToolsPath") ||
                ex.Message.Contains("$(") ||
                ex.Message.Contains("targets")))
            {
                _logger.LogWarning("Attempting to load project with defensive parsing due to: {Error}", ex.Message);
                return LoadProjectWithoutInvalidImports(projectPath, cancellationToken);
            }
            
            throw;
        }
    }
    
    private Task<ParsedProject> LoadProjectWithoutInvalidImports(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var projectXml = XDocument.Load(projectPath);
            var ns = projectXml.Root?.Name.Namespace ?? XNamespace.None;
            
            var imports = projectXml.Descendants(ns + "Import").ToList();
            var importErrors = projectXml.Descendants(ns + "ImportError").ToList();
            var invalidImports = new List<XElement>();
            
            foreach (var error in importErrors)
            {
                error.Remove();
            }
            
            // Remove ALL imports when loading defensively - we'll add back what's needed in SDK-style
            foreach (var import in imports)
            {
                var projectAttr = import.Attribute("Project")?.Value;
                if (!string.IsNullOrEmpty(projectAttr))
                {
                    _logger.LogDebug("Removing import for defensive parsing: {Import}", projectAttr);
                    invalidImports.Add(import);
                }
            }
            
            foreach (var invalidImport in invalidImports)
            {
                invalidImport.Remove();
            }
            
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(projectPath)}_temp_{Guid.NewGuid()}.csproj");
            projectXml.Save(tempPath);
            
            try
            {
                var globalProperties = new Dictionary<string, string>
                {
                    ["DesignTimeBuild"] = "true",
                    ["SkipInvalidConfigurations"] = "true",
                    ["_ResolveReferenceDependencies"] = "false",
                    ["_GetChildProjectCopyToOutputDirectoryItems"] = "false",
                    ["_SGenCheckForOutputs"] = "false",
                    ["_CompileTargetNameForLocalType"] = "Compile",
                    ["BuildProjectReferences"] = "false"
                };
                
                var project = new Project(tempPath, globalProperties, null, _projectCollection);
                
                var originalPath = Path.GetFullPath(projectPath);
                project.FullPath = originalPath;
                
                _logger.LogWarning("Successfully loaded project after removing {Count} non-essential imports", invalidImports.Count);
                
                var removedImports = invalidImports.Select(i => i.Attribute("Project")?.Value ?? "Unknown").ToList();
                
                return Task.FromResult(new ParsedProject
                {
                    Project = project,
                    LoadedWithDefensiveParsing = true,
                    RemovedImports = removedImports
                });
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project even with defensive parsing");
            
            try
            {
                return LoadProjectAsRawXml(projectPath, cancellationToken);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Failed to load project as raw XML");
                throw new InvalidOperationException($"Cannot parse project {projectPath} even with defensive parsing", ex);
            }
        }
    }
    
    private Task<ParsedProject> LoadProjectAsRawXml(string projectPath, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Attempting to create minimal project from raw XML: {ProjectPath}", projectPath);
        
        var projectXml = XDocument.Load(projectPath);
        var ns = projectXml.Root?.Name.Namespace ?? XNamespace.None;
        
        var minimalProject = new XDocument(
            new XElement(ns + "Project",
                new XAttribute("ToolsVersion", "4.0"),
                new XAttribute("DefaultTargets", "Build"),
                new XAttribute("xmlns", "http://schemas.microsoft.com/developer/msbuild/2003")
            )
        );
        
        var root = minimalProject.Root!;
        
        foreach (var propertyGroup in projectXml.Descendants(ns + "PropertyGroup"))
        {
            root.Add(new XElement(propertyGroup));
        }
        
        foreach (var itemGroup in projectXml.Descendants(ns + "ItemGroup"))
        {
            root.Add(new XElement(itemGroup));
        }
        
        root.Add(new XElement(ns + "Import", 
            new XAttribute("Project", @"$(MSBuildToolsPath)\Microsoft.CSharp.targets")));
        
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(projectPath)}_minimal_{Guid.NewGuid()}.csproj");
        minimalProject.Save(tempPath);
        
        try
        {
            var project = new Project(tempPath, null, null, _projectCollection);
            project.FullPath = Path.GetFullPath(projectPath);
            
            _logger.LogWarning("Successfully created minimal project from raw XML");
            return Task.FromResult(new ParsedProject
            {
                Project = project,
                LoadedWithDefensiveParsing = true,
                RemovedImports = new List<string> { "All non-essential imports removed" }
            });
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
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