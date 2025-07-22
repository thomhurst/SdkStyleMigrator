using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class DesignerFileHandler : IDesignerFileHandler
{
    private readonly ILogger<DesignerFileHandler> _logger;

    // Common designer file patterns
    private static readonly Dictionary<string, string[]> DesignerPatterns = new()
    {
        // WPF patterns
        [".xaml"] = new[] { ".xaml.cs" },
        [".baml"] = new[] { ".baml.cs" },
        
        // WinForms patterns
        [".cs"] = new[] { ".Designer.cs", ".designer.cs" },
        [".vb"] = new[] { ".Designer.vb", ".designer.vb" },
        [".resx"] = new[] { ".Designer.cs", ".designer.cs" },
        
        // Settings patterns
        [".settings"] = new[] { ".Designer.cs", ".designer.cs" },
        
        // Service References
        [".svcmap"] = new[] { ".cs", ".vb" },
        [".datasource"] = new[] { ".cs", ".vb" }
    };

    public DesignerFileHandler(ILogger<DesignerFileHandler> logger)
    {
        _logger = logger;
    }

    public DesignerFileRelationships AnalyzeDesignerRelationships(Project project)
    {
        var result = new DesignerFileRelationships
        {
            ProjectPath = project.FullPath
        };

        var projectDir = Path.GetDirectoryName(project.FullPath)!;
        
        // Analyze all compile items
        var compileItems = project.Items
            .Where(i => i.ItemType == "Compile")
            .ToList();

        // Group by DependentUpon relationships
        var dependentGroups = compileItems
            .Where(i => i.HasMetadata("DependentUpon"))
            .GroupBy(i => Path.Combine(Path.GetDirectoryName(i.EvaluatedInclude) ?? "", 
                                      i.GetMetadataValue("DependentUpon")))
            .ToList();

        foreach (var group in dependentGroups)
        {
            var parentFile = group.Key;
            var dependentFiles = group.Select(i => i.EvaluatedInclude).ToList();
            
            result.FileRelationships.Add(new FileRelationship
            {
                ParentFile = parentFile,
                DependentFiles = dependentFiles,
                RelationType = DetermineRelationType(parentFile, dependentFiles)
            });
        }

        // Find orphaned designer files (designer files without proper DependentUpon)
        FindOrphanedDesignerFiles(project, compileItems, result);
        
        // Analyze XAML relationships
        AnalyzeXamlRelationships(project, result);
        
        // Analyze WinForms relationships
        AnalyzeWinFormsRelationships(project, result);

        return result;
    }

    private void FindOrphanedDesignerFiles(Project project, List<ProjectItem> compileItems, 
        DesignerFileRelationships result)
    {
        var projectDir = Path.GetDirectoryName(project.FullPath)!;
        
        // Find potential designer files without DependentUpon
        var potentialDesignerFiles = compileItems
            .Where(i => !i.HasMetadata("DependentUpon"))
            .Where(i => IsDesignerFile(i.EvaluatedInclude))
            .ToList();

        foreach (var designerFile in potentialDesignerFiles)
        {
            var designerPath = designerFile.EvaluatedInclude;
            var potentialParent = FindPotentialParentFile(designerPath, compileItems);
            
            if (potentialParent != null)
            {
                result.OrphanedDesignerFiles.Add(new OrphanedDesignerFile
                {
                    DesignerFile = designerPath,
                    PotentialParent = potentialParent,
                    Reason = "Missing DependentUpon metadata"
                });
                
                _logger.LogWarning("Found orphaned designer file {DesignerFile} that should depend on {ParentFile}",
                    designerPath, potentialParent);
            }
        }
    }

    private bool IsDesignerFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        
        // Check common designer file patterns
        return fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".xaml.vb", StringComparison.OrdinalIgnoreCase);
    }

    private string? FindPotentialParentFile(string designerPath, List<ProjectItem> compileItems)
    {
        var designerFileName = Path.GetFileName(designerPath);
        var directory = Path.GetDirectoryName(designerPath) ?? "";
        
        // Try to find the parent file based on naming conventions
        foreach (var (extension, patterns) in DesignerPatterns)
        {
            foreach (var pattern in patterns)
            {
                if (designerFileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = designerFileName.Substring(0, 
                        designerFileName.Length - pattern.Length);
                    var potentialParentName = baseName + extension;
                    var potentialParentPath = Path.Combine(directory, potentialParentName);
                    
                    // Check if this potential parent exists in the project
                    if (compileItems.Any(i => i.EvaluatedInclude.Equals(potentialParentPath, 
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        return potentialParentPath;
                    }
                }
            }
        }
        
        return null;
    }

    private void AnalyzeXamlRelationships(Project project, DesignerFileRelationships result)
    {
        // Get all XAML files
        var xamlItems = project.Items
            .Where(i => i.ItemType == "Page" || 
                       i.ItemType == "ApplicationDefinition" ||
                       i.ItemType == "Resource")
            .Where(i => i.EvaluatedInclude.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var xamlItem in xamlItems)
        {
            var xamlPath = xamlItem.EvaluatedInclude;
            var expectedCodeBehind = xamlPath + ".cs";
            var expectedCodeBehindVb = xamlPath + ".vb";
            
            // Check if code-behind exists
            var codeBehindExists = project.Items.Any(i => 
                i.ItemType == "Compile" && 
                (i.EvaluatedInclude.Equals(expectedCodeBehind, StringComparison.OrdinalIgnoreCase) ||
                 i.EvaluatedInclude.Equals(expectedCodeBehindVb, StringComparison.OrdinalIgnoreCase)));
            
            if (!codeBehindExists)
            {
                result.XamlIssues.Add(new XamlIssue
                {
                    XamlFile = xamlPath,
                    Issue = "Missing code-behind file",
                    ExpectedCodeBehind = expectedCodeBehind
                });
            }
            else
            {
                // Check if the code-behind has proper DependentUpon
                var codeBehindItem = project.Items.FirstOrDefault(i => 
                    i.ItemType == "Compile" && 
                    (i.EvaluatedInclude.Equals(expectedCodeBehind, StringComparison.OrdinalIgnoreCase) ||
                     i.EvaluatedInclude.Equals(expectedCodeBehindVb, StringComparison.OrdinalIgnoreCase)));
                
                if (codeBehindItem != null && !codeBehindItem.HasMetadata("DependentUpon"))
                {
                    result.XamlIssues.Add(new XamlIssue
                    {
                        XamlFile = xamlPath,
                        Issue = "Code-behind missing DependentUpon metadata",
                        ExpectedCodeBehind = codeBehindItem.EvaluatedInclude
                    });
                }
            }
        }
    }

    private void AnalyzeWinFormsRelationships(Project project, DesignerFileRelationships result)
    {
        // Get all WinForms items
        var winFormsItems = project.Items
            .Where(i => i.ItemType == "Compile" &&
                       i.HasMetadata("SubType") &&
                       (i.GetMetadataValue("SubType") == "Form" ||
                        i.GetMetadataValue("SubType") == "UserControl" ||
                        i.GetMetadataValue("SubType") == "Component"))
            .ToList();

        foreach (var formItem in winFormsItems)
        {
            var formPath = formItem.EvaluatedInclude;
            var expectedDesigner = Path.Combine(
                Path.GetDirectoryName(formPath) ?? "",
                Path.GetFileNameWithoutExtension(formPath) + ".Designer" + 
                Path.GetExtension(formPath));
            
            // Check if designer file exists
            var designerItem = project.Items.FirstOrDefault(i => 
                i.ItemType == "Compile" && 
                i.EvaluatedInclude.Equals(expectedDesigner, StringComparison.OrdinalIgnoreCase));
            
            if (designerItem == null)
            {
                // Try lowercase .designer
                expectedDesigner = Path.Combine(
                    Path.GetDirectoryName(formPath) ?? "",
                    Path.GetFileNameWithoutExtension(formPath) + ".designer" + 
                    Path.GetExtension(formPath));
                    
                designerItem = project.Items.FirstOrDefault(i => 
                    i.ItemType == "Compile" && 
                    i.EvaluatedInclude.Equals(expectedDesigner, StringComparison.OrdinalIgnoreCase));
            }
            
            if (designerItem != null && !designerItem.HasMetadata("DependentUpon"))
            {
                // Make sure this file wasn't already identified as orphaned
                var alreadyIdentified = result.OrphanedDesignerFiles
                    .Any(o => o.DesignerFile.Equals(designerItem.EvaluatedInclude, 
                        StringComparison.OrdinalIgnoreCase));
                
                if (!alreadyIdentified)
                {
                    result.WinFormsIssues.Add(new WinFormsIssue
                    {
                        FormFile = formPath,
                        DesignerFile = designerItem.EvaluatedInclude,
                        Issue = "Designer file missing DependentUpon metadata"
                    });
                }
            }
        }
    }

    private string DetermineRelationType(string parentFile, List<string> dependentFiles)
    {
        var parentExt = Path.GetExtension(parentFile).ToLowerInvariant();
        
        if (parentExt == ".xaml")
            return "XAML";
        
        if (parentExt == ".resx")
            return "Resources";
            
        if (parentExt == ".settings")
            return "Settings";
            
        if (dependentFiles.Any(f => f.Contains(".Designer.", StringComparison.OrdinalIgnoreCase)))
            return "WinForms";
            
        return "Other";
    }

    public void MigrateDesignerRelationships(
        DesignerFileRelationships relationships, 
        XElement projectElement,
        MigrationResult result)
    {
        // Fix orphaned designer files
        if (relationships.OrphanedDesignerFiles.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var orphaned in relationships.OrphanedDesignerFiles)
            {
                // Add Update element to fix the relationship
                var updateElement = new XElement("Compile",
                    new XAttribute("Update", orphaned.DesignerFile));
                    
                var parentFileName = Path.GetFileName(orphaned.PotentialParent);
                updateElement.Add(new XElement("DependentUpon", parentFileName));
                
                itemGroup.Add(updateElement);
                
                result.Warnings.Add($"Fixed orphaned designer file relationship: {orphaned.DesignerFile} -> {orphaned.PotentialParent}");
                _logger.LogInformation("Fixed orphaned designer file {DesignerFile} to depend on {ParentFile}",
                    orphaned.DesignerFile, orphaned.PotentialParent);
            }
            
            if (itemGroup.HasElements)
                projectElement.Add(itemGroup);
        }
        
        // Fix XAML issues
        if (relationships.XamlIssues.Any(i => i.Issue.Contains("DependentUpon")))
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var issue in relationships.XamlIssues.Where(i => i.Issue.Contains("DependentUpon")))
            {
                var updateElement = new XElement("Compile",
                    new XAttribute("Update", issue.ExpectedCodeBehind));
                    
                var xamlFileName = Path.GetFileName(issue.XamlFile);
                updateElement.Add(new XElement("DependentUpon", xamlFileName));
                
                itemGroup.Add(updateElement);
                
                result.Warnings.Add($"Fixed XAML code-behind relationship: {issue.ExpectedCodeBehind} -> {issue.XamlFile}");
            }
            
            if (itemGroup.HasElements)
                projectElement.Add(itemGroup);
        }
        
        // Fix WinForms issues (skip if already fixed by orphaned files)
        var fixedFiles = relationships.OrphanedDesignerFiles
            .Select(o => o.DesignerFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            
        var winFormsToFix = relationships.WinFormsIssues
            .Where(i => !fixedFiles.Contains(i.DesignerFile))
            .ToList();
            
        if (winFormsToFix.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var issue in winFormsToFix)
            {
                var updateElement = new XElement("Compile",
                    new XAttribute("Update", issue.DesignerFile));
                    
                var formFileName = Path.GetFileName(issue.FormFile);
                updateElement.Add(new XElement("DependentUpon", formFileName));
                
                itemGroup.Add(updateElement);
                
                result.Warnings.Add($"Fixed WinForms designer relationship: {issue.DesignerFile} -> {issue.FormFile}");
            }
            
            if (itemGroup.HasElements)
                projectElement.Add(itemGroup);
        }
    }
}

// Models for designer file relationships
public class DesignerFileRelationships
{
    public string ProjectPath { get; set; } = string.Empty;
    public List<FileRelationship> FileRelationships { get; set; } = new();
    public List<OrphanedDesignerFile> OrphanedDesignerFiles { get; set; } = new();
    public List<XamlIssue> XamlIssues { get; set; } = new();
    public List<WinFormsIssue> WinFormsIssues { get; set; } = new();
}

public class FileRelationship
{
    public string ParentFile { get; set; } = string.Empty;
    public List<string> DependentFiles { get; set; } = new();
    public string RelationType { get; set; } = string.Empty;
}

public class OrphanedDesignerFile
{
    public string DesignerFile { get; set; } = string.Empty;
    public string PotentialParent { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class XamlIssue
{
    public string XamlFile { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string ExpectedCodeBehind { get; set; } = string.Empty;
}

public class WinFormsIssue
{
    public string FormFile { get; set; } = string.Empty;
    public string DesignerFile { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
}