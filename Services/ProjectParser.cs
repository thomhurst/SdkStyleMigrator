using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class ProjectParser : IProjectParser, IDisposable
{
    private readonly ILogger<ProjectParser> _logger;
    private readonly IMSBuildArtifactDetector _artifactDetector;
    private readonly ProjectCollection _projectCollection;

    public ProjectParser(ILogger<ProjectParser> logger, IMSBuildArtifactDetector artifactDetector)
    {
        _logger = logger;
        _artifactDetector = artifactDetector;
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
            var filteredPropertyGroup = new XElement(ns + "PropertyGroup");
            
            // Copy attributes from original PropertyGroup
            foreach (var attr in propertyGroup.Attributes())
            {
                filteredPropertyGroup.Add(attr);
            }
            
            // Copy only properties that are not MSBuild evaluation artifacts
            foreach (var property in propertyGroup.Elements())
            {
                var propertyName = property.Name.LocalName;
                var propertyValue = property.Value;
                
                // Use the artifact detector for more comprehensive filtering
                if (!_artifactDetector.IsPropertyArtifact(propertyName, propertyValue) &&
                    !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(propertyName))
                {
                    filteredPropertyGroup.Add(new XElement(property));
                }
                else
                {
                    _logger.LogDebug("Filtered out MSBuild evaluation artifact property: {PropertyName}", propertyName);
                }
            }
            
            // Only add the PropertyGroup if it has any properties
            if (filteredPropertyGroup.HasElements)
            {
                root.Add(filteredPropertyGroup);
            }
        }

        foreach (var itemGroup in projectXml.Descendants(ns + "ItemGroup"))
        {
            var filteredItemGroup = new XElement(ns + "ItemGroup");
            
            // Copy attributes from original ItemGroup
            foreach (var attr in itemGroup.Attributes())
            {
                filteredItemGroup.Add(attr);
            }
            
            // Copy only items that are not MSBuild evaluation artifacts
            foreach (var item in itemGroup.Elements())
            {
                var itemType = item.Name.LocalName;
                var itemInclude = item.Attribute("Include")?.Value;
                
                // Use the artifact detector for more comprehensive filtering
                if (!_artifactDetector.IsItemArtifact(itemType, itemInclude) &&
                    !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(itemType))
                {
                    filteredItemGroup.Add(new XElement(item));
                }
                else
                {
                    _logger.LogDebug("Filtered out MSBuild evaluation artifact item: {ItemType}", itemType);
                }
            }
            
            // Only add the ItemGroup if it has any items
            if (filteredItemGroup.HasElements)
            {
                root.Add(filteredItemGroup);
            }
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

        // Check if this is a .NET project type
        var projectTypeGuids = project.GetPropertyValue("ProjectTypeGuids");
        if (!string.IsNullOrEmpty(projectTypeGuids))
        {
            // Known .NET project type GUIDs
            var dotNetProjectGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}", // C#
                "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}", // VB.NET
                "{F2A71F9B-5D33-465A-A702-920D77279786}", // F#
                "{349C5851-65DF-11DA-9384-00065B846F21}", // Web Application
                "{E24C65DC-7377-472B-9ABA-BC803B73C61A}", // Web Site
                "{603C0E0B-DB56-11DC-BE95-000D561079B0}", // ASP.NET MVC 1
                "{F85E285D-A4E0-4152-9332-AB1D724D3325}", // ASP.NET MVC 2
                "{E53F8FEA-EAE0-44A6-8774-FFD645390401}", // ASP.NET MVC 3
                "{E3E379DF-F4C6-4180-9B81-6769533ABE47}", // ASP.NET MVC 4
                "{349C5853-65DF-11DA-9384-00065B846F21}", // ASP.NET MVC 5
                "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}", // .NET Core/5+
                "{60DC8134-EBA5-43B8-BCC9-BB4BC16C2548}", // WPF
                "{C252FEB5-A946-4202-B1D4-9916A0590387}", // Windows Service
                "{786C830F-07A1-408B-BD7F-6EE04809D6DB}", // Portable Class Library
                "{A1591282-1198-4647-A2B1-27E5FF5F6F3B}", // Silverlight
                "{BC8A1FFA-BEE3-4634-8014-F334798102B3}", // Windows Store App
                "{14822709-B5A1-4724-98CA-57A101D1B079}", // Windows Phone
            };

            // Known non-.NET project type GUIDs to skip
            var nonDotNetProjectGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}", // C++
                "{00D1A9C2-B5F0-4AF3-8072-F6C62B433612}", // SQL Server Database Project
                "{A9ACE9BB-CECE-4E62-9AA4-C7E7C5BD2124}", // Database Project
                "{4F174C21-8C12-11D0-8340-0000F80270F8}", // Database (other)
                "{3AC096D0-A1C2-E12C-1390-A8335801FDAB}", // Test Project (old)
                "{930C7802-8A8C-48F9-8165-68863BCCD9DD}", // WiX Installer
                "{54435603-DBB4-11D2-8724-00A0C9A8B90C}", // Visual Studio Installer
                "{978C614F-708E-4E1A-B201-565925725DBA}", // Deployment Merge Module
                "{AB322303-2255-48EF-A496-5904EB18DA55}", // Deployment Smart Device CAB
                "{F135691A-BF7E-435D-8960-F99683D2D49C}", // Distributed System
                "{BF6F8E12-879D-49E7-ADF0-5503146B24B8}", // Dynamics 2012 AX C# Project
                "{82B43B9B-A64C-4715-B499-D71E9CA2BD60}", // Extensibility Project
                "{6BC8ED88-2882-458C-8E55-DFD12B67127B}", // MonoTouch Project
                "{EFBA0AD7-5A72-4C68-AF49-83D382785DCF}", // Android Project
                "{F5B4F3BC-B597-4E2B-B552-EF5D8A32436F}", // MonoTouch Binding Project
                "{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1}", // Xamarin.iOS
                "{1E72D84B-E16E-4A2F-BE2F-88C25B3E33D9}", // Xamarin.Android
                "{CB4CE8C6-1BDB-4DC7-A4D3-65A190314484}", // Setup and Deployment Project
                "{06A35CCD-C46D-44D5-987B-CF40FF872267}", // Deployment Cab
                "{3EA9E505-35AC-4774-B492-AD1749C4943A}", // Deployment Smart Device Cab
                "{C8D11400-126E-41CD-887F-60BD40844F9E}", // Database project
                "{32F31D43-81CC-4C15-9DE6-3FC5453562B6}", // Workflow Foundation
                "{4D628B5B-2FBC-4AA6-8C16-197242AEB884}", // SharePoint (C#)
                "{EC05E597-79D4-47F3-ADA0-324C4F7C7484}", // SharePoint (VB.NET)
                "{593B0543-81F6-4436-BA1E-4747859CAAE2}", // SharePoint (Workflow)
                "{349C5851-65DF-11DA-9384-00065B846F21}", // Web Application
                "{BB1F664F-9266-4FD6-B973-E1E44974B511}", // SharePoint 2010 Project
            };

            // Check if any of the project type GUIDs are .NET types
            var guids = projectTypeGuids.Split(';').Select(g => g.Trim());
            var hasDotNetGuid = guids.Any(g => dotNetProjectGuids.Contains(g));
            var hasNonDotNetGuid = guids.Any(g => nonDotNetProjectGuids.Contains(g));

            if (hasNonDotNetGuid && !hasDotNetGuid)
            {
                _logger.LogInformation("Skipping non-.NET project with type GUIDs: {ProjectTypeGuids}", projectTypeGuids);
                return false;
            }
        }

        // Additional checks for non-standard projects based on file extension and content
        var extension = Path.GetExtension(project.FullPath).ToLowerInvariant();
        
        // Check for non-standard project extensions
        var nonStandardExtensions = new HashSet<string> { ".vcxproj", ".sqlproj", ".wixproj", ".shproj", ".pyproj", ".njsproj", ".jsproj", ".dbproj", ".deployproj" };
        if (nonStandardExtensions.Contains(extension))
        {
            _logger.LogInformation("Skipping non-standard project type: {Extension}", extension);
            return false;
        }

        // Check for SQL Server Data Tools projects
        if (project.Items.Any(i => i.ItemType == "SqlCmdVariable" || i.ItemType == "Build" && i.EvaluatedInclude.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Skipping SQL Server Data Tools project");
            return false;
        }

        // Check for SharePoint projects
        if (project.Items.Any(i => i.ItemType == "ProjectConfiguration" && i.EvaluatedInclude.Contains("SharePoint")))
        {
            _logger.LogInformation("Skipping SharePoint project");
            return false;
        }

        // Check for Setup/Installer projects
        if (project.GetPropertyValue("OutputType")?.Equals("Package", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Skipping installer/setup project");
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