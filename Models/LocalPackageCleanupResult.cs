namespace SdkMigrator.Models;

public class LocalPackageCleanupResult
{
    public List<CleanedFile> CleanedFiles { get; set; } = new();
    public List<string> CleanedDirectories { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public long TotalBytesFreed { get; set; }
    public bool Success => !Errors.Any();
}

public class CleanedFile
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty; // DLL, XML, PDB, Config, etc.
    public string? AssociatedPackage { get; set; }
    public string Reason { get; set; } = string.Empty;
}