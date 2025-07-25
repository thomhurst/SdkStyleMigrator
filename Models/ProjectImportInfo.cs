namespace SdkMigrator.Models;

/// <summary>
/// Represents information about an import statement in a project file
/// </summary>
public class ProjectImportInfo
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ImportPath { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public string? Label { get; set; }
    public string? Sdk { get; set; }
    public bool IsSystemImport { get; set; }
    public string Category { get; set; } = "Custom";
    public string? ResolvedPath { get; set; }
    public bool UserDecision { get; set; } = true; // Default to keep
}

/// <summary>
/// Groups imports by their file for easier management
/// </summary>
public class ImportGroup
{
    public string ImportFile { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<ProjectImportInfo> Imports { get; set; } = new();
    public int TotalCount => Imports.Count;
    public int SelectedCount => Imports.Count(i => i.UserDecision);
}

/// <summary>
/// Result of the import scanning process
/// </summary>
public class ImportScanResult
{
    public List<ImportGroup> ImportGroups { get; set; } = new();
    public int TotalImports => ImportGroups.Sum(g => g.TotalCount);
    public int SelectedImports => ImportGroups.Sum(g => g.SelectedCount);
    public bool HasCustomImports => ImportGroups.Any(g => g.Category != "System");
}

/// <summary>
/// Options for import selection behavior
/// </summary>
public class ImportSelectionOptions
{
    public bool InteractiveMode { get; set; } = true;
    public bool DefaultKeepAll { get; set; } = true;
    public List<string> AlwaysExcludePatterns { get; set; } = new();
    public List<string> AlwaysIncludePatterns { get; set; } = new();
}