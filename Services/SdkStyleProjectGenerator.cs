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
                    
                    // Add any additional metadata (e.g., PrivateAssets)
                    foreach (var metadata in package.Metadata)
                    {
                        packageElement.Add(new XAttribute(metadata.Key, metadata.Value));
                    }
                    
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

            // Migrate compile items (handle implicit includes)
            var compileItems = MigrateCompileItems(legacyProject);
            if (compileItems.HasElements)
            {
                projectElement.Add(compileItems);
            }
            
            // Migrate WPF/WinForms specific items
            var wpfWinFormsItems = MigrateWpfWinFormsItems(legacyProject);
            if (wpfWinFormsItems.HasElements)
            {
                projectElement.Add(wpfWinFormsItems);
            }

            // Migrate content items if necessary
            var contentItems = MigrateContentItems(legacyProject);
            if (contentItems.HasElements)
            {
                projectElement.Add(contentItems);
            }
            
            // Migrate other items (COM references, etc.)
            var otherItems = MigrateOtherItems(legacyProject, result);
            if (otherItems.HasElements)
            {
                projectElement.Add(otherItems);
            }
            
            // Migrate custom targets and imports
            MigrateCustomTargetsAndImports(legacyProject, projectElement, result);

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
        
        // Preserve important properties for compatibility
        foreach (var propName in LegacyProjectElements.PropertiesToPreserve)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
                _logger.LogDebug("Preserved property for compatibility: {PropertyName}", propName);
            }
        }

        // Log removed properties
        foreach (var property in legacyProject.Properties)
        {
            if (LegacyProjectElements.PropertiesToRemove.Contains(property.Name) || 
                LegacyProjectElements.AssemblyPropertiesToExtract.Contains(property.Name))
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

    private XElement MigrateCompileItems(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectDir = Path.GetDirectoryName(legacyProject.FullPath)!;
        
        // Get all compile items from the legacy project
        var compileItems = legacyProject.Items.Where(i => i.ItemType == "Compile").ToList();
        
        foreach (var item in compileItems)
        {
            var include = item.EvaluatedInclude;
            var extension = Path.GetExtension(include);
            
            // Skip files that are implicitly included in SDK-style projects
            if (LegacyProjectElements.ImplicitlyIncludedExtensions.Contains(extension))
            {
                // Check if it's in the project directory tree
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, include));
                if (fullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Only include if it has special metadata or is explicitly excluded
                    if (item.HasMetadata("Link") || 
                        item.HasMetadata("DependentUpon") || 
                        item.HasMetadata("AutoGen") ||
                        item.HasMetadata("DesignTime") ||
                        item.GetMetadataValue("Visible") == "false")
                    {
                        var element = new XElement("Compile", new XAttribute("Update", include));
                        CopyMetadata(item, element);
                        itemGroup.Add(element);
                    }
                    // Skip regular files that will be implicitly included
                    continue;
                }
            }
            
            // Include files outside project directory or with non-standard extensions
            var compileElement = new XElement("Compile", new XAttribute("Include", include));
            CopyMetadata(item, compileElement);
            itemGroup.Add(compileElement);
        }
        
        // Handle removed/excluded files
        var removedFiles = legacyProject.Items
            .Where(i => i.ItemType == "Compile" && i.GetMetadataValue("Exclude") == "true")
            .ToList();
            
        foreach (var item in removedFiles)
        {
            itemGroup.Add(new XElement("Compile", 
                new XAttribute("Remove", item.EvaluatedInclude)));
        }
        
        return itemGroup;
    }
    
    private XElement MigrateWpfWinFormsItems(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // Migrate WPF/WinForms specific items
        foreach (var itemType in LegacyProjectElements.WpfWinFormsItemTypes)
        {
            var items = legacyProject.Items.Where(i => i.ItemType == itemType);
            
            foreach (var item in items)
            {
                var element = new XElement(itemType, 
                    new XAttribute("Include", item.EvaluatedInclude));
                CopyMetadata(item, element);
                itemGroup.Add(element);
            }
        }
        
        return itemGroup;
    }

    private XElement MigrateContentItems(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // Convert Content items to None with CopyToOutputDirectory
        var contentItems = legacyProject.Items
            .Where(i => i.ItemType == "Content");

        foreach (var item in contentItems)
        {
            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            
            // Only include if it needs to be copied to output
            if (!string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never")
            {
                var element = new XElement("None",
                    new XAttribute("Include", item.EvaluatedInclude));
                
                element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
                CopyMetadata(item, element, "CopyToOutputDirectory");
                
                itemGroup.Add(element);
            }
        }
        
        // Also migrate None items that have CopyToOutputDirectory
        var noneItems = legacyProject.Items
            .Where(i => i.ItemType == "None")
            .Where(i => 
            {
                var copyToOutput = i.GetMetadataValue("CopyToOutputDirectory");
                return !string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never";
            });

        foreach (var item in noneItems)
        {
            var element = new XElement("None",
                new XAttribute("Include", item.EvaluatedInclude));

            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
            CopyMetadata(item, element, "CopyToOutputDirectory");

            itemGroup.Add(element);
        }

        return itemGroup;
    }
    
    private XElement MigrateOtherItems(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // Handle COM references
        var comReferences = legacyProject.Items.Where(i => i.ItemType == "COMReference");
        foreach (var comRef in comReferences)
        {
            var element = new XElement("COMReference",
                new XAttribute("Include", comRef.EvaluatedInclude));
            CopyMetadata(comRef, element);
            itemGroup.Add(element);
            
            // Add warning for manual review
            result.Warnings.Add($"COM Reference '{comRef.EvaluatedInclude}' needs manual review - COM references can be problematic in SDK-style projects");
        }
        
        // Handle EmbeddedResource items with special metadata
        var embeddedResources = legacyProject.Items
            .Where(i => i.ItemType == "EmbeddedResource")
            .Where(i => i.HasMetadata("Generator") || 
                       i.HasMetadata("LastGenOutput") ||
                       i.HasMetadata("SubType"));
                       
        foreach (var resource in embeddedResources)
        {
            var element = new XElement("EmbeddedResource",
                new XAttribute("Update", resource.EvaluatedInclude));
            CopyMetadata(resource, element);
            itemGroup.Add(element);
        }
        
        return itemGroup;
    }
    
    private void CopyMetadata(ProjectItem source, XElement target, params string[] excludeMetadata)
    {
        var metadataToSkip = new HashSet<string>(excludeMetadata, StringComparer.OrdinalIgnoreCase);
        metadataToSkip.Add("Include"); // Always skip Include as it's an attribute
        
        foreach (var metadata in source.Metadata)
        {
            if (!metadataToSkip.Contains(metadata.Name))
            {
                target.Add(new XElement(metadata.Name, metadata.EvaluatedValue));
            }
        }
    }
    
    private void MigrateCustomTargetsAndImports(Project legacyProject, XElement projectElement, MigrationResult result)
    {
        // Migrate custom imports (exclude standard ones)
        foreach (var import in legacyProject.Xml.Imports)
        {
            if (!LegacyProjectElements.ImportsToRemove.Contains(import.Project))
            {
                var importElement = new XElement("Import",
                    new XAttribute("Project", import.Project));
                    
                if (!string.IsNullOrEmpty(import.Condition))
                {
                    importElement.Add(new XAttribute("Condition", import.Condition));
                }
                
                projectElement.Add(importElement);
                _logger.LogDebug("Preserved custom import: {Import}", import.Project);
            }
        }
        
        // Migrate custom targets
        foreach (var target in legacyProject.Xml.Targets)
        {
            // Skip problematic targets unless they have complex logic
            if (LegacyProjectElements.ProblematicTargets.Contains(target.Name) && 
                !target.Children.Any())
            {
                result.Warnings.Add($"Removed empty '{target.Name}' target - consider using MSBuild SDK hooks instead");
                continue;
            }
            
            // For BeforeBuild/AfterBuild with content, add a warning
            if (LegacyProjectElements.ProblematicTargets.Contains(target.Name))
            {
                result.Warnings.Add($"Target '{target.Name}' was migrated but should be reviewed - consider using BeforeTargets/AfterTargets instead");
            }
            
            var targetElement = new XElement("Target", new XAttribute("Name", target.Name));
            
            // Copy target attributes
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                targetElement.Add(new XAttribute("BeforeTargets", target.BeforeTargets));
            if (!string.IsNullOrEmpty(target.AfterTargets))
                targetElement.Add(new XAttribute("AfterTargets", target.AfterTargets));
            if (!string.IsNullOrEmpty(target.DependsOnTargets))
                targetElement.Add(new XAttribute("DependsOnTargets", target.DependsOnTargets));
            if (!string.IsNullOrEmpty(target.Condition))
                targetElement.Add(new XAttribute("Condition", target.Condition));
            
            // Copy target content (simplified - real implementation would need to handle all task types)
            foreach (var child in target.Children)
            {
                // This is a simplified approach - in reality, we'd need to handle various task types
                result.Warnings.Add($"Target '{target.Name}' contains custom tasks that need manual review");
            }
            
            if (targetElement.HasAttributes || targetElement.HasElements)
            {
                projectElement.Add(targetElement);
            }
        }
        
        // Migrate PropertyGroups and ItemGroups with conditions
        foreach (var propertyGroup in legacyProject.Xml.PropertyGroups.Where(pg => !string.IsNullOrEmpty(pg.Condition)))
        {
            result.Warnings.Add($"Conditional PropertyGroup with condition '{propertyGroup.Condition}' needs manual review");
        }
    }
}