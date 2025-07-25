using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class TransitiveDependencyDetector : ITransitiveDependencyDetector
{
    private readonly ILogger<TransitiveDependencyDetector> _logger;

    // Essential packages that should never be marked as transitive
    private readonly HashSet<string> _essentialPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Test framework packages
        "Microsoft.NET.Test.Sdk",
        "xunit.runner.visualstudio",
        "NUnit3TestAdapter",
        "MSTest.TestAdapter",
        "coverlet.collector",
        
        // Build/Development packages
        "Microsoft.SourceLink.GitHub",
        "Microsoft.SourceLink.AzureRepos.Git",
        "Microsoft.SourceLink.GitLab",
        "Microsoft.SourceLink.Bitbucket.Git",
        
        // Analyzer packages
        "StyleCop.Analyzers",
        "SonarAnalyzer.CSharp",
        "Microsoft.CodeAnalysis.NetAnalyzers",
        "Microsoft.CodeAnalysis.FxCopAnalyzers",
        "Roslynator.Analyzers",
        
        // Other essential packages
        "Microsoft.AspNetCore.App",
        "Microsoft.NETCore.App",
        "NETStandard.Library"
    };

    private readonly HashSet<string> _commonTransitiveDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "System.IO",
        "System.Text.Encoding",
        "System.Runtime.Extensions",
        "System.Reflection",
        "System.Diagnostics.Debug",
        "System.Globalization",
        "System.Resources.ResourceManager",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Newtonsoft.Json",
        "System.Memory",
        "System.Buffers",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Threading.Tasks.Extensions",
        "System.ValueTuple"
    };

    private readonly Dictionary<string, HashSet<string>> _knownDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.AspNetCore.App"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Configuration",
            "Newtonsoft.Json"
        },
        ["Microsoft.EntityFrameworkCore"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.Extensions.Caching.Memory",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Logging"
        },
        ["NUnit"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NUnit.Framework"
        },
        ["xunit"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xunit.abstractions",
            "xunit.analyzers",
            "xunit.assert",
            "xunit.core",
            "xunit.extensibility.core",
            "xunit.extensibility.execution"
        }
    };

    public TransitiveDependencyDetector(ILogger<TransitiveDependencyDetector> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences,
        CancellationToken cancellationToken = default)
    {
        return DetectTransitiveDependenciesAsync(packageReferences, null, cancellationToken);
    }

    public Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences,
        string? projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var packages = packageReferences.ToList();
        var transitivePackages = new List<PackageReference>();

        foreach (var package in packages)
        {
            // Skip essential packages - they should never be marked as transitive
            if (_essentialPackages.Contains(package.PackageId))
            {
                _logger.LogDebug("Keeping {Package} as it's an essential package", package.PackageId);
                continue;
            }

            if (_commonTransitiveDependencies.Contains(package.PackageId))
            {
                package.IsTransitive = true;
                transitivePackages.Add(package);
                _logger.LogDebug("Marked {Package} as potentially transitive (common transitive dependency)", package.PackageId);
                continue;
            }

            foreach (var kvp in _knownDependencies)
            {
                if (packages.Any(p => p.PackageId.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)) &&
                    kvp.Value.Contains(package.PackageId))
                {
                    package.IsTransitive = true;
                    transitivePackages.Add(package);
                    _logger.LogDebug("Marked {Package} as potentially transitive (dependency of {Parent})",
                        package.PackageId, kvp.Key);
                    break;
                }
            }

            if (!package.IsTransitive)
            {
                if (package.PackageId.StartsWith("System.", StringComparison.OrdinalIgnoreCase) &&
                    packages.Any(p => p.PackageId.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)))
                {
                    package.IsTransitive = true;
                    transitivePackages.Add(package);
                    _logger.LogDebug("Marked {Package} as potentially transitive (System package with Microsoft packages present)",
                        package.PackageId);
                }
            }
        }

        _logger.LogInformation("Detected {Count} potentially transitive dependencies out of {Total} packages",
            transitivePackages.Count, packages.Count);

        return Task.FromResult<IEnumerable<PackageReference>>(packages);
    }

    public void Dispose()
    {
        // Nothing to dispose in the heuristic implementation
    }
}