namespace SdkMigrator.Models;

public class PackageReference
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsTransitive { get; set; }
    public string? TargetFramework { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}