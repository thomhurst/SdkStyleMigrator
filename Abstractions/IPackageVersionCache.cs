using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Provides caching for NuGet package version lookups and dependency resolution
/// to improve performance for large solutions.
/// </summary>
public interface IPackageVersionCache
{
    /// <summary>
    /// Gets the cached version for a package, or null if not cached.
    /// </summary>
    Task<string?> GetVersionAsync(string packageId, string? targetFramework = null, bool includePrerelease = false);

    /// <summary>
    /// Caches a version for a package.
    /// </summary>
    Task SetVersionAsync(string packageId, string version, string? targetFramework = null, bool includePrerelease = false);

    /// <summary>
    /// Gets all cached versions for a package.
    /// </summary>
    Task<IEnumerable<string>?> GetAllVersionsAsync(string packageId, bool includePrerelease = false);

    /// <summary>
    /// Caches all versions for a package.
    /// </summary>
    Task SetAllVersionsAsync(string packageId, IEnumerable<string> versions, bool includePrerelease = false);

    /// <summary>
    /// Gets cached package resolution result for an assembly.
    /// </summary>
    Task<PackageResolutionResult?> GetPackageResolutionAsync(string assemblyName, string? targetFramework = null);

    /// <summary>
    /// Caches package resolution result for an assembly.
    /// </summary>
    Task SetPackageResolutionAsync(string assemblyName, PackageResolutionResult result, string? targetFramework = null);

    /// <summary>
    /// Gets cached package dependencies.
    /// </summary>
    Task<HashSet<(string PackageId, string Version)>?> GetPackageDependenciesAsync(string packageId, string version, string? targetFramework = null);

    /// <summary>
    /// Caches package dependencies.
    /// </summary>
    Task SetPackageDependenciesAsync(string packageId, string version, HashSet<(string PackageId, string Version)> dependencies, string? targetFramework = null);

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    CacheStatistics GetStatistics();
}

public class CacheStatistics
{
    public int TotalEntries { get; set; }
    public int VersionCacheHits { get; set; }
    public int VersionCacheMisses { get; set; }
    public int ResolutionCacheHits { get; set; }
    public int ResolutionCacheMisses { get; set; }
    public int DependencyCacheHits { get; set; }
    public int DependencyCacheMisses { get; set; }
    public TimeSpan CacheUptime { get; set; }

    public double HitRate => TotalRequests > 0 ? (double)TotalHits / TotalRequests * 100 : 0;
    public int TotalHits => VersionCacheHits + ResolutionCacheHits + DependencyCacheHits;
    public int TotalMisses => VersionCacheMisses + ResolutionCacheMisses + DependencyCacheMisses;
    public int TotalRequests => TotalHits + TotalMisses;
}