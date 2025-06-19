using System.Text.RegularExpressions;
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
    private readonly MigrationOptions _options;

    public SdkStyleProjectGenerator(
        ILogger<SdkStyleProjectGenerator> logger,
        IPackageReferenceMigrator packageReferenceMigrator,
        ITransitiveDependencyDetector transitiveDependencyDetector,
        MigrationOptions options)
    {
        _logger = logger;
        _packageReferenceMigrator = packageReferenceMigrator;
        _transitiveDependencyDetector = transitiveDependencyDetector;
        _options = options;
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

            var sdkProject = new XDocument();
            var projectElement = new XElement("Project");
            
            var sdk = DetermineSdk(legacyProject);
            projectElement.Add(new XAttribute("Sdk", sdk));
            
            sdkProject.Add(projectElement);

            var propertyGroup = MigrateProperties(legacyProject, result);
            if (propertyGroup.HasElements)
            {
                projectElement.Add(propertyGroup);
            }

            var packages = await _packageReferenceMigrator.MigratePackagesAsync(legacyProject, cancellationToken);
            packages = await _transitiveDependencyDetector.DetectTransitiveDependenciesAsync(packages, cancellationToken);
            
            var packagesToInclude = packages.Where(p => !p.IsTransitive).ToList();
            if (packagesToInclude.Any())
            {
                var packageGroup = new XElement("ItemGroup");
                foreach (var package in packagesToInclude)
                {
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", package.PackageId),
                        new XAttribute("Version", package.Version));
                    
                    foreach (var metadata in package.Metadata)
                    {
                        packageElement.Add(new XAttribute(metadata.Key, metadata.Value));
                    }
                    
                    packageGroup.Add(packageElement);
                }
                projectElement.Add(packageGroup);
                result.MigratedPackages.AddRange(packagesToInclude);
            }

            var projectReferences = MigrateProjectReferences(legacyProject);
            if (projectReferences.HasElements)
            {
                projectElement.Add(projectReferences);
            }

            var compileItems = MigrateCompileItems(legacyProject, result);
            if (compileItems.HasElements)
            {
                projectElement.Add(compileItems);
            }
            
            var wpfWinFormsItems = MigrateWpfWinFormsItems(legacyProject);
            if (wpfWinFormsItems.HasElements)
            {
                projectElement.Add(wpfWinFormsItems);
            }

            var contentItems = MigrateContentItems(legacyProject);
            if (contentItems.HasElements)
            {
                projectElement.Add(contentItems);
            }
            
            var otherItems = MigrateOtherItems(legacyProject, result);
            if (otherItems.HasElements)
            {
                projectElement.Add(otherItems);
            }
            
            MigrateCustomTargetsAndImports(legacyProject, projectElement, result);

            if (!_options.DryRun)
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                sdkProject.Save(outputPath);
                _logger.LogInformation("Successfully migrated project to {OutputPath}", outputPath);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would migrate project to {OutputPath}", outputPath);
                _logger.LogDebug("[DRY RUN] Generated project content:\n{Content}", sdkProject.ToString());
            }
            
            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate project {ProjectPath}", legacyProject.FullPath);
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private string DetermineSdk(Project legacyProject)
    {
        var projectPath = legacyProject.FullPath;
        
        var hasWpfItems = legacyProject.Items.Any(i => 
            i.ItemType == "ApplicationDefinition" || 
            i.ItemType == "Page" ||
            (i.ItemType == "Compile" && i.EvaluatedInclude.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)));
            
        var hasWinFormsReferences = legacyProject.Items.Any(i => 
            i.ItemType == "Reference" && 
            (i.EvaluatedInclude.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.StartsWith("System.Drawing", StringComparison.OrdinalIgnoreCase)));
             
        if (hasWpfItems || hasWinFormsReferences)
        {
            return "Microsoft.NET.Sdk.WindowsDesktop";
        }
        
        var hasWebContent = legacyProject.Items.Any(i =>
            (i.ItemType == "Content" || i.ItemType == "None") &&
            (i.EvaluatedInclude.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.EndsWith(".ascx", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.Equals("web.config", StringComparison.OrdinalIgnoreCase)));
             
        var hasWebReferences = legacyProject.Items.Any(i =>
            i.ItemType == "Reference" &&
            (i.EvaluatedInclude.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.StartsWith("Microsoft.AspNet", StringComparison.OrdinalIgnoreCase)));
             
        if (hasWebContent || hasWebReferences)
        {
            return "Microsoft.NET.Sdk.Web";
        }
        
        return "Microsoft.NET.Sdk";
    }

    private XElement MigrateProperties(Project legacyProject, MigrationResult result)
    {
        var propertyGroup = new XElement("PropertyGroup");

        var targetFramework = GetTargetFramework(legacyProject);
        if (!string.IsNullOrEmpty(targetFramework))
        {
            propertyGroup.Add(new XElement("TargetFramework", targetFramework));
        }

        var outputType = legacyProject.GetPropertyValue("OutputType");
        if (!string.IsNullOrEmpty(outputType))
        {
            propertyGroup.Add(new XElement("OutputType", outputType));
        }

        var rootNamespace = legacyProject.GetPropertyValue("RootNamespace");
        var projectName = Path.GetFileNameWithoutExtension(legacyProject.FullPath);
        if (!string.IsNullOrEmpty(rootNamespace) && rootNamespace != projectName)
        {
            propertyGroup.Add(new XElement("RootNamespace", rootNamespace));
        }

        var assemblyName = legacyProject.GetPropertyValue("AssemblyName");
        if (!string.IsNullOrEmpty(assemblyName) && assemblyName != projectName)
        {
            propertyGroup.Add(new XElement("AssemblyName", assemblyName));
        }

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
        
        foreach (var propName in LegacyProjectElements.PropertiesToPreserve)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
                _logger.LogDebug("Preserved property for compatibility: {PropertyName}", propName);
            }
        }

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
        if (!string.IsNullOrEmpty(_options.TargetFramework))
        {
            _logger.LogInformation("Using override target framework: {TargetFramework}", _options.TargetFramework);
            return _options.TargetFramework;
        }
        
        var targetFrameworkVersion = project.GetPropertyValue("TargetFrameworkVersion");
        if (string.IsNullOrEmpty(targetFrameworkVersion))
        {
            return "net48";
        }

        if (targetFrameworkVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            var version = targetFrameworkVersion.Substring(1);
            
            var tfmMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["2.0"] = "net20",
                ["3.0"] = "net30",
                ["3.5"] = "net35",
                ["4.0"] = "net40",
                ["4.5"] = "net45",
                ["4.5.1"] = "net451",
                ["4.5.2"] = "net452",
                ["4.6"] = "net46",
                ["4.6.1"] = "net461",
                ["4.6.2"] = "net462",
                ["4.7"] = "net47",
                ["4.7.1"] = "net471",
                ["4.7.2"] = "net472",
                ["4.8"] = "net48",
                ["4.8.1"] = "net481"
            };
            
            if (tfmMappings.TryGetValue(version, out var tfm))
            {
                return tfm;
            }
            
            _logger.LogWarning("Unknown TargetFrameworkVersion: {Version}, defaulting to net48", targetFrameworkVersion);
            return "net48";
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

    private XElement MigrateCompileItems(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectDir = Path.GetDirectoryName(legacyProject.FullPath)!;
        
        var compileItems = legacyProject.Items.Where(i => i.ItemType == "Compile").ToList();
        
        foreach (var item in compileItems)
        {
            var include = item.EvaluatedInclude;
            var extension = Path.GetExtension(include);
            
            // Skip AssemblyInfo files as they will be auto-generated or moved to Directory.Build.props
            if (IsAssemblyInfoFile(include))
            {
                _logger.LogDebug("Skipping AssemblyInfo file from migration: {File}", include);
                result.RemovedElements.Add($"Compile item: {include} (AssemblyInfo file)");
                continue;
            }
            
            if (LegacyProjectElements.ImplicitlyIncludedExtensions.Contains(extension))
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, include));
                if (fullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
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
                    continue;
                }
            }
            
            var compileElement = new XElement("Compile", new XAttribute("Include", include));
            CopyMetadata(item, compileElement);
            itemGroup.Add(compileElement);
        }
        
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
        
        var contentItems = legacyProject.Items
            .Where(i => i.ItemType == "Content");

        foreach (var item in contentItems)
        {
            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            
            if (!string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never")
            {
                var element = new XElement("None",
                    new XAttribute("Include", item.EvaluatedInclude));
                
                element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
                CopyMetadata(item, element, "CopyToOutputDirectory");
                
                itemGroup.Add(element);
            }
        }
        
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
        
        var comReferences = legacyProject.Items.Where(i => i.ItemType == "COMReference");
        foreach (var comRef in comReferences)
        {
            var element = new XElement("COMReference",
                new XAttribute("Include", comRef.EvaluatedInclude));
            CopyMetadata(comRef, element);
            itemGroup.Add(element);
            
            result.Warnings.Add($"COM Reference '{comRef.EvaluatedInclude}' needs manual review - COM references can be problematic in SDK-style projects");
        }
        
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
        metadataToSkip.Add("Include");
        
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
        foreach (var import in legacyProject.Xml.Imports)
        {
            var importPath = import.Project;
            
            // Check if this is a Visual Studio-specific import that should be removed
            if (LegacyProjectElements.ImportsToRemove.Contains(importPath) ||
                IsVisualStudioSpecificImport(importPath))
            {
                result.RemovedElements.Add($"Import: {importPath}");
                _logger.LogDebug("Removed Visual Studio-specific import: {Import}", importPath);
            }
            else
            {
                // For custom imports, add a warning if they reference Visual Studio paths
                if (ContainsVisualStudioPath(importPath))
                {
                    result.Warnings.Add($"Import '{importPath}' references Visual Studio-specific paths and may not work in all environments");
                }
                
                var importElement = new XElement("Import",
                    new XAttribute("Project", importPath));
                    
                if (!string.IsNullOrEmpty(import.Condition))
                {
                    importElement.Add(new XAttribute("Condition", import.Condition));
                }
                
                projectElement.Add(importElement);
                _logger.LogDebug("Preserved custom import: {Import}", importPath);
            }
        }
        
        foreach (var target in legacyProject.Xml.Targets)
        {
            if (LegacyProjectElements.ProblematicTargets.Contains(target.Name) && 
                !target.Children.Any())
            {
                result.Warnings.Add($"Removed empty '{target.Name}' target - consider using MSBuild SDK hooks instead");
                continue;
            }
            
            if (LegacyProjectElements.ProblematicTargets.Contains(target.Name))
            {
                result.Warnings.Add($"Target '{target.Name}' was migrated but should be reviewed - consider using BeforeTargets/AfterTargets instead");
            }
            
            var targetElement = new XElement("Target", new XAttribute("Name", target.Name));
            
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                targetElement.Add(new XAttribute("BeforeTargets", target.BeforeTargets));
            if (!string.IsNullOrEmpty(target.AfterTargets))
                targetElement.Add(new XAttribute("AfterTargets", target.AfterTargets));
            if (!string.IsNullOrEmpty(target.DependsOnTargets))
                targetElement.Add(new XAttribute("DependsOnTargets", target.DependsOnTargets));
            if (!string.IsNullOrEmpty(target.Condition))
                targetElement.Add(new XAttribute("Condition", target.Condition));
            
            foreach (var child in target.Children)
            {
                result.Warnings.Add($"Target '{target.Name}' contains custom tasks that need manual review");
            }
            
            if (targetElement.HasAttributes || targetElement.HasElements)
            {
                projectElement.Add(targetElement);
            }
        }
        
        foreach (var propertyGroup in legacyProject.Xml.PropertyGroups.Where(pg => !string.IsNullOrEmpty(pg.Condition)))
        {
            result.Warnings.Add($"Conditional PropertyGroup with condition '{propertyGroup.Condition}' needs manual review");
        }
    }
    
    private bool IsAssemblyInfoFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return LegacyProjectElements.AssemblyInfoFilePatterns.Any(pattern => 
            fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }
    
    private bool IsVisualStudioSpecificImport(string importPath)
    {
        if (string.IsNullOrEmpty(importPath))
            return false;
            
        // Check for common patterns of Visual Studio-specific imports
        var patterns = new[]
        {
            @"\$\(VSToolsPath\)",
            @"\$\(MSBuildExtensionsPath32\)\\Microsoft\\VisualStudio",
            @"\$\(MSBuildExtensionsPath\)\\Microsoft\\VisualStudio",
            @"Microsoft\.WebApplication\.targets",
            @"Microsoft\.TypeScript\.targets",
            @"Microsoft\.TestTools\.targets",
            @"\.nuget\\NuGet\.targets",
            @"WebApplications\\Microsoft\.WebApplication\.targets"
        };
        
        return patterns.Any(pattern => 
            Regex.IsMatch(importPath, pattern, RegexOptions.IgnoreCase));
    }
    
    private bool ContainsVisualStudioPath(string importPath)
    {
        if (string.IsNullOrEmpty(importPath))
            return false;
            
        var vsPathIndicators = new[]
        {
            "$(VSToolsPath)",
            "$(VisualStudioVersion)",
            "\\VisualStudio\\",
            "\\v10.0\\",
            "\\v11.0\\",
            "\\v12.0\\",
            "\\v14.0\\",
            "\\v15.0\\",
            "\\v16.0\\",
            "\\v17.0\\"
        };
        
        return vsPathIndicators.Any(indicator => 
            importPath.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }
}