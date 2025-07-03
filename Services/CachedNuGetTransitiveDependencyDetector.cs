using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// Decorator for NuGetTransitiveDependencyDetector that uses the centralized cache.
/// </summary>
public class CachedNuGetTransitiveDependencyDetector : ITransitiveDependencyDetector
{
    private readonly NuGetTransitiveDependencyDetector _innerDetector;
    private readonly IPackageVersionCache _cache;
    private readonly ILogger<CachedNuGetTransitiveDependencyDetector> _logger;

    public CachedNuGetTransitiveDependencyDetector(
        NuGetTransitiveDependencyDetector innerDetector,
        IPackageVersionCache cache,
        ILogger<CachedNuGetTransitiveDependencyDetector> logger)
    {
        _innerDetector = innerDetector;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences,
        CancellationToken cancellationToken = default)
    {
        return await DetectTransitiveDependenciesAsync(packageReferences, null, cancellationToken);
    }

    public async Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences,
        string? projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var packages = packageReferences.ToList();
        
        // Pre-populate cache with dependency information if available
        foreach (var package in packages)
        {
            if (!string.IsNullOrEmpty(package.Version) && NuGetVersion.TryParse(package.Version, out var version))
            {
                var cachedDeps = await _cache.GetPackageDependenciesAsync(package.PackageId, package.Version);
                if (cachedDeps != null)
                {
                    _logger.LogDebug("Found cached dependencies for {PackageId} {Version}", 
                        package.PackageId, package.Version);
                }
            }
        }

        // Call the inner detector which will benefit from the cache
        var result = await _innerDetector.DetectTransitiveDependenciesAsync(packages, projectDirectory, cancellationToken);

        // Cache any new dependency information discovered
        foreach (var package in packages)
        {
            if (!string.IsNullOrEmpty(package.Version) && NuGetVersion.TryParse(package.Version, out var version))
            {
                var dependencies = await _innerDetector.GetPackageDependenciesAsync(
                    package.PackageId, 
                    package.Version, 
                    NuGetFramework.AnyFramework, 
                    cancellationToken);

                if (dependencies.Any())
                {
                    var depSet = dependencies
                        .Select(d => (d.Id, d.Version.ToString()))
                        .ToHashSet();
                    
                    await _cache.SetPackageDependenciesAsync(
                        package.PackageId, 
                        package.Version, 
                        depSet);
                }
            }
        }

        return result;
    }

    public void Dispose()
    {
        _innerDetector?.Dispose();
    }
}