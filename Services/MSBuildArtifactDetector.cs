using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using System.Text.RegularExpressions;

namespace SdkMigrator.Services;

/// <summary>
/// Service for detecting MSBuild evaluation artifacts using MSBuild's own evaluation APIs.
/// This provides a more robust approach than maintaining hardcoded lists.
/// </summary>
public class MSBuildArtifactDetector : IMSBuildArtifactDetector, IDisposable
{
    private readonly ILogger<MSBuildArtifactDetector> _logger;
    private readonly ProjectCollection _projectCollection;
    
    // Regex patterns for common MSBuild artifact naming conventions
    private static readonly Regex InternalPropertyPattern = new(@"^(_[A-Z]|MSBuild|DotNet|NET|NuGet)", RegexOptions.Compiled);
    private static readonly Regex InternalItemPattern = new(@"^(_[A-Z]|MSBuild|DotNet|NET|NuGet)", RegexOptions.Compiled);

    // Properties that are typically implicit in SDK-style projects
    private static readonly HashSet<string> SdkImplicitProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "TargetFrameworkVersion", "OutputType", "RootNamespace", "ProjectGuid",
        "SchemaVersion", "ProjectTypeGuids", "FileAlignment", "WarningLevel",
        "ErrorReport", "Prefer32Bit", "DebugSymbols", "DebugType", "Optimize",
        "OutputPath", "IntermediateOutputPath", "BaseIntermediateOutputPath", 
        "BaseOutputPath", "AppDesignerFolder", "Configuration", "Platform",
        "ProductVersion", "FileVersion", "OldToolsVersion", "UpgradeBackupLocation",
        "GenerateSerializationAssemblies", "AppendTargetFrameworkToOutputPath",
        "AppendRuntimeIdentifierToOutputPath", "CopyLocalLockFileAssemblies",
        "DisableImplicitFrameworkReferences", "DisableImplicitNuGetFallbackFolder",
        "DisableImplicitNuGetSources", "EnableDefaultItems", "EnableDefaultCompileItems",
        "EnableDefaultEmbeddedResourceItems", "EnableDefaultNoneItems", "EnableDefaultContentItems",
        "EnableDefaultPageItems", "EnableDefaultApplicationDefinitionItems",
        "EnableDefaultResourceItems", "EnableDefaultSplashScreenItems", "EnableDefaultXamlItems"
    };

    // Item types that are typically implicit in SDK-style projects
    private static readonly HashSet<string> SdkImplicitItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compile", "EmbeddedResource", "None", "Content", "Page", 
        "ApplicationDefinition", "Resource", "SplashScreen"
    };

    // Known MSBuild/SDK infrastructure directory names
    private static readonly HashSet<string> InfrastructureDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.WindowsDesktop",
        "Microsoft.NET.Sdk.BlazorWebAssembly", "MSBuild", "Microsoft Visual Studio",
        "dotnet", "sdk", "targets", "props"
    };

    public MSBuildArtifactDetector(ILogger<MSBuildArtifactDetector> logger)
    {
        _logger = logger;
        _projectCollection = new ProjectCollection();
    }

    public bool IsPropertyArtifact(string propertyName, string? propertyValue = null, string? containingProjectPath = null)
    {
        if (string.IsNullOrEmpty(propertyName))
            return false;

        // Check against naming patterns
        if (InternalPropertyPattern.IsMatch(propertyName))
        {
            _logger.LogDebug("Property {PropertyName} matches internal naming pattern", propertyName);
            return true;
        }

        // Check if it's from MSBuild infrastructure path
        if (!string.IsNullOrEmpty(containingProjectPath) && IsMSBuildInfrastructurePath(containingProjectPath))
        {
            _logger.LogDebug("Property {PropertyName} is from MSBuild infrastructure path: {Path}", propertyName, containingProjectPath);
            return true;
        }

        return false;
    }

    public bool IsItemArtifact(string itemType, string? itemInclude = null, string? containingProjectPath = null)
    {
        if (string.IsNullOrEmpty(itemType))
            return false;

        // Always preserve important user files, even if they match other patterns
        if (!string.IsNullOrEmpty(itemInclude))
        {
            var fileName = Path.GetFileName(itemInclude).ToLowerInvariant();
            
            // Configuration files
            if (fileName == "app.config" || fileName == "web.config" || 
                fileName == "appsettings.json" || fileName.StartsWith("appsettings.") ||
                fileName == "packages.config" || fileName.EndsWith(".config"))
            {
                _logger.LogDebug("Preserving configuration file: {ItemInclude}", itemInclude);
                return false; // Not an artifact - preserve it
            }
            
            // Documentation and project files
            if (fileName == "readme.md" || fileName == "license" || fileName == "license.txt" ||
                fileName.EndsWith(".md") || fileName.EndsWith(".txt") || fileName.EndsWith(".json") ||
                fileName.EndsWith(".xml") || fileName.EndsWith(".yml") || fileName.EndsWith(".yaml"))
            {
                _logger.LogDebug("Preserving user file: {ItemInclude}", itemInclude);
                return false; // Not an artifact - preserve it
            }
        }

        // Check against naming patterns
        if (InternalItemPattern.IsMatch(itemType))
        {
            _logger.LogDebug("Item type {ItemType} matches internal naming pattern", itemType);
            return true;
        }

        // Check if it's from MSBuild infrastructure path
        if (!string.IsNullOrEmpty(containingProjectPath) && IsMSBuildInfrastructurePath(containingProjectPath))
        {
            _logger.LogDebug("Item {ItemType} is from MSBuild infrastructure path: {Path}", itemType, containingProjectPath);
            return true;
        }

        return false;
    }

    public async Task<MSBuildArtifactAnalysis> AnalyzeProjectAsync(string projectPath)
    {
        var analysis = new MSBuildArtifactAnalysis();
        Project? project = null;
        ProjectRootElement? projectRootElement = null;

        try
        {
            _logger.LogInformation("Analyzing MSBuild artifacts for project: {ProjectPath}", projectPath);

            // Load the evaluated project
            project = _projectCollection.LoadProject(projectPath);
            
            // Load the raw XML structure to find explicitly defined elements
            projectRootElement = ProjectRootElement.Open(projectPath, _projectCollection);

            // Phase 1: Identify MSBuild internal/runtime-generated artifacts
            await AnalyzeEvaluatedPropertiesAsync(project, projectPath, analysis);
            await AnalyzeEvaluatedItemsAsync(project, projectPath, analysis);

            // Phase 2: Identify explicitly defined properties/items redundant in SDK-style
            AnalyzeExplicitProperties(projectRootElement, analysis);
            AnalyzeExplicitItems(projectRootElement, analysis);

            _logger.LogInformation("Found {ArtifactProperties} artifact properties, {ArtifactItems} artifact items, " +
                                 "{RedundantProperties} redundant properties, {RedundantItems} redundant items",
                                 analysis.ArtifactProperties.Count, analysis.ArtifactItems.Count,
                                 analysis.RedundantProperties.Count, analysis.RedundantItems.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze MSBuild artifacts for project: {ProjectPath}", projectPath);
            throw;
        }
        finally
        {
            // Always unload the project to release resources
            if (project != null)
            {
                _projectCollection.UnloadProject(project);
            }
        }
    }

    private async Task AnalyzeEvaluatedPropertiesAsync(Project project, string projectPath, MSBuildArtifactAnalysis analysis)
    {
        await Task.Run(() =>
        {
            foreach (var prop in project.AllEvaluatedProperties)
            {
                // 1. Properties reserved by MSBuild itself
                if (prop.IsReservedProperty)
                {
                    analysis.ArtifactProperties.Add(new ArtifactProperty(prop.Name, prop.EvaluatedValue, "Reserved MSBuild property"));
                    continue;
                }

                // 2. Properties generated at runtime without an XML source
                if (prop.Xml == null)
                {
                    analysis.ArtifactProperties.Add(new ArtifactProperty(prop.Name, prop.EvaluatedValue, "Runtime-generated property (no XML source)"));
                    continue;
                }

                // 3. Properties defined in MSBuild/SDK infrastructure files
                if (prop.Xml.ContainingProject.FullPath != projectPath &&
                    IsMSBuildInfrastructurePath(prop.Xml.ContainingProject.FullPath))
                {
                    analysis.ArtifactProperties.Add(new ArtifactProperty(prop.Name, prop.EvaluatedValue, 
                        $"Defined in MSBuild/SDK infrastructure: {Path.GetFileName(prop.Xml.ContainingProject.FullPath)}"));
                }

                // 4. Properties matching internal naming patterns
                else if (InternalPropertyPattern.IsMatch(prop.Name))
                {
                    analysis.ArtifactProperties.Add(new ArtifactProperty(prop.Name, prop.EvaluatedValue, "Matches internal naming pattern"));
                }
            }
        });
    }

    private async Task AnalyzeEvaluatedItemsAsync(Project project, string projectPath, MSBuildArtifactAnalysis analysis)
    {
        await Task.Run(() =>
        {
            foreach (var item in project.AllEvaluatedItems)
            {
                // 1. Items generated at runtime without an XML source
                if (item.Xml == null)
                {
                    analysis.ArtifactItems.Add(new ArtifactItem(item.ItemType, item.EvaluatedInclude, "Runtime-generated item (no XML source)"));
                    continue;
                }

                // 2. Items defined in MSBuild/SDK infrastructure files
                if (item.Xml.ContainingProject.FullPath != projectPath &&
                    IsMSBuildInfrastructurePath(item.Xml.ContainingProject.FullPath))
                {
                    analysis.ArtifactItems.Add(new ArtifactItem(item.ItemType, item.EvaluatedInclude, 
                        $"Defined in MSBuild/SDK infrastructure: {Path.GetFileName(item.Xml.ContainingProject.FullPath)}"));
                }

                // 3. Items matching internal naming patterns
                else if (InternalItemPattern.IsMatch(item.ItemType))
                {
                    analysis.ArtifactItems.Add(new ArtifactItem(item.ItemType, item.EvaluatedInclude, "Matches internal naming pattern"));
                }
            }
        });
    }

    private void AnalyzeExplicitProperties(ProjectRootElement projectRootElement, MSBuildArtifactAnalysis analysis)
    {
        foreach (var propElement in projectRootElement.Properties)
        {
            // Check if this property is typically implicit in SDK-style projects
            if (SdkImplicitProperties.Contains(propElement.Name))
            {
                analysis.RedundantProperties.Add(new RedundantProperty(propElement.Name, propElement.Value, 
                    "Property is typically implicit in SDK-style projects"));
            }
        }
    }

    private void AnalyzeExplicitItems(ProjectRootElement projectRootElement, MSBuildArtifactAnalysis analysis)
    {
        foreach (var itemGroupElement in projectRootElement.ItemGroups)
        {
            foreach (var itemElement in itemGroupElement.Items)
            {
                // Check if this item type is typically implicit in SDK-style projects
                if (SdkImplicitItemTypes.Contains(itemElement.ItemType))
                {
                    // For items, we need more sophisticated logic to determine if they're truly redundant
                    // This would require checking if the Include pattern matches SDK default globs
                    // For now, we'll mark them as potentially redundant
                    analysis.RedundantItems.Add(new RedundantItem(itemElement.ItemType, itemElement.Include,
                        "Item type is typically implicit in SDK-style projects (requires glob pattern analysis)"));
                }
            }
        }
    }

    private bool IsMSBuildInfrastructurePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            
            // Check if path contains known infrastructure directory names
            foreach (var infraDir in InfrastructureDirectoryNames)
            {
                if (normalizedPath.Contains(infraDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check common infrastructure locations
            var commonPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };

            foreach (var commonPath in commonPaths)
            {
                if (!string.IsNullOrEmpty(commonPath) && 
                    normalizedPath.StartsWith(commonPath, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedPath.Contains("MSBuild", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Contains("dotnet", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze path for MSBuild infrastructure: {Path}", path);
            return false;
        }
    }

    public void Dispose()
    {
        _projectCollection.Dispose();
        GC.SuppressFinalize(this);
    }
}