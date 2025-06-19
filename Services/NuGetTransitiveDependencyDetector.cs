using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using PackageReference = SdkMigrator.Models.PackageReference;

namespace SdkMigrator.Services;

public class NuGetTransitiveDependencyDetector : ITransitiveDependencyDetector
{
    private readonly ILogger<NuGetTransitiveDependencyDetector> _logger;
    private readonly SourceCacheContext _cache;
    private readonly ISettings _settings;
    private readonly List<SourceRepository> _repositories;
    private bool _disposed;
    
    public NuGetTransitiveDependencyDetector(ILogger<NuGetTransitiveDependencyDetector> logger)
    {
        _logger = logger;
        _cache = new SourceCacheContext();
        _settings = Settings.LoadDefaultSettings(root: null);
        
        var packageSourceProvider = new PackageSourceProvider(_settings);
        var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
        _repositories = sourceRepositoryProvider.GetRepositories().ToList();
        
        if (!_repositories.Any())
        {
            _repositories.Add(Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json"));
        }
    }
    
    private NuGetVersion? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
            
        // Handle special cases
        if (version == "*" || version.Contains("*"))
        {
            _logger.LogDebug("Skipping wildcard version: {Version}", version);
            return null;
        }
        
        // Try to parse as a normal version
        if (NuGetVersion.TryParse(version, out var parsedVersion))
        {
            return parsedVersion;
        }
        
        // Try to parse as a version range and get the minimum version
        if (VersionRange.TryParse(version, out var range) && range.MinVersion != null)
        {
            return range.MinVersion;
        }
        
        _logger.LogDebug("Could not parse version: {Version}", version);
        return null;
    }

    public async Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences, 
        CancellationToken cancellationToken = default)
    {
        var packages = packageReferences.ToList();
        
        if (!packages.Any())
        {
            _logger.LogInformation("No packages to analyze for transitive dependencies");
            return packages;
        }

        _logger.LogInformation("Analyzing {Count} packages for transitive dependencies using NuGet API", packages.Count);
        
        try
        {
            var framework = NuGetFramework.AnyFramework;
            var allDependencies = new HashSet<PackageIdentity>();
            var directPackages = new HashSet<PackageIdentity>();
            
            foreach (var package in packages)
            {
                var version = TryParseVersion(package.Version);
                if (version == null)
                {
                    _logger.LogWarning("Skipping package {PackageId} with unparseable version: {Version}", 
                        package.PackageId, package.Version);
                    continue;
                }
                
                var identity = new PackageIdentity(package.PackageId, version);
                directPackages.Add(identity);
                allDependencies.Add(identity);
            }
            
            var packageDependencyMap = new Dictionary<PackageIdentity, HashSet<PackageIdentity>>();
            
            foreach (var package in packages)
            {
                try
                {
                    var version = TryParseVersion(package.Version);
                    if (version == null)
                    {
                        _logger.LogWarning("Skipping dependency analysis for {PackageId} with unparseable version: {Version}", 
                            package.PackageId, package.Version);
                        continue;
                    }
                    
                    var dependencies = await GetPackageDependenciesAsync(
                        package.PackageId, 
                        package.Version, 
                        framework, 
                        cancellationToken);
                    
                    var identity = new PackageIdentity(package.PackageId, version);
                    packageDependencyMap[identity] = dependencies;
                    
                    foreach (var dep in dependencies)
                    {
                        allDependencies.Add(dep);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze dependencies for package {PackageId} {Version}", 
                        package.PackageId, package.Version);
                }
            }
            
            var transitiveCount = 0;
            foreach (var package in packages)
            {
                var version = TryParseVersion(package.Version);
                if (version == null)
                {
                    // Can't determine transitivity without valid version
                    continue;
                }
                
                var identity = new PackageIdentity(package.PackageId, version);
                
                var isTransitive = false;
                foreach (var kvp in packageDependencyMap)
                {
                    if (!kvp.Key.Equals(identity) && kvp.Value.Any(d => d.Id.Equals(identity.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        isTransitive = true;
                        break;
                    }
                }
                    
                if (isTransitive && !package.IsTransitive)
                {
                    package.IsTransitive = true;
                    transitiveCount++;
                    _logger.LogDebug("Marked {PackageId} as transitive dependency", package.PackageId);
                }
            }
            
            _logger.LogInformation("Detected {Count} transitive dependencies out of {Total} packages", 
                transitiveCount, packages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect transitive dependencies, falling back to heuristic detection");
            
            var fallbackDetector = new TransitiveDependencyDetector(
                new Logger<TransitiveDependencyDetector>(new LoggerFactory()));
            return await fallbackDetector.DetectTransitiveDependenciesAsync(packages, cancellationToken);
        }

        return packages;
    }
    
    private async Task<HashSet<PackageIdentity>> GetPackageDependenciesAsync(
        string packageId, 
        string version,
        NuGetFramework framework,
        CancellationToken cancellationToken)
    {
        var dependencies = new HashSet<PackageIdentity>();
        var nugetVersion = TryParseVersion(version);
        if (nugetVersion == null)
        {
            _logger.LogWarning("Cannot get dependencies for {PackageId} with unparseable version: {Version}", 
                packageId, version);
            return dependencies;
        }
        
        var packageIdentity = new PackageIdentity(packageId, nugetVersion);
        
        foreach (var repository in _repositories)
        {
            try
            {
                var dependencyInfoResource = await repository.GetResourceAsync<DependencyInfoResource>(cancellationToken);
                if (dependencyInfoResource == null) continue;
                
                var dependencyInfo = await dependencyInfoResource.ResolvePackage(
                    packageIdentity, 
                    framework, 
                    _cache, 
                    NullLogger.Instance, 
                    cancellationToken);
                    
                if (dependencyInfo?.Dependencies != null)
                {
                    foreach (var dep in dependencyInfo.Dependencies)
                    {
                        if (dep.VersionRange?.MinVersion != null)
                        {
                            dependencies.Add(new PackageIdentity(dep.Id, dep.VersionRange.MinVersion));
                        }
                    }
                    
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get dependencies from repository {Repository}", repository.PackageSource.Name);
            }
        }
        
        return dependencies;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _cache?.Dispose();
            _disposed = true;
        }
    }
}