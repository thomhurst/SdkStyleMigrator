using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class PackageReferenceMigrator : IPackageReferenceMigrator
{
    private readonly ILogger<PackageReferenceMigrator> _logger;

    public PackageReferenceMigrator(ILogger<PackageReferenceMigrator> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<PackageReference>> MigratePackagesAsync(Project project, CancellationToken cancellationToken = default)
    {
        var packageReferences = new List<PackageReference>();

        var packagesConfigPath = Path.Combine(Path.GetDirectoryName(project.FullPath)!, "packages.config");
        if (File.Exists(packagesConfigPath))
        {
            _logger.LogInformation("Found packages.config at {Path}", packagesConfigPath);
            packageReferences.AddRange(ParsePackagesConfig(packagesConfigPath));
        }

        var referenceItems = project.Items.Where(i => i.ItemType == "Reference" && i.HasMetadata("HintPath"));
        foreach (var reference in referenceItems)
        {
            var hintPath = reference.GetMetadataValue("HintPath");
            if (hintPath.Contains("packages"))
            {
                var packageRef = ExtractPackageFromHintPath(reference.EvaluatedInclude, hintPath);
                if (packageRef != null && !packageReferences.Any(p => p.PackageId == packageRef.PackageId))
                {
                    packageReferences.Add(packageRef);
                }
            }
        }

        var existingPackageRefs = project.Items.Where(i => i.ItemType == "PackageReference");
        foreach (var item in existingPackageRefs)
        {
            var packageRef = new PackageReference
            {
                PackageId = item.EvaluatedInclude,
                Version = item.GetMetadataValue("Version") ?? item.GetMetadataValue("VersionOverride") ?? "*"
            };

            if (!packageReferences.Any(p => p.PackageId == packageRef.PackageId))
            {
                packageReferences.Add(packageRef);
            }
        }

        _logger.LogInformation("Migrated {Count} package references", packageReferences.Count);
        return Task.FromResult<IEnumerable<PackageReference>>(packageReferences);
    }

    private IEnumerable<PackageReference> ParsePackagesConfig(string packagesConfigPath)
    {
        var packages = new List<PackageReference>();

        try
        {
            var doc = XDocument.Load(packagesConfigPath);
            var packageElements = doc.Root?.Elements("package") ?? Enumerable.Empty<XElement>();

            foreach (var package in packageElements)
            {
                var id = package.Attribute("id")?.Value;
                var version = package.Attribute("version")?.Value;
                var targetFramework = package.Attribute("targetFramework")?.Value;
                var developmentDependency = package.Attribute("developmentDependency")?.Value;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                {
                    var packageRef = new PackageReference
                    {
                        PackageId = id,
                        Version = version,
                        TargetFramework = targetFramework
                    };
                    
                    if (developmentDependency == "true")
                    {
                        packageRef.Metadata["PrivateAssets"] = "all";
                    }
                    
                    packages.Add(packageRef);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse packages.config at {Path}", packagesConfigPath);
        }

        return packages;
    }

    private PackageReference? ExtractPackageFromHintPath(string referenceName, string hintPath)
    {
        try
        {
            var parts = hintPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var packagesIndex = Array.FindIndex(parts, p => p.Equals("packages", StringComparison.OrdinalIgnoreCase));
            
            if (packagesIndex >= 0 && packagesIndex < parts.Length - 1)
            {
                var packageFolder = parts[packagesIndex + 1];
                
                // Try to find the version by looking for a pattern like x.y.z
                // Start from the end and work backwards to find a valid version pattern
                var versionMatch = Regex.Match(
                    packageFolder, 
                    @"^(.+?)\.(\d+(?:\.\d+)*)$");
                
                if (versionMatch.Success)
                {
                    var packageId = versionMatch.Groups[1].Value;
                    var version = versionMatch.Groups[2].Value;
                    
                    // Validate that we have a reasonable version number
                    if (version.Split('.').All(part => int.TryParse(part, out _)))
                    {
                        return new PackageReference
                        {
                            PackageId = packageId,
                            Version = version
                        };
                    }
                }
                
                // Fallback: try to match known version patterns more aggressively
                // This handles cases like: PackageName.1.2.3.4 or PackageName2.1.0.0
                var fallbackMatch = Regex.Match(
                    packageFolder,
                    @"^(.+?)\.(\d+\.\d+(?:\.\d+)*(?:\.\d+)?)$");
                    
                if (fallbackMatch.Success)
                {
                    return new PackageReference
                    {
                        PackageId = fallbackMatch.Groups[1].Value,
                        Version = fallbackMatch.Groups[2].Value
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract package info from hint path: {HintPath}", hintPath);
        }

        return null;
    }
}