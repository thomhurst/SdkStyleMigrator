using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IProjectParser
{
    Task<ParsedProject> ParseProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    bool IsLegacyProject(Project project);
}