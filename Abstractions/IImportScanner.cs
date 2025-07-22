using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Service for scanning and analyzing project imports
/// </summary>
public interface IImportScanner
{
    /// <summary>
    /// Scans all imports in the given projects
    /// </summary>
    Task<ImportScanResult> ScanProjectImportsAsync(
        IEnumerable<Project> projects,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Scans all imports in the given project file paths by reading XML directly
    /// </summary>
    Task<ImportScanResult> ScanProjectFileImportsAsync(
        IEnumerable<string> projectFilePaths,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Categorizes an import path
    /// </summary>
    string CategorizeImport(string importPath);
    
    /// <summary>
    /// Determines if an import is a system import that should be excluded
    /// </summary>
    bool IsSystemImport(string importPath);
    
    /// <summary>
    /// Resolves the actual file path of an import
    /// </summary>
    string? ResolveImportPath(string importPath, string projectDirectory);
}