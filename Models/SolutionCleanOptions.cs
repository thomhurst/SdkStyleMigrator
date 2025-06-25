namespace SdkMigrator.Models;

public class SolutionCleanOptions
{
    /// <summary>
    /// Remove references to non-existent project files
    /// </summary>
    public bool FixMissingProjects { get; set; }

    /// <summary>
    /// Remove duplicate project entries (same project path)
    /// </summary>
    public bool RemoveDuplicates { get; set; }

    /// <summary>
    /// Clean up orphaned configurations and ensure all projects have entries for all solution configs
    /// </summary>
    public bool FixConfigurations { get; set; }

    /// <summary>
    /// Remove old source control bindings (TFS/SCC)
    /// </summary>
    public bool RemoveSourceControlBindings { get; set; }

    /// <summary>
    /// Remove empty solution folders
    /// </summary>
    public bool RemoveEmptyFolders { get; set; }

    /// <summary>
    /// Remove references to non-standard project types (.vcxproj, .sqlproj, etc.)
    /// </summary>
    public bool RemoveNonStandardProjects { get; set; }

    /// <summary>
    /// Apply all safe fixes (excludes RemoveNonStandardProjects)
    /// </summary>
    public bool FixAll { get; set; }

    /// <summary>
    /// Perform a dry run without making changes
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Create backup of the solution file before making changes
    /// </summary>
    public bool CreateBackup { get; set; } = true;

    /// <summary>
    /// Apply all safe fixes when FixAll is true
    /// </summary>
    public void ApplyFixAll()
    {
        if (FixAll)
        {
            FixMissingProjects = true;
            RemoveDuplicates = true;
            FixConfigurations = true;
            RemoveSourceControlBindings = true;
            RemoveEmptyFolders = true;
            // RemoveNonStandardProjects is excluded from FixAll as it's more opinionated
        }
    }
}