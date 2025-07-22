using SdkMigrator.Services;

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
    public bool LoadedWithDefensiveParsing { get; set; }
    public List<string> ConvertedHintPaths { get; set; } = new();
    public List<RemovedMSBuildElement> RemovedMSBuildElements { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
    public List<string>? TargetFrameworks { get; set; }

    // Edge case tracking
    public ProjectTypeInfo? DetectedProjectType { get; set; }
    public List<NativeDependency> NativeDependencies { get; set; } = new();
    public ServiceReferenceInfo? ServiceReferences { get; set; }
    public bool HasCriticalBlockers { get; set; }
}