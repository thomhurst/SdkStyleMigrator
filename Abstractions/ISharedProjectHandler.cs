using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Shared (.shproj) project migration specifics
/// </summary>
public interface ISharedProjectHandler
{
    /// <summary>
    /// Detects shared project configuration and source files
    /// </summary>
    Task<SharedProjectInfo> DetectSharedProjectConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides migration guidance for shared projects (which may need conversion to class library)
    /// </summary>
    Task<SharedProjectMigrationGuidance> GetMigrationGuidanceAsync(
        SharedProjectInfo info,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts shared project to class library if appropriate
    /// </summary>
    Task<XElement?> ConvertToClassLibraryAsync(
        SharedProjectInfo info,
        string targetDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies projects that reference this shared project
    /// </summary>
    Task<List<string>> FindReferencingProjectsAsync(string sharedProjectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if shared project can be safely converted to class library
    /// </summary>
    bool CanConvertToClassLibrary(SharedProjectInfo info);
}