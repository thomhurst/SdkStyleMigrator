using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Blazor WebAssembly project migration specifics
/// </summary>
public interface IBlazorProjectHandler
{
    /// <summary>
    /// Detects Blazor configuration and hosting model
    /// </summary>
    Task<BlazorProjectInfo> DetectBlazorConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates Blazor specific elements to SDK-style format
    /// </summary>
    Task MigrateBlazorProjectAsync(
        BlazorProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures wwwroot folder and static assets are properly included
    /// </summary>
    void EnsureBlazorStaticAssetsIncluded(string projectDirectory, XElement projectElement);

    /// <summary>
    /// Migrates PWA configuration including service workers and manifests
    /// </summary>
    void MigratePwaConfiguration(string projectDirectory, XElement projectElement, BlazorProjectInfo info);

    /// <summary>
    /// Configures Blazor WebAssembly specific build properties
    /// </summary>
    void ConfigureBlazorProperties(XElement projectElement, BlazorProjectInfo info);
    
    /// <summary>
    /// Sets whether to generate modern Program.cs files during migration
    /// </summary>
    void SetGenerateModernProgramCs(bool enabled);
}