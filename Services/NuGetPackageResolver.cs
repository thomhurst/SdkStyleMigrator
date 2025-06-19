using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class NuGetPackageResolver : INuGetPackageResolver
{
    private readonly ILogger<NuGetPackageResolver> _logger;
    private readonly MigrationOptions _options;
    private readonly SourceCacheContext _cache;
    private readonly IEnumerable<SourceRepository> _repositories;
    private readonly NuGetLogger _nugetLogger;
    
    // Cache for package versions to avoid repeated API calls
    private readonly ConcurrentDictionary<string, List<NuGetVersion>> _versionCache = new();
    private readonly ConcurrentDictionary<string, PackageResolutionResult> _assemblyCache = new();
    
    // Well-known assembly to package mappings that don't follow standard naming
    private readonly Dictionary<string, (string PackageId, string? Notes)> _knownAssemblyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.VisualStudio.QualityTools.UnitTestFramework"] = ("MSTest.TestFramework", "Also requires MSTest.TestAdapter"),
        ["Microsoft.VisualStudio.TestPlatform.TestFramework"] = ("MSTest.TestFramework", "Also requires MSTest.TestAdapter"),
        ["Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions"] = ("MSTest.TestFramework.Extensions", null),
        ["xunit"] = ("xunit", "Also requires xunit.runner.visualstudio"),
        ["xunit.core"] = ("xunit.core", null),
        ["xunit.assert"] = ("xunit.assert", null),
        ["nunit.framework"] = ("NUnit", "Also requires NUnit3TestAdapter"),
        ["Moq"] = ("Moq", null),
        ["Castle.Core"] = ("Castle.Core", null),
        ["log4net"] = ("log4net", null),
        ["Serilog"] = ("Serilog", null),
        ["NLog"] = ("NLog", null),
        ["AutoMapper"] = ("AutoMapper", null),
        ["FluentValidation"] = ("FluentValidation", null),
        ["MediatR"] = ("MediatR", null),
        ["Polly"] = ("Polly", null),
        ["StackExchange.Redis"] = ("StackExchange.Redis", null),
        ["RabbitMQ.Client"] = ("RabbitMQ.Client", null),
        ["AWSSDK.Core"] = ("AWSSDK.Core", null),
        ["Azure.Storage.Blobs"] = ("Azure.Storage.Blobs", null),
        ["Google.Apis"] = ("Google.Apis", null),
        ["Grpc.Core"] = ("Grpc.Core", null),
        ["protobuf-net"] = ("protobuf-net", null),
        ["System.Data.SqlClient"] = ("System.Data.SqlClient", "Consider using Microsoft.Data.SqlClient instead"),
        ["EntityFramework"] = ("EntityFramework", "Consider using Microsoft.EntityFrameworkCore instead"),
        ["System.Windows.Forms"] = ("System.Windows.Forms", "For .NET Core/5+ projects"),
        ["System.Drawing"] = ("System.Drawing.Common", null),
        ["System.Configuration.ConfigurationManager"] = ("System.Configuration.ConfigurationManager", null),
        ["Unity"] = ("Unity", "Unity container for dependency injection"),
        ["Ninject"] = ("Ninject", null),
        ["SimpleInjector"] = ("SimpleInjector", null),
        ["StructureMap"] = ("StructureMap", "No longer maintained, consider alternatives"),
        ["CommonServiceLocator"] = ("CommonServiceLocator", null)
    };

    public NuGetPackageResolver(ILogger<NuGetPackageResolver> logger, MigrationOptions options)
    {
        _logger = logger;
        _options = options;
        _cache = new SourceCacheContext();
        _nugetLogger = new NuGetLogger(logger);
        
        // Initialize NuGet repositories from system configuration
        var providers = Repository.Provider.GetCoreV3();
        var packageSources = DiscoverNuGetSources();
        
        _repositories = packageSources.Select(source => new SourceRepository(source, providers)).ToList();
        
        _logger.LogInformation("NuGet package resolver initialized with {Count} repositories:", _repositories.Count());
        foreach (var repo in _repositories)
        {
            _logger.LogInformation("  - {Name}: {Source}", repo.PackageSource.Name, repo.PackageSource.Source);
        }
    }

    private List<PackageSource> DiscoverNuGetSources()
    {
        var sources = new List<PackageSource>();
        
        try
        {
            ISettings settings;
            
            // Check if a specific NuGet config file was provided
            if (!string.IsNullOrEmpty(_options?.NuGetConfigPath) && File.Exists(_options.NuGetConfigPath))
            {
                _logger.LogInformation("Loading NuGet configuration from specified file: {ConfigPath}", _options.NuGetConfigPath);
                var configDirectory = Path.GetDirectoryName(_options.NuGetConfigPath) ?? Directory.GetCurrentDirectory();
                var configFileName = Path.GetFileName(_options.NuGetConfigPath);
                settings = Settings.LoadSpecificSettings(configDirectory, configFileName);
            }
            else
            {
                // Get NuGet settings from the working directory (will search up for nuget.config files)
                var workingDirectory = _options?.DirectoryPath ?? Directory.GetCurrentDirectory();
                settings = Settings.LoadDefaultSettings(root: workingDirectory);
                _logger.LogDebug("Loading NuGet settings from directory: {Directory}", workingDirectory);
            }
            
            // Get all enabled package sources from the configuration
            var sourceProvider = new PackageSourceProvider(settings);
            var configuredSources = sourceProvider.LoadPackageSources()
                .Where(s => s.IsEnabled)
                .ToList();
            
            if (configuredSources.Any())
            {
                _logger.LogInformation("Found {Count} configured NuGet sources from system settings", configuredSources.Count);
                
                // The credentials should already be loaded with the sources
                // Just log which sources have credentials
                foreach (var source in configuredSources)
                {
                    if (source.Credentials != null && !string.IsNullOrEmpty(source.Credentials.Username))
                    {
                        _logger.LogInformation("Source {Source} has credentials configured for user: {User}", 
                            source.Name, source.Credentials.Username);
                    }
                }
                
                sources.AddRange(configuredSources);
            }
            else
            {
                _logger.LogWarning("No NuGet sources found in configuration, adding default NuGet.org source");
                sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org"));
            }
            
            // Ensure NuGet.org is included if not already present
            if (!sources.Any(s => s.Source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Adding NuGet.org as it was not in configured sources");
                sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load NuGet configuration, falling back to default NuGet.org source");
            sources.Clear();
            sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org"));
        }
        
        return sources;
    }

    public async Task<string?> GetLatestStableVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var versions = await GetAllVersionsAsync(packageId, includePrerelease: false, cancellationToken);
        return versions.FirstOrDefault();
    }

    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var versions = await GetAllVersionsAsync(packageId, includePrerelease, cancellationToken);
        return versions.FirstOrDefault();
    }

    public async Task<IEnumerable<string>> GetAllVersionsAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{packageId}_{includePrerelease}";
        
        // Check cache first
        if (_versionCache.TryGetValue(cacheKey, out var cachedVersions))
        {
            return cachedVersions.Select(v => v.ToString());
        }

        var allVersions = new List<NuGetVersion>();
        var searchedSources = new List<string>();

        foreach (var repository in _repositories)
        {
            try
            {
                _logger.LogDebug("Searching for package {PackageId} in repository {Repository}", 
                    packageId, repository.PackageSource.Name);
                    
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                if (resource == null)
                {
                    _logger.LogWarning("Could not get FindPackageByIdResource from repository {Repository}", 
                        repository.PackageSource.Name);
                    continue;
                }
                
                var versions = await resource.GetAllVersionsAsync(packageId, _cache, _nugetLogger, cancellationToken);
                
                if (versions != null && versions.Any())
                {
                    allVersions.AddRange(versions);
                    searchedSources.Add(repository.PackageSource.Name);
                    _logger.LogDebug("Found {Count} versions of {PackageId} in {Repository}", 
                        versions.Count(), packageId, repository.PackageSource.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get versions for package {PackageId} from repository {Repository}", 
                    packageId, repository.PackageSource.Name);
            }
        }

        // Filter and sort versions
        var filteredVersions = allVersions
            .Where(v => includePrerelease || !v.IsPrerelease)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();

        if (filteredVersions.Any())
        {
            _logger.LogInformation("Package {PackageId} found in repositories: {Sources}", 
                packageId, string.Join(", ", searchedSources));
        }
        else
        {
            _logger.LogDebug("Package {PackageId} not found in any configured repository", packageId);
        }

        // Cache the results
        _versionCache.TryAdd(cacheKey, filteredVersions);

        return filteredVersions.Select(v => v.ToString());
    }

    public async Task<PackageResolutionResult?> ResolveAssemblyToPackageAsync(string assemblyName, string? targetFramework = null, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = $"{assemblyName}_{targetFramework ?? "any"}";
        if (_assemblyCache.TryGetValue(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        // Check known mappings first
        if (_knownAssemblyMappings.TryGetValue(assemblyName, out var knownMapping))
        {
            var version = await GetLatestStableVersionAsync(knownMapping.PackageId, cancellationToken);
            if (version != null)
            {
                var result = new PackageResolutionResult
                {
                    PackageId = knownMapping.PackageId,
                    Version = version,
                    Notes = knownMapping.Notes
                };

                // Add additional packages for test frameworks
                if (assemblyName.Equals("Microsoft.VisualStudio.QualityTools.UnitTestFramework", StringComparison.OrdinalIgnoreCase) ||
                    assemblyName.Equals("Microsoft.VisualStudio.TestPlatform.TestFramework", StringComparison.OrdinalIgnoreCase))
                {
                    result.AdditionalPackages.Add("MSTest.TestAdapter");
                }
                else if (assemblyName.Equals("xunit", StringComparison.OrdinalIgnoreCase))
                {
                    result.AdditionalPackages.Add("xunit.runner.visualstudio");
                }
                else if (assemblyName.Equals("nunit.framework", StringComparison.OrdinalIgnoreCase))
                {
                    result.AdditionalPackages.Add("NUnit3TestAdapter");
                }

                _assemblyCache.TryAdd(cacheKey, result);
                return result;
            }
        }

        // Try common patterns for assembly to package resolution
        var packageIdsToTry = GeneratePackageIdCandidates(assemblyName);
        
        foreach (var candidateId in packageIdsToTry)
        {
            var version = await GetLatestStableVersionAsync(candidateId, cancellationToken);
            if (version != null)
            {
                var result = new PackageResolutionResult
                {
                    PackageId = candidateId,
                    Version = version
                };

                _assemblyCache.TryAdd(cacheKey, result);
                return result;
            }
        }

        _logger.LogWarning("Could not resolve assembly {AssemblyName} to a NuGet package", assemblyName);
        return null;
    }

    private List<string> GeneratePackageIdCandidates(string assemblyName)
    {
        var candidates = new List<string>();
        
        // Direct match
        candidates.Add(assemblyName);
        
        // Without version suffix (e.g., "Assembly.1.0" -> "Assembly")
        var withoutVersion = System.Text.RegularExpressions.Regex.Replace(assemblyName, @"\.\d+(\.\d+)*$", "");
        if (withoutVersion != assemblyName)
        {
            candidates.Add(withoutVersion);
        }
        
        // Common Microsoft patterns
        if (assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(assemblyName); // Many System.* packages match assembly names
            candidates.Add($"Microsoft.{assemblyName}"); // Some are under Microsoft.*
        }
        else if (assemblyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(assemblyName);
            
            // Try without Microsoft prefix for some packages
            var withoutPrefix = assemblyName.Substring("Microsoft.".Length);
            candidates.Add(withoutPrefix);
        }
        
        // Try with common suffixes
        candidates.Add($"{assemblyName}.Core");
        candidates.Add($"{assemblyName}.Abstractions");
        
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private class NuGetLogger : NuGet.Common.ILogger
    {
        private readonly ILogger<NuGetPackageResolver> _logger;

        public NuGetLogger(ILogger<NuGetPackageResolver> logger)
        {
            _logger = logger;
        }

        public void Log(NuGet.Common.LogLevel level, string data) => LogCore(level, data);
        public void Log(ILogMessage message) => LogCore(message.Level, message.Message);
        public Task LogAsync(NuGet.Common.LogLevel level, string data) { LogCore(level, data); return Task.CompletedTask; }
        public Task LogAsync(ILogMessage message) { LogCore(message.Level, message.Message); return Task.CompletedTask; }

        public void LogDebug(string data) => _logger.LogDebug("NuGet: {Message}", data);
        public void LogVerbose(string data) => _logger.LogTrace("NuGet: {Message}", data);
        public void LogInformation(string data) => _logger.LogInformation("NuGet: {Message}", data);
        public void LogMinimal(string data) => _logger.LogInformation("NuGet: {Message}", data);
        public void LogWarning(string data) => _logger.LogWarning("NuGet: {Message}", data);
        public void LogError(string data) => _logger.LogError("NuGet: {Message}", data);
        public void LogInformationSummary(string data) => _logger.LogInformation("NuGet: {Message}", data);

        private void LogCore(NuGet.Common.LogLevel level, string data)
        {
            switch (level)
            {
                case NuGet.Common.LogLevel.Debug:
                    _logger.LogDebug("NuGet: {Message}", data);
                    break;
                case NuGet.Common.LogLevel.Verbose:
                    _logger.LogTrace("NuGet: {Message}", data);
                    break;
                case NuGet.Common.LogLevel.Information:
                    _logger.LogInformation("NuGet: {Message}", data);
                    break;
                case NuGet.Common.LogLevel.Minimal:
                    _logger.LogInformation("NuGet: {Message}", data);
                    break;
                case NuGet.Common.LogLevel.Warning:
                    _logger.LogWarning("NuGet: {Message}", data);
                    break;
                case NuGet.Common.LogLevel.Error:
                    _logger.LogError("NuGet: {Message}", data);
                    break;
            }
        }
    }
}