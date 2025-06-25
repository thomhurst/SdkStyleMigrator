using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

/// <summary>
/// Reads Directory.Build.props, Directory.Build.targets, and Directory.Packages.props files
/// to understand inherited properties and avoid duplication during migration.
/// </summary>
public class DirectoryBuildPropsReader : IDirectoryBuildPropsReader
{
    private readonly ILogger<DirectoryBuildPropsReader> _logger;

    public DirectoryBuildPropsReader(ILogger<DirectoryBuildPropsReader> logger)
    {
        _logger = logger;
    }

    public Dictionary<string, string> GetInheritedProperties(string projectPath)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projectDir = Path.GetDirectoryName(projectPath);
        
        if (string.IsNullOrEmpty(projectDir))
            return properties;

        // Walk up the directory tree looking for Directory.Build.props
        var currentDir = projectDir;
        var propsFiles = new List<string>();

        while (!string.IsNullOrEmpty(currentDir))
        {
            var propsFile = Path.Combine(currentDir, "Directory.Build.props");
            if (File.Exists(propsFile))
            {
                propsFiles.Add(propsFile);
                _logger.LogDebug("Found Directory.Build.props at: {Path}", propsFile);
            }

            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == currentDir) // Reached root
                break;
            currentDir = parentDir;
        }

        // Process files from root to leaf (properties in child files override parent)
        propsFiles.Reverse();
        foreach (var propsFile in propsFiles)
        {
            try
            {
                var doc = XDocument.Load(propsFile);
                var propertyGroups = doc.Root?.Elements("PropertyGroup") ?? Enumerable.Empty<XElement>();

                foreach (var propGroup in propertyGroups)
                {
                    foreach (var prop in propGroup.Elements())
                    {
                        var name = prop.Name.LocalName;
                        var value = prop.Value;

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            properties[name] = value;
                            _logger.LogTrace("Found property {Name}={Value} in {File}", name, value, propsFile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read Directory.Build.props at {Path}", propsFile);
            }
        }

        _logger.LogDebug("Found {Count} inherited properties for {Project}", properties.Count, projectPath);
        return properties;
    }

    public bool HasDirectoryBuildTargets(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
            return false;

        // Walk up the directory tree looking for Directory.Build.targets
        var currentDir = projectDir;
        while (!string.IsNullOrEmpty(currentDir))
        {
            var targetsFile = Path.Combine(currentDir, "Directory.Build.targets");
            if (File.Exists(targetsFile))
            {
                _logger.LogDebug("Found Directory.Build.targets at: {Path}", targetsFile);
                return true;
            }

            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == currentDir) // Reached root
                break;
            currentDir = parentDir;
        }

        return false;
    }

    public HashSet<string> GetCentrallyManagedPackages(string projectPath)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDir = Path.GetDirectoryName(projectPath);
        
        if (string.IsNullOrEmpty(projectDir))
            return packages;

        // Walk up the directory tree looking for Directory.Packages.props
        var currentDir = projectDir;
        while (!string.IsNullOrEmpty(currentDir))
        {
            var packagesFile = Path.Combine(currentDir, "Directory.Packages.props");
            if (File.Exists(packagesFile))
            {
                _logger.LogDebug("Found Directory.Packages.props at: {Path}", packagesFile);
                
                try
                {
                    var doc = XDocument.Load(packagesFile);
                    var packageVersions = doc.Root?.Elements("ItemGroup")
                        .SelectMany(ig => ig.Elements("PackageVersion")) ?? Enumerable.Empty<XElement>();

                    foreach (var packageVersion in packageVersions)
                    {
                        var includeAttr = packageVersion.Attribute("Include")?.Value;
                        if (!string.IsNullOrEmpty(includeAttr))
                        {
                            packages.Add(includeAttr);
                        }
                    }

                    // Check if Central Package Management is enabled
                    var managementEnabled = doc.Root?.Elements("PropertyGroup")
                        .SelectMany(pg => pg.Elements("ManagePackageVersionsCentrally"))
                        .Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase)) ?? false;

                    if (!managementEnabled)
                    {
                        _logger.LogDebug("Central Package Management not enabled in {Path}", packagesFile);
                        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read Directory.Packages.props at {Path}", packagesFile);
                }
                
                break; // Only use the first Directory.Packages.props found
            }

            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == currentDir) // Reached root
                break;
            currentDir = parentDir;
        }

        _logger.LogDebug("Found {Count} centrally managed packages for {Project}", packages.Count, projectPath);
        return packages;
    }
}