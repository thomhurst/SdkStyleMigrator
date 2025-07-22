using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

public interface ITestProjectHandler
{
    /// <summary>
    /// Detects test framework and configuration in a project
    /// </summary>
    Task<TestProjectInfo> DetectTestFrameworkAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates test configuration to SDK-style format
    /// </summary>
    Task MigrateTestConfigurationAsync(
        TestProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts legacy .testsettings to modern .runsettings format
    /// </summary>
    void ConvertTestSettingsToRunSettings(string testSettingsPath, string outputPath);
}