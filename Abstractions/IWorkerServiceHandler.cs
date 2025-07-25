using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Worker Service project migration specifics
/// </summary>
public interface IWorkerServiceHandler
{
    /// <summary>
    /// Detects Worker Service configuration and hosted services
    /// </summary>
    Task<WorkerServiceInfo> DetectWorkerServiceConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates Worker Service specific elements to SDK-style format
    /// </summary>
    Task MigrateWorkerServiceProjectAsync(
        WorkerServiceInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures appsettings.json and configuration files are properly included
    /// </summary>
    void EnsureWorkerConfigurationIncluded(string projectDirectory, XElement projectElement);

    /// <summary>
    /// Sets up Worker Service specific build properties
    /// </summary>
    void ConfigureWorkerServiceProperties(XElement projectElement, WorkerServiceInfo info);
}