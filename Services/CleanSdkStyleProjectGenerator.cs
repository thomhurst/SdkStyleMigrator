using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using System.Linq;
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
    private readonly ITestProjectHandler _testProjectHandler;
    private readonly IDesignerFileHandler _designerFileHandler;
    private readonly IBuildEventMigrator _buildEventMigrator;
    private readonly INativeDependencyHandler _nativeDependencyHandler;
    
    // Special project type handlers
    private readonly IAzureFunctionsHandler _azureFunctionsHandler;
    private readonly IMauiProjectHandler _mauiProjectHandler;
    private readonly IBlazorProjectHandler _blazorProjectHandler;
    private readonly IWorkerServiceHandler _workerServiceHandler;
    private readonly IGrpcServiceHandler _grpcServiceHandler;
    private readonly IUwpProjectHandler _uwpProjectHandler;
    private readonly IDatabaseProjectHandler _databaseProjectHandler;
    private readonly IDockerProjectHandler _dockerProjectHandler;
    private readonly ISharedProjectHandler _sharedProjectHandler;
    private readonly IOfficeProjectHandler _officeProjectHandler;
    private ImportScanResult? _importScanResult;
    private TargetScanResult? _targetScanResult;
    private bool _centralPackageManagementEnabled;
    private bool _generateModernProgramCs;

    public CleanSdkStyleProjectGenerator(
        ILogger<CleanSdkStyleProjectGenerator> logger,
        IProjectParser projectParser,
        IPackageReferenceMigrator packageMigrator,
        ITransitiveDependencyDetector transitiveDepsDetector,
        IAssemblyInfoExtractor assemblyInfoExtractor,
        IAuditService auditService,
        IDirectoryBuildPropsReader directoryBuildPropsReader,
        IMSBuildArtifactDetector artifactDetector,
        IAssemblyReferenceConverter assemblyReferenceConverter,
        ITestProjectHandler testProjectHandler,
        IDesignerFileHandler designerFileHandler,
        IBuildEventMigrator buildEventMigrator,
        INativeDependencyHandler nativeDependencyHandler,
        IAzureFunctionsHandler azureFunctionsHandler,
        IMauiProjectHandler mauiProjectHandler,
        IBlazorProjectHandler blazorProjectHandler,
        IWorkerServiceHandler workerServiceHandler,
        IGrpcServiceHandler grpcServiceHandler,
        IUwpProjectHandler uwpProjectHandler,
        IDatabaseProjectHandler databaseProjectHandler,
        IDockerProjectHandler dockerProjectHandler,
        ISharedProjectHandler sharedProjectHandler,
        IOfficeProjectHandler officeProjectHandler)
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
        _testProjectHandler = testProjectHandler;
        _designerFileHandler = designerFileHandler;
        _buildEventMigrator = buildEventMigrator;
        _nativeDependencyHandler = nativeDependencyHandler;
        
        // Assign special project type handlers
        _azureFunctionsHandler = azureFunctionsHandler;
        _mauiProjectHandler = mauiProjectHandler;
        _blazorProjectHandler = blazorProjectHandler;
        _workerServiceHandler = workerServiceHandler;
        _grpcServiceHandler = grpcServiceHandler;
        _uwpProjectHandler = uwpProjectHandler;
        _databaseProjectHandler = databaseProjectHandler;
        _dockerProjectHandler = dockerProjectHandler;
        _sharedProjectHandler = sharedProjectHandler;
        _officeProjectHandler = officeProjectHandler;
    }

    public void SetImportScanResult(ImportScanResult? importScanResult)
    {
        _importScanResult = importScanResult;
    }
    
    public void SetTargetScanResult(TargetScanResult? targetScanResult)
    {
        _targetScanResult = targetScanResult;
    }

    public void SetCentralPackageManagementEnabled(bool enabled)
    {
        _centralPackageManagementEnabled = enabled;
        _logger.LogInformation("CPM Debug: SetCentralPackageManagementEnabled called with value: {Enabled}", enabled);
    }
    
    public void SetGenerateModernProgramCs(bool enabled)
    {
        _generateModernProgramCs = enabled;
        _logger.LogInformation("GenerateModernProgramCs set to: {Enabled}", enabled);
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
            var sdkAttribute = sdkType == "MSBuild.SDK.SystemWeb" 
                ? "MSBuild.SDK.SystemWeb/4.0.104" 
                : sdkType;
            projectElement.Add(new XAttribute("Sdk", sdkAttribute));

            // Create main property group
            var mainPropertyGroup = new XElement("PropertyGroup");
            projectElement.Add(mainPropertyGroup);

            // Migrate basic properties (skip those already in Directory.Build.props)
            MigrateBasicProperties(legacyProject, mainPropertyGroup, inheritedProperties, sdkType);

            // Handle AssemblyInfo to prevent conflicts
            HandleAssemblyInfo(legacyProject, mainPropertyGroup, inheritedProperties);

            // Apply special project type handling
            await ApplySpecialProjectTypeHandling(legacyProject, projectElement, result, cancellationToken);

            // Migrate package references
            var migratedPackages = await MigratePackageReferencesAsync(legacyProject, projectElement, centrallyManagedPackages, result, sdkType, cancellationToken);

            // Migrate project references
            MigrateProjectReferences(legacyProject, projectElement);

            // Migrate COM references
            MigrateCOMReferences(legacyProject, projectElement);

            // Migrate compile items (if needed)
            MigrateCompileItems(legacyProject, projectElement, sdkType);

            // Add excluded compile items (skip for SystemWeb SDK)
            if (sdkType != "MSBuild.SDK.SystemWeb")
            {
                AddExcludedCompileItems(legacyProject, projectElement);
            }

            // Migrate content and resources
            MigrateContentAndResources(legacyProject, projectElement, sdkType);

            // Migrate WPF/WinForms specific items
            MigrateDesignerItems(legacyProject, projectElement, sdkType);

            // Analyze and fix designer file relationships (skip for SystemWeb SDK)
            if (sdkType != "MSBuild.SDK.SystemWeb")
            {
                var designerRelationships = _designerFileHandler.AnalyzeDesignerRelationships(legacyProject);
                _designerFileHandler.MigrateDesignerRelationships(designerRelationships, projectElement, result, sdkType);
            }
            else
            {
                _logger.LogDebug("Skipping designer file relationship migration for SystemWeb SDK project");
            }

            // Migrate custom item types
            MigrateCustomItemTypes(legacyProject, projectElement, sdkType);

            // Detect and migrate native dependencies
            var nativeDependencies = _nativeDependencyHandler.DetectNativeDependencies(legacyProject);
            if (nativeDependencies.Any())
            {
                _nativeDependencyHandler.MigrateNativeDependencies(nativeDependencies, projectElement, result);
                _logger.LogInformation("Detected and migrated {Count} native dependencies", nativeDependencies.Count);
            }

            // Migrate InternalsVisibleTo from AssemblyInfo
            await MigrateInternalsVisibleToAsync(legacyProject, projectElement, cancellationToken);

            // Migrate conditional elements (Choose/When/Otherwise)
            MigrateConditionalElements(legacyProject, projectElement);

            // Migrate conditional properties (DefineConstants, etc.)
            MigrateConditionalProperties(legacyProject, projectElement, inheritedProperties.Any(p => p.Key == "TargetFrameworks"));

            // Migrate custom imports, targets and build events
            MigrateCustomImports(legacyProject, projectElement, result);
            MigrateCustomTargets(legacyProject, projectElement, result);

            // Save the project
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                projectElement);

            doc.Save(outputPath);

            result.Success = true;
            result.MigratedPackages.AddRange(migratedPackages);
            result.SdkType = sdkType;

            // Add build validation warnings for MSBuild.SDK.SystemWeb projects
            if (sdkType == "MSBuild.SDK.SystemWeb")
            {
                result.Warnings.Add("This project uses MSBuild.SDK.SystemWeb and requires 'msbuild' for building (not 'dotnet build')");
                result.Warnings.Add("Publishing requires 'msbuild' with standard msdeploy scripts (not 'dotnet publish')");
                result.Warnings.Add("Ensure your CI/CD pipeline uses 'msbuild' commands for .NET Framework web projects");
                result.Warnings.Add("MSBuild.SDK.SystemWeb provides modern SDK-style project format while maintaining .NET Framework compatibility");
            }
            
            // Extract target frameworks from the generated project
            var targetFrameworksProperty = projectElement.Elements("PropertyGroup")
                .SelectMany(pg => pg.Elements())
                .FirstOrDefault(p => p.Name == "TargetFrameworks" || p.Name == "TargetFramework");
            if (targetFrameworksProperty != null && !string.IsNullOrEmpty(targetFrameworksProperty.Value))
            {
                result.TargetFrameworks = targetFrameworksProperty.Value.Contains(';') 
                    ? targetFrameworksProperty.Value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
                    : new List<string> { targetFrameworksProperty.Value };
            }
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
        var projectPath = project.FullPath;
        var fileExtension = Path.GetExtension(projectPath).ToLowerInvariant();

        // Special project types that need different SDKs or shouldn't be migrated
        switch (fileExtension)
        {
            case ".sqlproj":
                _logger.LogWarning("Database projects (.sqlproj) require special handling and may not migrate correctly");
                return "Microsoft.NET.Sdk"; // Will need manual intervention
                
            case ".dcproj":
                _logger.LogWarning("Docker orchestration projects (.dcproj) are not standard MSBuild projects and should be handled separately");
                return "Microsoft.NET.Sdk"; // Will need manual intervention
                
            case ".shproj":
                _logger.LogWarning("Shared projects (.shproj) have a different structure and require special migration approach");
                return "Microsoft.NET.Sdk"; // Will need manual intervention
                
            case ".fsproj":
                _logger.LogInformation("Detected F# project - using Microsoft.NET.Sdk with F# support");
                return "Microsoft.NET.Sdk"; // F# uses the standard SDK with F# targets
                
            case ".vbproj":
                _logger.LogInformation("Detected VB.NET project - using Microsoft.NET.Sdk with VB support");
                return "Microsoft.NET.Sdk"; // VB.NET uses the standard SDK with VB targets
        }

        // Check for Azure Functions
        if (HasPackageReference(project, "Microsoft.NET.Sdk.Functions") ||
            HasPackageReference(project, "Microsoft.Azure.WebJobs") ||
            HasPackageReference(project, "Microsoft.Azure.Functions"))
        {
            _logger.LogInformation("Detected Azure Functions project - using Microsoft.NET.Sdk.Functions");
            return "Microsoft.NET.Sdk.Functions";
        }

        // Check for Worker Service
        if (HasPackageReference(project, "Microsoft.Extensions.Hosting") ||
            HasPackageReference(project, "Microsoft.Extensions.Hosting.WindowsServices") ||
            HasPackageReference(project, "Microsoft.Extensions.Hosting.Systemd"))
        {
            _logger.LogInformation("Detected Worker Service project - using Microsoft.NET.Sdk.Worker");
            return "Microsoft.NET.Sdk.Worker";
        }

        // Check for gRPC services - they use Web SDK but need special handling
        if (HasPackageReference(project, "Grpc.AspNetCore") ||
            HasPackageReference(project, "Google.Protobuf") ||
            HasPackageReference(project, "Grpc.Tools"))
        {
            _logger.LogInformation("Detected gRPC service project - using Microsoft.NET.Sdk.Web");
            // gRPC projects use the Web SDK but may need special proto file handling
            return "Microsoft.NET.Sdk.Web";
        }

        // Check for MAUI/Xamarin
        if (HasPackageReference(project, "Microsoft.Maui") ||
            HasPackageReference(project, "Xamarin.Forms"))
        {
            var mauiProjectTypeGuids = project.Properties
                .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;
            
            // Xamarin.Android
            if (!string.IsNullOrEmpty(mauiProjectTypeGuids) && 
                mauiProjectTypeGuids.Contains("{EFBA0AD7-5A72-4C68-AF49-83D382785DCF}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Xamarin.Android projects require special handling for migration to .NET MAUI");
            }
            
            // Xamarin.iOS
            if (!string.IsNullOrEmpty(mauiProjectTypeGuids) && 
                mauiProjectTypeGuids.Contains("{6BC8ED88-2882-458C-8E55-DFD12B67127B}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Xamarin.iOS projects require special handling for migration to .NET MAUI");
            }
            
            _logger.LogInformation("Detected MAUI/Xamarin project - using Microsoft.NET.Sdk.Maui");
            return "Microsoft.NET.Sdk.Maui";
        }

        // CRITICAL: Check for web projects FIRST, before .NET Framework override
        var projectTypeGuids = project.Properties
            .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;

        if (!string.IsNullOrEmpty(projectTypeGuids))
        {
            // Check for UWP
            if (projectTypeGuids.Contains("{A5A43C5B-DE2A-4C0C-9213-0A381AF9435A}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("UWP projects have limited migration paths and may require manual conversion to WinUI 3");
                return "Microsoft.NET.Sdk"; // UWP SDK is complex and may not migrate cleanly
            }

            // Check for Office/VSTO Add-ins
            if (projectTypeGuids.Contains("{BAA0C2D2-18E2-41B9-852F-F413020CAA33}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Office/VSTO Add-in projects require special COM references and manifest handling");
                return "Microsoft.NET.Sdk"; // Will need manual intervention for manifest and COM references
            }

            // Web Application Project
            if (projectTypeGuids.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase))
            {
                // Check if it's a Blazor project
                if (HasPackageReference(project, "Microsoft.AspNetCore.Components.WebAssembly"))
                {
                    _logger.LogInformation("Detected Blazor WebAssembly project - using Microsoft.NET.Sdk.BlazorWebAssembly");
                    return "Microsoft.NET.Sdk.BlazorWebAssembly";
                }
                
                if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Detected .NET Framework web project - using MSBuild.SDK.SystemWeb");
                    return "MSBuild.SDK.SystemWeb";
                }
                else
                {
                    return "Microsoft.NET.Sdk.Web";
                }
            }

            // Web Site Project (less common, but also supported by SystemWeb)
            if (projectTypeGuids.Contains("{E24C65DC-7377-472B-9ABA-BC803B73C61A}", StringComparison.OrdinalIgnoreCase))
            {
                if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Detected .NET Framework web site project - using MSBuild.SDK.SystemWeb");
                    return "MSBuild.SDK.SystemWeb";
                }
                else
                {
                    return "Microsoft.NET.Sdk.Web";
                }
            }

            // Blazor WebAssembly (only for .NET Core 3.0+)
            if (projectTypeGuids.Contains("{A9ACE9BB-CECE-4E62-9AA4-C7E7C5BD2124}", StringComparison.OrdinalIgnoreCase))
                return "Microsoft.NET.Sdk.BlazorWebAssembly";
        }

        // For .NET Framework projects (non-web), use the default SDK
        if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft.NET.Sdk";
        }

        // Check for WPF/WinForms by items (only from explicit XML)
        var hasWpfItems = HasExplicitItemsOfType(project, new[] { "ApplicationDefinition", "Page" });
        var hasWinFormsItems = HasExplicitWinFormsItems(project);

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

    private bool HasPackageReference(Project project, string packageId)
    {
        // Check in both Reference items with HintPath and PackageReference items
        return project.Items
            .Where(i => i.ItemType == "Reference" || i.ItemType == "PackageReference")
            .Any(i => 
            {
                if (i.ItemType == "PackageReference")
                {
                    return i.EvaluatedInclude.Equals(packageId, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // For Reference items, check if the HintPath contains the package name
                    var hintPath = i.GetMetadataValue("HintPath");
                    return !string.IsNullOrEmpty(hintPath) && 
                           hintPath.Contains(packageId, StringComparison.OrdinalIgnoreCase);
                }
            });
    }

    private bool HasExplicitItemsOfType(Project project, string[] itemTypes)
    {
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            if (itemGroup.Items.Any(i => itemTypes.Contains(i.ItemType)))
            {
                return true;
            }
        }
        return false;
    }

    private bool HasExplicitWinFormsItems(Project project)
    {
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items.Where(i => i.ItemType == "Compile"))
            {
                var subTypeElement = item.Metadata.FirstOrDefault(m => m.Name == "SubType");
                if (subTypeElement != null)
                {
                    var subType = subTypeElement.Value;
                    if (subType == "Form" || subType == "UserControl")
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private List<(string Include, Dictionary<string, string> Metadata)> GetExplicitItemsFromXml(Project project, string itemType)
    {
        var items = new List<(string Include, Dictionary<string, string> Metadata)>();
        
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items.Where(i => i.ItemType == itemType))
            {
                var metadata = new Dictionary<string, string>();
                foreach (var meta in item.Metadata)
                {
                    metadata[meta.Name] = meta.Value;
                }
                items.Add((item.Include, metadata));
            }
        }
        
        return items;
    }

    private string SafeExpandString(Project project, string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // If the value contains MSBuild property syntax, try to expand it safely
        if (value.Contains("$("))
        {
            try
            {
                var expanded = project.ExpandString(value);
                
                // If expansion resulted in empty string but original had content,
                // preserve the original MSBuild variable syntax
                if (string.IsNullOrEmpty(expanded) && !string.IsNullOrEmpty(value))
                {
                    _logger.LogDebug("Preserving MSBuild variable in value: {Value} (expansion was empty)", value);
                    return value;
                }
                
                // If expansion was successful and different, use it
                return expanded;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to expand MSBuild property in '{Value}': {Error}. Preserving original.", value, ex.Message);
                return value;
            }
        }

        // No MSBuild properties, return as-is
        return value;
    }

    private void MigrateBasicProperties(Project project, XElement propertyGroup, Dictionary<string, string> inheritedProperties, string sdkType)
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

        // For SystemWeb SDK, add DefaultItemExcludes to ensure publish directory is excluded
        if (sdkType == "MSBuild.SDK.SystemWeb")
        {
            // Add standard exclusions plus publish directory
            // The MSBuild.SDK.SystemWeb may not have all the standard SDK exclusions
            AddPropertyIfNotInherited("DefaultItemExcludes", "$(DefaultItemExcludes);publish\\**");
            _logger.LogDebug("Added DefaultItemExcludes for SystemWeb SDK to exclude publish directory");
        }

        // Check if project needs multi-targeting
        var targetFrameworkVersions = project.Properties
            .Where(p => p.Name == "TargetFrameworkVersion")
            .Select(p => new { Version = p.EvaluatedValue, Condition = p.Xml?.Condition })
            .Where(x => !string.IsNullOrEmpty(x.Version))
            .GroupBy(x => x.Version)
            .ToList();

        var targetFrameworkProfiles = project.Properties
            .Where(p => p.Name == "TargetFrameworkProfile")
            .Select(p => new { Profile = p.EvaluatedValue, Condition = p.Xml?.Condition })
            .Where(x => !string.IsNullOrEmpty(x.Profile))
            .Distinct()
            .ToList();

        // Determine if multi-targeting is needed
        var needsMultiTargeting = targetFrameworkVersions.Count > 1 || 
                                  targetFrameworkProfiles.Count > 1 ||
                                  (project.Properties.Any(p => p.Name == "TargetFrameworks")); // Already multi-targeted

        string targetFramework = string.Empty;
        List<string> allTargetFrameworks = new();
        
        if (needsMultiTargeting)
        {
            _logger.LogInformation("Project requires multi-targeting support");
            var frameworks = DetermineTargetFrameworks(project, targetFrameworkVersions, targetFrameworkProfiles);
            AddPropertyIfNotInherited("TargetFrameworks", string.Join(";", frameworks));
            allTargetFrameworks.AddRange(frameworks);
            // For multi-targeting, use the first framework for compatibility checks
            targetFramework = frameworks.FirstOrDefault() ?? ConvertTargetFramework(project);
        }
        else
        {
            // Single target framework
            targetFramework = ConvertTargetFramework(project);
            AddPropertyIfNotInherited("TargetFramework", targetFramework);
            allTargetFrameworks.Add(targetFramework);
        }

        // Get properties that are explicitly defined in the project file (not MSBuild defaults)
        var explicitProperties = new Dictionary<string, string>();
        foreach (var propGroup in project.Xml.PropertyGroups)
        {
            foreach (var prop in propGroup.Properties)
            {
                if (!string.IsNullOrEmpty(prop.Value))
                {
                    explicitProperties[prop.Name] = prop.Value;
                }
            }
        }

        // Output type - only if explicitly defined
        if (explicitProperties.TryGetValue("OutputType", out var outputType))
        {
            AddPropertyIfNotInherited("OutputType", SafeExpandString(project, outputType));
        }

        // Assembly name - only if explicitly defined
        if (explicitProperties.TryGetValue("AssemblyName", out var assemblyName))
        {
            AddPropertyIfNotInherited("AssemblyName", SafeExpandString(project, assemblyName));
        }

        // Root namespace - only if explicitly defined and different from AssemblyName
        if (explicitProperties.TryGetValue("RootNamespace", out var rootNamespace))
        {
            var expandedRootNamespace = SafeExpandString(project, rootNamespace);
            var expandedAssemblyName = explicitProperties.ContainsKey("AssemblyName") 
                ? SafeExpandString(project, explicitProperties["AssemblyName"]) 
                : null;
                
            if (expandedRootNamespace != expandedAssemblyName)
            {
                AddPropertyIfNotInherited("RootNamespace", expandedRootNamespace);
            }
        }

        // Language version - only if explicitly defined
        if (explicitProperties.TryGetValue("LangVersion", out var langVersion))
        {
            _logger.LogDebug("LangVersion explicitly defined in project: {Value}", langVersion);
            AddPropertyIfNotInherited("LangVersion", SafeExpandString(project, langVersion));
        }
        else
        {
            _logger.LogDebug("No explicit LangVersion property found in project file");
        }

        // Nullable - only if explicitly defined in original project
        if (explicitProperties.TryGetValue("Nullable", out var nullable))
        {
            AddPropertyIfNotInherited("Nullable", SafeExpandString(project, nullable));
        }

        // Strong naming properties
        MigrateStrongNaming(project, propertyGroup, inheritedProperties);

        // For multi-targeting, check each framework for WPF/WinForms needs
        if (needsMultiTargeting)
        {
            var frameworks = DetermineTargetFrameworks(project, targetFrameworkVersions, targetFrameworkProfiles);
            
            // Check if any modern framework needs WPF/WinForms
            var needsWpfOrWinForms = frameworks.Any(f => 
                f.StartsWith("net") && !f.Contains(".") && !f.StartsWith("net4", StringComparison.OrdinalIgnoreCase));
                
            if (needsWpfOrWinForms)
            {
                var hasWpfItems = HasExplicitItemsOfType(project, new[] { "ApplicationDefinition", "Page" });
                var hasWinFormsItems = HasExplicitWinFormsItems(project);

                if (hasWpfItems)
                    propertyGroup.Add(new XElement("UseWPF", "true"));
                if (hasWinFormsItems)
                    propertyGroup.Add(new XElement("UseWindowsForms", "true"));
            }
        }
        else
        {
            // For .NET 5+ WPF/WinForms projects (not .NET Framework)
            if (targetFramework?.StartsWith("net") == true &&
                !targetFramework.Contains(".") &&
                !targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
            {
                var hasWpfItems = HasExplicitItemsOfType(project, new[] { "ApplicationDefinition", "Page" });
                var hasWinFormsItems = HasExplicitWinFormsItems(project);

                if (hasWpfItems)
                    propertyGroup.Add(new XElement("UseWPF", "true"));
                if (hasWinFormsItems)
                    propertyGroup.Add(new XElement("UseWindowsForms", "true"));
            }
        }

        // Add SystemWeb-specific properties
        if (sdkType == "MSBuild.SDK.SystemWeb")
        {
            AddPropertyIfNotInherited("GeneratedBindingRedirectsAction", "Overwrite");
            _logger.LogInformation("Added GeneratedBindingRedirectsAction=Overwrite for SystemWeb SDK project");
        }

        // Migrate conditional compilation symbols will be done later with the projectElement
    }

    private void MigrateConditionalProperties(Project project, XElement projectElement, bool isMultiTargeting)
    {
        // Get all DefineConstants across configurations - only from explicit XML
        var defineConstantsFromXml = new List<(string Value, string Condition)>();
        
        foreach (var propGroup in project.Xml.PropertyGroups)
        {
            var condition = propGroup.Condition;
            if (!string.IsNullOrEmpty(condition))
            {
                foreach (var prop in propGroup.Properties)
                {
                    if (prop.Name == "DefineConstants" && !string.IsNullOrEmpty(prop.Value))
                    {
                        defineConstantsFromXml.Add((prop.Value, condition));
                    }
                }
            }
        }

        if (defineConstantsFromXml.Any())
        {
            foreach (var (value, condition) in defineConstantsFromXml)
            {
                var expandedValue = SafeExpandString(project, value);
                if (string.IsNullOrEmpty(expandedValue))
                    continue;

                // Convert legacy conditions to modern framework conditions if multi-targeting
                var processedCondition = condition;
                if (isMultiTargeting)
                {
                    processedCondition = ConvertConditionToFrameworkSpecific(condition);
                }

                var propGroup = new XElement("PropertyGroup");
                if (!string.IsNullOrEmpty(processedCondition))
                {
                    propGroup.Add(new XAttribute("Condition", processedCondition));
                }
                propGroup.Add(new XElement("DefineConstants", expandedValue));
                projectElement.Add(propGroup);

                _logger.LogDebug("Migrated conditional DefineConstants: {Constants} with condition: {Condition}", 
                    expandedValue, processedCondition);
            }
        }

        // Migrate other conditional properties (PlatformTarget, etc.) - only from explicit XML
        var conditionalPropsFromXml = new List<(string PropertyName, string PropertyValue, string Condition)>();
        var conditionalPropNames = new[] { "PlatformTarget", "Prefer32Bit", "AllowUnsafeBlocks", "DebugType", "DebugSymbols", "Optimize" };
        
        foreach (var propGroup in project.Xml.PropertyGroups)
        {
            var condition = propGroup.Condition;
            if (!string.IsNullOrEmpty(condition))
            {
                foreach (var prop in propGroup.Properties)
                {
                    if (conditionalPropNames.Contains(prop.Name) && !string.IsNullOrEmpty(prop.Value))
                    {
                        conditionalPropsFromXml.Add((prop.Name, prop.Value, condition));
                    }
                }
            }
        }

        var groupedConditionalProps = conditionalPropsFromXml
            .GroupBy(x => x.Condition)
            .ToList();

        foreach (var group in groupedConditionalProps)
        {
            var condition = group.Key;
            if (isMultiTargeting)
            {
                condition = ConvertConditionToFrameworkSpecific(condition);
            }

            var propGroup = new XElement("PropertyGroup");
            if (!string.IsNullOrEmpty(condition))
            {
                propGroup.Add(new XAttribute("Condition", condition));
            }

            foreach (var (propertyName, propertyValue, _) in group)
            {
                propGroup.Add(new XElement(propertyName, SafeExpandString(project, propertyValue)));
            }

            projectElement.Add(propGroup);
        }
    }

    private string ConvertConditionToFrameworkSpecific(string condition)
    {
        // Common legacy conditions to framework-specific conditions
        if (condition.Contains("'$(Configuration)|$(Platform)'"))
        {
            // Keep configuration/platform conditions as-is
            return condition;
        }
        
        // Convert TargetFrameworkVersion conditions to TargetFramework
        if (condition.Contains("TargetFrameworkVersion"))
        {
            // Example: '$(TargetFrameworkVersion)' == 'v4.5' becomes '$(TargetFramework)' == 'net45'
            var pattern = @"'\$\(TargetFrameworkVersion\)'\s*==\s*'v?(\d+\.\d+)'";
            var match = System.Text.RegularExpressions.Regex.Match(condition, pattern);
            if (match.Success)
            {
                var version = match.Groups[1].Value.Replace(".", "");
                return $"'$(TargetFramework)' == 'net{version}'";
            }
        }

        return condition;
    }

    private string ConvertTargetFramework(Project project)
    {
        var targetFrameworkVersion = project.Properties
            .FirstOrDefault(p => p.Name == "TargetFrameworkVersion")?.EvaluatedValue;

        var targetFrameworkProfile = project.Properties
            .FirstOrDefault(p => p.Name == "TargetFrameworkProfile")?.EvaluatedValue;

        // Handle PCL projects
        if (!string.IsNullOrEmpty(targetFrameworkProfile) && targetFrameworkProfile.StartsWith("Profile"))
        {
            return ConvertPortableClassLibrary(targetFrameworkProfile);
        }

        if (string.IsNullOrEmpty(targetFrameworkVersion))
            return "net8.0";

        // Remove 'v' prefix and convert
        var version = targetFrameworkVersion.TrimStart('v');

        // Handle Client Profile
        if (targetFrameworkProfile == "Client")
        {
            // Client profiles should upgrade to full framework
            _logger.LogInformation("Converting .NET Framework Client Profile to full framework");
        }

        // .NET Framework 4.x
        if (version.StartsWith("4."))
        {
            return $"net{version.Replace(".", "")}";
        }

        // .NET Framework 3.5 and earlier
        if (version == "3.5")
        {
            return "net35";
        }
        if (version == "3.0")
        {
            return "net30";
        }
        if (version == "2.0")
        {
            return "net20";
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

    private List<string> DetermineTargetFrameworks(Project project, 
        dynamic targetFrameworkVersions,
        dynamic targetFrameworkProfiles)
    {
        var frameworks = new HashSet<string>();

        // If we have multiple framework versions, add each
        if (targetFrameworkVersions.Any())
        {
            foreach (var group in targetFrameworkVersions)
            {
                var tempProject = project;
                // Temporarily set the property to get the correct conversion
                var originalValue = project.Properties.FirstOrDefault(p => p.Name == "TargetFrameworkVersion")?.EvaluatedValue;
                
                // Convert each framework version
                var version = group.Key.TrimStart('v');
                
                // Check if there's a corresponding profile
                var profilesForVersion = new List<dynamic>();
                foreach (var p in targetFrameworkProfiles)
                {
                    if (p.Condition?.Contains(version) == true)
                    {
                        profilesForVersion.Add(p);
                    }
                }

                if (profilesForVersion.Any())
                {
                    foreach (var profile in profilesForVersion)
                    {
                        if (profile.Profile.StartsWith("Profile"))
                        {
                            frameworks.Add(ConvertPortableClassLibrary(profile.Profile));
                        }
                        else
                        {
                            frameworks.Add(ConvertFrameworkVersion(version, profile.Profile));
                        }
                    }
                }
                else
                {
                    frameworks.Add(ConvertFrameworkVersion(version, null));
                }
            }
        }
        else
        {
            // Just convert the current framework
            frameworks.Add(ConvertTargetFramework(project));
        }

        // Always add a modern framework for compatibility
        if (!frameworks.Any(f => f.StartsWith("net") && !f.StartsWith("net4") && !f.StartsWith("netcoreapp")))
        {
            frameworks.Add("net8.0");
        }

        return frameworks.OrderBy(f => f).ToList();
    }

    private string ConvertFrameworkVersion(string version, string? profile)
    {
        // Handle Client Profile
        if (profile == "Client")
        {
            _logger.LogInformation("Converting .NET Framework Client Profile to full framework");
        }

        // .NET Framework 4.x
        if (version.StartsWith("4."))
        {
            return $"net{version.Replace(".", "")}";
        }

        // .NET Framework 3.5 and earlier
        if (version == "3.5") return "net35";
        if (version == "3.0") return "net30";
        if (version == "2.0") return "net20";

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

    private string ConvertPortableClassLibrary(string profile)
    {
        // Map PCL profiles to .NET Standard versions
        var pclToNetStandardMap = new Dictionary<string, string>
        {
            ["Profile7"] = "netstandard1.1",    // .NET Framework 4.5, Windows 8
            ["Profile31"] = "netstandard1.0",   // Windows 8.1, Windows Phone 8.1
            ["Profile32"] = "netstandard1.2",   // Windows 8.1, Windows Phone 8.1
            ["Profile44"] = "netstandard1.2",   // .NET Framework 4.5.1, Windows 8.1
            ["Profile49"] = "netstandard1.0",   // .NET Framework 4.5, Windows Phone 8
            ["Profile78"] = "netstandard1.0",   // .NET Framework 4.5, Windows 8, Windows Phone 8
            ["Profile84"] = "netstandard1.0",   // Windows Phone 8.1
            ["Profile111"] = "netstandard1.1",  // .NET Framework 4.5, Windows 8, Windows Phone 8.1
            ["Profile151"] = "netstandard1.2",  // .NET Framework 4.5.1, Windows 8.1, Windows Phone 8.1
            ["Profile157"] = "netstandard1.0",  // Windows 8.1, Windows Phone 8.1, Windows Phone 8
            ["Profile259"] = "netstandard1.0"   // .NET Framework 4.5, Windows 8, Windows Phone 8.1, Windows Phone 8
        };

        if (pclToNetStandardMap.TryGetValue(profile, out var netStandard))
        {
            _logger.LogInformation("Converting PCL {Profile} to {NetStandard}", profile, netStandard);
            return netStandard;
        }

        _logger.LogWarning("Unknown PCL profile {Profile}, defaulting to netstandard2.0", profile);
        return "netstandard2.0";
    }

    private async Task<List<PackageReference>> MigratePackageReferencesAsync(Project project, XElement projectElement, HashSet<string> centrallyManagedPackages, MigrationResult result, string sdkType, CancellationToken cancellationToken)
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

        // 3. Detect and handle test project specifics
        var testProjectInfo = await _testProjectHandler.DetectTestFrameworkAsync(project, cancellationToken);
        if (testProjectInfo.DetectedFrameworks.Any())
        {
            _logger.LogInformation("Detected test project with frameworks: {Frameworks}", 
                string.Join(", ", testProjectInfo.DetectedFrameworks));
            
            // Let the test handler add/update packages and configuration
            var packageList = allPackageReferences.ToList();
            await _testProjectHandler.MigrateTestConfigurationAsync(
                testProjectInfo, 
                projectElement, 
                packageList,
                result,
                cancellationToken);
            
            // Update the set with any new packages added by test handler
            allPackageReferences.Clear();
            foreach (var pkg in packageList)
            {
                allPackageReferences.Add(pkg);
            }
        }

        // Filter out packages that are implicit in SystemWeb SDK
        if (sdkType == "MSBuild.SDK.SystemWeb")
        {
            var systemWebImplicitPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.AspNet.Mvc",
                "Microsoft.AspNet.WebApi",
                "Microsoft.AspNet.WebApi.Core",
                "Microsoft.AspNet.WebApi.WebHost",
                "Microsoft.AspNet.WebPages",
                "Microsoft.AspNet.Razor",
                "Microsoft.Web.Infrastructure",
                "System.Web.Helpers",
                "System.Web.Mvc",
                "System.Web.Optimization",
                "System.Web.Razor",
                "System.Web.WebPages",
                "System.Web.WebPages.Deployment",
                "System.Web.WebPages.Razor"
            };

            var beforeCount = allPackageReferences.Count;
            allPackageReferences = allPackageReferences
                .Where(p => !systemWebImplicitPackages.Contains(p.PackageId))
                .ToHashSet(new PackageReferenceComparer());
            
            if (beforeCount > allPackageReferences.Count)
            {
                _logger.LogInformation("Removed {Count} implicit SystemWeb SDK packages", beforeCount - allPackageReferences.Count);
            }
        }

        if (allPackageReferences.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            _logger.LogInformation("CPM Debug: Processing {Count} package references. CPM enabled: {CpmEnabled}", 
                allPackageReferences.Count, _centralPackageManagementEnabled);

            foreach (var package in allPackageReferences)
            {
                var packageRef = new XElement("PackageReference",
                    new XAttribute("Include", package.PackageId));

                // Only add version if not centrally managed
                if (!_centralPackageManagementEnabled && !centrallyManagedPackages.Contains(package.PackageId))
                {
                    _logger.LogInformation("CPM Debug: Adding version {Version} to package {PackageId} (CPM enabled: {CpmEnabled})", 
                        package.Version ?? "*", package.PackageId, _centralPackageManagementEnabled);
                    packageRef.Add(new XAttribute("Version", package.Version ?? "*"));
                }
                else
                {
                    _logger.LogInformation("CPM Debug: Package {PackageId} is centrally managed (CPM enabled: {CpmEnabled}, existing: {InExisting}), omitting version", 
                        package.PackageId, _centralPackageManagementEnabled, centrallyManagedPackages.Contains(package.PackageId));
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
        
        return allPackageReferences.ToList();
    }

    private void MigrateProjectReferences(Project project, XElement projectElement)
    {
        var projectRefs = GetExplicitItemsFromXml(project, "ProjectReference");

        if (projectRefs.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var (include, metadata) in projectRefs)
            {
                var expandedInclude = SafeExpandString(project, include);
                var element = new XElement("ProjectReference",
                    new XAttribute("Include", expandedInclude));
                
                // Only preserve essential metadata that affects build behavior
                // SDK-style projects don't need Name, Project GUID, or most legacy metadata
                foreach (var (key, value) in metadata)
                {
                    if (!string.IsNullOrEmpty(value) && IsEssentialProjectReferenceMetadata(key))
                    {
                        element.Add(new XElement(key, SafeExpandString(project, value)));
                    }
                }
                
                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
        }
    }

    private void MigrateCompileItems(Project project, XElement projectElement, string sdkType)
    {
        // For SystemWeb SDK, exclude all .cs and .resx files
        var compileItems = GetExplicitItemsFromXml(project, "Compile")
            .Where(item => 
            {
                var expandedInclude = SafeExpandString(project, item.Include);
                
                // For SystemWeb SDK, exclude all .cs and .resx files
                if (sdkType == "MSBuild.SDK.SystemWeb")
                {
                    if (expandedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        expandedInclude.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Excluding {ItemType} item '{Include}' from SystemWeb SDK project", "Compile", expandedInclude);
                        return false;
                    }
                    return !IsAssemblyInfoFile(expandedInclude);
                }
                
                // For other SDKs, just exclude .cs files and AssemblyInfo
                return !expandedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                       !IsAssemblyInfoFile(expandedInclude);
            })
            .ToList();

        if (compileItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var (include, metadata) in compileItems)
            {
                var expandedInclude = SafeExpandString(project, include);
                var element = new XElement("Compile",
                    new XAttribute("Include", expandedInclude));

                // Add metadata
                foreach (var (key, value) in metadata)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        element.Add(new XElement(key, SafeExpandString(project, value)));
                    }
                }
                
                itemGroup.Add(element);
            }

            projectElement.Add(itemGroup);
        }
        
        // Handle .cs files with metadata using Update items (not for SystemWeb SDK)
        if (sdkType != "MSBuild.SDK.SystemWeb")
        {
            MigrateCsFilesWithMetadata(project, projectElement);
        }
    }

    private void MigrateCsFilesWithMetadata(Project project, XElement projectElement)
    {
        // Find .cs files that have metadata that needs to be preserved (only from explicit XML)
        var csFilesWithMetadata = GetExplicitItemsFromXml(project, "Compile")
            .Where(item =>
            {
                var expandedInclude = SafeExpandString(project, item.Include);
                if (!expandedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    IsAssemblyInfoFile(expandedInclude))
                {
                    return false;
                }
                
                // Check if it's a designer file
                var fileName = Path.GetFileName(expandedInclude);
                if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping designer file with metadata: {File}", expandedInclude);
                    return false;
                }
                
                // Check if it has important metadata
                var importantMetadata = new[] { "DependentUpon", "SubType", "Generator", "LastGenOutput", "DesignTime", "AutoGen", "CustomToolNamespace", "Link" };
                return item.Metadata.Any(m => importantMetadata.Contains(m.Key) && !string.IsNullOrEmpty(m.Value));
            })
            .ToList();

        if (csFilesWithMetadata.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var (include, metadata) in csFilesWithMetadata)
            {
                var expandedInclude = SafeExpandString(project, include);
                
                // Linked files need Include (they're outside project directory)
                // Other files with metadata use Update (they're auto-included by SDK)
                var hasLink = metadata.ContainsKey("Link") && !string.IsNullOrEmpty(metadata["Link"]);
                var attributeName = hasLink ? "Include" : "Update";
                var element = new XElement("Compile",
                    new XAttribute(attributeName, expandedInclude));

                // Add metadata
                foreach (var (key, value) in metadata)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        element.Add(new XElement(key, SafeExpandString(project, value)));
                    }
                }
                
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

    private void MigrateContentAndResources(Project project, XElement projectElement, string sdkType)
    {
        // Get content items from explicit XML only - ignore implicit MSBuild items
        var contentItems = new List<ProjectItemElement>();
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items)
            {
                if ((item.ItemType == "Content" ||
                     item.ItemType == "None" ||
                     item.ItemType == "EmbeddedResource") &&
                    !_artifactDetector.IsItemArtifact(item.ItemType, item.Include))
                {
                    // For SystemWeb SDK, skip .resx and designer.cs files regardless of item type
                    if (sdkType == "MSBuild.SDK.SystemWeb")
                    {
                        var fileName = Path.GetFileName(item.Include);
                        if (item.Include.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) ||
                            item.Include.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                            item.Include.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Excluding {ItemType} file '{Include}' from SystemWeb SDK project", 
                                item.ItemType, item.Include);
                            continue;
                        }
                    }
                    
                    contentItems.Add(item);
                }
            }
        }

        if (contentItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");
            var hasItems = false;

            foreach (var item in contentItems)
            {
                // Check if this file is automatically included by the SDK
                if (IsAutomaticallyIncludedBySdk(item, project))
                {
                    // Check if the item has custom metadata that needs to be preserved
                    if (HasCustomMetadata(item))
                    {
                        // For SystemWeb SDK, skip .resx and designer.cs files entirely even with metadata
                        if (sdkType == "MSBuild.SDK.SystemWeb")
                        {
                            if (item.Include.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) ||
                                item.Include.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                                item.Include.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogDebug("Skipping {ItemType} file with metadata for SystemWeb SDK: {File}", 
                                    item.ItemType, item.Include);
                                continue;
                            }
                        }
                        
                        // For .resx files and other SDK-included files with metadata, use Update
                        var attributeName = "Update";
                        
                        // Exception: Files with Link metadata need Include (they're outside project)
                        var hasLinkMetadata = item.Metadata.Any(m => m.Name == "Link");
                        if (hasLinkMetadata)
                        {
                            attributeName = "Include";
                        }
                        
                        var element = new XElement(item.ItemType,
                            new XAttribute(attributeName, item.Include));
                        PreserveMetadata(item, element);
                        itemGroup.Add(element);
                        hasItems = true;

                        _logger.LogDebug("Using {Attribute} for SDK-default file with custom metadata: {File}", 
                            attributeName, item.Include);
                    }
                    else
                    {
                        // Skip entirely - SDK will handle it
                        _logger.LogDebug("Skipping SDK-default content file: {File}", item.Include);
                    }
                }
                else
                {
                    // For SystemWeb SDK, skip .resx and designer.cs files even if not auto-included
                    if (sdkType == "MSBuild.SDK.SystemWeb")
                    {
                        if (item.Include.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) ||
                            item.Include.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                            item.Include.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Skipping non-auto-included {ItemType} file for SystemWeb SDK: {File}", 
                                item.ItemType, item.Include);
                            continue;
                        }
                    }
                    
                    // Not auto-included, so we need to explicitly include it
                    var element = new XElement(item.ItemType,
                        new XAttribute("Include", item.Include));

                    PreserveMetadata(item, element);
                    itemGroup.Add(element);
                    hasItems = true;
                }
            }

            // Add items if any
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

    private void MigrateCustomImports(Project project, XElement projectElement, MigrationResult result)
    {
        // Get all imports from the legacy project
        var customImports = project.Xml.Imports
            .Where(i => !IsKnownSystemImport(i.Project))
            .ToList();

        foreach (var import in customImports)
        {
            // Check if user has decided to keep this import
            bool shouldKeepImport = true;
            
            if (_importScanResult != null)
            {
                // Find this import in the scan result
                var importInfo = _importScanResult.ImportGroups
                    .SelectMany(g => g.Imports)
                    .FirstOrDefault(i => 
                        i.ProjectPath == project.FullPath && 
                        i.ImportPath == import.Project);
                
                if (importInfo != null)
                {
                    shouldKeepImport = importInfo.UserDecision;
                    
                    if (!shouldKeepImport)
                    {
                        _logger.LogInformation("Skipping import {Import} based on user selection", import.Project);
                        result.Warnings.Add($"Removed import: {import.Project} (based on user selection)");
                        continue;
                    }
                }
            }
            
            var importElement = new XElement("Import",
                new XAttribute("Project", import.Project));
            
            if (!string.IsNullOrEmpty(import.Condition))
            {
                importElement.Add(new XAttribute("Condition", import.Condition));
            }
            
            if (!string.IsNullOrEmpty(import.Label))
            {
                importElement.Add(new XAttribute("Label", import.Label));
            }
            
            projectElement.Add(importElement);
            
            result.Warnings.Add($"Preserved custom import: {import.Project}. Verify compatibility with SDK-style projects.");
            _logger.LogInformation("Preserved custom import: {Import}", import.Project);
        }
    }

    private bool IsKnownSystemImport(string importPath)
    {
        if (string.IsNullOrEmpty(importPath))
            return false;

        // List of known system imports that are handled by the SDK
        var knownImports = new[]
        {
            "Microsoft.Common.props",
            "Microsoft.CSharp.targets",
            "Microsoft.VisualBasic.targets", 
            "Microsoft.FSharp.targets",
            "Microsoft.Common.targets",
            "Microsoft.WebApplication.targets",
            "Microsoft.NET.Sdk.props",
            "Microsoft.NET.Sdk.targets",
            "System.Data.Entity.Design.targets",
            "EntityFramework.targets",
            "Microsoft.Bcl.Build.targets",
            "Microsoft.TestPlatform.targets",
            "VSTest.targets"
        };

        return knownImports.Any(known => 
            importPath.EndsWith(known, StringComparison.OrdinalIgnoreCase) ||
            importPath.Contains($"\\{known}", StringComparison.OrdinalIgnoreCase) ||
            importPath.Contains($"/{known}", StringComparison.OrdinalIgnoreCase));
    }

    private void MigrateCustomTargets(Project project, XElement projectElement, MigrationResult result)
    {
        // Migrate all custom targets from the legacy project
        foreach (var target in project.Xml.Targets)
        {
            // Skip common targets that are handled by the SDK
            if (IsCommonSdkTarget(target.Name))
            {
                _logger.LogDebug("Skipping SDK-handled target: {TargetName}", target.Name);
                continue;
            }
            
            // Check if user has decided to keep this target
            bool shouldKeepTarget = true;
            
            if (_targetScanResult != null)
            {
                // Find this target in the scan result
                var targetInfo = _targetScanResult.TargetGroups
                    .SelectMany(g => g.Targets)
                    .FirstOrDefault(t => 
                        t.ProjectPath == project.FullPath && 
                        t.TargetName == target.Name);
                
                if (targetInfo != null)
                {
                    shouldKeepTarget = targetInfo.UserDecision;
                    
                    if (!shouldKeepTarget)
                    {
                        _logger.LogInformation("Skipping target {Target} based on user selection", target.Name);
                        result.Warnings.Add($"Removed target: {target.Name} (based on user selection)");
                        continue;
                    }
                }
            }

            var targetElement = new XElement("Target",
                new XAttribute("Name", target.Name));

            // Add target attributes
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                targetElement.Add(new XAttribute("BeforeTargets", target.BeforeTargets));
            if (!string.IsNullOrEmpty(target.AfterTargets))
                targetElement.Add(new XAttribute("AfterTargets", target.AfterTargets));
            if (!string.IsNullOrEmpty(target.DependsOnTargets))
                targetElement.Add(new XAttribute("DependsOnTargets", target.DependsOnTargets));
            if (!string.IsNullOrEmpty(target.Condition))
                targetElement.Add(new XAttribute("Condition", target.Condition));
            if (!string.IsNullOrEmpty(target.Inputs))
                targetElement.Add(new XAttribute("Inputs", target.Inputs));
            if (!string.IsNullOrEmpty(target.Outputs))
                targetElement.Add(new XAttribute("Outputs", target.Outputs));
            if (!string.IsNullOrEmpty(target.Returns))
                targetElement.Add(new XAttribute("Returns", target.Returns));

            // Migrate target tasks
            foreach (var task in target.Children)
            {
                var taskElement = ConvertProjectElementToXElement(task);
                if (taskElement != null)
                {
                    targetElement.Add(taskElement);
                }
            }

            projectElement.Add(targetElement);
            result.Warnings.Add($"Preserved custom target '{target.Name}'. Manual review recommended.");
            _logger.LogInformation("Preserved custom target: {TargetName}", target.Name);
        }

        // Use comprehensive build event migration
        _buildEventMigrator.MigrateBuildEvents(project, projectElement, result);

        // Migrate UsingTask elements
        foreach (var usingTask in project.Xml.UsingTasks)
        {
            var element = new XElement("UsingTask",
                new XAttribute("TaskName", usingTask.TaskName));
                
            if (!string.IsNullOrEmpty(usingTask.AssemblyFile))
                element.Add(new XAttribute("AssemblyFile", usingTask.AssemblyFile));
            if (!string.IsNullOrEmpty(usingTask.AssemblyName))
                element.Add(new XAttribute("AssemblyName", usingTask.AssemblyName));
            if (!string.IsNullOrEmpty(usingTask.Condition))
                element.Add(new XAttribute("Condition", usingTask.Condition));
            if (!string.IsNullOrEmpty(usingTask.TaskFactory))
                element.Add(new XAttribute("TaskFactory", usingTask.TaskFactory));
                
            projectElement.Add(element);
            result.Warnings.Add($"Preserved UsingTask '{usingTask.TaskName}'. Verify custom task compatibility.");
            _logger.LogInformation("Preserved UsingTask: {TaskName}", usingTask.TaskName);
        }
    }

    private void MigrateConditionalElements(Project project, XElement projectElement)
    {
        // Migrate Choose/When/Otherwise constructs from the root level
        foreach (var choose in project.Xml.ChooseElements)
        {
            var chooseElement = new XElement("Choose");
            
            foreach (var when in choose.WhenElements)
            {
                var whenElement = new XElement("When",
                    new XAttribute("Condition", when.Condition));
                
                // Process all children of the When element
                foreach (var child in when.Children)
                {
                    var childElement = ConvertProjectElementToXElement(child);
                    if (childElement != null)
                        whenElement.Add(childElement);
                }
                
                chooseElement.Add(whenElement);
            }
            
            // Add Otherwise element if present
            if (choose.OtherwiseElement != null)
            {
                var otherwiseElement = new XElement("Otherwise");
                foreach (var child in choose.OtherwiseElement.Children)
                {
                    var childElement = ConvertProjectElementToXElement(child);
                    if (childElement != null)
                        otherwiseElement.Add(childElement);
                }
                chooseElement.Add(otherwiseElement);
            }
            
            projectElement.Add(chooseElement);
            _logger.LogInformation("Preserved conditional Choose/When/Otherwise construct");
        }
    }

    private bool IsCommonSdkTarget(string targetName)
    {
        var commonTargets = new[]
        {
            "Build", "Rebuild", "Clean", "Compile", "Publish",
            "BeforeBuild", "AfterBuild", "BeforeRebuild", "AfterRebuild",
            "BeforeClean", "AfterClean", "BeforePublish", "AfterPublish",
            "BeforeCompile", "AfterCompile", "CoreCompile",
            "PrepareForBuild", "PrepareForRun", "PrepareResources",
            "AssignTargetPaths", "GetTargetPath", "GetCopyToOutputDirectoryItems"
        };

        return commonTargets.Contains(targetName, StringComparer.OrdinalIgnoreCase);
    }

    private XElement? ConvertProjectElementToXElement(Microsoft.Build.Construction.ProjectElement element)
    {
        switch (element)
        {
            case Microsoft.Build.Construction.ProjectTaskElement task:
                var taskElement = new XElement(task.Name);
                
                if (!string.IsNullOrEmpty(task.Condition))
                    taskElement.Add(new XAttribute("Condition", task.Condition));
                    
                foreach (var param in task.Parameters)
                {
                    taskElement.Add(new XAttribute(param.Key, param.Value));
                }
                
                // Handle task outputs
                foreach (var output in task.Outputs)
                {
                    var outputElement = new XElement("Output");
                    if (!string.IsNullOrEmpty(output.TaskParameter))
                        outputElement.Add(new XAttribute("TaskParameter", output.TaskParameter));
                    if (!string.IsNullOrEmpty(output.PropertyName))
                        outputElement.Add(new XAttribute("PropertyName", output.PropertyName));
                    if (!string.IsNullOrEmpty(output.ItemType))
                        outputElement.Add(new XAttribute("ItemName", output.ItemType));
                    if (!string.IsNullOrEmpty(output.Condition))
                        outputElement.Add(new XAttribute("Condition", output.Condition));
                    
                    taskElement.Add(outputElement);
                }
                
                return taskElement;
                
            case Microsoft.Build.Construction.ProjectPropertyGroupElement propGroup:
                var propGroupElement = new XElement("PropertyGroup");
                if (!string.IsNullOrEmpty(propGroup.Condition))
                    propGroupElement.Add(new XAttribute("Condition", propGroup.Condition));
                    
                foreach (var prop in propGroup.Properties)
                {
                    var propElement = new XElement(prop.Name, prop.Value);
                    if (!string.IsNullOrEmpty(prop.Condition))
                        propElement.Add(new XAttribute("Condition", prop.Condition));
                    propGroupElement.Add(propElement);
                }
                
                return propGroupElement;
                
            case Microsoft.Build.Construction.ProjectItemGroupElement itemGroup:
                var itemGroupElement = new XElement("ItemGroup");
                if (!string.IsNullOrEmpty(itemGroup.Condition))
                    itemGroupElement.Add(new XAttribute("Condition", itemGroup.Condition));
                    
                foreach (var item in itemGroup.Items)
                {
                    // Apply transformations to items inside conditional blocks
                    var transformedElement = TransformConditionalItem(item);
                    if (transformedElement != null)
                        itemGroupElement.Add(transformedElement);
                }
                
                return itemGroupElement;
                
            case Microsoft.Build.Construction.ProjectOnErrorElement onError:
                var onErrorElement = new XElement("OnError",
                    new XAttribute("ExecuteTargets", onError.ExecuteTargetsAttribute));
                if (!string.IsNullOrEmpty(onError.Condition))
                    onErrorElement.Add(new XAttribute("Condition", onError.Condition));
                return onErrorElement;
                
            case Microsoft.Build.Construction.ProjectChooseElement choose:
                var chooseElement = new XElement("Choose");
                
                foreach (var when in choose.WhenElements)
                {
                    var whenElement = new XElement("When",
                        new XAttribute("Condition", when.Condition));
                    
                    foreach (var child in when.Children)
                    {
                        var childElement = ConvertProjectElementToXElement(child);
                        if (childElement != null)
                            whenElement.Add(childElement);
                    }
                    
                    chooseElement.Add(whenElement);
                }
                
                if (choose.OtherwiseElement != null)
                {
                    var otherwiseElement = new XElement("Otherwise");
                    foreach (var child in choose.OtherwiseElement.Children)
                    {
                        var childElement = ConvertProjectElementToXElement(child);
                        if (childElement != null)
                            otherwiseElement.Add(childElement);
                    }
                    chooseElement.Add(otherwiseElement);
                }
                
                return chooseElement;
                
            default:
                _logger.LogWarning("Unsupported project element type: {Type}", element.GetType().Name);
                return null;
        }
    }

    private XElement? TransformConditionalItem(Microsoft.Build.Construction.ProjectItemElement item)
    {
        // Handle Reference items that should be converted to PackageReference
        if (item.ItemType == "Reference")
        {
            // Check if this reference can be converted to a package reference
            var transformedItem = TransformReferenceToPackageReference(item);
            if (transformedItem != null)
                return transformedItem;
        }
        
        // Handle COM references with proper metadata preservation
        if (item.ItemType == "COMReference")
        {
            return TransformCOMReference(item);
        }
        
        // For other item types, apply standard transformations while preserving structure
        return TransformStandardItem(item);
    }

    private XElement? TransformReferenceToPackageReference(Microsoft.Build.Construction.ProjectItemElement item)
    {
        try
        {
            // Create a temporary ProjectItem for the converter
            // Note: This is a simplified approach - ideally we'd integrate with AssemblyReferenceConverter
            var hintPathMetadata = item.Metadata.FirstOrDefault(m => m.Name == "HintPath");
            
            // If it has a HintPath pointing to packages folder, likely convertible
            if (hintPathMetadata != null && 
                (hintPathMetadata.Value.Contains("packages\\") || hintPathMetadata.Value.Contains("packages/")))
            {
                // Extract package name and attempt conversion
                var packageName = ExtractPackageNameFromReference(item.Include, hintPathMetadata.Value);
                if (!string.IsNullOrEmpty(packageName))
                {
                    _logger.LogDebug("Converting conditional Reference {Reference} to PackageReference", item.Include);
                    
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", packageName));
                    
                    // Try to extract version from HintPath
                    var version = ExtractVersionFromHintPath(hintPathMetadata.Value);
                    if (!string.IsNullOrEmpty(version))
                    {
                        packageElement.Add(new XAttribute("Version", version));
                    }
                    
                    // Preserve condition if present
                    if (!string.IsNullOrEmpty(item.Condition))
                        packageElement.Add(new XAttribute("Condition", item.Condition));
                    
                    return packageElement;
                }
            }
            
            // If not convertible, fall back to standard transformation
            return TransformStandardItem(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transform conditional Reference {Reference}", item.Include);
            return TransformStandardItem(item);
        }
    }

    private XElement TransformCOMReference(Microsoft.Build.Construction.ProjectItemElement item)
    {
        var comElement = new XElement("COMReference",
            new XAttribute("Include", item.Include));
        
        // Preserve condition if present
        if (!string.IsNullOrEmpty(item.Condition))
            comElement.Add(new XAttribute("Condition", item.Condition));
            
        // Preserve critical COM metadata
        var comMetadata = new[]
        {
            "Guid", "VersionMajor", "VersionMinor", "Lcid",
            "WrapperTool", "Isolated", "EmbedInteropTypes",
            "Private", "HintPath"
        };

        foreach (var metadataName in comMetadata)
        {
            var metadata = item.Metadata.FirstOrDefault(m => m.Name == metadataName);
            if (metadata != null && !string.IsNullOrEmpty(metadata.Value))
            {
                var metaElement = new XElement(metadataName, metadata.Value);
                if (!string.IsNullOrEmpty(metadata.Condition))
                    metaElement.Add(new XAttribute("Condition", metadata.Condition));
                comElement.Add(metaElement);
            }
        }

        // Ensure EmbedInteropTypes has a value (defaults differ between legacy and SDK)
        var hasEmbedInteropTypes = item.Metadata.Any(m => m.Name == "EmbedInteropTypes");
        if (!hasEmbedInteropTypes)
        {
            comElement.Add(new XElement("EmbedInteropTypes", "false"));
        }
        
        return comElement;
    }

    private XElement TransformStandardItem(Microsoft.Build.Construction.ProjectItemElement item)
    {
        var itemElement = new XElement(item.ItemType);
        
        // Add attributes
        if (!string.IsNullOrEmpty(item.Include))
            itemElement.Add(new XAttribute("Include", item.Include));
        if (!string.IsNullOrEmpty(item.Update))
            itemElement.Add(new XAttribute("Update", item.Update));
        if (!string.IsNullOrEmpty(item.Remove))
            itemElement.Add(new XAttribute("Remove", item.Remove));
        if (!string.IsNullOrEmpty(item.Exclude))
            itemElement.Add(new XAttribute("Exclude", item.Exclude));
        if (!string.IsNullOrEmpty(item.Condition))
            itemElement.Add(new XAttribute("Condition", item.Condition));
            
        // Add metadata, preserving MSBuild variables in paths
        foreach (var metadata in item.Metadata)
        {
            var value = metadata.Value;
            
            // For path-related metadata, preserve MSBuild variables
            if (metadata.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase) && 
                value.Contains("$("))
            {
                // Keep MSBuild variables as-is (e.g., $(SolutionDir), $(ProjectDir))
                _logger.LogDebug("Preserving MSBuild variable in conditional metadata {Name}: {Value}", 
                    metadata.Name, value);
            }
            
            var metaElement = new XElement(metadata.Name, value);
            if (!string.IsNullOrEmpty(metadata.Condition))
                metaElement.Add(new XAttribute("Condition", metadata.Condition));
            itemElement.Add(metaElement);
        }
        
        return itemElement;
    }

    private string? ExtractPackageNameFromReference(string referenceName, string hintPath)
    {
        try
        {
            // Simple heuristic: if reference name is a known assembly, map to package
            // This is a basic implementation - could be enhanced with a lookup table
            var knownMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "System.Web.Http", "Microsoft.AspNet.WebApi.Core" },
                { "EntityFramework", "EntityFramework" },
                { "Newtonsoft.Json", "Newtonsoft.Json" },
                { "Microsoft.Web.Infrastructure", "Microsoft.Web.Infrastructure" },
                { "System.Web.Mvc", "Microsoft.AspNet.Mvc" }
            };
            
            var assemblyName = referenceName.Split(',')[0].Trim();
            if (knownMappings.TryGetValue(assemblyName, out var packageName))
            {
                return packageName;
            }
            
            // Try to extract from hint path (e.g., packages\Newtonsoft.Json.12.0.3\lib...)
            var match = System.Text.RegularExpressions.Regex.Match(hintPath, 
                @"packages[/\\]([^/\\]+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
            if (match.Success)
            {
                var packageWithVersion = match.Groups[1].Value;
                // Remove version suffix (e.g., "Newtonsoft.Json.12.0.3" -> "Newtonsoft.Json")
                var versionMatch = System.Text.RegularExpressions.Regex.Match(packageWithVersion, @"^(.+?)\.(\d+\..*)$");
                if (versionMatch.Success)
                {
                    return versionMatch.Groups[1].Value;
                }
                return packageWithVersion;
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? ExtractVersionFromHintPath(string hintPath)
    {
        try
        {
            // Extract version from packages path (e.g., packages\Newtonsoft.Json.12.0.3\lib...)
            var match = System.Text.RegularExpressions.Regex.Match(hintPath, 
                @"packages[/\\][^/\\]+\.(\d+(?:\.\d+)*(?:-[^/\\]*)?)[/\\]", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            
            return null;
        }
        catch
        {
            return null;
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

        // Check if we're using SystemWeb SDK
        var sdkAttribute = projectElement.Attribute("Sdk")?.Value;
        var isSystemWebSdk = sdkAttribute?.StartsWith("MSBuild.SDK.SystemWeb", StringComparison.OrdinalIgnoreCase) ?? false;

        // Filter out System.Web related references if using SystemWeb SDK
        if (isSystemWebSdk)
        {
            var systemWebImplicitReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Web",
                "System.Web.Abstractions",
                "System.Web.ApplicationServices",
                "System.Web.DataVisualization",
                "System.Web.DynamicData",
                "System.Web.Entity",
                // System.Web.Extensions is NOT implicitly included and must be kept as explicit reference
                "System.Web.Mobile",
                "System.Web.RegularExpressions",
                "System.Web.Routing",
                "System.Web.Services"
            };

            var beforeCount = unconvertedReferences.Count;
            unconvertedReferences = unconvertedReferences
                .Where(r => !systemWebImplicitReferences.Contains(r.Identity.Name))
                .ToList();

            if (beforeCount > unconvertedReferences.Count)
            {
                _logger.LogInformation("Removed {Count} implicit SystemWeb SDK references", beforeCount - unconvertedReferences.Count);
            }
        }

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

        // Get all compiled files from explicit project XML (not MSBuild defaults)
        var compiledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items.Where(i => i.ItemType == "Compile"))
            {
                var expandedInclude = SafeExpandString(project, item.Include);
                if (!string.IsNullOrEmpty(expandedInclude))
                {
                    compiledFiles.Add(Path.GetFullPath(Path.Combine(projectDir, expandedInclude)));
                }
            }
        }

        // Find all .cs files in the project directory
        var allCsFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                       !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                       !f.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}"));

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

    private void MigrateDesignerItems(Project project, XElement projectElement, string sdkType)
    {
        // For SystemWeb SDK, skip this entirely as all designer files are auto-included
        if (sdkType == "MSBuild.SDK.SystemWeb")
        {
            _logger.LogDebug("Skipping designer items migration for SystemWeb SDK project");
            return;
        }
        
        // Get items from explicit XML only - ignore implicit MSBuild items
        var wpfItems = new List<ProjectItemElement>();
        var winFormsItems = new List<ProjectItemElement>();
        
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items)
            {
                // WPF items
                if (item.ItemType == "ApplicationDefinition" ||
                    item.ItemType == "Page" ||
                    item.ItemType == "Resource")
                {
                    wpfItems.Add(item);
                }
                
                // WinForms items with SubType
                if (item.ItemType == "Compile")
                {
                    var subTypeMetadata = item.Metadata.FirstOrDefault(m => m.Name == "SubType");
                    if (subTypeMetadata != null)
                    {
                        var subType = subTypeMetadata.Value;
                        if (subType == "Form" || subType == "UserControl" || subType == "Component")
                        {
                            winFormsItems.Add(item);
                        }
                    }
                }
            }
        }
        
        // Check if UseWPF will be set (affects how we handle WPF items)
        var hasWpfItems = wpfItems.Any(i => i.ItemType == "ApplicationDefinition" || i.ItemType == "Page");

        if (wpfItems.Any() || winFormsItems.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var item in wpfItems)
            {
                // When UseWPF is true, these are auto-included
                // Use Update if they have custom metadata, otherwise skip
                if (hasWpfItems && IsWpfItemAutoIncluded(item))
                {
                    if (HasCustomMetadata(item))
                    {
                        var element = new XElement(item.ItemType,
                            new XAttribute("Update", item.Include));
                        PreserveMetadata(item, element);
                        itemGroup.Add(element);
                    }
                    // Otherwise skip - SDK will handle it
                }
                else
                {
                    // Not auto-included, use Include
                    var element = new XElement(item.ItemType,
                        new XAttribute("Include", item.Include));
                    PreserveMetadata(item, element);
                    itemGroup.Add(element);
                }
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

    private void MigrateCustomItemTypes(Project project, XElement projectElement, string sdkType)
    {
        var standardTypes = new HashSet<string>
        {
            "Compile", "Content", "None", "EmbeddedResource",
            "Reference", "ProjectReference", "PackageReference",
            "Folder", "ApplicationDefinition", "Page", "Resource"
        };

        // Get custom items from explicit XML only - ignore implicit MSBuild items
        var allExplicitItems = new List<ProjectItemElement>();
        
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items)
            {
                allExplicitItems.Add(item);
            }
        }

        // Log items being filtered out
        var legacyItems = allExplicitItems
            .Where(i => LegacyProjectElements.ItemsToRemove.Contains(i.ItemType))
            .GroupBy(i => i.ItemType);

        foreach (var group in legacyItems)
        {
            _logger.LogInformation("Removing legacy item type '{ItemType}' ({Count} items)",
                group.Key, group.Count());
        }

        var customItems = allExplicitItems
            .Where(i => !standardTypes.Contains(i.ItemType))
            .Where(i => !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(i.ItemType))
            .Where(i => !LegacyProjectElements.ItemsToRemove.Contains(i.ItemType))
            .Where(i => !_artifactDetector.IsItemArtifact(i.ItemType, i.Include))
            .Where(i => 
            {
                // For SystemWeb SDK, skip .resx and designer.cs files
                if (sdkType == "MSBuild.SDK.SystemWeb")
                {
                    if (i.Include.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) ||
                        i.Include.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                        i.Include.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping custom item {ItemType} file for SystemWeb SDK: {File}", 
                            i.ItemType, i.Include);
                        return false;
                    }
                }
                return true;
            })
            .GroupBy(i => i.ItemType);

        foreach (var group in customItems)
        {
            _logger.LogDebug("Migrating custom item type: {ItemType}", group.Key);
            var itemGroup = new XElement("ItemGroup");
            foreach (var item in group)
            {
                var element = new XElement(item.ItemType,
                    new XAttribute("Include", item.Include));
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
        // Get COM references from explicit XML only - ignore implicit MSBuild items
        var comReferences = new List<ProjectItemElement>();
        
        foreach (var itemGroup in project.Xml.ItemGroups)
        {
            foreach (var item in itemGroup.Items)
            {
                if (item.ItemType == "COMReference")
                {
                    comReferences.Add(item);
                }
            }
        }

        if (comReferences.Any())
        {
            var itemGroup = new XElement("ItemGroup");

            foreach (var comRef in comReferences)
            {
                var element = new XElement("COMReference",
                    new XAttribute("Include", comRef.Include));

                // Preserve critical COM metadata
                var comMetadata = new[]
                {
                    "Guid", "VersionMajor", "VersionMinor", "Lcid",
                    "WrapperTool", "Isolated", "EmbedInteropTypes",
                    "Private", "HintPath"
                };

                foreach (var metadataName in comMetadata)
                {
                    var metadata = comRef.Metadata.FirstOrDefault(m => m.Name == metadataName);
                    if (metadata != null && !string.IsNullOrEmpty(metadata.Value))
                    {
                        element.Add(new XElement(metadataName, metadata.Value));
                    }
                }

                // Ensure EmbedInteropTypes has a value (defaults differ between legacy and SDK)
                var hasEmbedInteropTypes = comRef.Metadata.Any(m => m.Name == "EmbedInteropTypes");
                if (!hasEmbedInteropTypes)
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
                
                // TypeScript files
                if (extension == ".ts" || extension == ".tsx")
                {
                    return true;
                }
                
                // Documentation files
                if (extension == ".md" || extension == ".txt")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsWpfItemAutoIncluded(ProjectItem item)
    {
        var extension = Path.GetExtension(item.EvaluatedInclude).ToLowerInvariant();
        var fileName = Path.GetFileName(item.EvaluatedInclude);
        var fileNameLower = fileName?.ToLowerInvariant();
        
        // ApplicationDefinition: App.xaml or Application.xaml
        if (item.ItemType == "ApplicationDefinition")
        {
            if (fileNameLower == "app.xaml" || fileNameLower == "application.xaml")
            {
                return true;
            }
        }
        
        // Page: all .xaml files except App.xaml/Application.xaml
        if (item.ItemType == "Page" && extension == ".xaml")
        {
            if (fileNameLower != "app.xaml" && fileNameLower != "application.xaml")
            {
                return true;
            }
        }
        
        // Resource: image files and other resources
        if (item.ItemType == "Resource")
        {
            var imageExtensions = new[] { ".bmp", ".ico", ".gif", ".jpg", ".jpeg", ".png", ".tiff", ".pdf" };
            if (imageExtensions.Contains(extension))
            {
                return true;
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

    // Overloaded helper methods for XML elements (ProjectItemElement instead of ProjectItem)
    
    private bool HasCustomMetadata(ProjectItemElement item)
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
            var metadataElement = item.Metadata.FirstOrDefault(m => m.Name == metadata);
            if (metadataElement != null && !string.IsNullOrEmpty(metadataElement.Value))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAutomaticallyIncludedBySdk(ProjectItemElement item, Project? project = null)
    {
        var fileName = Path.GetFileName(item.Include);
        var extension = Path.GetExtension(item.Include)?.ToLowerInvariant();
        var fileNameLower = fileName?.ToLowerInvariant();

        // SDK automatically includes certain files as Content
        if (item.ItemType == "Content")
        {
            // Check if this is likely a web project (will use Web SDK or SystemWeb SDK)
            if (project != null)
            {
                var projectTypeGuids = project.Properties
                    .FirstOrDefault(p => p.Name == "ProjectTypeGuids")?.EvaluatedValue;
                var isWebProject = projectTypeGuids?.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase) ?? false;
                var isWebSiteProject = projectTypeGuids?.Contains("{E24C65DC-7377-472B-9ABA-BC803B73C61A}", StringComparison.OrdinalIgnoreCase) ?? false;

                if (isWebProject || isWebSiteProject)
                {
                    // Check if this will be a SystemWeb SDK project
                    var targetFramework = ConvertTargetFramework(project);
                    var isSystemWebSdk = targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase);
                    
                    if (isSystemWebSdk)
                    {
                        // MSBuild.SDK.SystemWeb auto-includes these patterns
                        var systemWebPatterns = new[]
                        {
                            "wwwroot/**/*",
                            "Areas/**/*",
                            "Views/**/*",
                            "Content/**/*",
                            "Scripts/**/*",
                            "fonts/**/*",
                            "*.cshtml",
                            "*.aspx",
                            "*.ascx",
                            "*.asax",
                            "*.ashx",
                            "*.asmx",
                            "*.htm",
                            "*.html",
                            "*.css",
                            "*.js",
                            "*.json",
                            "*.map",
                            "web.config",
                            "*.config"
                        };

                        foreach (var pattern in systemWebPatterns)
                        {
                            if (IsMatchingPattern(item.Include, pattern))
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        // Microsoft.NET.Sdk.Web auto-includes these patterns
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
                            if (IsMatchingPattern(item.Include, pattern))
                            {
                                return true;
                            }
                        }
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
            var hasGenerator = item.Metadata.Any(m => m.Name == "Generator");
            var hasLastGenOutput = item.Metadata.Any(m => m.Name == "LastGenOutput");
            if (!hasGenerator && !hasLastGenOutput)
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

    private void PreserveMetadata(ProjectItemElement item, XElement element)
    {
        // Critical metadata to preserve
        var importantMetadata = new[]
        {
            "Link", "DependentUpon", "SubType", "Generator",
            "LastGenOutput", "CopyToOutputDirectory", "Private",
            "SpecificVersion", "CustomToolNamespace", "DesignTime",
            "AutoGen", "DesignTimeSharedInput"
        };

        foreach (var metadataName in importantMetadata)
        {
            var metadata = item.Metadata.FirstOrDefault(m => m.Name == metadataName);
            if (metadata != null && !string.IsNullOrEmpty(metadata.Value))
            {
                element.Add(new XElement(metadataName, metadata.Value));
            }
        }
    }

    private bool IsWpfItemAutoIncluded(ProjectItemElement item)
    {
        var extension = Path.GetExtension(item.Include).ToLowerInvariant();
        var fileName = Path.GetFileName(item.Include);
        var fileNameLower = fileName?.ToLowerInvariant();
        
        // ApplicationDefinition: App.xaml or Application.xaml
        if (item.ItemType == "ApplicationDefinition")
        {
            if (fileNameLower == "app.xaml" || fileNameLower == "application.xaml")
            {
                return true;
            }
        }
        
        // Page: all .xaml files except App.xaml/Application.xaml
        if (item.ItemType == "Page" && extension == ".xaml")
        {
            if (fileNameLower != "app.xaml" && fileNameLower != "application.xaml")
            {
                return true;
            }
        }
        
        // Resource: image files and other resources
        if (item.ItemType == "Resource")
        {
            var imageExtensions = new[] { ".bmp", ".ico", ".gif", ".jpg", ".jpeg", ".png", ".tiff", ".pdf" };
            if (imageExtensions.Contains(extension))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private static bool IsEssentialProjectReferenceMetadata(string metadataKey)
    {
        // Only preserve metadata that affects build behavior in SDK-style projects
        // Exclude common legacy metadata that SDK-style projects don't need:
        // - Project: GUID of the referenced project (not used in SDK-style)
        // - Name: Display name of the project (MSBuild can determine from file)
        // - SpecificVersion: Not typically needed in SDK-style projects
        
        var legacyMetadataToExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Project",           // Project GUID - not used in SDK-style projects
            "Name",              // Project name - MSBuild determines this automatically
            "SpecificVersion",   // Usually not needed in SDK-style projects
            "Package"            // Legacy metadata
        };
        
        // Preserve metadata that might affect build behavior
        var essentialMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Private",           // Controls whether assembly is copied to output
            "IncludeAssets",     // Controls which assets are included
            "ExcludeAssets",     // Controls which assets are excluded
            "PrivateAssets",     // Controls asset flow to consuming projects
            "ReferenceOutputAssembly", // Controls if assembly should be referenced
            "OutputItemType",    // Controls output item type
            "SetTargetFramework" // Framework targeting for multi-target scenarios
        };
        
        // Don't include legacy metadata that SDK-style projects don't need
        if (legacyMetadataToExclude.Contains(metadataKey))
        {
            return false;
        }
        
        // Include known essential metadata
        if (essentialMetadata.Contains(metadataKey))
        {
            return true;
        }
        
        // By default, exclude unknown metadata to keep ProjectReferences clean
        // SDK-style projects work well with minimal metadata
        return false;
    }

    /// <summary>
    /// Applies special project type-specific handling during migration
    /// </summary>
    private async Task ApplySpecialProjectTypeHandling(
        Project legacyProject,
        XElement projectElement,
        MigrationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectTypeDetected = false;
            var packageReferences = new List<PackageReference>();

            // Extract existing package references for handler use
            ExtractExistingPackageReferences(legacyProject, packageReferences);

            // Azure Functions projects
            if (HasPackageReference(legacyProject, "Microsoft.NET.Sdk.Functions") ||
                HasPackageReference(legacyProject, "Microsoft.Azure.WebJobs") ||
                HasPackageReference(legacyProject, "Microsoft.Azure.Functions"))
            {
                _azureFunctionsHandler.SetGenerateModernProgramCs(_generateModernProgramCs);
                var info = await _azureFunctionsHandler.DetectFunctionsConfigurationAsync(legacyProject, cancellationToken);
                await _azureFunctionsHandler.MigrateFunctionsProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Azure Functions specific handling");
            }

            // MAUI/Xamarin projects
            if (HasPackageReference(legacyProject, "Microsoft.Maui") ||
                HasPackageReference(legacyProject, "Xamarin.Forms"))
            {
                _mauiProjectHandler.SetGenerateModernProgramCs(_generateModernProgramCs);
                var info = await _mauiProjectHandler.DetectMauiConfigurationAsync(legacyProject, cancellationToken);
                await _mauiProjectHandler.MigrateMauiProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied MAUI/Xamarin specific handling");
            }

            // Blazor projects
            if (HasPackageReference(legacyProject, "Microsoft.AspNetCore.Components.WebAssembly") ||
                HasPackageReference(legacyProject, "Microsoft.AspNetCore.Components.Server"))
            {
                _blazorProjectHandler.SetGenerateModernProgramCs(_generateModernProgramCs);
                var info = await _blazorProjectHandler.DetectBlazorConfigurationAsync(legacyProject, cancellationToken);
                await _blazorProjectHandler.MigrateBlazorProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Blazor specific handling");
            }

            // Worker Service projects
            if (HasPackageReference(legacyProject, "Microsoft.Extensions.Hosting") ||
                HasPackageReference(legacyProject, "Microsoft.Extensions.Hosting.WindowsServices") ||
                HasPackageReference(legacyProject, "Microsoft.Extensions.Hosting.Systemd"))
            {
                _workerServiceHandler.SetGenerateModernProgramCs(_generateModernProgramCs);
                var info = await _workerServiceHandler.DetectWorkerServiceConfigurationAsync(legacyProject, cancellationToken);
                await _workerServiceHandler.MigrateWorkerServiceProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Worker Service specific handling");
            }

            // gRPC projects
            if (HasPackageReference(legacyProject, "Grpc.AspNetCore") ||
                HasPackageReference(legacyProject, "Grpc.Tools") ||
                legacyProject.AllEvaluatedItems.Any(item => item.ItemType == "Protobuf"))
            {
                _grpcServiceHandler.SetGenerateModernProgramCs(_generateModernProgramCs);
                var info = await _grpcServiceHandler.DetectGrpcConfigurationAsync(legacyProject, cancellationToken);
                await _grpcServiceHandler.MigrateGrpcProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied gRPC specific handling");
            }

            // UWP projects
            var projectTypeGuids = legacyProject.GetPropertyValue("ProjectTypeGuids");
            if (projectTypeGuids.Contains("A5A43C5B-DE2A-4C0C-9213-0A381AF9435A"))
            {
                var info = await _uwpProjectHandler.DetectUwpConfigurationAsync(legacyProject, cancellationToken);
                await _uwpProjectHandler.MigrateUwpProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied UWP specific handling");
            }

            // Database projects (.sqlproj)
            if (legacyProject.FullPath.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase))
            {
                var info = await _databaseProjectHandler.DetectDatabaseConfigurationAsync(legacyProject, cancellationToken);
                await _databaseProjectHandler.MigrateDatabaseProjectAsync(info, projectElement, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Database project specific handling");
            }

            // Docker projects (.dcproj)
            if (legacyProject.FullPath.EndsWith(".dcproj", StringComparison.OrdinalIgnoreCase))
            {
                var info = await _dockerProjectHandler.DetectDockerConfigurationAsync(legacyProject, cancellationToken);
                await _dockerProjectHandler.MigrateDockerProjectAsync(info, projectElement, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Docker project specific handling");
            }

            // Shared projects (.shproj)
            if (legacyProject.FullPath.EndsWith(".shproj", StringComparison.OrdinalIgnoreCase))
            {
                var info = await _sharedProjectHandler.DetectSharedProjectConfigurationAsync(legacyProject, cancellationToken);
                var guidance = await _sharedProjectHandler.GetMigrationGuidanceAsync(info, result, cancellationToken);
                
                if (guidance.ShouldConvertToClassLibrary)
                {
                    var convertedProject = await _sharedProjectHandler.ConvertToClassLibraryAsync(info, Path.GetDirectoryName(legacyProject.FullPath) ?? string.Empty, cancellationToken);
                    if (convertedProject != null)
                    {
                        // Replace project element with converted class library
                        projectElement.ReplaceWith(convertedProject);
                        result.Warnings.Add("Shared project converted to class library - update referencing projects");
                    }
                }
                
                projectTypeDetected = true;
                _logger.LogInformation("Applied Shared project specific handling");
            }

            // Office/VSTO projects
            if (HasInteropReference(legacyProject, "Microsoft.Office.Interop") ||
                HasPackageReference(legacyProject, "Microsoft.Office"))
            {
                var info = await _officeProjectHandler.DetectOfficeConfigurationAsync(legacyProject, cancellationToken);
                await _officeProjectHandler.MigrateOfficeProjectAsync(info, projectElement, packageReferences, result, cancellationToken);
                projectTypeDetected = true;
                _logger.LogInformation("Applied Office/VSTO specific handling");
            }

            if (!projectTypeDetected)
            {
                _logger.LogDebug("No special project type handling required for: {ProjectPath}", legacyProject.FullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply special project type handling for {ProjectPath}", legacyProject.FullPath);
            result.Warnings.Add($"Special project type handling failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts existing package references from the legacy project
    /// </summary>
    private void ExtractExistingPackageReferences(Project legacyProject, List<PackageReference> packageReferences)
    {
        var packageItems = legacyProject.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference");

        foreach (var item in packageItems)
        {
            var packageRef = new PackageReference
            {
                PackageId = item.EvaluatedInclude,
                Version = item.GetMetadataValue("Version")
            };

            if (!string.IsNullOrEmpty(item.GetMetadataValue("PrivateAssets")))
                packageRef.Metadata["PrivateAssets"] = item.GetMetadataValue("PrivateAssets");

            packageReferences.Add(packageRef);
        }
    }

    /// <summary>
    /// Checks if a project has a specific interop reference
    /// </summary>
    private bool HasInteropReference(Project project, string interopName)
    {
        return project.AllEvaluatedItems
            .Where(item => item.ItemType == "Reference" || item.ItemType == "COMReference")
            .Any(item => item.EvaluatedInclude.Contains(interopName, StringComparison.OrdinalIgnoreCase));
    }
}