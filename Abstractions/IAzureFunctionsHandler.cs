using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Azure Functions project migration specifics
/// </summary>
public interface IAzureFunctionsHandler
{
    /// <summary>
    /// Detects Azure Functions configuration and dependencies
    /// </summary>
    Task<FunctionsProjectInfo> DetectFunctionsConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates Azure Functions specific elements to SDK-style format
    /// </summary>
    Task MigrateFunctionsProjectAsync(
        FunctionsProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures function.json, host.json, and local.settings.json are properly included
    /// </summary>
    void EnsureFunctionsFilesIncluded(string projectDirectory, XElement projectElement);

    /// <summary>
    /// Migrates Functions runtime and extension bundle configuration
    /// </summary>
    void MigrateFunctionsRuntime(XElement projectElement, FunctionsProjectInfo info);
}