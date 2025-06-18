namespace SdkMigrator.Models;

public class MigrationResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> RemovedElements { get; set; } = new();
    public List<PackageReference> MigratedPackages { get; set; } = new();
}