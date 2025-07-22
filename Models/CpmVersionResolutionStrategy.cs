namespace SdkMigrator.Models;

public enum CpmVersionResolutionStrategy
{
    /// <summary>
    /// Use the highest version found across all projects
    /// </summary>
    UseHighest,
    
    /// <summary>
    /// Use the lowest version found across all projects (most conservative)
    /// </summary>
    UseLowest,
    
    /// <summary>
    /// Use the latest stable version (no prereleases unless all versions are prereleases)
    /// </summary>
    UseLatestStable,
    
    /// <summary>
    /// Use the version that appears most frequently across projects
    /// </summary>
    UseMostCommon,
    
    /// <summary>
    /// Use semantic version compatibility - prefer versions that maintain compatibility
    /// </summary>
    SemanticCompatible,
    
    /// <summary>
    /// Use target framework aware resolution - consider compatibility with all target frameworks
    /// </summary>
    FrameworkCompatible
}

public class CpmVersionResolutionOptions
{
    /// <summary>
    /// Strategy to use for resolving version conflicts
    /// </summary>
    public CpmVersionResolutionStrategy Strategy { get; set; } = CpmVersionResolutionStrategy.UseHighest;
    
    /// <summary>
    /// Whether to prefer stable versions over prereleases
    /// </summary>
    public bool PreferStableVersions { get; set; } = true;
    
    /// <summary>
    /// Whether to check target framework compatibility when resolving versions
    /// </summary>
    public bool CheckFrameworkCompatibility { get; set; } = true;
    
    /// <summary>
    /// Custom version resolution rules for specific packages
    /// </summary>
    public Dictionary<string, string> PackageVersionOverrides { get; set; } = new();
    
    /// <summary>
    /// Whether to analyze semantic version compatibility
    /// </summary>
    public bool UseSemanticVersioning { get; set; } = true;
}