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

            // Migrate package references
            await MigratePackageReferencesAsync(legacyProject, projectElement, cancellationToken);

            // Migrate project references
            MigrateProjectReferences(legacyProject, projectElement);

            // Migrate compile items (if needed)
            MigrateCompileItems(legacyProject, projectElement);

            // Migrate content and resources
            MigrateContentAndResources(legacyProject, projectElement);

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
        var projectTypeGuids = project.Properties
            .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;

        if (!string.IsNullOrEmpty(projectTypeGuids))
        {
            // Web project
            if (projectTypeGuids.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.Web";
            
            // Blazor WebAssembly
            if (projectTypeGuids.Contains("{A9ACE9BB-CECE-4E62-9AA4-C7E7C5BD2124}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.BlazorWebAssembly";
            
            // WPF/WinForms
            if (projectTypeGuids.Contains("{60dc8134-eba5-43b8-bcc9-bb4bc16c2548}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.WindowsDesktop";
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

        // Generate assembly info
        propertyGroup.Add(new XElement("GenerateAssemblyInfo", "true"));
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
                       i.HasMetadata("Link"))
            .ToList();

        if (compileItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            
            foreach (var item in compileItems)
            {
                var element = new XElement("Compile",
                    new XAttribute("Include", item.EvaluatedInclude));
                
                if (item.HasMetadata("Link"))
                {
                    element.Add(new XElement("Link", item.GetMetadataValue("Link")));
                }
                
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
                
                if (item.HasMetadata("CopyToOutputDirectory"))
                {
                    element.Add(new XElement("CopyToOutputDirectory", 
                        item.GetMetadataValue("CopyToOutputDirectory")));
                }
                
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
}