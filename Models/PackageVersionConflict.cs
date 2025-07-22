namespace SdkMigrator.Models;

public class PackageVersionConflict
{
    public string PackageId { get; set; } = string.Empty;
    public List<ProjectPackageVersion> RequestedVersions { get; set; } = new();
}

public class ProjectPackageVersion
{
    public string ProjectPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsTransitive { get; set; }
}

public class ProjectPackageReference : PackageReference
{
    public string ProjectPath { get; set; } = string.Empty;
}

public class PackageVersionResolution
{
    public Dictionary<string, string> ResolvedVersions { get; set; } = new();
    public List<ProjectVersionUpdate> ProjectsNeedingUpdate { get; set; } = new();
}

public class ProjectVersionUpdate
{
    public string ProjectPath { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string OldVersion { get; set; } = string.Empty;
    public string NewVersion { get; set; } = string.Empty;
}

public enum ConflictResolutionStrategy
{
    UseHighest,
    UseLowest,
    UseLatestStable,
    UseMostCommon,
    Interactive
}