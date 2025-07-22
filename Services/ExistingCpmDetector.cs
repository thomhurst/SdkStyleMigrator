using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class ExistingCpmDetector
{
    private readonly ILogger<ExistingCpmDetector> _logger;

    public ExistingCpmDetector(ILogger<ExistingCpmDetector> logger)
    {
        _logger = logger;
    }

    public ExistingCpmInfo DetectExistingCpm(string solutionDirectory)
    {
        var result = new ExistingCpmInfo
        {
            SolutionDirectory = solutionDirectory
        };

        try
        {
            // Look for Directory.Packages.props in current directory and parent directories
            var directoryPackagesPath = FindDirectoryPackagesProps(solutionDirectory);
            
            if (!string.IsNullOrEmpty(directoryPackagesPath))
            {
                result.HasExistingCpm = true;
                result.DirectoryPackagesPropsPath = directoryPackagesPath;
                result.ExistingPackages = ParseExistingPackages(directoryPackagesPath);
                
                _logger.LogInformation("Found existing Directory.Packages.props at {Path} with {PackageCount} packages", 
                    directoryPackagesPath, result.ExistingPackages.Count);
            }

            // Check Directory.Build.props for CPM configuration
            var directoryBuildPropsPath = FindDirectoryBuildProps(solutionDirectory);
            if (!string.IsNullOrEmpty(directoryBuildPropsPath))
            {
                result.DirectoryBuildPropsPath = directoryBuildPropsPath;
                result.CpmConfiguration = ParseCpmConfiguration(directoryBuildPropsPath);
            }

            // Scan for projects that might already have CPM-style PackageReferences (without versions)
            result.ProjectsWithCpmReferences = ScanForCpmStyleReferences(solutionDirectory);

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting existing CPM configuration in {Directory}", solutionDirectory);
            result.HasErrors = true;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private string? FindDirectoryPackagesProps(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        
        while (directory != null)
        {
            var packagesPropsPath = Path.Combine(directory.FullName, "Directory.Packages.props");
            if (File.Exists(packagesPropsPath))
            {
                return packagesPropsPath;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private string? FindDirectoryBuildProps(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        
        while (directory != null)
        {
            var buildPropsPath = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(buildPropsPath))
            {
                return buildPropsPath;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private List<ExistingCpmPackage> ParseExistingPackages(string directoryPackagesPropsPath)
    {
        var packages = new List<ExistingCpmPackage>();

        try
        {
            var doc = XDocument.Load(directoryPackagesPropsPath);
            var root = doc.Root;

            if (root == null)
                return packages;

            // Parse PackageVersion elements
            var packageVersions = root.Descendants("PackageVersion");
            foreach (var packageVersion in packageVersions)
            {
                var packageId = packageVersion.Attribute("Include")?.Value;
                var version = packageVersion.Attribute("Version")?.Value;

                if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(version))
                {
                    packages.Add(new ExistingCpmPackage
                    {
                        PackageId = packageId,
                        Version = version,
                        ElementType = "PackageVersion"
                    });
                }
            }

            // Parse GlobalPackageReference elements (for analyzers, etc.)
            var globalPackageRefs = root.Descendants("GlobalPackageReference");
            foreach (var globalPackageRef in globalPackageRefs)
            {
                var packageId = globalPackageRef.Attribute("Include")?.Value;
                var version = globalPackageRef.Attribute("Version")?.Value;

                if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(version))
                {
                    packages.Add(new ExistingCpmPackage
                    {
                        PackageId = packageId,
                        Version = version,
                        ElementType = "GlobalPackageReference"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing existing Directory.Packages.props at {Path}", directoryPackagesPropsPath);
        }

        return packages;
    }

    private CpmConfiguration ParseCpmConfiguration(string directoryBuildPropsPath)
    {
        var config = new CpmConfiguration();

        try
        {
            var doc = XDocument.Load(directoryBuildPropsPath);
            var root = doc.Root;

            if (root == null)
                return config;

            // Check for ManagePackageVersionsCentrally property
            var manageVersionsCentrally = root.Descendants("ManagePackageVersionsCentrally").FirstOrDefault();
            if (manageVersionsCentrally != null)
            {
                bool.TryParse(manageVersionsCentrally.Value, out bool enabled);
                config.ManagePackageVersionsCentrally = enabled;
            }

            // Check for CentralPackageTransitivePinningEnabled property
            var transitivePinning = root.Descendants("CentralPackageTransitivePinningEnabled").FirstOrDefault();
            if (transitivePinning != null)
            {
                bool.TryParse(transitivePinning.Value, out bool enabled);
                config.CentralPackageTransitivePinningEnabled = enabled;
            }

            // Check for EnablePackageVersionOverride property
            var versionOverride = root.Descendants("EnablePackageVersionOverride").FirstOrDefault();
            if (versionOverride != null)
            {
                bool.TryParse(versionOverride.Value, out bool enabled);
                config.EnablePackageVersionOverride = enabled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing CPM configuration from Directory.Build.props at {Path}", directoryBuildPropsPath);
        }

        return config;
    }

    private List<string> ScanForCpmStyleReferences(string solutionDirectory)
    {
        var projectsWithCpmReferences = new List<string>();

        try
        {
            var projectFiles = Directory.GetFiles(solutionDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(solutionDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(solutionDirectory, "*.fsproj", SearchOption.AllDirectories));

            foreach (var projectFile in projectFiles)
            {
                if (HasCpmStyleReferences(projectFile))
                {
                    projectsWithCpmReferences.Add(projectFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning for CPM-style references in {Directory}", solutionDirectory);
        }

        return projectsWithCpmReferences;
    }

    private bool HasCpmStyleReferences(string projectFile)
    {
        try
        {
            var doc = XDocument.Load(projectFile);
            var root = doc.Root;

            if (root == null)
                return false;

            // Look for PackageReference elements without Version attributes
            var packageReferences = root.Descendants("PackageReference");
            return packageReferences.Any(pr => 
                pr.Attribute("Include") != null && 
                pr.Attribute("Version") == null &&
                pr.Elements("Version").Any() == false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking CPM-style references in {ProjectFile}", projectFile);
            return false;
        }
    }
}

public class ExistingCpmInfo
{
    public string SolutionDirectory { get; set; } = string.Empty;
    public bool HasExistingCpm { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public string? DirectoryBuildPropsPath { get; set; }
    public List<ExistingCpmPackage> ExistingPackages { get; set; } = new();
    public CpmConfiguration CpmConfiguration { get; set; } = new();
    public List<string> ProjectsWithCpmReferences { get; set; } = new();
    public bool HasErrors { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ExistingCpmPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty; // PackageVersion or GlobalPackageReference
}

public class CpmConfiguration
{
    public bool ManagePackageVersionsCentrally { get; set; }
    public bool CentralPackageTransitivePinningEnabled { get; set; }
    public bool EnablePackageVersionOverride { get; set; }
}