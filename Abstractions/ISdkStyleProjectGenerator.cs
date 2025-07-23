using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ISdkStyleProjectGenerator
{
    Task<MigrationResult> GenerateSdkStyleProjectAsync(Project legacyProject, string outputPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets the import scan result to be used during project generation
    /// </summary>
    void SetImportScanResult(ImportScanResult? importScanResult);
    
    /// <summary>
    /// Sets the target scan result to be used during project generation
    /// </summary>
    void SetTargetScanResult(TargetScanResult? targetScanResult);
    
    /// <summary>
    /// Sets whether Central Package Management is enabled for this migration
    /// </summary>
    void SetCentralPackageManagementEnabled(bool enabled);
}