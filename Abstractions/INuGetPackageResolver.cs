using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface INuGetPackageResolver
{
    /// <summary>
    /// Resolves the latest stable version of a NuGet package
    /// </summary>
    Task<string?> GetLatestStableVersionAsync(string packageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resolves the latest version of a NuGet package (including prerelease)
    /// </summary>
    Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all available versions of a package
    /// </summary>
    Task<IEnumerable<string>> GetAllVersionsAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resolves assembly references to NuGet packages
    /// </summary>
    Task<PackageResolutionResult?> ResolveAssemblyToPackageAsync(string assemblyName, string? targetFramework = null, CancellationToken cancellationToken = default);
}

public class PackageResolutionResult
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> AdditionalPackages { get; set; } = new();
    public string? Notes { get; set; }
}