using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;
using System.Xml.Linq;

namespace SdkMigrator.Services;

/// <summary>
/// Clean implementation of SDK-style project generator following SOLID principles.
/// This replaces the previous 2300-line God class with a focused implementation.
/// </summary>
public class CleanSdkStyleProjectGenerator : ISdkStyleProjectGenerator
{
    private readonly ILogger<CleanSdkStyleProjectGenerator> _logger;
    private readonly IProjectParser _projectParser;
    private readonly IPackageReferenceMigrator _packageMigrator;
    private readonly ITransitiveDependencyDetector _transitiveDepsDetector;
    private readonly IAssemblyInfoExtractor _assemblyInfoExtractor;
    private readonly IAuditService _auditService;
    private readonly IDirectoryBuildPropsReader _directoryBuildPropsReader;
    private readonly IMSBuildArtifactDetector _artifactDetector;
    private readonly IAssemblyReferenceConverter _assemblyReferenceConverter;

    public CleanSdkStyleProjectGenerator(
        ILogger<CleanSdkStyleProjectGenerator> logger,
        IProjectParser projectParser,
        IPackageReferenceMigrator packageMigrator,
        ITransitiveDependencyDetector transitiveDepsDetector,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IAuditService auditService,
        IDirectoryBuildPropsReader directoryBuildPropsReader,
        IMSBuildArtifactDetector artifactDetector,
        IAssemblyReferenceConverter assemblyReferenceConverter)
    {
        _logger = logger;
        _projectParser = projectParser;
        _packageMigrator = packageMigrator;
        _transitiveDepsDetector = transitiveDepsDetector;
        _assemblyInfoExtractor = assemblyInfoExtractor;
        _auditService = auditService;
        _directoryBuildPropsReader = directoryBuildPropsReader;
        _artifactDetector = artifactDetector;
        _assemblyReferenceConverter = assemblyReferenceConverter;
    }

    public async Task<MigrationResult> GenerateSdkStyleProjectAsync(
        Project legacyProject,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult
        {
            ProjectPath = outputPath,
            Success = false
        };

        try
        {
            _logger.LogInformation("Generating SDK-style project for: {ProjectPath}", legacyProject.FullPath);

            // Check for inherited properties from Directory.Build.props
            var inheritedProperties = _directoryBuildPropsReader.GetInheritedProperties(outputPath);
            var centrallyManagedPackages = _directoryBuildPropsReader.GetCentrallyManagedPackages(outputPath);
            var hasDirectoryBuildTargets = _directoryBuildPropsReader.HasDirectoryBuildTargets(outputPath);

            if (inheritedProperties.Any())
            {
                _logger.LogInformation("Found {Count} inherited properties from Directory.Build.props", inheritedProperties.Count);
            }

            // Create the root project element
            var projectElement = new XElement("Project");

            // Determine and set SDK
            var sdkType = DetermineSdkType(legacyProject);
            projectElement.Add(new XAttribute("Sdk", sdkType));

            // Create main property group
            var mainPropertyGroup = new XElement("PropertyGroup");
            projectElement.Add(mainPropertyGroup);

            // Migrate basic properties (skip those already in Directory.Build.props)
            MigrateBasicProperties(legacyProject, mainPropertyGroup, inheritedProperties);

            // Handle AssemblyInfo to prevent conflicts
            HandleAssemblyInfo(legacyProject, mainPropertyGroup, inheritedProperties);

            // Migrate package references
            await MigratePackageReferencesAsync(legacyProject, projectElement, centrallyManagedPackages, cancellationToken);

            // Migrate project references
            MigrateProjectReferences(legacyProject, projectElement);

            // Migrate COM references
            MigrateCOMReferences(legacyProject, projectElement);

            // Migrate compile items (if needed)
            MigrateCompileItems(legacyProject, projectElement);

            // Add excluded compile items
            AddExcludedCompileItems(legacyProject, projectElement);

            // Migrate content and resources
            MigrateContentAndResources(legacyProject, projectElement);

            // Migrate WPF/WinForms specific items
            MigrateDesignerItems(legacyProject, projectElement);

            // Migrate custom item types
            MigrateCustomItemTypes(legacyProject, projectElement);

            // Migrate InternalsVisibleTo from AssemblyInfo
            await MigrateInternalsVisibleToAsync(legacyProject, projectElement, cancellationToken);

            // Migrate custom targets and build events
            MigrateCustomTargets(legacyProject, projectElement);

            // Save the project
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                projectElement);

            doc.Save(outputPath);

            result.Success = true;
            _logger.LogInformation("Successfully generated SDK-style project at: {OutputPath}", outputPath);

            // Log the migration
            await _auditService.LogFileCreationAsync(new FileCreationAudit
            {
                FilePath = outputPath,
                FileHash = "",
                FileSize = new FileInfo(outputPath).Length,
                CreationType = "SDK-style project generation"
            }, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate SDK-style project");
            result.Success = false;
            result.Errors.Add($"Migration failed: {ex.Message}");
            return result;
        }
    }

    private string DetermineSdkType(Project project)
    {
        var targetFramework = ConvertTargetFramework(project);

        // For .NET Framework projects, always use the default SDK
        if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft.NET.Sdk";
        }

        var projectTypeGuids = project.Properties
            .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;

        if (!string.IsNullOrEmpty(projectTypeGuids))
        {
            // Web project
            if (projectTypeGuids.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.Web";

            // Blazor WebAssembly (only for .NET Core 3.0+)
            if (projectTypeGuids.Contains("{A9ACE9BB-CECE-4E62-9AA4-C7E7C5BD2124}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.BlazorWebAssembly";
        }

        // Check for WPF/WinForms by items
        var hasWpfItems = project.Items.Any(i => i.ItemType == "ApplicationDefinition" || i.ItemType == "Page");
        var hasWinFormsItems = project.Items.Any(i =>
            i.ItemType == "Compile" &&
            i.HasMetadata("SubType") &&
            (i.GetMetadataValue("SubType") == "Form" || i.GetMetadataValue("SubType") == "UserControl"));

        if (hasWpfItems || hasWinFormsItems)
        {
            // Microsoft.NET.Sdk.WindowsDesktop is only for .NET Core 3.x
            // .NET 5+ uses Microsoft.NET.Sdk with UseWPF/UseWindowsForms
            // .NET Framework uses Microsoft.NET.Sdk (already handled above)
            if (targetFramework.StartsWith("netcoreapp3", StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft.NET.Sdk.WindowsDesktop";
            }
        }

        return "Microsoft.NET.Sdk";
    }

    private void MigrateBasicProperties(Project project, XElement propertyGroup, Dictionary<string, string> inheritedProperties)
    {
        // Helper to add property only if not inherited
        void AddPropertyIfNotInherited(string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (inheritedProperties.TryGetValue(name, out var inheritedValue) &&
                inheritedValue.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping property {Name}={Value} (inherited from Directory.Build.props)", name, value);
                return;
            }

            propertyGroup.Add(new XElement(name, value));
        }

        // Target framework
        var targetFramework = ConvertTargetFramework(project);
        AddPropertyIfNotInherited("TargetFramework", targetFramework);

        // Output type
        var outputType = project.Properties
            .FirstOrDefault(p => p.Name == "OutputType")?.EvaluatedValue;
        AddPropertyIfNotInherited("OutputType", outputType ?? "");

        // Assembly name
        var assemblyName = project.Properties
            .FirstOrDefault(p => p.Name == "AssemblyName")?.EvaluatedValue;
        AddPropertyIfNotInherited("AssemblyName", assemblyName ?? "");

        // Root namespace
        var rootNamespace = project.Properties
            .FirstOrDefault(p => p.Name == "RootNamespace")?.EvaluatedValue;
        if (!string.IsNullOrEmpty(rootNamespace) && rootNamespace != assemblyName)
        {
            AddPropertyIfNotInherited("RootNamespace", rootNamespace);
        }

        // Language version
        var langVersion = project.Properties
            .FirstOrDefault(p => p.Name == "LangVersion")?.EvaluatedValue;
        AddPropertyIfNotInherited("LangVersion", langVersion ?? "");

        // Nullable - only add if not inherited and applicable
        if (targetFramework?.StartsWith("net") == true &&
            int.TryParse(targetFramework.Substring(3, 1), out var version) && version >= 6 &&
            !inheritedProperties.ContainsKey("Nullable"))
        {
            propertyGroup.Add(new XElement("Nullable", "enable"));
        }

        // Strong naming properties
        MigrateStrongNaming(project, propertyGroup, inheritedProperties);

        // For .NET 5+ WPF/WinForms projects (not .NET Framework)
        if (targetFramework?.StartsWith("net") == true &&
            !targetFramework.Contains(".") &&
            !targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
        {
            var hasWpfItems = project.Items.Any(i => i.ItemType == "ApplicationDefinition" || i.ItemType == "Page");
            var hasWinFormsItems = project.Items.Any(i =>
                i.ItemType == "Compile" &&
                i.HasMetadata("SubType") &&
                (i.GetMetadataValue("SubType") == "Form" || i.GetMetadataValue("SubType") == "UserControl"));

            if (hasWpfItems)
                propertyGroup.Add(new XElement("UseWPF", "true"));
            if (hasWinFormsItems)
                propertyGroup.Add(new XElement("UseWindowsForms", "true"));
        }
    }

    private string ConvertTargetFramework(Project project)
    {
        var targetFrameworkVersion = project.Properties
            .FirstOrDefault(p => p.Name == "TargetFrameworkVersion")?.EvaluatedValue;

        if (string.IsNullOrEmpty(targetFrameworkVersion))
            return "net8.0";

        // Remove 'v' prefix and convert
        var version = targetFrameworkVersion.TrimStart('v');

        // .NET Framework 4.x
        if (version.StartsWith("4."))
        {
            return $"net{version.Replace(".", "")}";
        }

        // .NET Core 2.x, 3.x
        if (version.StartsWith("2.") || version.StartsWith("3."))
        {
            return $"netcoreapp{version}";
        }

        // .NET 5+
        if (int.TryParse(version.Split('.')[0], out var majorVersion) && majorVersion >= 5)
        {
            return $"net{version}";
        }

        return "net8.0";
    }

    private async Task MigratePackageReferencesAsync(Project project, XElement projectElement, HashSet<string> centrallyManagedPackages, CancellationToken cancellationToken)
    {
        // Determine the target framework early, as it's needed for assembly-to-package conversion
        var targetFramework = ConvertTargetFramework(project);

        // Collect all package references from various sources
        var allPackageReferences = new HashSet<Models.PackageReference>(new PackageReferenceComparer());

        // 1. Get packages from packages.config / existing PackageReference items
        var existingPackages = await _packageMigrator.MigratePackagesAsync(project, cancellationToken);
        foreach (var pkg in existingPackages)
        {
            allPackageReferences.Add(pkg);
        }

        // 2. Get packages from legacy assembly references (framework-aware conversion)
        var conversionResult = await _assemblyReferenceConverter.ConvertReferencesAsync(project, targetFramework, existingPackages, cancellationToken);
        foreach (var pkg in conversionResult.PackageReferences)
        {
            allPackageReferences.Add(pkg);
        }

        // Log any warnings from the conversion
        foreach (var warning in conversionResult.Warnings)
        {
            _logger.LogWarning(warning);
        }

        _logger.LogInformation("Combined {ExistingCount} existing packages with {ConvertedCount} packages from assembly references, total unique: {TotalCount}",
            existingPackages.Count(), conversionResult.PackageReferences.Count, allPackageReferences.Count);

        if (allPackageReferences.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var package in allPackageReferences)
            {
                var packageRef = new XElement("PackageReference",
                    new XAttribute("Include", package.PackageId));

                // Only add version if not centrally managed
                if (!centrallyManagedPackages.Contains(package.PackageId))
                {
                    packageRef.Add(new XAttribute("Version", package.Version ?? "*"));
                }
                else
                {
                    _logger.LogDebug("Package {PackageId} is centrally managed, omitting version", package.PackageId);
                }

                itemGroup.Add(packageRef);
            }

            projectElement.Add(itemGroup);
        }

        // Migrate unconverted references
        if (conversionResult.UnconvertedReferences.Any())
        {
            MigrateUnconvertedReferences(conversionResult.UnconvertedReferences, projectElement);
        }
    }

    private void MigrateProjectReferences(Project project, XElement projectElement)
    {
        var projectRefs = project.Items
            .Where(i => i.ItemType == "ProjectReference")
            .ToList();

        if (projectRefs.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var projRef in projectRefs)
            {
                var element = new XElement("ProjectReference",
                    new XAttribute("Include", projRef.EvaluatedInclude));

                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
        }
    }

    private void MigrateCompileItems(Project project, XElement projectElement)
    {
        var compileItems = project.Items
            .Where(i => i.ItemType == "Compile")
            .Where(i => !i.EvaluatedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) // Exclude ALL .cs files
            .Where(i => !IsAssemblyInfoFile(i.EvaluatedInclude)) // Exclude AssemblyInfo files
            .ToList();

        if (compileItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var item in compileItems)
            {
                var element = new XElement("Compile",
                    new XAttribute("Include", item.EvaluatedInclude));

                PreserveMetadata(item, element);
                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
        }
        
        // Handle .cs files with metadata using Update items
        MigrateCsFilesWithMetadata(project, projectElement);
    }

    private void MigrateCsFilesWithMetadata(Project project, XElement projectElement)
    {
        // Find .cs files that have metadata that needs to be preserved
        var csFilesWithMetadata = project.Items
            .Where(i => i.ItemType == "Compile")
            .Where(i => i.EvaluatedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(i => !IsAssemblyInfoFile(i.EvaluatedInclude)) // Exclude AssemblyInfo files
            .Where(i => i.HasMetadata("DependentUpon") || 
                       i.HasMetadata("SubType") ||
                       i.HasMetadata("Generator") ||
                       i.HasMetadata("LastGenOutput") ||
                       i.HasMetadata("DesignTime") ||
                       i.HasMetadata("AutoGen") ||
                       i.HasMetadata("CustomToolNamespace") ||
                       i.HasMetadata("Link")) // Linked files need explicit inclusion
            .ToList();

        if (csFilesWithMetadata.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var item in csFilesWithMetadata)
            {
                // Linked files need Include (they're outside project directory)
                // Other files with metadata use Update (they're auto-included by SDK)
                var attributeName = item.HasMetadata("Link") ? "Include" : "Update";
                var element = new XElement("Compile",
                    new XAttribute(attributeName, item.EvaluatedInclude));

                PreserveMetadata(item, element);
                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
        }
    }

    private bool IsAssemblyInfoFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return LegacyProjectElements.AssemblyInfoFilePatterns
            .Any(pattern => fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void MigrateContentAndResources(Project project, XElement projectElement)
    {
        var contentItems = project.Items
            .Where(i => i.ItemType == "Content" ||
                       i.ItemType == "None" ||
                       i.ItemType == "EmbeddedResource")
            .Where(i => !_artifactDetector.IsItemArtifact(i.ItemType, i.EvaluatedInclude)) // Filter out MSBuild artifacts
            .ToList();

        if (contentItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            var removeItemGroup = new XElement("ItemGroup");
            var hasItems = false;
            var hasRemoveItems = false;

            foreach (var item in contentItems)
            {
                // Check if this file is automatically included by the SDK
                if (IsAutomaticallyIncludedBySdk(item))
                {
                    // Check if the item has custom metadata that needs to be preserved
                    if (HasCustomMetadata(item))
                    {
                        // We need to remove the auto-included version and re-add with metadata
                        var removeElement = new XElement(item.ItemType,
                            new XAttribute("Remove", item.EvaluatedInclude));
                        removeItemGroup.Add(removeElement);
                        hasRemoveItems = true;

                        // Now add it back with the custom metadata
                        var element = new XElement(item.ItemType,
                            new XAttribute("Include", item.EvaluatedInclude));
                        PreserveMetadata(item, element);
                        itemGroup.Add(element);
                        hasItems = true;

                        _logger.LogDebug("Removing and re-adding SDK-default file with custom metadata: {File}", item.EvaluatedInclude);
                    }
                    else
                    {
                        // Skip entirely - SDK will handle it
                        _logger.LogDebug("Skipping SDK-default content file: {File}", item.EvaluatedInclude);
                    }
                }
                else
                {
                    // Not auto-included, so we need to explicitly include it
                    var element = new XElement(item.ItemType,
                        new XAttribute("Include", item.EvaluatedInclude));

                    PreserveMetadata(item, element);
                    itemGroup.Add(element);
                    hasItems = true;
                }
            }

            // Add Remove items first if any
            if (hasRemoveItems)
            {
                projectElement.Add(removeItemGroup);
            }

            // Then add Include items
            if (hasItems)
            {
                projectElement.Add(itemGroup);
            }
        }
    }

    private bool HasCustomMetadata(ProjectItem item)
    {
        // List of metadata that indicates custom behavior
        var customMetadata = new[]
        {
            "CopyToOutputDirectory",
            "CopyToPublishDirectory",
            "Link",
            "DependentUpon",
            "Generator",
            "LastGenOutput",
            "CustomToolNamespace",
            "SubType",
            "DesignTime",
            "AutoGen",
            "DesignTimeSharedInput",
            "Private"
        };

        foreach (var metadata in customMetadata)
        {
            if (item.HasMetadata(metadata) && !string.IsNullOrEmpty(item.GetMetadataValue(metadata)))
            {
                return true;
            }
        }

        return false;
    }

    private void MigrateCustomTargets(Project project, XElement projectElement)
    {
        // Migrate pre/post build events
        var preBuild = project.Properties
            .FirstOrDefault(p => p.Name == "PreBuildEvent")?.EvaluatedValue;
        var postBuild = project.Properties
            .FirstOrDefault(p => p.Name == "PostBuildEvent")?.EvaluatedValue;

        if (!string.IsNullOrWhiteSpace(preBuild) || !string.IsNullOrWhiteSpace(postBuild))
        {
            if (!string.IsNullOrWhiteSpace(preBuild))
            {
                var target = new XElement("Target",
                    new XAttribute("Name", "PreBuild"),
                    new XAttribute("BeforeTargets", "PreBuildEvent"),
                    new XElement("Exec", new XAttribute("Command", preBuild)));

                projectElement.Add(target);
            }

            if (!string.IsNullOrWhiteSpace(postBuild))
            {
                var target = new XElement("Target",
                    new XAttribute("Name", "PostBuild"),
                    new XAttribute("AfterTargets", "PostBuildEvent"),
                    new XElement("Exec", new XAttribute("Command", postBuild)));

                projectElement.Add(target);
            }
        }
    }

    private void HandleAssemblyInfo(Project project, XElement propertyGroup, Dictionary<string, string> inheritedProperties)
    {
        // Check if GenerateAssemblyInfo is already set in Directory.Build.props
        if (inheritedProperties.ContainsKey("GenerateAssemblyInfo"))
        {
            _logger.LogDebug("GenerateAssemblyInfo already set in Directory.Build.props");
            return;
        }

        var projectDir = Path.GetDirectoryName(project.FullPath)!;
        var assemblyInfoPaths = new[]
        {
            Path.Combine(projectDir, "Properties", "AssemblyInfo.cs"),
            Path.Combine(projectDir, "AssemblyInfo.cs"),
            Path.Combine(projectDir, "Properties", "AssemblyInfo.vb"),
            Path.Combine(projectDir, "AssemblyInfo.vb")
        };

        if (assemblyInfoPaths.Any(File.Exists))
        {
            // Disable auto-generation to prevent conflicts
            propertyGroup.Add(new XElement("GenerateAssemblyInfo", "false"));
            _logger.LogInformation("AssemblyInfo file found. Setting GenerateAssemblyInfo to false.");
        }
        else
        {
            propertyGroup.Add(new XElement("GenerateAssemblyInfo", "true"));
        }
    }

    private void MigrateUnconvertedReferences(List<UnconvertedReference> unconvertedReferences, XElement projectElement)
    {
        if (!unconvertedReferences.Any())
            return;

        var itemGroup = new XElement("ItemGroup");

        foreach (var reference in unconvertedReferences)
        {
            var element = new XElement("Reference",
                new XAttribute("Include", reference.Identity.ToString()));

            // Add HintPath if available
            if (!string.IsNullOrEmpty(reference.HintPath))
            {
                element.Add(new XElement("HintPath", reference.HintPath));
            }

            // Add Private if specified
            if (reference.Private.HasValue)
            {
                element.Add(new XElement("Private", reference.Private.Value.ToString()));
            }

            // Add any additional metadata
            foreach (var metadata in reference.Metadata)
            {
                element.Add(new XElement(metadata.Key, metadata.Value));
            }

            itemGroup.Add(element);

            _logger.LogInformation("Preserved unconverted reference '{Reference}': {Reason}",
                reference.Identity.Name, reference.Reason);
        }

        projectElement.Add(itemGroup);
    }

    private void AddExcludedCompileItems(Project project, XElement projectElement)
    {
        var projectDir = Path.GetDirectoryName(project.FullPath)!;

        // Get all compiled files from the project
        var compiledFiles = project.Items
            .Where(i => i.ItemType == "Compile")
            .Select(i => Path.GetFullPath(Path.Combine(projectDir, i.EvaluatedInclude)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find all .cs files in the project directory
        var allCsFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                       !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var excludedFiles = allCsFiles.Where(f => !compiledFiles.Contains(f)).ToList();

        if (excludedFiles.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            foreach (var file in excludedFiles)
            {
                var relativePath = Path.GetRelativePath(projectDir, file);
                itemGroup.Add(new XElement("Compile", new XAttribute("Remove", relativePath)));
                _logger.LogDebug("Adding Compile Remove for: {File}", relativePath);
            }
            projectElement.Add(itemGroup);
        }
    }

    private void MigrateDesignerItems(Project project, XElement projectElement)
    {
        // WPF items
        var wpfItems = project.Items
            .Where(i => i.ItemType == "ApplicationDefinition" ||
                       i.ItemType == "Page" ||
                       i.ItemType == "Resource")
            .ToList();

        // WinForms items with SubType
        var winFormsItems = project.Items
            .Where(i => i.ItemType == "Compile" &&
                       i.HasMetadata("SubType") &&
                       (i.GetMetadataValue("SubType") == "Form" ||
                        i.GetMetadataValue("SubType") == "UserControl" ||
                        i.GetMetadataValue("SubType") == "Component"))
            .ToList();

        if (wpfItems.Any() || winFormsItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var item in wpfItems)
            {
                var element = new XElement(item.ItemType,
                    new XAttribute("Include", item.EvaluatedInclude));
                PreserveMetadata(item, element);
                itemGroup.Add(element);
            }

            foreach (var item in winFormsItems)
            {
                // These are already handled in MigrateCompileItems but need SubType preserved
                // Skip if already migrated
                continue;
            }

            if (itemGroup.HasElements)
                projectElement.Add(itemGroup);
        }
    }

    private void MigrateCustomItemTypes(Project project, XElement projectElement)
    {
        var standardTypes = new HashSet<string>
        {
            "Compile", "Content", "None", "EmbeddedResource",
            "Reference", "ProjectReference", "PackageReference",
            "Folder", "ApplicationDefinition", "Page", "Resource"
        };

        // Log items being filtered out
        var legacyItems = project.Items
            .Where(i => LegacyProjectElements.ItemsToRemove.Contains(i.ItemType))
            .GroupBy(i => i.ItemType);

        foreach (var group in legacyItems)
        {
            _logger.LogInformation("Removing legacy item type '{ItemType}' ({Count} items)",
                group.Key, group.Count());
        }

        var customItems = project.Items
            .Where(i => !standardTypes.Contains(i.ItemType))
            .Where(i => !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(i.ItemType))
            .Where(i => !LegacyProjectElements.ItemsToRemove.Contains(i.ItemType))
            .Where(i => !_artifactDetector.IsItemArtifact(i.ItemType, i.EvaluatedInclude))
            .GroupBy(i => i.ItemType);

        foreach (var group in customItems)
        {
            _logger.LogDebug("Migrating custom item type: {ItemType}", group.Key);
            var itemGroup = new XElement("ItemGroup");
            foreach (var item in group)
            {
                var element = new XElement(item.ItemType,
                    new XAttribute("Include", item.EvaluatedInclude));
                PreserveMetadata(item, element);
                itemGroup.Add(element);
            }

            if (itemGroup.HasElements)
            {
                projectElement.Add(itemGroup);
                _logger.LogInformation("Preserved custom item type: {ItemType}", group.Key);
            }
        }
    }

    private void PreserveMetadata(ProjectItem item, XElement element)
    {
        // Critical metadata to preserve
        var importantMetadata = new[]
        {
            "Link", "DependentUpon", "SubType", "Generator",
            "LastGenOutput", "CopyToOutputDirectory", "Private",
            "SpecificVersion", "CustomToolNamespace", "DesignTime",
            "AutoGen", "DesignTimeSharedInput"
        };

        foreach (var metadata in importantMetadata)
        {
            if (item.HasMetadata(metadata))
            {
                var value = item.GetMetadataValue(metadata);
                if (!string.IsNullOrEmpty(value))
                {
                    element.Add(new XElement(metadata, value));
                }
            }
        }
    }

    private void MigrateCOMReferences(Project project, XElement projectElement)
    {
        var comReferences = project.Items
            .Where(i => i.ItemType == "COMReference")
            .ToList();

        if (comReferences.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var comRef in comReferences)
            {
                var element = new XElement("COMReference",
                    new XAttribute("Include", comRef.EvaluatedInclude));

                // Preserve critical COM metadata
                var comMetadata = new[]
                {
                    "Guid", "VersionMajor", "VersionMinor", "Lcid",
                    "WrapperTool", "Isolated", "EmbedInteropTypes",
                    "Private", "HintPath"
                };

                foreach (var metadata in comMetadata)
                {
                    if (comRef.HasMetadata(metadata))
                    {
                        var value = comRef.GetMetadataValue(metadata);
                        if (!string.IsNullOrEmpty(value))
                        {
                            element.Add(new XElement(metadata, value));
                        }
                    }
                }

                // Ensure EmbedInteropTypes has a value (defaults differ between legacy and SDK)
                if (!comRef.HasMetadata("EmbedInteropTypes"))
                {
                    // Legacy projects often defaulted to false, SDK projects default to true
                    // Explicitly set to false to maintain legacy behavior
                    element.Add(new XElement("EmbedInteropTypes", "false"));
                }

                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
            _logger.LogInformation("Migrated {Count} COM references", comReferences.Count);
        }
    }

    private void MigrateStrongNaming(Project project, XElement propertyGroup, Dictionary<string, string> inheritedProperties)
    {
        // Check if assembly signing is enabled
        var signAssembly = project.Properties
            .FirstOrDefault(p => p.Name == "SignAssembly")?.EvaluatedValue;

        if (signAssembly?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Only add if not already in Directory.Build.props
            if (!inheritedProperties.ContainsKey("SignAssembly"))
            {
                propertyGroup.Add(new XElement("SignAssembly", "true"));
            }
            else
            {
                _logger.LogDebug("SignAssembly already set in Directory.Build.props");
            }

            // Migrate the key file path
            var keyFile = project.Properties
                .FirstOrDefault(p => p.Name == "AssemblyOriginatorKeyFile")?.EvaluatedValue;

            if (!string.IsNullOrEmpty(keyFile) && !inheritedProperties.ContainsKey("AssemblyOriginatorKeyFile"))
            {
                // Ensure the path is relative to the project file
                var projectDir = Path.GetDirectoryName(project.FullPath)!;
                var keyFilePath = keyFile;

                // If the key file path is absolute, make it relative
                if (Path.IsPathRooted(keyFile))
                {
                    keyFilePath = Path.GetRelativePath(projectDir, keyFile);
                }

                // Verify the key file exists
                var absoluteKeyPath = Path.GetFullPath(Path.Combine(projectDir, keyFilePath));
                if (File.Exists(absoluteKeyPath))
                {
                    propertyGroup.Add(new XElement("AssemblyOriginatorKeyFile", keyFilePath));
                    _logger.LogInformation("Migrated strong name key file: {KeyFile}", keyFilePath);
                }
                else
                {
                    _logger.LogWarning("Strong name key file not found: {KeyFile}", absoluteKeyPath);
                    // Still add the property to maintain the intent
                    propertyGroup.Add(new XElement("AssemblyOriginatorKeyFile", keyFilePath));
                }
            }

            // Check for delay signing
            var delaySign = project.Properties
                .FirstOrDefault(p => p.Name == "DelaySign")?.EvaluatedValue;

            if (delaySign?.Equals("true", StringComparison.OrdinalIgnoreCase) == true &&
                !inheritedProperties.ContainsKey("DelaySign"))
            {
                propertyGroup.Add(new XElement("DelaySign", "true"));
                _logger.LogInformation("Preserved DelaySign setting");
            }
        }
    }

    private bool IsAutomaticallyIncludedBySdk(ProjectItem item)
    {
        var fileName = Path.GetFileName(item.EvaluatedInclude);
        var extension = Path.GetExtension(item.EvaluatedInclude)?.ToLowerInvariant();
        var fileNameLower = fileName?.ToLowerInvariant();

        // SDK automatically includes certain files as Content
        if (item.ItemType == "Content")
        {
            // Check if this is likely a web project (will use Web SDK)
            var project = item.Project;
            var projectTypeGuids = project.Properties
                .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;
            var isWebProject = projectTypeGuids?.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase) ?? false;

            if (isWebProject)
            {
                // Web SDK auto-includes these patterns
                var webSdkPatterns = new[]
                {
                    "wwwroot/**/*",
                    "Areas/**/*.cshtml",
                    "Areas/**/*.razor",
                    "Views/**/*.cshtml",
                    "Views/**/*.razor",
                    "Pages/**/*.cshtml",
                    "Pages/**/*.razor",
                    "appsettings.json",
                    "appsettings.*.json",
                    "web.config"
                };

                foreach (var pattern in webSdkPatterns)
                {
                    if (IsMatchingPattern(item.EvaluatedInclude, pattern))
                    {
                        return true;
                    }
                }
            }

            // Check for specific file patterns that are auto-included by all SDKs
            if (fileName != null)
            {
                // These files are auto-included as Content by Microsoft.NET.Sdk
                if (fileNameLower == "app.config" || fileNameLower == "packages.config")
                {
                    return true;
                }
            }
        }

        // SDK automatically includes .resx files as EmbeddedResource
        if (item.ItemType == "EmbeddedResource" && extension == ".resx")
        {
            // Only if it doesn't have custom metadata
            if (!item.HasMetadata("Generator") && !item.HasMetadata("LastGenOutput"))
            {
                return true;
            }
        }

        // None items that are auto-included by SDK
        if (item.ItemType == "None")
        {
            // Check for files that SDK includes as None by default
            if (fileName != null)
            {
                // .config files (except app.config which is Content)
                if (extension == ".config" && fileNameLower != "app.config" && fileNameLower != "web.config")
                {
                    return true;
                }

                // .json files (except appsettings which are Content in web projects)
                if (extension == ".json" && fileNameLower != null && !fileNameLower.StartsWith("appsettings"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsMatchingPattern(string path, string pattern)
    {
        // Convert pattern to regex
        var regexPattern = pattern
            .Replace("\\", "/")
            .Replace(".", "\\.")
            .Replace("**", ".*")
            .Replace("*", "[^/]*");

        var normalizedPath = path.Replace("\\", "/");
        return System.Text.RegularExpressions.Regex.IsMatch(normalizedPath, $"^{regexPattern}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private async Task MigrateInternalsVisibleToAsync(Project project, XElement projectElement, CancellationToken cancellationToken)
    {
        try
        {
            // Extract assembly properties from AssemblyInfo files
            var projectDirectory = Path.GetDirectoryName(project.FullPath);
            if (string.IsNullOrEmpty(projectDirectory))
                return;

            var assemblyProperties = await _assemblyInfoExtractor.ExtractAssemblyPropertiesAsync(projectDirectory, cancellationToken);

            // If there are any InternalsVisibleTo attributes, add them as ItemGroup
            if (assemblyProperties.InternalsVisibleTo.Any())
            {
                _logger.LogInformation("Migrating {Count} InternalsVisibleTo attributes", assemblyProperties.InternalsVisibleTo.Count);

                var itemGroup = new XElement("ItemGroup");
                foreach (var internalsVisibleTo in assemblyProperties.InternalsVisibleTo)
                {
                    itemGroup.Add(new XElement("InternalsVisibleTo",
                        new XAttribute("Include", internalsVisibleTo)));
                    _logger.LogDebug("Added InternalsVisibleTo: {Value}", internalsVisibleTo);
                }

                projectElement.Add(itemGroup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate InternalsVisibleTo attributes");
        }
    }
}