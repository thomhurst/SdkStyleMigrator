using Microsoft.Build.Evaluation;

namespace SdkMigrator.Abstractions;

public interface IProjectParser
{
    Task<Project> ParseProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    bool IsLegacyProject(Project project);
}