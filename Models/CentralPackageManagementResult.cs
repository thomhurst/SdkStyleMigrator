namespace SdkMigrator.Models;

public class CentralPackageManagementResult
{
    public bool Success { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public int PackageCount { get; set; }
    public int ProjectsUpdated { get; set; }
    public List<PackageVersionConflict> VersionConflicts { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class PackageVersionConflict
{
    public string PackageId { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
    public string ResolvedVersion { get; set; } = string.Empty;
    public string ResolutionReason { get; set; } = string.Empty;
}