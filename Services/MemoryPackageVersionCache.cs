using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// In-memory implementation of package version cache with configurable TTL.
/// </summary>
public class MemoryPackageVersionCache : IPackageVersionCache, IDisposable
{
    private readonly ILogger<MemoryPackageVersionCache> _logger;
    private readonly PackageCacheOptions _options;
    private readonly ConcurrentDictionary<string, CacheEntry<string>> _versionCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IEnumerable<string>>> _allVersionsCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<PackageResolutionResult>> _resolutionCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<HashSet<(string, string)>>> _dependencyCache = new();
    private readonly CacheStatistics _statistics = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public MemoryPackageVersionCache(ILogger<MemoryPackageVersionCache> logger, IOptions<PackageCacheOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // Set up periodic cleanup of expired entries
        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        _logger.LogInformation("Package version cache initialized with TTL: {TTL} minutes", _options.CacheTTLMinutes);
    }

    public Task<string?> GetVersionAsync(string packageId, string? targetFramework = null, bool includePrerelease = false)
    {
        var key = GenerateVersionKey(packageId, targetFramework, includePrerelease);

        if (_versionCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _statistics.VersionCacheHits++;
            _logger.LogDebug("Cache hit for package version: {PackageId} ({Framework}, prerelease={IncludePrerelease})",
                packageId, targetFramework ?? "any", includePrerelease);
            return Task.FromResult<string?>(entry.Value);
        }

        _statistics.VersionCacheMisses++;
        return Task.FromResult<string?>(null);
    }

    public Task SetVersionAsync(string packageId, string version, string? targetFramework = null, bool includePrerelease = false)
    {
        var key = GenerateVersionKey(packageId, targetFramework, includePrerelease);
        var entry = new CacheEntry<string>(version, _options.CacheTTLMinutes);

        _versionCache.AddOrUpdate(key, entry, (k, v) => entry);
        _logger.LogDebug("Cached package version: {PackageId} = {Version} ({Framework}, prerelease={IncludePrerelease})",
            packageId, version, targetFramework ?? "any", includePrerelease);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>?> GetAllVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var key = $"all:{packageId}:{includePrerelease}";

        if (_allVersionsCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _statistics.VersionCacheHits++;
            _logger.LogDebug("Cache hit for all versions: {PackageId} (prerelease={IncludePrerelease})",
                packageId, includePrerelease);
            return Task.FromResult<IEnumerable<string>?>(entry.Value);
        }

        _statistics.VersionCacheMisses++;
        return Task.FromResult<IEnumerable<string>?>(null);
    }

    public Task SetAllVersionsAsync(string packageId, IEnumerable<string> versions, bool includePrerelease = false)
    {
        var key = $"all:{packageId}:{includePrerelease}";
        var entry = new CacheEntry<IEnumerable<string>>(versions.ToList(), _options.CacheTTLMinutes);

        _allVersionsCache.AddOrUpdate(key, entry, (k, v) => entry);
        _logger.LogDebug("Cached all versions for package: {PackageId} ({Count} versions, prerelease={IncludePrerelease})",
            packageId, versions.Count(), includePrerelease);

        return Task.CompletedTask;
    }

    public Task<PackageResolutionResult?> GetPackageResolutionAsync(string assemblyName, string? targetFramework = null)
    {
        var key = $"resolution:{assemblyName}:{targetFramework ?? "any"}";

        if (_resolutionCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _statistics.ResolutionCacheHits++;
            _logger.LogDebug("Cache hit for package resolution: {AssemblyName} ({Framework})",
                assemblyName, targetFramework ?? "any");
            return Task.FromResult<PackageResolutionResult?>(entry.Value);
        }

        _statistics.ResolutionCacheMisses++;
        return Task.FromResult<PackageResolutionResult?>(null);
    }

    public Task SetPackageResolutionAsync(string assemblyName, PackageResolutionResult result, string? targetFramework = null)
    {
        var key = $"resolution:{assemblyName}:{targetFramework ?? "any"}";
        var entry = new CacheEntry<PackageResolutionResult>(result, _options.CacheTTLMinutes);

        _resolutionCache.AddOrUpdate(key, entry, (k, v) => entry);
        _logger.LogDebug("Cached package resolution: {AssemblyName} => {PackageId} {Version} ({Framework})",
            assemblyName, result.PackageId, result.Version, targetFramework ?? "any");

        return Task.CompletedTask;
    }

    public Task<HashSet<(string PackageId, string Version)>?> GetPackageDependenciesAsync(string packageId, string version, string? targetFramework = null)
    {
        var key = $"deps:{packageId}:{version}:{targetFramework ?? "any"}";

        if (_dependencyCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            _statistics.DependencyCacheHits++;
            _logger.LogDebug("Cache hit for package dependencies: {PackageId} {Version} ({Framework})",
                packageId, version, targetFramework ?? "any");
            return Task.FromResult<HashSet<(string, string)>?>(entry.Value);
        }

        _statistics.DependencyCacheMisses++;
        return Task.FromResult<HashSet<(string, string)>?>(null);
    }

    public Task SetPackageDependenciesAsync(string packageId, string version, HashSet<(string PackageId, string Version)> dependencies, string? targetFramework = null)
    {
        var key = $"deps:{packageId}:{version}:{targetFramework ?? "any"}";
        var entry = new CacheEntry<HashSet<(string, string)>>(dependencies, _options.CacheTTLMinutes);

        _dependencyCache.AddOrUpdate(key, entry, (k, v) => entry);
        _logger.LogDebug("Cached package dependencies: {PackageId} {Version} ({Count} dependencies, {Framework})",
            packageId, version, dependencies.Count, targetFramework ?? "any");

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _versionCache.Clear();
        _allVersionsCache.Clear();
        _resolutionCache.Clear();
        _dependencyCache.Clear();

        _logger.LogInformation("Package version cache cleared");
        return Task.CompletedTask;
    }

    public CacheStatistics GetStatistics()
    {
        _statistics.TotalEntries = _versionCache.Count + _allVersionsCache.Count +
                                  _resolutionCache.Count + _dependencyCache.Count;
        _statistics.CacheUptime = DateTime.UtcNow - _startTime;
        return _statistics;
    }

    private string GenerateVersionKey(string packageId, string? targetFramework, bool includePrerelease)
    {
        return $"version:{packageId}:{targetFramework ?? "any"}:{includePrerelease}";
    }

    private void CleanupExpiredEntries(object? state)
    {
        try
        {
            var expiredVersions = _versionCache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredVersions)
            {
                _versionCache.TryRemove(key, out _);
            }

            var expiredAllVersions = _allVersionsCache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredAllVersions)
            {
                _allVersionsCache.TryRemove(key, out _);
            }

            var expiredResolutions = _resolutionCache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredResolutions)
            {
                _resolutionCache.TryRemove(key, out _);
            }

            var expiredDependencies = _dependencyCache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredDependencies)
            {
                _dependencyCache.TryRemove(key, out _);
            }

            var totalExpired = expiredVersions.Count + expiredAllVersions.Count +
                              expiredResolutions.Count + expiredDependencies.Count;

            if (totalExpired > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired cache entries", totalExpired);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _disposed = true;

            var stats = GetStatistics();
            _logger.LogInformation(
                "Package cache shutting down. Statistics - Total entries: {TotalEntries}, Hit rate: {HitRate:F1}%, " +
                "Version hits: {VersionHits}, Resolution hits: {ResolutionHits}, Dependency hits: {DependencyHits}",
                stats.TotalEntries, stats.HitRate, stats.VersionCacheHits,
                stats.ResolutionCacheHits, stats.DependencyCacheHits);
        }
    }

    private class CacheEntry<T>
    {
        public T Value { get; }
        public DateTime ExpiresAt { get; }

        public CacheEntry(T value, int ttlMinutes)
        {
            Value = value;
            ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes);
        }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}

public class PackageCacheOptions
{
    /// <summary>
    /// Time-to-live for cached entries in minutes. Default is 60 minutes.
    /// </summary>
    public int CacheTTLMinutes { get; set; } = 60;

    /// <summary>
    /// Whether caching is enabled. Default is true.
    /// </summary>
    public bool EnableCaching { get; set; } = true;
}