using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IAssemblyInfoExtractor
{
    Task<AssemblyProperties> ExtractAssemblyPropertiesAsync(string projectDirectory, CancellationToken cancellationToken = default);
    Task<AssemblyProperties> ExtractFromProjectAsync(Project project, CancellationToken cancellationToken = default);
}