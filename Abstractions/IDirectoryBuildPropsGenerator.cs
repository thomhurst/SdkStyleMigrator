using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IDirectoryBuildPropsGenerator
{
    Task GenerateDirectoryBuildPropsAsync(string rootDirectory, Dictionary<string, AssemblyProperties> projectProperties, CancellationToken cancellationToken = default);
}