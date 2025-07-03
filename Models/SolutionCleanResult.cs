namespace SdkMigrator.Models;

public class SolutionCleanResult
{
    public bool Success { get; set; }
    public string SolutionPath { get; set; } = string.Empty;
    public string? BackupPath { get; set; }

    // Summary of changes made
    public int ProjectsRemoved { get; set; }
    public int DuplicatesRemoved { get; set; }
    public int ConfigurationsFixed { get; set; }
    public int ConfigurationsAdded { get; set; }
    public int EmptyFoldersRemoved { get; set; }
    public int SourceControlBindingsRemoved { get; set; }

    // Detailed lists
    public List<string> RemovedProjects { get; set; } = new();
    public List<string> RemovedDuplicates { get; set; } = new();
    public List<string> FixedConfigurations { get; set; } = new();
    public List<string> AddedConfigurations { get; set; } = new();
    public List<string> RemovedFolders { get; set; } = new();
    public List<string> RemovedSections { get; set; } = new();

    // Issues that couldn't be fixed automatically
    public List<SolutionIssue> UnfixableIssues { get; set; } = new();

    // Warnings
    public List<string> Warnings { get; set; } = new();

    // Errors
    public List<string> Errors { get; set; } = new();

    public bool HasChanges => ProjectsRemoved > 0 ||
                             DuplicatesRemoved > 0 ||
                             ConfigurationsFixed > 0 ||
                             ConfigurationsAdded > 0 ||
                             EmptyFoldersRemoved > 0 ||
                             SourceControlBindingsRemoved > 0;
}

public class SolutionIssue
{
    public SolutionIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectPath { get; set; }
    public string? Details { get; set; }
}

public enum SolutionIssueType
{
    DuplicateGuid,
    InvalidGuid,
    UnknownProjectType,
    CorruptedEntry,
    Other
}