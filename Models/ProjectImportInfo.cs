using ReactiveUI;

namespace SdkMigrator.Models;

/// <summary>
/// Represents information about an import statement in a project file
/// </summary>
public class ProjectImportInfo : ReactiveObject
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ImportPath { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public string? Label { get; set; }
    public string? Sdk { get; set; }
    public bool IsSystemImport { get; set; }
    public string Category { get; set; } = "Custom";
    public string? ResolvedPath { get; set; }
    private bool _userDecision = true;
    public bool UserDecision
    {
        get => _userDecision;
        set => this.RaiseAndSetIfChanged(ref _userDecision, value);
    } // Default to keep
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
    public bool HasCustomImports => ImportGroups.Any(g => g.Imports.Any(i => !i.IsSystemImport));
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