namespace SdkMigrator.Models;

/// <summary>
/// Represents information about a target in a project file
/// </summary>
public class ProjectTargetInfo
{
    public string ProjectPath { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public string? DependsOnTargets { get; set; }
    public string? BeforeTargets { get; set; }
    public string? AfterTargets { get; set; }
    public string? Label { get; set; }
    public bool IsSystemTarget { get; set; }
    public string Category { get; set; } = "Custom";
    public List<string> Tasks { get; set; } = new();
    public bool UserDecision { get; set; } = true; // Default to keep
    
    /// <summary>
    /// Gets a brief description of what this target does based on its tasks
    /// </summary>
    public string Description
    {
        get
        {
            if (Tasks.Count == 0)
                return "Empty target";
            
            var taskTypes = Tasks.Distinct().ToList();
            if (taskTypes.Count == 1)
                return $"Executes {taskTypes[0]} task";
            
            return $"Executes {taskTypes.Count} different tasks";
        }
    }
}

/// <summary>
/// Groups targets by their category for easier management
/// </summary>
public class TargetGroup
{
    public string GroupName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<ProjectTargetInfo> Targets { get; set; } = new();
    public int TotalCount => Targets.Count;
    public int SelectedCount => Targets.Count(t => t.UserDecision);
}

/// <summary>
/// Result of the target scanning process
/// </summary>
public class TargetScanResult
{
    public List<TargetGroup> TargetGroups { get; set; } = new();
    public int TotalTargets => TargetGroups.Sum(g => g.TotalCount);
    public int SelectedTargets => TargetGroups.Sum(g => g.SelectedCount);
    public bool HasCustomTargets => TargetGroups.Any(g => g.Category != "System");
}

/// <summary>
/// Options for target selection behavior
/// </summary>
public class TargetSelectionOptions
{
    public bool InteractiveMode { get; set; } = true;
    public bool DefaultKeepAll { get; set; } = true;
    public List<string> AlwaysExcludePatterns { get; set; } = new();
    public List<string> AlwaysIncludePatterns { get; set; } = new();
}