namespace SdkMigrator.Abstractions;

/// <summary>
/// Service for detecting MSBuild evaluation artifacts that should be filtered out during migration.
/// </summary>
public interface IMSBuildArtifactDetector
{
    /// <summary>
    /// Determines if a property name represents an MSBuild evaluation artifact.
    /// </summary>
    /// <param name="propertyName">The property name to check</param>
    /// <param name="propertyValue">The property value (optional, for additional context)</param>
    /// <param name="containingProjectPath">The path of the project file containing this property (optional)</param>
    /// <returns>True if the property is an MSBuild artifact and should be filtered out</returns>
    bool IsPropertyArtifact(string propertyName, string? propertyValue = null, string? containingProjectPath = null);

    /// <summary>
    /// Determines if an item type represents an MSBuild evaluation artifact.
    /// </summary>
    /// <param name="itemType">The item type to check</param>
    /// <param name="itemInclude">The item include value (optional, for additional context)</param>
    /// <param name="containingProjectPath">The path of the project file containing this item (optional)</param>
    /// <returns>True if the item is an MSBuild artifact and should be filtered out</returns>
    bool IsItemArtifact(string itemType, string? itemInclude = null, string? containingProjectPath = null);

    /// <summary>
    /// Analyzes a legacy project to identify all MSBuild evaluation artifacts.
    /// This provides comprehensive analysis using MSBuild's evaluation APIs.
    /// </summary>
    /// <param name="projectPath">Path to the legacy project file</param>
    /// <returns>Analysis result containing identified artifacts</returns>
    Task<MSBuildArtifactAnalysis> AnalyzeProjectAsync(string projectPath);
}

/// <summary>
/// Result of MSBuild artifact analysis for a project.
/// </summary>
public class MSBuildArtifactAnalysis
{
    /// <summary>
    /// Properties that are MSBuild evaluation artifacts (runtime-generated, reserved, or from SDK infrastructure)
    /// </summary>
    public List<ArtifactProperty> ArtifactProperties { get; set; } = new();

    /// <summary>
    /// Items that are MSBuild evaluation artifacts (runtime-generated or from SDK infrastructure)
    /// </summary>
    public List<ArtifactItem> ArtifactItems { get; set; } = new();

    /// <summary>
    /// Properties explicitly defined in the project that would be implicit in SDK-style projects
    /// </summary>
    public List<RedundantProperty> RedundantProperties { get; set; } = new();

    /// <summary>
    /// Items explicitly defined in the project that would be implicit in SDK-style projects
    /// </summary>
    public List<RedundantItem> RedundantItems { get; set; } = new();
}

public record ArtifactProperty(string Name, string Value, string Reason);
public record ArtifactItem(string Type, string Include, string Reason);
public record RedundantProperty(string Name, string Value, string Reason);
public record RedundantItem(string Type, string Include, string Reason);