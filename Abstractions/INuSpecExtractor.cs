using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface INuSpecExtractor
{
    /// <summary>
    /// Extracts metadata from a .nuspec file
    /// </summary>
    Task<NuSpecMetadata?> ExtractMetadataAsync(string nuspecPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Finds .nuspec file associated with a project
    /// </summary>
    Task<string?> FindNuSpecFileAsync(string projectPath, CancellationToken cancellationToken = default);
}