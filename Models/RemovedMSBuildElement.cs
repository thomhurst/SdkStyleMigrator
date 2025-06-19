namespace SdkMigrator.Models;

public class RemovedMSBuildElement
{
    public string ElementType { get; set; } = string.Empty; // Import, Target, UsingTask, PropertyGroup, etc.
    public string Name { get; set; } = string.Empty;
    public string XmlContent { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public string SuggestedMigrationPath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}