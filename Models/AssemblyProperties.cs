namespace SdkMigrator.Models;

public class AssemblyProperties
{
    public string? Company { get; set; }
    public string? Product { get; set; }
    public string? Copyright { get; set; }
    public string? Trademark { get; set; }
    public string? AssemblyVersion { get; set; }
    public string? FileVersion { get; set; }
    public string? AssemblyTitle { get; set; }
    public string? AssemblyDescription { get; set; }
    public string? AssemblyConfiguration { get; set; }
    public string? NeutralResourcesLanguage { get; set; }
    public bool? ComVisible { get; set; }
    public string? Guid { get; set; }
    public List<string> InternalsVisibleTo { get; set; } = new();
    public Dictionary<string, string> OtherProperties { get; set; } = new();

    public bool HasProperties()
    {
        return !string.IsNullOrEmpty(Company) ||
               !string.IsNullOrEmpty(Product) ||
               !string.IsNullOrEmpty(Copyright) ||
               !string.IsNullOrEmpty(Trademark) ||
               !string.IsNullOrEmpty(AssemblyVersion) ||
               !string.IsNullOrEmpty(FileVersion) ||
               !string.IsNullOrEmpty(AssemblyTitle) ||
               !string.IsNullOrEmpty(AssemblyDescription) ||
               !string.IsNullOrEmpty(AssemblyConfiguration) ||
               !string.IsNullOrEmpty(NeutralResourcesLanguage) ||
               ComVisible.HasValue ||
               !string.IsNullOrEmpty(Guid) ||
               InternalsVisibleTo.Any() ||
               OtherProperties.Any();
    }
}