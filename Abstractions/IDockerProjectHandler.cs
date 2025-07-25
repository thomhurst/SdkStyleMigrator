using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Docker (.dcproj) project migration specifics
/// </summary>
public interface IDockerProjectHandler
{
    /// <summary>
    /// Detects Docker project configuration and compose files
    /// </summary>
    Task<DockerProjectInfo> DetectDockerConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates Docker project to modern format
    /// </summary>
    Task MigrateDockerProjectAsync(
        DockerProjectInfo info, 
        XElement projectElement,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures docker-compose.yml and Dockerfiles are properly included
    /// </summary>
    void EnsureDockerFilesIncluded(string projectDirectory, XElement projectElement, DockerProjectInfo info);

    /// <summary>
    /// Checks if Docker project requires special handling or migration
    /// </summary>
    bool RequiresSpecialHandling(DockerProjectInfo info);

    /// <summary>
    /// Provides migration guidance for Docker orchestration projects
    /// </summary>
    DockerMigrationGuidance GetMigrationGuidance(DockerProjectInfo info);
}