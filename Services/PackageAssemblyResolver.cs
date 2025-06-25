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

    // Framework-aware mapping of well-known packages and their assemblies
    // Structure: [PackageId] => [FrameworkPattern] => [Assemblies]
    // Framework patterns: "netframework" (net4x), "netcoreapp" (netcoreapp2.x/3.x), "net" (net5+), "*" (all frameworks)
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _wellKnownPackageAssembliesByFramework = new(StringComparer.OrdinalIgnoreCase)
    {
        // Framework-specific packages - System.* assemblies that behave differently across frameworks
        ["System.Drawing.Common"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Drawing", "System.Drawing.Common" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.Drawing", "System.Drawing.Common" }
            // No "netframework" entry - System.Drawing is built-in for .NET Framework
        },
        ["System.Windows.Forms"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Windows.Forms" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.Windows.Forms" }
            // No "netframework" entry - System.Windows.Forms is built-in for .NET Framework
        },
        ["System.Configuration.ConfigurationManager"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Configuration.ConfigurationManager" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.Configuration.ConfigurationManager" }
            // System.Configuration is built-in for .NET Framework
        },
        ["System.Security.Permissions"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Security.Permissions" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.Security.Permissions" }
        },
        ["System.Windows.Extensions"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Windows.Extensions" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.Windows.Extensions" }
        },
        
        // .NET Framework specific packages (Microsoft.AspNet.* family)
        ["Microsoft.AspNet.WebApi.Core"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Http", "System.Net.Http.Formatting", "System.Web.Http.WebHost" }
        },
        ["Microsoft.AspNet.WebApi.Client"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Net.Http.Formatting" }
        },
        ["Microsoft.AspNet.WebApi.WebHost"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Http.WebHost" }
        },
        ["Microsoft.AspNet.Mvc"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Mvc", "System.Web.Helpers", "System.Web.Razor", "System.Web.WebPages", "System.Web.WebPages.Deployment", "System.Web.WebPages.Razor" }
        },
        ["Microsoft.AspNet.Razor"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.Razor" }
        },
        ["Microsoft.AspNet.WebPages"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Web.WebPages", "System.Web.WebPages.Deployment", "System.Web.WebPages.Razor", "System.Web.Helpers" }
        },
        ["System.Data.SqlClient"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "System.Data.SqlClient" },
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.Data.SqlClient" }
            // For net5+, Microsoft.Data.SqlClient is preferred
        },
        
        // Cross-framework packages that work the same way across all target frameworks
        ["Newtonsoft.Json"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Newtonsoft.Json" }
        },
        ["EntityFramework"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netframework"] = new(StringComparer.OrdinalIgnoreCase) { "EntityFramework", "EntityFramework.SqlServer", "EntityFramework.SqlServerCompact" }
        },
        ["Microsoft.EntityFrameworkCore"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions", "Microsoft.EntityFrameworkCore.Relational" }
        },
        ["Microsoft.EntityFrameworkCore.SqlServer"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.EntityFrameworkCore.SqlServer" }
        },
        ["Microsoft.Data.SqlClient"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Data.SqlClient" }
        },
        ["NUnit"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "nunit.framework" }
        },
        ["xunit"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.core", "xunit.assert", "xunit.abstractions", "xunit.execution.desktop", "xunit.execution.dotnet" }
        },
        ["xunit.core"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.core", "xunit.abstractions" }
        },
        ["xunit.assert"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "xunit.assert" }
        },
        ["MSTest.TestFramework"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.VisualStudio.TestPlatform.TestFramework", "Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions", "Microsoft.VisualStudio.QualityTools.UnitTestFramework" }
        },
        ["MSTest.TestAdapter"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter", "Microsoft.VisualStudio.TestPlatform.MSTestAdapter.PlatformServices" }
        },
        ["Moq"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Moq", "Castle.Core", "System.Threading.Tasks.Extensions" }
        },
        ["AutoMapper"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "AutoMapper" }
        },
        ["log4net"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "log4net" }
        },
        ["NLog"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "NLog" }
        },
        ["Serilog"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Serilog" }
        },
        ["Microsoft.Windows.Compatibility"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["netcoreapp"] = new(StringComparer.OrdinalIgnoreCase) { "System.ServiceModel", "System.ServiceModel.Duplex", "System.ServiceModel.Http", "System.ServiceModel.NetTcp", "System.ServiceModel.Primitives", "System.ServiceModel.Security" },
            ["net"] = new(StringComparer.OrdinalIgnoreCase) { "System.ServiceModel", "System.ServiceModel.Duplex", "System.ServiceModel.Http", "System.ServiceModel.NetTcp", "System.ServiceModel.Primitives", "System.ServiceModel.Security" }
        },
        ["Microsoft.Extensions.DependencyInjection"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions" }
        },
        ["Microsoft.Extensions.Logging"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions" }
        },
        ["Microsoft.Extensions.Configuration"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.Configuration", "Microsoft.Extensions.Configuration.Abstractions" }
        }
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

    /// <summary>
    /// Determines if a target framework moniker matches a framework pattern from our mappings.
    /// </summary>
    /// <param name="targetFrameworkMoniker">The target framework (e.g., "net6.0", "net472", "netcoreapp3.1")</param>
    /// <param name="pattern">The framework pattern ("netframework", "netcoreapp", "net", "*", or specific moniker)</param>
    /// <returns>True if the target framework is compatible with the pattern</returns>
    private bool IsFrameworkCompatible(string targetFrameworkMoniker, string pattern)
    {
        if (string.IsNullOrEmpty(targetFrameworkMoniker) || string.IsNullOrEmpty(pattern))
            return false;

        // Universal pattern matches all frameworks
        if (pattern.Equals("*", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exact match
        if (targetFrameworkMoniker.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle framework family patterns
        var lowerTarget = targetFrameworkMoniker.ToLowerInvariant();
        var lowerPattern = pattern.ToLowerInvariant();

        return lowerPattern switch
        {
            "netframework" => lowerTarget.StartsWith("net4") || lowerTarget == "net35" || lowerTarget == "net20",
            "netcoreapp" => lowerTarget.StartsWith("netcoreapp"),
            "net" => lowerTarget.StartsWith("net") && 
                     !lowerTarget.StartsWith("net4") && 
                     !lowerTarget.StartsWith("netcoreapp") &&
                     !lowerTarget.StartsWith("netframework"),
            _ => false
        };
    }

    /// <summary>
    /// Exposes the well-known framework-aware package mappings for reverse lookup scenarios.
    /// </summary>
    /// <returns>Read-only dictionary of framework-aware package mappings.</returns>
    public IReadOnlyDictionary<string, Dictionary<string, HashSet<string>>> GetWellKnownMappings()
    {
        return _wellKnownPackageAssembliesByFramework;
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

        // Check framework-aware well-known mappings first
        if (_wellKnownPackageAssembliesByFramework.TryGetValue(packageId, out var frameworkMappings))
        {
            foreach (var entry in frameworkMappings)
            {
                if (IsFrameworkCompatible(targetFramework, entry.Key))
                {
                    foreach (var assembly in entry.Value)
                    {
                        assemblies.Add(assembly);
                    }
                }
            }
            _logger.LogDebug("Found {Count} known assemblies for package {PackageId} for TFM {TargetFramework}", assemblies.Count, packageId, targetFramework);
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