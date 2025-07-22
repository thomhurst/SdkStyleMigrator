using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Service for scanning and analyzing project targets
/// </summary>
public interface ITargetScanner
{
    /// <summary>
    /// Scans all targets in the given project file paths by reading XML directly
    /// </summary>
    Task<TargetScanResult> ScanProjectFileTargetsAsync(
        IEnumerable<string> projectFilePaths,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Categorizes a target based on its name and content
    /// </summary>
    string CategorizeTarget(string targetName, List<string> taskNames);
    
    /// <summary>
    /// Determines if a target is a system target that should be excluded
    /// </summary>
    bool IsSystemTarget(string targetName);
}