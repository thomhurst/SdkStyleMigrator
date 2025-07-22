using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

public interface IBuildEventMigrator
{
    void MigrateBuildEvents(Project legacyProject, XElement newProjectRoot, MigrationResult result);
}