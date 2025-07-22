namespace SdkMigrator.Models;

public class CentralPackageManagementResult
{
    public bool Success { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public int PackageCount { get; set; }
    public int ProjectsUpdated { get; set; }
    public List<CpmPackageVersionConflict> VersionConflicts { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class CpmPackageVersionConflict
{
    public string PackageId { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
    public string ResolvedVersion { get; set; } = string.Empty;
    public string ResolutionReason { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<string> TargetFrameworks { get; set; } = new();
    public bool HasWarnings { get; set; }
    public List<string> Warnings { get; set; } = new();
}