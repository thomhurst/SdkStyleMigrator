namespace SdkMigrator.Models;

public class MigrationOptions
{
    public string DirectoryPath { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public string? OutputDirectory { get; set; }
    public string? TargetFramework { get; set; }
    public bool CreateBackup { get; set; }
    public bool Force { get; set; }
    public int MaxDegreeOfParallelism { get; set; } = 1;
    public string LogLevel { get; set; } = "Information";
    public bool UseOfflineMode { get; set; }
    public string? NuGetConfigPath { get; set; }
}