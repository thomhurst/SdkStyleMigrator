using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Office/VSTO Add-in project migration specifics
/// </summary>
public interface IOfficeProjectHandler
{
    /// <summary>
    /// Detects Office/VSTO configuration and deployment manifests
    /// </summary>
    Task<OfficeProjectInfo> DetectOfficeConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates Office/VSTO specific elements to SDK-style format
    /// </summary>
    Task MigrateOfficeProjectAsync(
        OfficeProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures Office interop references are properly configured
    /// </summary>
    void ConfigureOfficeInteropReferences(XElement projectElement, OfficeProjectInfo info);

    /// <summary>
    /// Migrates deployment manifests and ClickOnce configuration
    /// </summary>
    void MigrateDeploymentConfiguration(string projectDirectory, XElement projectElement, OfficeProjectInfo info);

    /// <summary>
    /// Handles ribbon XML and custom task panes
    /// </summary>
    void MigrateOfficeCustomizations(string projectDirectory, XElement projectElement, OfficeProjectInfo info);

    /// <summary>
    /// Checks if VSTO project can be migrated to modern format
    /// </summary>
    bool CanMigrateToModernFormat(OfficeProjectInfo info);
}