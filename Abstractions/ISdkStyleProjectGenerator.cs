using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ISdkStyleProjectGenerator
{
    Task<MigrationResult> GenerateSdkStyleProjectAsync(Project legacyProject, string outputPath, CancellationToken cancellationToken = default);
}