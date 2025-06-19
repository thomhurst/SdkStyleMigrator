using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class NuSpecExtractor : INuSpecExtractor
{
    private readonly ILogger<NuSpecExtractor> _logger;
    private static readonly XNamespace NuSpecNamespace = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
    private static readonly XNamespace NuSpecNamespace2011 = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";
    private static readonly XNamespace NuSpecNamespace2012 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";
    private static readonly XNamespace NuSpecNamespace2013 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    public NuSpecExtractor(ILogger<NuSpecExtractor> logger)
    {
        _logger = logger;
    }

    public Task<string?> FindNuSpecFileAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
            return Task.FromResult<string?>(null);

        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        // Look for .nuspec files in common patterns
        var patterns = new[]
        {
            $"{projectName}.nuspec",           // ProjectName.nuspec
            "*.nuspec",                        // Any .nuspec in project directory
            $"../{projectName}.nuspec",        // One level up
            $"../nuget/{projectName}.nuspec",  // Common nuget folder
            $"nuget/{projectName}.nuspec"      // nuget subfolder
        };

        foreach (var pattern in patterns)
        {
            var searchPath = Path.Combine(projectDir, pattern);
            var directory = Path.GetDirectoryName(searchPath) ?? projectDir;
            var fileName = Path.GetFileName(searchPath);
            
            if (Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    var nuspecFile = files[0];
                    _logger.LogInformation("Found .nuspec file for project {Project}: {NuSpec}", projectPath, nuspecFile);
                    return Task.FromResult<string?>(nuspecFile);
                }
            }
        }

        _logger.LogDebug("No .nuspec file found for project {Project}", projectPath);
        return Task.FromResult<string?>(null);
    }

    public async Task<NuSpecMetadata?> ExtractMetadataAsync(string nuspecPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(nuspecPath))
        {
            _logger.LogWarning("NuSpec file not found: {Path}", nuspecPath);
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(nuspecPath, cancellationToken);
            var doc = XDocument.Parse(content);
            var root = doc.Root;
            
            if (root == null)
                return null;

            // Determine the namespace
            var ns = root.Name.Namespace;
            
            var metadataElement = root.Element(ns + "metadata");
            if (metadataElement == null)
                return null;

            var metadata = new NuSpecMetadata
            {
                Id = GetElementValue(metadataElement, ns, "id"),
                Version = GetElementValue(metadataElement, ns, "version"),
                Authors = GetElementValue(metadataElement, ns, "authors"),
                Owners = GetElementValue(metadataElement, ns, "owners"),
                Description = GetElementValue(metadataElement, ns, "description"),
                ReleaseNotes = GetElementValue(metadataElement, ns, "releaseNotes"),
                Summary = GetElementValue(metadataElement, ns, "summary"),
                Language = GetElementValue(metadataElement, ns, "language"),
                ProjectUrl = GetElementValue(metadataElement, ns, "projectUrl"),
                IconUrl = GetElementValue(metadataElement, ns, "iconUrl"),
                Icon = GetElementValue(metadataElement, ns, "icon"),
                LicenseUrl = GetElementValue(metadataElement, ns, "licenseUrl"),
                RequireLicenseAcceptance = GetBoolValue(metadataElement, ns, "requireLicenseAcceptance"),
                Tags = GetElementValue(metadataElement, ns, "tags"),
                Copyright = GetElementValue(metadataElement, ns, "copyright"),
                Title = GetElementValue(metadataElement, ns, "title"),
                DevelopmentDependency = GetBoolValue(metadataElement, ns, "developmentDependency"),
                Serviceable = GetBoolValue(metadataElement, ns, "serviceable")
            };

            // Extract license element (newer format)
            var licenseElement = metadataElement.Element(ns + "license");
            if (licenseElement != null)
            {
                var licenseType = licenseElement.Attribute("type")?.Value;
                if (licenseType == "expression")
                {
                    metadata.License = licenseElement.Value;
                }
                else if (licenseType == "file")
                {
                    metadata.License = $"LICENSE_FILE:{licenseElement.Value}";
                }
            }

            // Extract repository metadata
            var repositoryElement = metadataElement.Element(ns + "repository");
            if (repositoryElement != null)
            {
                metadata.RepositoryType = repositoryElement.Attribute("type")?.Value;
                metadata.RepositoryUrl = repositoryElement.Attribute("url")?.Value;
                metadata.RepositoryBranch = repositoryElement.Attribute("branch")?.Value;
                metadata.RepositoryCommit = repositoryElement.Attribute("commit")?.Value;
            }

            // Extract dependencies
            var dependenciesElement = metadataElement.Element(ns + "dependencies");
            if (dependenciesElement != null)
            {
                ExtractDependencies(dependenciesElement, ns, metadata);
            }

            // Extract files
            var filesElement = root.Element(ns + "files");
            if (filesElement != null)
            {
                ExtractFiles(filesElement, ns, metadata);
            }

            // Extract contentFiles (PackageReference format)
            var contentFilesElement = metadataElement.Element(ns + "contentFiles");
            if (contentFilesElement != null)
            {
                ExtractContentFiles(contentFilesElement, ns, metadata);
            }

            _logger.LogInformation("Successfully extracted metadata from {NuSpec}", nuspecPath);
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract metadata from {NuSpec}", nuspecPath);
            return null;
        }
    }

    private string? GetElementValue(XElement parent, XNamespace ns, string elementName)
    {
        return parent.Element(ns + elementName)?.Value?.Trim();
    }

    private bool? GetBoolValue(XElement parent, XNamespace ns, string elementName)
    {
        var value = GetElementValue(parent, ns, elementName);
        if (string.IsNullOrEmpty(value))
            return null;
        
        return bool.TryParse(value, out var result) ? result : null;
    }

    private void ExtractDependencies(XElement dependenciesElement, XNamespace ns, NuSpecMetadata metadata)
    {
        // Handle both grouped and ungrouped dependencies
        var groups = dependenciesElement.Elements(ns + "group");
        
        if (groups.Any())
        {
            // Grouped by target framework
            foreach (var group in groups)
            {
                var targetFramework = group.Attribute("targetFramework")?.Value;
                foreach (var dep in group.Elements(ns + "dependency"))
                {
                    var dependency = new NuSpecDependency
                    {
                        Id = dep.Attribute("id")?.Value ?? string.Empty,
                        Version = dep.Attribute("version")?.Value ?? string.Empty,
                        TargetFramework = targetFramework,
                        Include = dep.Attribute("include")?.Value,
                        Exclude = dep.Attribute("exclude")?.Value
                    };
                    metadata.Dependencies.Add(dependency);
                }
            }
        }
        else
        {
            // Flat list of dependencies
            foreach (var dep in dependenciesElement.Elements(ns + "dependency"))
            {
                var dependency = new NuSpecDependency
                {
                    Id = dep.Attribute("id")?.Value ?? string.Empty,
                    Version = dep.Attribute("version")?.Value ?? string.Empty,
                    Include = dep.Attribute("include")?.Value,
                    Exclude = dep.Attribute("exclude")?.Value
                };
                metadata.Dependencies.Add(dependency);
            }
        }
    }

    private void ExtractFiles(XElement filesElement, XNamespace ns, NuSpecMetadata metadata)
    {
        foreach (var fileElement in filesElement.Elements(ns + "file"))
        {
            var file = new NuSpecFile
            {
                Source = fileElement.Attribute("src")?.Value ?? string.Empty,
                Target = fileElement.Attribute("target")?.Value ?? string.Empty,
                Exclude = fileElement.Attribute("exclude")?.Value
            };
            metadata.Files.Add(file);
        }
    }

    private void ExtractContentFiles(XElement contentFilesElement, XNamespace ns, NuSpecMetadata metadata)
    {
        foreach (var fileElement in contentFilesElement.Elements(ns + "files"))
        {
            var contentFile = new NuSpecContentFile
            {
                Include = fileElement.Attribute("include")?.Value ?? string.Empty,
                Exclude = fileElement.Attribute("exclude")?.Value,
                BuildAction = fileElement.Attribute("buildAction")?.Value,
                CopyToOutput = fileElement.Attribute("copyToOutput")?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase),
                Flatten = fileElement.Attribute("flatten")?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase)
            };
            metadata.ContentFiles.Add(contentFile);
        }
    }
}