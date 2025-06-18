using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;

namespace SdkMigrator.Services;

public class SdkStyleProjectGenerator : ISdkStyleProjectGenerator
{
    private readonly ILogger<SdkStyleProjectGenerator> _logger;
    private readonly IPackageReferenceMigrator _packageReferenceMigrator;
    private readonly ITransitiveDependencyDetector _transitiveDependencyDetector;

    public SdkStyleProjectGenerator(
        ILogger<SdkStyleProjectGenerator> logger,
        IPackageReferenceMigrator packageReferenceMigrator,
        ITransitiveDependencyDetector transitiveDependencyDetector)
    {
        _logger = logger;
        _packageReferenceMigrator = packageReferenceMigrator;
        _transitiveDependencyDetector = transitiveDependencyDetector;
    }

    public async Task<MigrationResult> GenerateSdkStyleProjectAsync(
        Project legacyProject, 
        string outputPath, 
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult
        {
            ProjectPath = legacyProject.FullPath,
            OutputPath = outputPath
        };

        try
        {
            _logger.LogInformation("Starting migration for {ProjectPath}", legacyProject.FullPath);

            // Create new SDK-style project
            var sdkProject = new XDocument();
            var projectElement = new XElement("Project");
            
            // Determine SDK type based on project extension
            var sdk = DetermineSdk(legacyProject.FullPath);
            projectElement.Add(new XAttribute("Sdk", sdk));
            
            sdkProject.Add(projectElement);

            // Migrate properties
            var propertyGroup = MigrateProperties(legacyProject, result);
            if (propertyGroup.HasElements)
            {
                projectElement.Add(propertyGroup);
            }

            // Migrate package references
            var packages = await _packageReferenceMigrator.MigratePackagesAsync(legacyProject, cancellationToken);
            packages = await _transitiveDependencyDetector.DetectTransitiveDependenciesAsync(packages, cancellationToken);
            
            // Add non-transitive packages
            var packagesToInclude = packages.Where(p => !p.IsTransitive).ToList();
            if (packagesToInclude.Any())
            {
                var packageGroup = new XElement("ItemGroup");
                foreach (var package in packagesToInclude)
                {
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", package.PackageId),
                        new XAttribute("Version", package.Version));
                    packageGroup.Add(packageElement);
                }
                projectElement.Add(packageGroup);
                result.MigratedPackages.AddRange(packagesToInclude);
            }

            // Migrate project references
            var projectReferences = MigrateProjectReferences(legacyProject);
            if (projectReferences.HasElements)
            {
                projectElement.Add(projectReferences);
            }

            // Migrate content items if necessary
            var contentItems = MigrateContentItems(legacyProject);
            if (contentItems.HasElements)
            {
                projectElement.Add(contentItems);
            }

            // Save the new project file
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            sdkProject.Save(outputPath);
            result.Success = true;
            
            _logger.LogInformation("Successfully migrated project to {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate project {ProjectPath}", legacyProject.FullPath);
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private string DetermineSdk(string projectPath)
    {
        var extension = Path.GetExtension(projectPath).ToLowerInvariant();
        return extension switch
        {
            ".csproj" => "Microsoft.NET.Sdk",
            ".vbproj" => "Microsoft.NET.Sdk",
            ".fsproj" => "Microsoft.NET.Sdk",
            _ => "Microsoft.NET.Sdk"
        };
    }

    private XElement MigrateProperties(Project legacyProject, MigrationResult result)
    {
        var propertyGroup = new XElement("PropertyGroup");

        // Essential properties
        var targetFramework = GetTargetFramework(legacyProject);
        if (!string.IsNullOrEmpty(targetFramework))
        {
            propertyGroup.Add(new XElement("TargetFramework", targetFramework));
        }

        // Optional but useful properties
        var outputType = legacyProject.GetPropertyValue("OutputType");
        if (!string.IsNullOrEmpty(outputType))
        {
            propertyGroup.Add(new XElement("OutputType", outputType));
        }

        // Migrate RootNamespace only if different from project name
        var rootNamespace = legacyProject.GetPropertyValue("RootNamespace");
        var projectName = Path.GetFileNameWithoutExtension(legacyProject.FullPath);
        if (!string.IsNullOrEmpty(rootNamespace) && rootNamespace != projectName)
        {
            propertyGroup.Add(new XElement("RootNamespace", rootNamespace));
        }

        // Migrate AssemblyName only if different from project name
        var assemblyName = legacyProject.GetPropertyValue("AssemblyName");
        if (!string.IsNullOrEmpty(assemblyName) && assemblyName != projectName)
        {
            propertyGroup.Add(new XElement("AssemblyName", assemblyName));
        }

        // Migrate other important properties
        var importantProperties = new[]
        {
            "LangVersion",
            "Nullable",
            "GenerateAssemblyInfo",
            "GenerateDocumentationFile",
            "NoWarn",
            "TreatWarningsAsErrors",
            "WarningsAsErrors",
            "DefineConstants",
            "PlatformTarget",
            "Prefer32Bit",
            "AllowUnsafeBlocks"
        };

        foreach (var propName in importantProperties)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
            }
        }

        // Log removed properties
        foreach (var property in legacyProject.Properties)
        {
            if (LegacyProjectElements.PropertiesToRemove.Contains(property.Name))
            {
                result.RemovedElements.Add($"Property: {property.Name}");
                _logger.LogDebug("Removed legacy property: {PropertyName}", property.Name);
            }
        }

        return propertyGroup;
    }

    private string GetTargetFramework(Project project)
    {
        var targetFrameworkVersion = project.GetPropertyValue("TargetFrameworkVersion");
        if (string.IsNullOrEmpty(targetFrameworkVersion))
        {
            return "net8.0"; // Default to .NET 8
        }

        // Convert from v4.5.2 to net452, v4.6.1 to net461, etc.
        if (targetFrameworkVersion.StartsWith("v"))
        {
            var version = targetFrameworkVersion.Substring(1).Replace(".", "");
            return $"net{version}";
        }

        return targetFrameworkVersion;
    }

    private XElement MigrateProjectReferences(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectReferences = legacyProject.Items.Where(i => i.ItemType == "ProjectReference");

        foreach (var reference in projectReferences)
        {
            var includeValue = reference.EvaluatedInclude;
            var element = new XElement("ProjectReference",
                new XAttribute("Include", includeValue));

            // Preserve important metadata
            var metadataToPreserve = new[] { "Name", "Private", "SpecificVersion" };
            foreach (var metadata in metadataToPreserve)
            {
                var value = reference.GetMetadataValue(metadata);
                if (!string.IsNullOrEmpty(value))
                {
                    element.Add(new XElement(metadata, value));
                }
            }

            itemGroup.Add(element);
        }

        return itemGroup;
    }

    private XElement MigrateContentItems(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // In SDK-style projects, most content is included automatically
        // Only migrate items that need special handling
        var contentItems = legacyProject.Items
            .Where(i => i.ItemType == "Content" || i.ItemType == "None")
            .Where(i => 
            {
                var copyToOutput = i.GetMetadataValue("CopyToOutputDirectory");
                return !string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never";
            });

        foreach (var item in contentItems)
        {
            var element = new XElement(item.ItemType,
                new XAttribute("Include", item.EvaluatedInclude));

            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            if (!string.IsNullOrEmpty(copyToOutput))
            {
                element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
            }

            itemGroup.Add(element);
        }

        return itemGroup;
    }
}