using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

/// <summary>
/// Advanced MSBuild analysis service for complex property evaluation,
/// target resolution, and build configuration analysis
/// </summary>
public class MSBuildAnalyzer
{
    private readonly ILogger<MSBuildAnalyzer> _logger;

    public MSBuildAnalyzer(ILogger<MSBuildAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes complex MSBuild inheritance chains including imported .props and .targets files
    /// </summary>
    public async Task<MSBuildAnalysisResult> AnalyzeProjectBuildConfiguration(
        Project project, 
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new MSBuildAnalysisResult
        {
            ProjectPath = project.FullPath,
            EvaluatedProperties = new Dictionary<string, string>(),
            ImportedFiles = new List<ImportedFile>(),
            CustomTargets = new List<string>(),
            BuildComplexity = "Low"
        };

        try
        {
            // Analyze evaluated properties with their source
            await AnalyzeEvaluatedProperties(project, result, cancellationToken);

            // Analyze imported .props and .targets files
            await AnalyzeImportedFiles(project, result, cancellationToken);

            // Analyze custom build targets and tasks
            await AnalyzeCustomTargets(project, result, cancellationToken);

            // Analyze build conditions and dynamic properties
            await AnalyzeBuildConditions(project, result, cancellationToken);

            // Analyze Directory.Build.props inheritance
            await AnalyzeDirectoryBuildProps(projectDirectory, result, cancellationToken);

            // Determine overall build complexity
            DetermineBuildComplexity(result);

            _logger.LogInformation("MSBuild analysis complete: Complexity={Complexity}, Imports={ImportCount}, CustomTargets={TargetCount}",
                result.BuildComplexity, result.ImportedFiles.Count, result.CustomTargets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze MSBuild configuration for {ProjectPath}", project.FullPath);
            result.AnalysisErrors.Add($"MSBuild analysis error: {ex.Message}");
        }

        return result;
    }

    private async Task AnalyzeEvaluatedProperties(Project project, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        // Get all evaluated properties with their values
        foreach (var property in project.AllEvaluatedProperties)
        {
            var value = property.EvaluatedValue;
            var unevaluatedValue = property.UnevaluatedValue;
            
            // Track properties that use complex evaluation
            if (value != unevaluatedValue && unevaluatedValue.Contains("$("))
            {
                result.ComplexProperties.Add(new ComplexProperty
                {
                    Name = property.Name,
                    EvaluatedValue = value,
                    UnevaluatedValue = unevaluatedValue,
                    Source = property.Xml?.ContainingProject?.FullPath ?? "Unknown"
                });
            }

            result.EvaluatedProperties[property.Name] = value;
        }

        // Detect property overrides
        var propertyGroups = project.Xml.PropertyGroups;
        foreach (var group in propertyGroups)
        {
            if (group.Condition?.Length > 0)
            {
                result.ConditionalPropertyGroups.Add(new ConditionalElement
                {
                    Condition = group.Condition,
                    ElementType = "PropertyGroup",
                    Properties = group.Properties.Select(p => p.Name).ToList()
                });
            }
        }
    }

    private async Task AnalyzeImportedFiles(Project project, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        // Analyze all imports including those from SDK
        foreach (var import in project.Imports)
        {
            var importedProject = import.ImportedProject;
            if (importedProject != null)
            {
                var importedFile = new ImportedFile
                {
                    Path = importedProject.FullPath,
                    ImportLocation = import.ImportingElement?.Location.File ?? "Unknown",
                    IsImplicit = import.IsImported,
                    Condition = import.ImportingElement?.Condition ?? string.Empty
                };

                // Categorize import type
                if (importedProject.FullPath.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
                {
                    importedFile.Type = "Props";
                }
                else if (importedProject.FullPath.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
                {
                    importedFile.Type = "Targets";
                }
                else
                {
                    importedFile.Type = "Other";
                }

                // Check if it's a custom import (not from SDK)
                if (!importedProject.FullPath.Contains("sdk", StringComparison.OrdinalIgnoreCase) &&
                    !importedProject.FullPath.Contains("nuget", StringComparison.OrdinalIgnoreCase))
                {
                    importedFile.IsCustom = true;
                    result.HasCustomImports = true;
                }

                result.ImportedFiles.Add(importedFile);

                // Analyze properties defined in imported files
                await AnalyzeImportedProperties(importedProject, result, cancellationToken);
            }
        }
    }

    private async Task AnalyzeImportedProperties(ProjectRootElement importedProject, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        foreach (var propertyGroup in importedProject.PropertyGroups)
        {
            foreach (var property in propertyGroup.Properties)
            {
                if (!result.ImportedProperties.ContainsKey(property.Name))
                {
                    result.ImportedProperties[property.Name] = new List<string>();
                }
                result.ImportedProperties[property.Name].Add(importedProject.FullPath);
            }
        }
    }

    private async Task AnalyzeCustomTargets(Project project, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        // Find all targets, especially custom ones
        foreach (var target in project.Xml.Targets)
        {
            // Skip well-known targets
            var wellKnownTargets = new[] { "Build", "Clean", "Rebuild", "Restore", "Pack", "Publish" };
            if (!wellKnownTargets.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.CustomTargets.Add(target.Name);

                // Check for complex target dependencies
                if (!string.IsNullOrEmpty(target.DependsOnTargets))
                {
                    result.TargetDependencies[target.Name] = target.DependsOnTargets.Split(';').ToList();
                }

                // Check for custom tasks
                foreach (var task in target.Tasks)
                {
                    if (!task.Name.StartsWith("Microsoft.") && !task.Name.StartsWith("System."))
                    {
                        result.CustomTasks.Add($"{target.Name}.{task.Name}");
                    }
                }
            }

            // Check for BeforeTargets/AfterTargets hooks
            if (!string.IsNullOrEmpty(target.BeforeTargets) || !string.IsNullOrEmpty(target.AfterTargets))
            {
                result.TargetHooks.Add(new TargetHook
                {
                    TargetName = target.Name,
                    BeforeTargets = target.BeforeTargets,
                    AfterTargets = target.AfterTargets
                });
            }
        }
    }

    private async Task AnalyzeBuildConditions(Project project, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        // Analyze complex conditions in the project
        var conditionPattern = @"\$\(.*?\)|'[^']*'|""[^""]*""|Exists\([^)]+\)|HasTrailingSlash\([^)]+\)";
        var complexConditions = new List<string>();

        // Check property conditions
        foreach (var property in project.Xml.Properties)
        {
            if (!string.IsNullOrEmpty(property.Condition))
            {
                var matches = Regex.Matches(property.Condition, conditionPattern);
                if (matches.Count > 2) // Complex condition
                {
                    complexConditions.Add($"Property '{property.Name}': {property.Condition}");
                }
            }
        }

        // Check item conditions
        foreach (var item in project.Xml.Items)
        {
            if (!string.IsNullOrEmpty(item.Condition))
            {
                complexConditions.Add($"Item '{item.ItemType}': {item.Condition}");
            }
        }

        if (complexConditions.Any())
        {
            result.ComplexConditions = complexConditions;
            result.HasComplexConditions = true;
        }
    }

    private async Task AnalyzeDirectoryBuildProps(string projectDirectory, MSBuildAnalysisResult result, CancellationToken cancellationToken)
    {
        // Walk up directory tree looking for Directory.Build.props/targets
        var currentDir = new DirectoryInfo(projectDirectory);
        var buildFiles = new List<string>();

        while (currentDir != null)
        {
            var propsFile = Path.Combine(currentDir.FullName, "Directory.Build.props");
            var targetsFile = Path.Combine(currentDir.FullName, "Directory.Build.targets");

            if (File.Exists(propsFile))
            {
                buildFiles.Add(propsFile);
                result.DirectoryBuildFiles.Add(new DirectoryBuildFile
                {
                    Path = propsFile,
                    Type = "Props",
                    Level = GetDirectoryLevel(projectDirectory, currentDir.FullName)
                });
            }

            if (File.Exists(targetsFile))
            {
                buildFiles.Add(targetsFile);
                result.DirectoryBuildFiles.Add(new DirectoryBuildFile
                {
                    Path = targetsFile,
                    Type = "Targets",
                    Level = GetDirectoryLevel(projectDirectory, currentDir.FullName)
                });
            }

            currentDir = currentDir.Parent;

            // Stop at solution root or drive root
            if (currentDir?.GetFiles("*.sln").Any() == true || currentDir?.Parent == null)
            {
                break;
            }
        }

        // Analyze property inheritance from Directory.Build files
        foreach (var file in buildFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var xml = XDocument.Parse(content);
                
                foreach (var propertyGroup in xml.Descendants("PropertyGroup"))
                {
                    foreach (var property in propertyGroup.Elements())
                    {
                        if (!result.DirectoryBuildProperties.ContainsKey(property.Name.LocalName))
                        {
                            result.DirectoryBuildProperties[property.Name.LocalName] = new List<string>();
                        }
                        result.DirectoryBuildProperties[property.Name.LocalName].Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze Directory.Build file {File}: {Error}", file, ex.Message);
            }
        }
    }

    private void DetermineBuildComplexity(MSBuildAnalysisResult result)
    {
        var complexityScore = 0;

        // Factor in various complexity indicators
        if (result.HasCustomImports) complexityScore += 2;
        if (result.CustomTargets.Count > 3) complexityScore += 2;
        if (result.CustomTasks.Count > 0) complexityScore += 3;
        if (result.HasComplexConditions) complexityScore += 2;
        if (result.TargetHooks.Count > 2) complexityScore += 2;
        if (result.DirectoryBuildFiles.Count > 2) complexityScore += 1;
        if (result.ComplexProperties.Count > 5) complexityScore += 2;
        if (result.ImportedFiles.Count(f => f.IsCustom) > 3) complexityScore += 3;

        // Determine complexity level
        if (complexityScore >= 8)
            result.BuildComplexity = "High";
        else if (complexityScore >= 4)
            result.BuildComplexity = "Medium";
        else
            result.BuildComplexity = "Low";

        result.ComplexityScore = complexityScore;
    }

    private int GetDirectoryLevel(string projectDir, string currentDir)
    {
        var projectPath = Path.GetFullPath(projectDir);
        var currentPath = Path.GetFullPath(currentDir);
        
        var level = 0;
        while (!string.Equals(currentPath, projectPath, StringComparison.OrdinalIgnoreCase) && 
               currentPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
        {
            currentPath = Path.GetDirectoryName(currentPath) ?? string.Empty;
            level++;
        }
        
        return level;
    }
}

// Result models
public class MSBuildAnalysisResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public Dictionary<string, string> EvaluatedProperties { get; set; } = new();
    public List<ImportedFile> ImportedFiles { get; set; } = new();
    public List<string> CustomTargets { get; set; } = new();
    public List<string> CustomTasks { get; set; } = new();
    public List<ComplexProperty> ComplexProperties { get; set; } = new();
    public List<ConditionalElement> ConditionalPropertyGroups { get; set; } = new();
    public Dictionary<string, List<string>> TargetDependencies { get; set; } = new();
    public List<TargetHook> TargetHooks { get; set; } = new();
    public List<string> ComplexConditions { get; set; } = new();
    public List<DirectoryBuildFile> DirectoryBuildFiles { get; set; } = new();
    public Dictionary<string, List<string>> ImportedProperties { get; set; } = new();
    public Dictionary<string, List<string>> DirectoryBuildProperties { get; set; } = new();
    public List<string> AnalysisErrors { get; set; } = new();
    
    public bool HasCustomImports { get; set; }
    public bool HasComplexConditions { get; set; }
    public string BuildComplexity { get; set; } = "Low";
    public int ComplexityScore { get; set; }
}

public class ImportedFile
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Props, Targets, Other
    public string ImportLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public bool IsImplicit { get; set; }
    public bool IsCustom { get; set; }
}

public class ComplexProperty
{
    public string Name { get; set; } = string.Empty;
    public string EvaluatedValue { get; set; } = string.Empty;
    public string UnevaluatedValue { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public class ConditionalElement
{
    public string Condition { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public List<string> Properties { get; set; } = new();
}

public class TargetHook
{
    public string TargetName { get; set; } = string.Empty;
    public string? BeforeTargets { get; set; }
    public string? AfterTargets { get; set; }
}

public class DirectoryBuildFile
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Props or Targets
    public int Level { get; set; } // Distance from project directory
}