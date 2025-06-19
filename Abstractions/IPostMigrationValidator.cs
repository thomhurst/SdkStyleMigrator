using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IPostMigrationValidator
{
    /// <summary>
    /// Validates a migrated project to ensure it's properly configured
    /// </summary>
    Task<PostMigrationValidationResult> ValidateProjectAsync(
        string projectPath, 
        MigrationResult migrationResult,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Validates all migrated projects in a solution
    /// </summary>
    Task<PostMigrationValidationReport> ValidateSolutionAsync(
        string solutionDirectory,
        IEnumerable<MigrationResult> migrationResults,
        CancellationToken cancellationToken = default);
}

public class PostMigrationValidationResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SuggestedFix { get; set; }
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public class PostMigrationValidationReport
{
    public int TotalProjects { get; set; }
    public int ValidProjects { get; set; }
    public int ProjectsWithIssues { get; set; }
    public List<PostMigrationValidationResult> ProjectResults { get; set; } = new();
    public Dictionary<string, int> IssuesByCategory { get; set; } = new();
    public Dictionary<ValidationSeverity, int> IssuesBySeverity { get; set; } = new();
}