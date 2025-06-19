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

            var projectReferences = MigrateProjectReferences(legacyProject, result);
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

    private XElement MigrateProjectReferences(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectReferences = legacyProject.Items.Where(i => i.ItemType == "ProjectReference");
        var projectDir = Path.GetDirectoryName(legacyProject.FullPath)!;

        foreach (var reference in projectReferences)
        {
            var includeValue = reference.EvaluatedInclude;
            var resolvedPath = ResolveProjectReferencePath(projectDir, includeValue, result);
            
            if (resolvedPath != includeValue)
            {
                _logger.LogInformation("Fixed project reference path: {OldPath} -> {NewPath}", includeValue, resolvedPath);
            }
            
            var element = new XElement("ProjectReference",
                new XAttribute("Include", resolvedPath));

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
            .Where(i => i.ItemType == "Content")
            .Where(i =>
            {
                var path = i.EvaluatedInclude;
                var fileName = Path.GetFileName(path);
                
                // Skip DLL files from packages or NuGet cache
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    (path.Contains(".nuget", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("packages", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
                
                // Skip items from NuGet packages or .nuget folders
                return !path.Contains(".nuget", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains("packages", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"/Users/", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"\.nuget\", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"/.nuget/", StringComparison.OrdinalIgnoreCase);
            });

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
                _logger.LogDebug("Migrated Content item as None: {Include}", item.EvaluatedInclude);
            }
        }
        
        var noneItems = legacyProject.Items
            .Where(i => i.ItemType == "None")
            .Where(i => 
            {
                var copyToOutput = i.GetMetadataValue("CopyToOutputDirectory");
                return !string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never";
            })
            .Where(i =>
            {
                var path = i.EvaluatedInclude;
                var fileName = Path.GetFileName(path);
                
                // Skip DLL files from packages or NuGet cache
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    (path.Contains(".nuget", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("packages", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
                
                // Skip items from NuGet packages or .nuget folders
                return !path.Contains(".nuget", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains("packages", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"/Users/", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"\.nuget\", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains(@"/.nuget/", StringComparison.OrdinalIgnoreCase);
            });

        foreach (var item in noneItems)
        {
            var element = new XElement("None",
                new XAttribute("Include", item.EvaluatedInclude));

            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
            CopyMetadata(item, element, "CopyToOutputDirectory");

            itemGroup.Add(element);
            _logger.LogDebug("Migrated None item: {Include}", item.EvaluatedInclude);
        }

        return itemGroup;
    }
    
    private XElement MigrateOtherItems(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // Migrate assembly references that are not part of the implicit framework references
        var assemblyReferences = legacyProject.Items.Where(i => i.ItemType == "Reference");
        foreach (var reference in assemblyReferences)
        {
            var referenceName = reference.EvaluatedInclude;
            
            // Extract just the assembly name without version info
            var assemblyName = referenceName.Split(',')[0].Trim();
            
            // Skip references that are implicitly included in the framework
            var implicitFrameworkReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "System.Core", "System.Data", "System.Xml", "System.Xml.Linq",
                "Microsoft.CSharp", "System.Net.Http", "System.IO.Compression.FileSystem"
            };
            
            if (implicitFrameworkReferences.Contains(assemblyName))
            {
                _logger.LogDebug("Skipping implicit framework reference: {Reference}", assemblyName);
                continue;
            }
            
            // Framework extensions and special references that need to be preserved
            var frameworkExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Windows.Forms", "System.Drawing", "System.Web", "System.Web.Extensions",
                "Microsoft.VisualStudio.QualityTools.UnitTestFramework", "Microsoft.VisualStudio.TestTools.UnitTesting",
                "System.Configuration", "System.ServiceModel", "System.Runtime.Serialization",
                "System.ComponentModel.DataAnnotations", "System.Web.Http", "System.Web.Mvc"
            };
            
            if (frameworkExtensions.Contains(assemblyName) || 
                assemblyName.StartsWith("Microsoft.VisualStudio", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Windows", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.ServiceModel", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.ComponentModel", StringComparison.OrdinalIgnoreCase))
            {
                var element = new XElement("Reference",
                    new XAttribute("Include", assemblyName));
                
                // Copy important metadata
                var hintPath = reference.GetMetadataValue("HintPath");
                if (!string.IsNullOrEmpty(hintPath))
                {
                    element.Add(new XElement("HintPath", hintPath));
                }
                
                var privateValue = reference.GetMetadataValue("Private");
                if (!string.IsNullOrEmpty(privateValue))
                {
                    element.Add(new XElement("Private", privateValue));
                }
                
                var specificVersion = reference.GetMetadataValue("SpecificVersion");
                if (!string.IsNullOrEmpty(specificVersion))
                {
                    element.Add(new XElement("SpecificVersion", specificVersion));
                }
                
                itemGroup.Add(element);
                _logger.LogInformation("Preserved framework extension reference: {Reference}", assemblyName);
            }
            else if (!string.IsNullOrEmpty(reference.GetMetadataValue("HintPath")))
            {
                // This is likely a third-party assembly with a HintPath - preserve it
                var element = new XElement("Reference",
                    new XAttribute("Include", assemblyName));
                
                var hintPath = reference.GetMetadataValue("HintPath");
                element.Add(new XElement("HintPath", hintPath));
                
                var privateValue = reference.GetMetadataValue("Private");
                if (!string.IsNullOrEmpty(privateValue))
                {
                    element.Add(new XElement("Private", privateValue));
                }
                
                itemGroup.Add(element);
                _logger.LogInformation("Preserved assembly reference with HintPath: {Reference}", assemblyName);
            }
        }
        
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
        // Remove ALL imports - SDK-style projects should not need any of the legacy imports
        foreach (var import in legacyProject.Xml.Imports)
        {
            var importPath = import.Project;
            result.RemovedElements.Add($"Import: {importPath}");
            _logger.LogDebug("Removed import: {Import}", importPath);
            
            // Add a warning if this looks like a custom project import
            if (!string.IsNullOrEmpty(importPath) && 
                !importPath.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                !importPath.Contains("MSBuild", StringComparison.OrdinalIgnoreCase) &&
                !importPath.Contains("VisualStudio", StringComparison.OrdinalIgnoreCase) &&
                (importPath.StartsWith(".") || !importPath.Contains("$(")))
            {
                result.Warnings.Add($"Removed import '{importPath}' - if this is a custom project import, you may need to add it back manually");
            }
        }
        
        foreach (var target in legacyProject.Xml.Targets)
        {
            // Check if this is a common MSBuild target that should be removed
            var commonTargets = new[] 
            { 
                "BeforeBuild", "AfterBuild", "BeforeCompile", "AfterCompile",
                "BeforePublish", "AfterPublish", "BeforeResolveReferences", "AfterResolveReferences",
                "EnsureNuGetPackageBuildImports", "BuildPackage", "BeforeClean", "AfterClean"
            };
            
            if (commonTargets.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.RemovedElements.Add($"Target: {target.Name}");
                result.Warnings.Add($"Removed '{target.Name}' target - use SDK extensibility points instead (e.g., BeforeTargets/AfterTargets attributes)");
                _logger.LogDebug("Removed MSBuild target: {Target}", target.Name);
                continue;
            }
            
            // For any other targets, add a strong warning but preserve them
            var targetElement = new XElement("Target", new XAttribute("Name", target.Name));
            
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                targetElement.Add(new XAttribute("BeforeTargets", target.BeforeTargets));
            if (!string.IsNullOrEmpty(target.AfterTargets))
                targetElement.Add(new XAttribute("AfterTargets", target.AfterTargets));
            if (!string.IsNullOrEmpty(target.DependsOnTargets))
                targetElement.Add(new XAttribute("DependsOnTargets", target.DependsOnTargets));
            if (!string.IsNullOrEmpty(target.Condition))
                targetElement.Add(new XAttribute("Condition", target.Condition));
            
            projectElement.Add(targetElement);
            result.Warnings.Add($"Target '{target.Name}' was preserved but should be reviewed for SDK-style compatibility");
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
    
    private string ResolveProjectReferencePath(string currentProjectDir, string referencePath, MigrationResult result)
    {
        try
        {
            // First try the path as-is
            var fullPath = Path.GetFullPath(Path.Combine(currentProjectDir, referencePath));
            if (File.Exists(fullPath))
            {
                return referencePath;
            }
            
            // Get the filename to search for
            var fileName = Path.GetFileName(referencePath);
            
            // Try common patterns for fixing paths
            
            // 1. Try looking in parent directories (up to 3 levels)
            var parentDir = currentProjectDir;
            for (int i = 0; i < 3; i++)
            {
                parentDir = Path.GetDirectoryName(parentDir);
                if (string.IsNullOrEmpty(parentDir))
                    break;
                    
                var foundFiles = Directory.GetFiles(parentDir, fileName, SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                    
                if (foundFiles.Count == 1)
                {
                    var relativePath = Path.GetRelativePath(currentProjectDir, foundFiles[0]);
                    _logger.LogDebug("Found project reference in parent directory: {Path}", relativePath);
                    return relativePath.Replace('\\', Path.DirectorySeparatorChar);
                }
            }
            
            // 2. Try removing extra path segments (e.g., "..\..\src\Project\Project.csproj" -> "..\Project\Project.csproj")
            var pathParts = referencePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length > 2)
            {
                // Try removing intermediate directories
                for (int skip = 1; skip < pathParts.Length - 1; skip++)
                {
                    var testPath = Path.Combine(
                        string.Join(Path.DirectorySeparatorChar.ToString(), pathParts.Take(pathParts.Length - skip - 1)),
                        pathParts.Last()
                    );
                    
                    fullPath = Path.GetFullPath(Path.Combine(currentProjectDir, testPath));
                    if (File.Exists(fullPath))
                    {
                        _logger.LogDebug("Fixed project reference by simplifying path: {OldPath} -> {NewPath}", referencePath, testPath);
                        return testPath;
                    }
                }
            }
            
            // 3. If the reference contains solution folder paths, try to resolve without them
            if (referencePath.Contains("$(") || referencePath.Contains("%"))
            {
                result.Warnings.Add($"Project reference '{referencePath}' contains variables that cannot be resolved");
                _logger.LogWarning("Project reference contains variables that cannot be resolved: {Path}", referencePath);
            }
            
            result.Warnings.Add($"Could not resolve project reference path: '{referencePath}' - please verify manually");
            _logger.LogWarning("Could not resolve project reference path: {Path}", referencePath);
            return referencePath; // Return original if we can't fix it
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving project reference path: {Path}", referencePath);
            return referencePath;
        }
    }
    
}