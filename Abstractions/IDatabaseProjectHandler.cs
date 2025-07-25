using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles Database (.sqlproj) project migration specifics
/// </summary>
public interface IDatabaseProjectHandler
{
    /// <summary>
    /// Detects database project configuration and SQL scripts
    /// </summary>
    Task<DatabaseProjectInfo> DetectDatabaseConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates database project to modern format (SQL Server Data Tools)
    /// </summary>
    Task MigrateDatabaseProjectAsync(
        DatabaseProjectInfo info, 
        XElement projectElement,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures SQL scripts and schema files are properly included
    /// </summary>
    void EnsureSqlFilesIncluded(string projectDirectory, XElement projectElement, DatabaseProjectInfo info);

    /// <summary>
    /// Migrates database references and deployment settings
    /// </summary>
    void MigrateDatabaseReferences(XElement projectElement, DatabaseProjectInfo info);

    /// <summary>
    /// Checks if database project can be migrated or requires manual handling
    /// </summary>
    bool CanMigrateToModernFormat(DatabaseProjectInfo info);
}