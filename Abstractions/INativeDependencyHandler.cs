using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using SdkMigrator.Services;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

public interface INativeDependencyHandler
{
    List<NativeDependency> DetectNativeDependencies(Project project);
    void MigrateNativeDependencies(List<NativeDependency> dependencies, XElement projectRoot, MigrationResult result);
}