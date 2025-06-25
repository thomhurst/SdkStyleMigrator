using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
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

    public CleanSdkStyleProjectGenerator(
        ILogger<CleanSdkStyleProjectGenerator> logger,
        IProjectParser projectParser,
        IPackageReferenceMigrator packageMigrator,
        ITransitiveDependencyDetector transitiveDepsDetector,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IAuditService auditService)
    {
        _logger = logger;
        _projectParser = projectParser;
        _packageMigrator = packageMigrator;
        _transitiveDepsDetector = transitiveDepsDetector;
        _assemblyInfoExtractor = assemblyInfoExtractor;
        _auditService = auditService;
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

            // Create the root project element
            var projectElement = new XElement("Project");
            
            // Determine and set SDK
            var sdkType = DetermineSdkType(legacyProject);
            projectElement.Add(new XAttribute("Sdk", sdkType));

            // Create main property group
            var mainPropertyGroup = new XElement("PropertyGroup");
            projectElement.Add(mainPropertyGroup);

            // Migrate basic properties
            MigrateBasicProperties(legacyProject, mainPropertyGroup);

            // Handle AssemblyInfo to prevent conflicts
            HandleAssemblyInfo(legacyProject, mainPropertyGroup);

            // Migrate package references
            await MigratePackageReferencesAsync(legacyProject, projectElement, cancellationToken);

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

    private void MigrateBasicProperties(Project project, XElement propertyGroup)
    {
        // Target framework
        var targetFramework = ConvertTargetFramework(project);
        if (!string.IsNullOrEmpty(targetFramework))
        {
            propertyGroup.Add(new XElement("TargetFramework", targetFramework));
        }

        // Output type
        var outputType = project.Properties
            .FirstOrDefault(p => p.Name == "OutputType")?.EvaluatedValue;
        if (!string.IsNullOrEmpty(outputType))
        {
            propertyGroup.Add(new XElement("OutputType", outputType));
        }

        // Assembly name
        var assemblyName = project.Properties
            .FirstOrDefault(p => p.Name == "AssemblyName")?.EvaluatedValue;
        if (!string.IsNullOrEmpty(assemblyName))
        {
            propertyGroup.Add(new XElement("AssemblyName", assemblyName));
        }

        // Root namespace
        var rootNamespace = project.Properties
            .FirstOrDefault(p => p.Name == "RootNamespace")?.EvaluatedValue;
        if (!string.IsNullOrEmpty(rootNamespace) && rootNamespace != assemblyName)
        {
            propertyGroup.Add(new XElement("RootNamespace", rootNamespace));
        }

        // Language version
        var langVersion = project.Properties
            .FirstOrDefault(p => p.Name == "LangVersion")?.EvaluatedValue;
        if (!string.IsNullOrEmpty(langVersion))
        {
            propertyGroup.Add(new XElement("LangVersion", langVersion));
        }

        // Nullable
        if (targetFramework?.StartsWith("net") == true && 
            int.TryParse(targetFramework.Substring(3, 1), out var version) && version >= 6)
        {
            propertyGroup.Add(new XElement("Nullable", "enable"));
        }

        // Strong naming properties
        MigrateStrongNaming(project, propertyGroup);

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

    private async Task MigratePackageReferencesAsync(Project project, XElement projectElement, CancellationToken cancellationToken)
    {
        var packages = await _packageMigrator.MigratePackagesAsync(project, cancellationToken);

        if (packages.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var package in packages)
            {
                var packageRef = new XElement("PackageReference",
                    new XAttribute("Include", package.PackageId),
                    new XAttribute("Version", package.Version ?? "*"));
                
                itemGroup.Add(packageRef);
            }
            
            projectElement.Add(itemGroup);
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
            .Where(i => !i.EvaluatedInclude.EndsWith(".cs") || 
                       i.EvaluatedInclude.Contains("*") ||
                       i.HasMetadata("Link") ||
                       i.HasMetadata("DependentUpon"))
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
    }

    private void MigrateContentAndResources(Project project, XElement projectElement)
    {
        var contentItems = project.Items
            .Where(i => i.ItemType == "Content" || 
                       i.ItemType == "None" ||
                       i.ItemType == "EmbeddedResource")
            .ToList();

        if (contentItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var item in contentItems)
            {
                var element = new XElement(item.ItemType,
                    new XAttribute("Include", item.EvaluatedInclude));
                
                PreserveMetadata(item, element);
                itemGroup.Add(element);
            }
            
            projectElement.Add(itemGroup);
        }
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

    private void HandleAssemblyInfo(Project project, XElement propertyGroup)
    {
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

        var customItems = project.Items
            .Where(i => !standardTypes.Contains(i.ItemType))
            .GroupBy(i => i.ItemType);

        foreach (var group in customItems)
        {
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

    private void MigrateStrongNaming(Project project, XElement propertyGroup)
    {
        // Check if assembly signing is enabled
        var signAssembly = project.Properties
            .FirstOrDefault(p => p.Name == "SignAssembly")?.EvaluatedValue;
        
        if (signAssembly?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            propertyGroup.Add(new XElement("SignAssembly", "true"));
            
            // Migrate the key file path
            var keyFile = project.Properties
                .FirstOrDefault(p => p.Name == "AssemblyOriginatorKeyFile")?.EvaluatedValue;
            
            if (!string.IsNullOrEmpty(keyFile))
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
            
            if (delaySign?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                propertyGroup.Add(new XElement("DelaySign", "true"));
                _logger.LogInformation("Preserved DelaySign setting");
            }
        }
    }
}