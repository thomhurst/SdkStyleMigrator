using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using SdkMigrator.Services;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

public interface IDesignerFileHandler
{
    /// <summary>
    /// Analyzes designer file relationships in a project
    /// </summary>
    DesignerFileRelationships AnalyzeDesignerRelationships(Project project);

    /// <summary>
    /// Migrates designer file relationships to SDK-style format
    /// </summary>
    void MigrateDesignerRelationships(
        DesignerFileRelationships relationships, 
        XElement projectElement,
        MigrationResult result);
}