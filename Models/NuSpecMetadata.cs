namespace SdkMigrator.Models;

public class NuSpecMetadata
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Authors { get; set; }
    public string? Owners { get; set; }
    public string? Description { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? Summary { get; set; }
    public string? Language { get; set; }
    public string? ProjectUrl { get; set; }
    public string? IconUrl { get; set; }
    public string? Icon { get; set; }
    public string? LicenseUrl { get; set; }
    public string? License { get; set; }
    public bool? RequireLicenseAcceptance { get; set; }
    public string? Tags { get; set; }
    public string? Copyright { get; set; }
    public string? Repository { get; set; }
    public string? RepositoryType { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? RepositoryBranch { get; set; }
    public string? RepositoryCommit { get; set; }
    public bool? Serviceable { get; set; }
    public string? Title { get; set; }
    public bool? DevelopmentDependency { get; set; }
    public List<NuSpecDependency> Dependencies { get; set; } = new();
    public List<NuSpecFile> Files { get; set; } = new();
    public List<NuSpecContentFile> ContentFiles { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class NuSpecDependency
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? TargetFramework { get; set; }
    public string? Include { get; set; }
    public string? Exclude { get; set; }
}

public class NuSpecFile
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Exclude { get; set; }
}

public class NuSpecContentFile
{
    public string Include { get; set; } = string.Empty;
    public string? Exclude { get; set; }
    public string? BuildAction { get; set; }
    public bool? CopyToOutput { get; set; }
    public bool? Flatten { get; set; }
}