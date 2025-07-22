using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class ImportScanner : IImportScanner
{
    private readonly ILogger<ImportScanner> _logger;
    
    // Known system imports that are handled by SDK
    private static readonly HashSet<string> SystemImports = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Common.props",
        "Microsoft.CSharp.targets",
        "Microsoft.VisualBasic.targets", 
        "Microsoft.FSharp.targets",
        "Microsoft.Common.targets",
        "Microsoft.WebApplication.targets",
        "Microsoft.NET.Sdk.props",
        "Microsoft.NET.Sdk.targets",
        "System.Data.Entity.Design.targets",
        "EntityFramework.targets",
        "Microsoft.Bcl.Build.targets",
        "Microsoft.TestPlatform.targets",
        "VSTest.targets",
        "Microsoft.NET.Test.Sdk.targets",
        "Microsoft.NET.Test.Sdk.props"
    };
    
    // Patterns for categorizing imports
    private static readonly Dictionary<string, string> ImportCategories = new()
    {
        { @"\.nuget[\\/]", "NuGet" },
        { @"packages[\\/]", "NuGet Package" },
        { @"\.paket[\\/]", "Paket" },
        { @"Directory\.Build\.(props|targets)", "Directory.Build" },
        { @"\.props$", "Props File" },
        { @"\.targets$", "Targets File" },
        { @"Microsoft\.", "Microsoft" },
        { @"EntityFramework", "Entity Framework" },
        { @"WebApplication", "Web Application" },
        { @"Test", "Testing" },
        { @"StyleCop", "Code Analysis" },
        { @"FxCop", "Code Analysis" },
        { @"CodeAnalysis", "Code Analysis" },
        { @"PostSharp", "AOP Framework" },
        { @"Fody", "IL Weaving" }
    };

    public ImportScanner(ILogger<ImportScanner> logger)
    {
        _logger = logger;
    }

    public async Task<ImportScanResult> ScanProjectImportsAsync(
        IEnumerable<Project> projects, 
        CancellationToken cancellationToken = default)
    {
        var result = new ImportScanResult();
        var importsByFile = new Dictionary<string, List<ProjectImportInfo>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Scanning imports in project: {ProjectFile}", project.FullPath);
            
            // Scan all imports in the project
            foreach (var import in project.Xml.Imports)
            {
                var importInfo = new ProjectImportInfo
                {
                    ProjectPath = project.FullPath,
                    ImportPath = import.Project,
                    Condition = import.Condition,
                    Label = import.Label,
                    Sdk = import.Sdk,
                    IsSystemImport = IsSystemImport(import.Project),
                    Category = CategorizeImport(import.Project),
                    ResolvedPath = ResolveImportPath(import.Project, Path.GetDirectoryName(project.FullPath)!)
                };
                
                // Group by the import file
                var groupKey = GetImportGroupKey(importInfo);
                if (!importsByFile.ContainsKey(groupKey))
                {
                    importsByFile[groupKey] = new List<ProjectImportInfo>();
                }
                importsByFile[groupKey].Add(importInfo);
            }
        }
        
        // Convert to ImportGroups
        foreach (var kvp in importsByFile.OrderBy(k => k.Key))
        {
            var group = new ImportGroup
            {
                ImportFile = kvp.Key,
                Category = kvp.Value.FirstOrDefault()?.Category ?? "Unknown",
                Imports = kvp.Value
            };
            result.ImportGroups.Add(group);
        }
        
        _logger.LogInformation("Import scan complete. Found {TotalImports} imports in {GroupCount} groups", 
            result.TotalImports, result.ImportGroups.Count);
        
        return await Task.FromResult(result);
    }

    public async Task<ImportScanResult> ScanProjectFileImportsAsync(
        IEnumerable<string> projectFilePaths,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportScanResult();
        var importsByFile = new Dictionary<string, List<ProjectImportInfo>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var projectPath in projectFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!File.Exists(projectPath))
            {
                _logger.LogWarning("Project file not found: {ProjectPath}", projectPath);
                continue;
            }
            
            _logger.LogInformation("Scanning imports in project file: {ProjectFile}", projectPath);
            
            try
            {
                // Read and parse the project XML directly
                var projectXml = XDocument.Load(projectPath);
                var ns = projectXml.Root?.Name.Namespace ?? XNamespace.None;
                var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
                
                // Find all Import elements
                var imports = projectXml.Descendants(ns + "Import");
                
                foreach (var import in imports)
                {
                    var importProject = import.Attribute("Project")?.Value;
                    if (string.IsNullOrEmpty(importProject))
                        continue;
                    
                    var importInfo = new ProjectImportInfo
                    {
                        ProjectPath = projectPath,
                        ImportPath = importProject,
                        Condition = import.Attribute("Condition")?.Value,
                        Label = import.Attribute("Label")?.Value,
                        Sdk = import.Attribute("Sdk")?.Value,
                        IsSystemImport = IsSystemImport(importProject),
                        Category = CategorizeImport(importProject),
                        ResolvedPath = ResolveImportPath(importProject, projectDir)
                    };
                    
                    // Group by the import file
                    var groupKey = GetImportGroupKey(importInfo);
                    if (!importsByFile.ContainsKey(groupKey))
                    {
                        importsByFile[groupKey] = new List<ProjectImportInfo>();
                    }
                    importsByFile[groupKey].Add(importInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning imports in project: {ProjectPath}", projectPath);
            }
        }
        
        // Convert to ImportGroups
        foreach (var kvp in importsByFile.OrderBy(k => k.Key))
        {
            var group = new ImportGroup
            {
                ImportFile = kvp.Key,
                Category = kvp.Value.FirstOrDefault()?.Category ?? "Unknown",
                Imports = kvp.Value
            };
            
            result.ImportGroups.Add(group);
        }
        
        _logger.LogInformation("Import scan complete. Found {ImportCount} imports in {GroupCount} groups", 
            result.TotalImports, result.ImportGroups.Count);
        
        return await Task.FromResult(result);
    }

    public string CategorizeImport(string importPath)
    {
        if (string.IsNullOrEmpty(importPath))
            return "Unknown";
            
        // Check against known patterns
        foreach (var pattern in ImportCategories)
        {
            if (Regex.IsMatch(importPath, pattern.Key, RegexOptions.IgnoreCase))
            {
                return pattern.Value;
            }
        }
        
        // Check file extension
        var extension = Path.GetExtension(importPath)?.ToLowerInvariant();
        return extension switch
        {
            ".props" => "Props File",
            ".targets" => "Targets File",
            _ => "Custom"
        };
    }

    public bool IsSystemImport(string importPath)
    {
        if (string.IsNullOrEmpty(importPath))
            return false;
            
        var fileName = Path.GetFileName(importPath);
        
        // Check if it's a known system import
        if (SystemImports.Contains(fileName))
            return true;
            
        // Check for MSBuild paths
        if (importPath.Contains("$(MSBuildToolsPath)", StringComparison.OrdinalIgnoreCase) ||
            importPath.Contains("$(MSBuildExtensionsPath)", StringComparison.OrdinalIgnoreCase) ||
            importPath.Contains("$(MSBuildBinPath)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // Check for SDK imports
        if (importPath.Contains("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            return true;
            
        return false;
    }

    public string? ResolveImportPath(string importPath, string projectDirectory)
    {
        try
        {
            // Handle MSBuild variables
            if (importPath.Contains("$("))
            {
                // For now, just return the original path
                // In a full implementation, we'd resolve MSBuild properties
                return importPath;
            }
            
            // Try to resolve relative paths
            if (!Path.IsPathRooted(importPath))
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, importPath));
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            
            return importPath;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve import path: {ImportPath}", importPath);
            return importPath;
        }
    }
    
    private string GetImportGroupKey(ProjectImportInfo importInfo)
    {
        // For NuGet package imports, group by package name
        if (importInfo.Category == "NuGet Package" && importInfo.ImportPath.Contains("packages"))
        {
            var match = Regex.Match(importInfo.ImportPath, @"packages[\\/]([^\\/]+\.\d+[^\\/]*)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return $"Package: {match.Groups[1].Value}";
            }
        }
        
        // For other imports, use the file name or path
        var fileName = Path.GetFileName(importInfo.ImportPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            return fileName;
        }
        
        return importInfo.ImportPath;
    }
}