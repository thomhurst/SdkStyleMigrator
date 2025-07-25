using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles UWP project migration specifics
/// </summary>
public interface IUwpProjectHandler
{
    /// <summary>
    /// Detects UWP configuration and app manifest
    /// </summary>
    Task<UwpProjectInfo> DetectUwpConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates UWP specific elements to SDK-style format
    /// </summary>
    Task MigrateUwpProjectAsync(
        UwpProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures Package.appxmanifest and assets are properly included
    /// </summary>
    void EnsureUwpAssetsIncluded(string projectDirectory, XElement projectElement);

    /// <summary>
    /// Migrates certificate and packaging configuration
    /// </summary>
    void MigratePackagingConfiguration(XElement projectElement, UwpProjectInfo info);

    /// <summary>
    /// Sets up UWP specific build properties
    /// </summary>
    void ConfigureUwpProperties(XElement projectElement, UwpProjectInfo info);
}