using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Packaging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class PackageAssemblyResolver
{
    private readonly ILogger<PackageAssemblyResolver> _logger;
    private readonly INuGetPackageResolver _nugetResolver;
    private readonly string _packagesPath;

    // Cache of package ID -> list of assemblies it provides
    private readonly ConcurrentDictionary<string, HashSet<string>> _packageAssemblyCache = new();

    // Comprehensive mapping of well-known packages and their assemblies
    private readonly Dictionary<string, HashSet<string>> _wellKnownPackageAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Newtonsoft.Json"] = new(StringComparer.OrdinalIgnoreCase) { "Newtonsoft.Json" },
        ["EntityFramework"] = new(StringComparer.OrdinalIgnoreCase) { "EntityFramework", "EntityFramework.SqlServer", "EntityFramework.SqlServerCompact" },
        ["Microsoft.EntityFrameworkCore"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions", "Microsoft.EntityFrameworkCore.Relational" },
        ["Microsoft.EntityFrameworkCore.SqlServer"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.EntityFrameworkCore.SqlServer" },
        ["Microsoft.EntityFrameworkCore.Design"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.EntityFrameworkCore.Design" },
        ["NUnit"] = new(StringComparer.OrdinalIgnoreCase) { "nunit.framework" },
        ["xunit"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.core", "xunit.assert", "xunit.abstractions", "xunit.execution.desktop", "xunit.execution.dotnet" },
        ["xunit.core"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.core", "xunit.abstractions" },
        ["xunit.assert"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.assert" },
        ["MSTest.TestFramework"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.VisualStudio.TestPlatform.TestFramework", "Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions", "Microsoft.VisualStudio.QualityTools.UnitTestFramework" },
        ["MSTest.TestAdapter"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter", "Microsoft.VisualStudio.TestPlatform.MSTestAdapter.PlatformServices" },
        ["Moq"] = new(StringComparer.OrdinalIgnoreCase) { "Moq", "Castle.Core", "System.Threading.Tasks.Extensions" },
        ["Castle.Core"] = new(StringComparer.OrdinalIgnoreCase) { "Castle.Core" },
        ["AutoMapper"] = new(StringComparer.OrdinalIgnoreCase) { "AutoMapper" },
        ["AutoMapper.Extensions.Microsoft.DependencyInjection"] = new(StringComparer.OrdinalIgnoreCase) { "AutoMapper.Extensions.Microsoft.DependencyInjection" },
        ["log4net"] = new(StringComparer.OrdinalIgnoreCase) { "log4net" },
        ["NLog"] = new(StringComparer.OrdinalIgnoreCase) { "NLog" },
        ["Serilog"] = new(StringComparer.OrdinalIgnoreCase) { "Serilog" },
        ["Microsoft.AspNet.WebApi.Core"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Http", "System.Net.Http.Formatting", "System.Web.Http.WebHost" },
        ["Microsoft.AspNet.WebApi.Client"] = new(StringComparer.OrdinalIgnoreCase) { "System.Net.Http.Formatting" },
        ["Microsoft.AspNet.WebApi.WebHost"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Http.WebHost" },
        ["Microsoft.AspNet.Mvc"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Mvc", "System.Web.Helpers", "System.Web.Razor", "System.Web.WebPages", "System.Web.WebPages.Deployment", "System.Web.WebPages.Razor" },
        ["Microsoft.AspNet.Razor"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Razor" },
        ["Microsoft.AspNet.WebPages"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.WebPages", "System.Web.WebPages.Deployment", "System.Web.WebPages.Razor", "System.Web.Helpers" },
        ["System.Data.SqlClient"] = new(StringComparer.OrdinalIgnoreCase) { "System.Data.SqlClient" },
        ["Microsoft.Data.SqlClient"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Data.SqlClient" },
        ["System.Configuration.ConfigurationManager"] = new(StringComparer.OrdinalIgnoreCase) { "System.Configuration.ConfigurationManager" },
        ["System.Drawing.Common"] = new(StringComparer.OrdinalIgnoreCase) { "System.Drawing", "System.Drawing.Common" },
        ["System.Runtime.Caching"] = new(StringComparer.OrdinalIgnoreCase) { "System.Runtime.Caching" },
        ["System.Security.Cryptography.Xml"] = new(StringComparer.OrdinalIgnoreCase) { "System.Security.Cryptography.Xml" },
        ["System.Security.Permissions"] = new(StringComparer.OrdinalIgnoreCase) { "System.Security.Permissions" },
        ["System.Windows.Extensions"] = new(StringComparer.OrdinalIgnoreCase) { "System.Windows.Extensions" },
        ["Microsoft.Windows.Compatibility"] = new(StringComparer.OrdinalIgnoreCase) { "System.ServiceModel", "System.ServiceModel.Duplex", "System.ServiceModel.Http", "System.ServiceModel.NetTcp", "System.ServiceModel.Primitives", "System.ServiceModel.Security" },
        ["AWSSDK.Core"] = new(StringComparer.OrdinalIgnoreCase) { "AWSSDK.Core" },
        ["AWSSDK.S3"] = new(StringComparer.OrdinalIgnoreCase) { "AWSSDK.S3" },
        ["RabbitMQ.Client"] = new(StringComparer.OrdinalIgnoreCase) { "RabbitMQ.Client" },
        ["StackExchange.Redis"] = new(StringComparer.OrdinalIgnoreCase) { "StackExchange.Redis", "StackExchange.Redis.StrongName", "Pipelines.Sockets.Unofficial" },
        ["protobuf-net"] = new(StringComparer.OrdinalIgnoreCase) { "protobuf-net", "protobuf-net.Core" },
        ["Grpc.Core"] = new(StringComparer.OrdinalIgnoreCase) { "Grpc.Core", "Grpc.Core.Api" },
        ["Azure.Storage.Blobs"] = new(StringComparer.OrdinalIgnoreCase) { "Azure.Storage.Blobs", "Azure.Storage.Common", "Azure.Core" },
        ["Azure.Core"] = new(StringComparer.OrdinalIgnoreCase) { "Azure.Core" },
        ["Dapper"] = new(StringComparer.OrdinalIgnoreCase) { "Dapper" },
        ["FluentValidation"] = new(StringComparer.OrdinalIgnoreCase) { "FluentValidation" },
        ["MediatR"] = new(StringComparer.OrdinalIgnoreCase) { "MediatR", "MediatR.Contracts" },
        ["Polly"] = new(StringComparer.OrdinalIgnoreCase) { "Polly" },
        ["Unity"] = new(StringComparer.OrdinalIgnoreCase) { "Unity", "Unity.Abstractions", "Unity.Container" },
        ["Unity.Container"] = new(StringComparer.OrdinalIgnoreCase) { "Unity.Container", "Unity.Abstractions" },
        ["Ninject"] = new(StringComparer.OrdinalIgnoreCase) { "Ninject" },
        ["SimpleInjector"] = new(StringComparer.OrdinalIgnoreCase) { "SimpleInjector" },
        ["Autofac"] = new(StringComparer.OrdinalIgnoreCase) { "Autofac" },
        ["StructureMap"] = new(StringComparer.OrdinalIgnoreCase) { "StructureMap" },
        ["CommonServiceLocator"] = new(StringComparer.OrdinalIgnoreCase) { "CommonServiceLocator", "Microsoft.Practices.ServiceLocation" },
        ["Microsoft.Extensions.DependencyInjection"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions" },
        ["Microsoft.Extensions.Logging"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions" },
        ["Microsoft.Extensions.Configuration"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.Configuration", "Microsoft.Extensions.Configuration.Abstractions" },
        ["RestSharp"] = new(StringComparer.OrdinalIgnoreCase) { "RestSharp" },
        ["Refit"] = new(StringComparer.OrdinalIgnoreCase) { "Refit" },
        ["IdentityModel"] = new(StringComparer.OrdinalIgnoreCase) { "IdentityModel" },
        ["System.IdentityModel.Tokens.Jwt"] = new(StringComparer.OrdinalIgnoreCase) { "System.IdentityModel.Tokens.Jwt" },
        ["Microsoft.IdentityModel.Tokens"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.IdentityModel.Tokens", "Microsoft.IdentityModel.Logging", "Microsoft.IdentityModel.JsonWebTokens" }
    };

    public PackageAssemblyResolver(ILogger<PackageAssemblyResolver> logger, INuGetPackageResolver nugetResolver, MigrationOptions options)
    {
        _logger = logger;
        _nugetResolver = nugetResolver;

        // Determine packages folder path
        _packagesPath = options.DirectoryPath != null
            ? Path.Combine(Path.GetDirectoryName(options.DirectoryPath) ?? "", "packages")
            : Path.Combine(Directory.GetCurrentDirectory(), "packages");
    }

    public async Task<HashSet<string>> GetAssembliesProvidedByPackagesAsync(
        List<Models.PackageReference> packages,
        string targetFramework,
        CancellationToken cancellationToken = default)
    {
        var allAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            var assemblies = await GetAssembliesForPackageAsync(package.PackageId, package.Version, targetFramework, cancellationToken);
            foreach (var assembly in assemblies)
            {
                allAssemblies.Add(assembly);
            }
        }

        return allAssemblies;
    }

    public async Task<HashSet<string>> GetAssembliesForPackageAsync(
        string packageId,
        string version,
        string targetFramework,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{packageId}_{version}";

        // Check cache first
        if (_packageAssemblyCache.TryGetValue(cacheKey, out var cachedAssemblies))
        {
            return cachedAssemblies;
        }

        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check well-known mappings first
        if (_wellKnownPackageAssemblies.TryGetValue(packageId, out var knownAssemblies))
        {
            foreach (var assembly in knownAssemblies)
            {
                assemblies.Add(assembly);
            }
            _logger.LogDebug("Found {Count} known assemblies for package {PackageId}", assemblies.Count, packageId);
        }

        // Try to read from local packages folder if it exists
        if (Directory.Exists(_packagesPath))
        {
            var packagePath = Path.Combine(_packagesPath, $"{packageId}.{version}");
            if (Directory.Exists(packagePath))
            {
                try
                {
                    var libPath = Path.Combine(packagePath, "lib");
                    if (Directory.Exists(libPath))
                    {
                        var tfm = NuGetFramework.Parse(targetFramework);
                        var foundAssemblies = await GetAssembliesFromPackageFolderAsync(libPath, tfm);

                        foreach (var assembly in foundAssemblies)
                        {
                            assemblies.Add(assembly);
                        }

                        _logger.LogDebug("Found {Count} assemblies for package {PackageId} from local folder",
                            foundAssemblies.Count, packageId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading assemblies from package folder: {PackagePath}", packagePath);
                }
            }
        }

        // Try to analyze packages.config references
        if (!assemblies.Any())
        {
            // Use NuGet resolver to check if this package exists
            var resolvedPackage = await _nugetResolver.ResolveAssemblyToPackageAsync(packageId, targetFramework, cancellationToken);
            if (resolvedPackage != null && resolvedPackage.IncludedAssemblies.Any())
            {
                foreach (var assembly in resolvedPackage.IncludedAssemblies)
                {
                    assemblies.Add(assembly);
                }
            }
        }

        // Cache the results
        _packageAssemblyCache.TryAdd(cacheKey, assemblies);

        return assemblies;
    }

    private async Task<HashSet<string>> GetAssembliesFromPackageFolderAsync(string libPath, NuGetFramework targetFramework)
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get all framework folders
        var frameworkFolders = Directory.GetDirectories(libPath);

        // Find the best matching framework folder
        string? bestMatch = null;
        var bestFramework = NuGetFramework.UnsupportedFramework;

        foreach (var folder in frameworkFolders)
        {
            var folderName = Path.GetFileName(folder);
            try
            {
                var framework = NuGetFramework.Parse(folderName);
                if (!framework.IsUnsupported && DefaultCompatibilityProvider.Instance.IsCompatible(targetFramework, framework))
                {
                    if (bestFramework.IsUnsupported ||
                        framework.Version > bestFramework.Version)
                    {
                        bestFramework = framework;
                        bestMatch = folder;
                    }
                }
            }
            catch
            {
                // Not a valid framework folder name, skip it
            }
        }

        // If no match found, try portable or netstandard
        if (bestMatch == null)
        {
            bestMatch = frameworkFolders.FirstOrDefault(f =>
                Path.GetFileName(f).StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith("portable", StringComparison.OrdinalIgnoreCase));
        }

        // Last resort - any folder
        if (bestMatch == null && frameworkFolders.Length > 0)
        {
            bestMatch = frameworkFolders[0];
        }

        if (bestMatch != null)
        {
            // Get all DLL files
            var dllFiles = Directory.GetFiles(bestMatch, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (var dll in dllFiles)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dll);
                assemblies.Add(assemblyName);
            }
        }

        return assemblies;
    }

    public bool IsAssemblyProvidedByPackage(string assemblyName, HashSet<string> packageAssemblies)
    {
        // Direct match
        if (packageAssemblies.Contains(assemblyName))
            return true;

        // Check without version suffix (e.g., "Assembly, Version=1.0.0.0" -> "Assembly")
        var simpleName = assemblyName.Split(',')[0].Trim();
        if (packageAssemblies.Contains(simpleName))
            return true;

        // Check for partial matches (some packages include version in assembly name)
        return packageAssemblies.Any(pa =>
            pa.Equals(simpleName, StringComparison.OrdinalIgnoreCase) ||
            simpleName.Equals(pa, StringComparison.OrdinalIgnoreCase));
    }
}