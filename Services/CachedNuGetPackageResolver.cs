using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// Decorator for NuGetPackageResolver that adds caching capabilities.
/// </summary>
public class CachedNuGetPackageResolver : INuGetPackageResolver
{
    private readonly INuGetPackageResolver _innerResolver;
    private readonly IPackageVersionCache _cache;
    private readonly ILogger<CachedNuGetPackageResolver> _logger;

    public CachedNuGetPackageResolver(
        INuGetPackageResolver innerResolver,
        IPackageVersionCache cache,
        ILogger<CachedNuGetPackageResolver> logger)
    {
        _innerResolver = innerResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetLatestStableVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cachedVersion = await _cache.GetVersionAsync(packageId, targetFramework: null, includePrerelease: false);
        if (cachedVersion != null)
        {
            _logger.LogDebug("Using cached latest stable version for {PackageId}: {Version}", packageId, cachedVersion);
            return cachedVersion;
        }

        // Fetch from inner resolver
        var version = await _innerResolver.GetLatestStableVersionAsync(packageId, cancellationToken);

        // Cache the result if found
        if (version != null)
        {
            await _cache.SetVersionAsync(packageId, version, targetFramework: null, includePrerelease: false);
        }

        return version;
    }

    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cachedVersion = await _cache.GetVersionAsync(packageId, targetFramework: null, includePrerelease);
        if (cachedVersion != null)
        {
            _logger.LogDebug("Using cached latest version for {PackageId}: {Version} (prerelease={IncludePrerelease})",
                packageId, cachedVersion, includePrerelease);
            return cachedVersion;
        }

        // Fetch from inner resolver
        var version = await _innerResolver.GetLatestVersionAsync(packageId, includePrerelease, cancellationToken);

        // Cache the result if found
        if (version != null)
        {
            await _cache.SetVersionAsync(packageId, version, targetFramework: null, includePrerelease);
        }

        return version;
    }

    public async Task<IEnumerable<string>> GetAllVersionsAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cachedVersions = await _cache.GetAllVersionsAsync(packageId, includePrerelease);
        if (cachedVersions != null)
        {
            _logger.LogDebug("Using cached versions for {PackageId}: {Count} versions (prerelease={IncludePrerelease})",
                packageId, cachedVersions.Count(), includePrerelease);
            return cachedVersions;
        }

        // Fetch from inner resolver
        var versions = await _innerResolver.GetAllVersionsAsync(packageId, includePrerelease, cancellationToken);
        var versionList = versions.ToList();

        // Cache the result
        if (versionList.Any())
        {
            await _cache.SetAllVersionsAsync(packageId, versionList, includePrerelease);
        }

        return versionList;
    }

    public async Task<PackageResolutionResult?> ResolveAssemblyToPackageAsync(string assemblyName, string? targetFramework = null, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cachedResult = await _cache.GetPackageResolutionAsync(assemblyName, targetFramework);
        if (cachedResult != null)
        {
            _logger.LogDebug("Using cached package resolution for {AssemblyName}: {PackageId} {Version}",
                assemblyName, cachedResult.PackageId, cachedResult.Version);
            return cachedResult;
        }

        // Fetch from inner resolver
        var result = await _innerResolver.ResolveAssemblyToPackageAsync(assemblyName, targetFramework, cancellationToken);

        // Cache the result if found
        if (result != null)
        {
            await _cache.SetPackageResolutionAsync(assemblyName, result, targetFramework);
        }

        return result;
    }
}