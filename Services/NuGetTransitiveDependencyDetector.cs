using System.Xml.Linq;
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

    // Caches for performance optimization
    private readonly Dictionary<string, NuGetVersion?> _versionCache = new();
    private readonly Dictionary<string, HashSet<PackageIdentity>> _dependencyCache = new();

    // Essential packages that should never be marked as transitive
    private readonly HashSet<string> _essentialPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Test framework packages
        "Microsoft.NET.Test.Sdk",
        "xunit.runner.visualstudio",
        "NUnit3TestAdapter",
        "MSTest.TestAdapter",
        "coverlet.collector",
        "xunit",
        "NUnit",
        "MSTest.TestFramework",
        "FluentAssertions",
        "Moq",
        "NSubstitute",
        "FakeItEasy",
        "Shouldly",
        
        // Build/Development packages
        "Microsoft.SourceLink.GitHub",
        "Microsoft.SourceLink.AzureRepos.Git",
        "Microsoft.SourceLink.GitLab",
        "Microsoft.SourceLink.Bitbucket.Git",
        "GitInfo",
        "Nerdbank.GitVersioning",
        
        // Analyzer packages (these provide build-time assets)
        "StyleCop.Analyzers",
        "SonarAnalyzer.CSharp",
        "Microsoft.CodeAnalysis.NetAnalyzers",
        "Microsoft.CodeAnalysis.FxCopAnalyzers",
        "Roslynator.Analyzers",
        "Microsoft.CodeAnalysis.PublicApiAnalyzers",
        "Microsoft.VisualStudio.Threading.Analyzers",
        
        // Source generators
        "System.Text.Json", // Has source generators
        "Microsoft.Extensions.Logging.Abstractions", // Has source generators
        "Microsoft.Extensions.Options", // Has source generators
        
        // Framework packages
        "Microsoft.AspNetCore.App",
        "Microsoft.NETCore.App",
        "NETStandard.Library",
        
        // Commonly directly used packages
        "Newtonsoft.Json",
        "System.Text.Json",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Hosting",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Dapper",
        "AutoMapper",
        "MediatR",
        "FluentValidation",
        "Polly",
        "Serilog",
        "NLog",
        "log4net"
    };

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

    private async Task<NuGetVersion?> TryParseVersionAsync(string? version, string packageId, string? projectDirectory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        // Handle wildcard versions
        if (version == "*" || version.Contains("*"))
        {
            _logger.LogDebug("Found wildcard version for {PackageId}: {Version}", packageId, version);

            // First try to find version in central package management files
            if (!string.IsNullOrEmpty(projectDirectory))
            {
                var centralVersion = await GetVersionFromCentralPackageManagementAsync(packageId, projectDirectory);
                if (centralVersion != null)
                {
                    _logger.LogInformation("Resolved wildcard version for {PackageId} from central package management: {Version}", packageId, centralVersion);
                    return centralVersion;
                }
            }

            // If not found, get latest version from NuGet
            var latestVersion = await GetLatestVersionFromNuGetAsync(packageId, cancellationToken);
            if (latestVersion != null)
            {
                _logger.LogInformation("Resolved wildcard version for {PackageId} from NuGet (latest): {Version}", packageId, latestVersion);
                return latestVersion;
            }

            _logger.LogWarning("Could not resolve wildcard version for {PackageId}", packageId);
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

    private Task<NuGetVersion?> GetVersionFromCentralPackageManagementAsync(string packageId, string projectDirectory)
    {
        try
        {
            var currentDir = projectDirectory;
            // Search up to 5 levels for Directory.Packages.props
            for (int i = 0; i < 5; i++)
            {
                if (string.IsNullOrEmpty(currentDir))
                    break;

                var packagesPropsPath = Path.Combine(currentDir, "Directory.Packages.props");
                if (File.Exists(packagesPropsPath))
                {
                    var doc = XDocument.Load(packagesPropsPath);
                    var packageVersion = doc.Descendants("PackageVersion")
                        .FirstOrDefault(pv => string.Equals(pv.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));

                    if (packageVersion != null)
                    {
                        var versionStr = packageVersion.Attribute("Version")?.Value;
                        if (!string.IsNullOrEmpty(versionStr) && NuGetVersion.TryParse(versionStr, out var version))
                        {
                            return Task.FromResult<NuGetVersion?>(version);
                        }
                    }
                }

                var parent = Directory.GetParent(currentDir);
                if (parent == null)
                    break;
                currentDir = parent.FullName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching for central package management version for {PackageId}", packageId);
        }

        return Task.FromResult<NuGetVersion?>(null);
    }

    private async Task<NuGetVersion?> GetLatestVersionFromNuGetAsync(string packageId, CancellationToken cancellationToken)
    {
        // Check cache first
        var cacheKey = $"latest:{packageId}";
        if (_versionCache.TryGetValue(cacheKey, out var cachedVersion))
        {
            _logger.LogDebug("Using cached latest version for {PackageId}: {Version}", packageId, cachedVersion);
            return cachedVersion;
        }

        try
        {
            foreach (var repository in _repositories)
            {
                try
                {
                    var findPackageResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                    if (findPackageResource == null) continue;

                    var versions = await findPackageResource.GetAllVersionsAsync(packageId, _cache, NullLogger.Instance, cancellationToken);
                    if (versions != null && versions.Any())
                    {
                        // Get the latest stable version, or latest pre-release if no stable versions
                        var latestStable = versions.Where(v => !v.IsPrerelease).OrderByDescending(v => v).FirstOrDefault();
                        var result = latestStable ?? versions.OrderByDescending(v => v).First();

                        // Cache the result
                        _versionCache[cacheKey] = result;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get versions from repository {Repository} for package {PackageId}",
                        repository.PackageSource.Name, packageId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting latest version for {PackageId} from NuGet", packageId);
        }

        // Cache null result to avoid repeated failed lookups
        _versionCache[cacheKey] = null;
        return null;
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
                var version = await TryParseVersionAsync(package.Version, package.PackageId, projectDirectory, cancellationToken);
                if (version == null)
                {
                    _logger.LogWarning("Could not resolve version for package {PackageId}: {Version}",
                        package.PackageId, package.Version);
                    continue;
                }

                // Update the package version if it was resolved from wildcard
                if (package.Version == "*" || (package.Version?.Contains("*") ?? false))
                {
                    package.Version = version.ToString();
                    _logger.LogDebug("Updated package {PackageId} version from wildcard to {Version}", package.PackageId, version);
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
                    var version = await TryParseVersionAsync(package.Version, package.PackageId, projectDirectory, cancellationToken);
                    if (version == null)
                    {
                        _logger.LogWarning("Skipping dependency analysis for {PackageId} - could not resolve version: {Version}",
                            package.PackageId, package.Version);
                        continue;
                    }

                    var dependencies = await GetPackageDependenciesAsync(
                        package.PackageId,
                        version.ToString(),
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
                var version = await TryParseVersionAsync(package.Version, package.PackageId, projectDirectory, cancellationToken);
                if (version == null)
                {
                    // Can't determine transitivity without valid version
                    continue;
                }

                var identity = new PackageIdentity(package.PackageId, version);

                var isTransitive = false;
                string? transitiveReason = null;

                foreach (var kvp in packageDependencyMap)
                {
                    if (!kvp.Key.Equals(identity) && kvp.Value.Any(d => d.Id.Equals(identity.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        isTransitive = true;
                        transitiveReason = $"brought in by {kvp.Key.Id}";
                        break;
                    }
                }

                if (isTransitive && !package.IsTransitive)
                {
                    // Skip essential packages - they should never be marked as transitive
                    if (_essentialPackages.Contains(package.PackageId))
                    {
                        _logger.LogDebug("Keeping {PackageId} as it's an essential package", package.PackageId);
                        continue;
                    }

                    package.IsTransitive = true;
                    transitiveCount++;
                    _logger.LogInformation("Marked {PackageId} as transitive dependency ({Reason})", package.PackageId, transitiveReason);
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

    public async Task<HashSet<PackageIdentity>> GetPackageDependenciesAsync(
        string packageId,
        string version,
        NuGetFramework framework,
        CancellationToken cancellationToken)
    {
        // Create cache key
        var cacheKey = $"{packageId}:{version}:{framework}";
        if (_dependencyCache.TryGetValue(cacheKey, out var cachedDependencies))
        {
            _logger.LogDebug("Using cached dependencies for {PackageId} {Version}", packageId, version);
            return new HashSet<PackageIdentity>(cachedDependencies);
        }

        var dependencies = new HashSet<PackageIdentity>();
        NuGetVersion? nugetVersion;
        if (NuGetVersion.TryParse(version, out var parsed))
        {
            nugetVersion = parsed;
        }
        else
        {
            _logger.LogWarning("Cannot get dependencies for {PackageId} with unparseable version: {Version}",
                packageId, version);
            _dependencyCache[cacheKey] = dependencies;
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

        // Cache the result
        _dependencyCache[cacheKey] = new HashSet<PackageIdentity>(dependencies);
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