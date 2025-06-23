using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class OfflinePackageResolver : INuGetPackageResolver
{
    private readonly ILogger<OfflinePackageResolver> _logger;

    // Hardcoded package versions for offline use
    private readonly Dictionary<string, string> _packageVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Test frameworks
        ["MSTest.TestFramework"] = "3.1.1",
        ["MSTest.TestAdapter"] = "3.1.1",
        ["xunit"] = "2.6.6",
        ["xunit.runner.visualstudio"] = "2.5.6",
        ["NUnit"] = "3.13.3",
        ["NUnit3TestAdapter"] = "4.5.0",

        // Mocking and testing tools
        ["Moq"] = "4.20.70",
        ["Castle.Core"] = "5.1.1",
        ["FluentAssertions"] = "6.12.0",

        // Logging
        ["log4net"] = "2.0.15",
        ["Serilog"] = "3.1.1",
        ["NLog"] = "5.2.7",

        // JSON and serialization
        ["Newtonsoft.Json"] = "13.0.3",
        ["System.Text.Json"] = "8.0.0",

        // Data access
        ["EntityFramework"] = "6.4.4",
        ["Microsoft.EntityFrameworkCore"] = "8.0.0",
        ["Dapper"] = "2.1.24",
        ["Microsoft.Data.SqlClient"] = "5.1.2",
        ["System.Data.SqlClient"] = "4.8.6",

        // Web frameworks
        ["Microsoft.AspNet.Mvc"] = "5.2.9",
        ["Microsoft.AspNet.WebApi.Core"] = "5.2.9",
        ["Microsoft.AspNet.WebApi.WebHost"] = "5.2.9",
        ["Microsoft.AspNet.WebApi.Client"] = "5.2.9",

        // Dependency injection
        ["Unity"] = "5.11.10",
        ["Ninject"] = "3.3.6",
        ["SimpleInjector"] = "5.4.3",
        ["CommonServiceLocator"] = "2.0.7",

        // Other common packages
        ["AutoMapper"] = "12.0.1",
        ["FluentValidation"] = "11.8.1",
        ["Polly"] = "8.2.0",
        ["MediatR"] = "12.2.0",
        ["StackExchange.Redis"] = "2.7.10",
        ["RabbitMQ.Client"] = "6.8.1",

        // System packages
        ["System.Configuration.ConfigurationManager"] = "8.0.0",
        ["System.Drawing.Common"] = "8.0.0",
        ["System.Windows.Forms"] = "4.0.0-preview3.19504.8"
    };

    private readonly Dictionary<string, (string PackageId, string? Notes)> _assemblyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.VisualStudio.QualityTools.UnitTestFramework"] = ("MSTest.TestFramework", "Also requires MSTest.TestAdapter"),
        ["Microsoft.VisualStudio.TestPlatform.TestFramework"] = ("MSTest.TestFramework", "Also requires MSTest.TestAdapter"),
        ["xunit"] = ("xunit", "Also requires xunit.runner.visualstudio"),
        ["nunit.framework"] = ("NUnit", "Also requires NUnit3TestAdapter"),
        ["System.Drawing"] = ("System.Drawing.Common", null),
        ["System.Net.Http.Formatting"] = ("Microsoft.AspNet.WebApi.Client", null),
        ["System.Web.Mvc"] = ("Microsoft.AspNet.Mvc", null),
        ["System.Web.Http"] = ("Microsoft.AspNet.WebApi.Core", null),
        ["System.Web.Http.WebHost"] = ("Microsoft.AspNet.WebApi.WebHost", null),
        ["System.Windows.Forms"] = ("System.Windows.Forms", "For .NET Core/5+ projects"),
        ["System.Configuration.ConfigurationManager"] = ("System.Configuration.ConfigurationManager", null)
    };

    public OfflinePackageResolver(ILogger<OfflinePackageResolver> logger)
    {
        _logger = logger;
        _logger.LogInformation("Using offline package resolver with {Count} hardcoded package versions", _packageVersions.Count);
    }

    public Task<string?> GetLatestStableVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (_packageVersions.TryGetValue(packageId, out var version))
        {
            return Task.FromResult<string?>(version);
        }

        _logger.LogWarning("Package {PackageId} not found in offline cache", packageId);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        return GetLatestStableVersionAsync(packageId, cancellationToken);
    }

    public Task<IEnumerable<string>> GetAllVersionsAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        if (_packageVersions.TryGetValue(packageId, out var version))
        {
            return Task.FromResult<IEnumerable<string>>(new[] { version });
        }

        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    public Task<PackageResolutionResult?> ResolveAssemblyToPackageAsync(string assemblyName, string? targetFramework = null, CancellationToken cancellationToken = default)
    {
        // Check direct mappings first
        if (_assemblyMappings.TryGetValue(assemblyName, out var mapping))
        {
            if (_packageVersions.TryGetValue(mapping.PackageId, out var version))
            {
                var result = new PackageResolutionResult
                {
                    PackageId = mapping.PackageId,
                    Version = version,
                    Notes = mapping.Notes
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

                return Task.FromResult<PackageResolutionResult?>(result);
            }
        }

        // Try direct package name match
        if (_packageVersions.TryGetValue(assemblyName, out var directVersion))
        {
            return Task.FromResult<PackageResolutionResult?>(new PackageResolutionResult
            {
                PackageId = assemblyName,
                Version = directVersion
            });
        }

        _logger.LogWarning("Could not resolve assembly {AssemblyName} to a package in offline mode", assemblyName);
        return Task.FromResult<PackageResolutionResult?>(null);
    }
}