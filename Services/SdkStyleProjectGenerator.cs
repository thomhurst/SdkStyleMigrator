using System.Text.RegularExpressions;
using System.Xml;
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
    private readonly INuGetPackageResolver _nugetResolver;
    private readonly INuSpecExtractor _nuspecExtractor;
    private readonly ProjectTypeDetector _projectTypeDetector;
    private readonly BuildEventMigrator _buildEventMigrator;
    private readonly DeploymentDetector _deploymentDetector;
    private readonly NativeDependencyHandler _nativeDependencyHandler;
    private readonly ServiceReferenceDetector _serviceReferenceDetector;
    private readonly MigrationOptions _options;
    private readonly CustomTargetAnalyzer _customTargetAnalyzer;
    private readonly EntityFrameworkMigrationHandler _entityFrameworkHandler;
    private readonly T4TemplateHandler _t4TemplateHandler;
    private readonly NuGetAssetsResolver _nugetAssetsResolver;

    public SdkStyleProjectGenerator(
        ILogger<SdkStyleProjectGenerator> logger,
        IPackageReferenceMigrator packageReferenceMigrator,
        ITransitiveDependencyDetector transitiveDependencyDetector,
        INuGetPackageResolver nugetResolver,
        INuSpecExtractor nuspecExtractor,
        ProjectTypeDetector projectTypeDetector,
        BuildEventMigrator buildEventMigrator,
        DeploymentDetector deploymentDetector,
        NativeDependencyHandler nativeDependencyHandler,
        ServiceReferenceDetector serviceReferenceDetector,
        CustomTargetAnalyzer customTargetAnalyzer,
        EntityFrameworkMigrationHandler entityFrameworkHandler,
        T4TemplateHandler t4TemplateHandler,
        NuGetAssetsResolver nugetAssetsResolver,
        MigrationOptions options)
    {
        _logger = logger;
        _packageReferenceMigrator = packageReferenceMigrator;
        _transitiveDependencyDetector = transitiveDependencyDetector;
        _nugetResolver = nugetResolver;
        _nuspecExtractor = nuspecExtractor;
        _projectTypeDetector = projectTypeDetector;
        _buildEventMigrator = buildEventMigrator;
        _deploymentDetector = deploymentDetector;
        _nativeDependencyHandler = nativeDependencyHandler;
        _serviceReferenceDetector = serviceReferenceDetector;
        _customTargetAnalyzer = customTargetAnalyzer;
        _entityFrameworkHandler = entityFrameworkHandler;
        _t4TemplateHandler = t4TemplateHandler;
        _nugetAssetsResolver = nugetAssetsResolver;
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
            
            // Check if project is already SDK-style
            if (!string.IsNullOrEmpty(legacyProject.Xml.Sdk))
            {
                _logger.LogInformation("Project {ProjectPath} is already SDK-style (SDK: {Sdk}), no migration needed", 
                    legacyProject.FullPath, legacyProject.Xml.Sdk);
                result.Success = true;
                result.Warnings.Add($"Project is already SDK-style with SDK '{legacyProject.Xml.Sdk}' - no migration needed");
                return result;
            }

            // 1. Detect project type first
            var projectTypeInfo = _projectTypeDetector.DetectProjectType(legacyProject);
            result.DetectedProjectType = projectTypeInfo;
            
            if (!projectTypeInfo.CanMigrate)
            {
                result.Success = false;
                result.HasCriticalBlockers = true;
                result.Errors.Add(projectTypeInfo.MigrationBlocker!);
                _logger.LogError("Project cannot be migrated: {Reason}", projectTypeInfo.MigrationBlocker);
                return result;
            }

            // 2. Check deployment method
            var deploymentInfo = _deploymentDetector.DetectDeploymentMethod(legacyProject);
            result.DeploymentInfo = deploymentInfo;
            _deploymentDetector.AddDeploymentWarnings(deploymentInfo, result);

            // 3. Detect service references
            var serviceRefInfo = _serviceReferenceDetector.DetectServiceReferences(legacyProject);
            result.ServiceReferences = serviceRefInfo;
            _serviceReferenceDetector.AddServiceReferenceWarnings(serviceRefInfo, result);

            // 4. Detect native dependencies
            var nativeDeps = _nativeDependencyHandler.DetectNativeDependencies(legacyProject);
            result.NativeDependencies = nativeDeps;

            // Check if the output file already exists and has SDK attribute (idempotent check)
            if (File.Exists(outputPath) && outputPath == legacyProject.FullPath)
            {
                var existingDoc = XDocument.Load(outputPath);
                if (existingDoc.Root?.Attribute("Sdk") != null)
                {
                    _logger.LogInformation("Project {ProjectPath} already has SDK attribute, skipping migration", outputPath);
                    result.Success = true;
                    result.Warnings.Add("Project already migrated to SDK-style format");
                    return result;
                }
            }

            var sdkProject = new XDocument();
            var projectElement = new XElement("Project");
            
            // Use detected SDK or fall back to heuristics
            var sdk = projectTypeInfo.SuggestedSdk ?? DetermineSdk(legacyProject);
            projectElement.Add(new XAttribute("Sdk", sdk));
            
            sdkProject.Add(projectElement);

            var propertyGroup = MigrateProperties(legacyProject, result);
            
            // Add required properties from project type detection
            if (projectTypeInfo.RequiredProperties.Any())
            {
                foreach (var prop in projectTypeInfo.RequiredProperties)
                {
                    if (!string.IsNullOrEmpty(prop.Value))
                    {
                        propertyGroup.Add(new XElement(prop.Key, prop.Value));
                        _logger.LogInformation("Added required property {Property} = {Value}", prop.Key, prop.Value);
                    }
                }
            }
            
            // Extract and migrate NuSpec metadata if present
            var nuspecPath = await _nuspecExtractor.FindNuSpecFileAsync(legacyProject.FullPath, cancellationToken);
            if (!string.IsNullOrEmpty(nuspecPath))
            {
                var nuspecMetadata = await _nuspecExtractor.ExtractMetadataAsync(nuspecPath, cancellationToken);
                if (nuspecMetadata != null)
                {
                    MigrateNuSpecMetadata(propertyGroup, nuspecMetadata, result);
                    result.RemovedElements.Add($"NuSpec file: {Path.GetFileName(nuspecPath)} (metadata migrated to project file)");
                }
            }
            
            if (propertyGroup.HasElements)
            {
                projectElement.Add(propertyGroup);
            }

            var packages = await _packageReferenceMigrator.MigratePackagesAsync(legacyProject, cancellationToken);
            var projectDirectory = Path.GetDirectoryName(legacyProject.FullPath);
            packages = await _transitiveDependencyDetector.DetectTransitiveDependenciesAsync(packages, projectDirectory, cancellationToken);
            
            var packagesToInclude = packages.Where(p => !p.IsTransitive).ToList();
            
            // Add required package references from project type detection
            if (projectTypeInfo.RequiredPackageReferences.Any())
            {
                foreach (var requiredPackage in projectTypeInfo.RequiredPackageReferences)
                {
                    if (!packagesToInclude.Any(p => p.PackageId.Equals(requiredPackage, StringComparison.OrdinalIgnoreCase)))
                    {
                        packagesToInclude.Add(new PackageReference
                        {
                            PackageId = requiredPackage,
                            Version = "latest" // Will be resolved by package manager
                        });
                        _logger.LogInformation("Added required package reference: {Package}", requiredPackage);
                    }
                }
            }
            
            if (packagesToInclude.Any())
            {
                var packageGroup = new XElement("ItemGroup");
                foreach (var package in packagesToInclude)
                {
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", package.PackageId));
                    
                    // Only add Version if not using Central Package Management
                    if (!_options.EnableCentralPackageManagement)
                    {
                        packageElement.Add(new XAttribute("Version", package.Version));
                    }
                    
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
            
            // Handle linked files separately to ensure they're preserved
            var linkedItems = MigrateLinkedItems(legacyProject, result);
            if (linkedItems.HasElements)
            {
                projectElement.Add(linkedItems);
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
            
            var otherItems = await MigrateOtherItemsAsync(legacyProject, result, projectElement, cancellationToken);
            if (otherItems != null && otherItems.Name == "MergedItemGroups")
            {
                // Handle multiple item groups
                foreach (var itemGroup in otherItems.Elements())
                {
                    if (itemGroup.HasElements)
                    {
                        projectElement.Add(itemGroup);
                    }
                }
            }
            else if (otherItems != null && otherItems.HasElements)
            {
                projectElement.Add(otherItems);
            }
            
            // Migrate native dependencies
            if (nativeDeps.Any())
            {
                _nativeDependencyHandler.MigrateNativeDependencies(nativeDeps, projectElement, result);
            }
            
            // Handle Entity Framework
            var efInfo = await _entityFrameworkHandler.DetectEntityFrameworkAsync(legacyProject, cancellationToken);
            if (efInfo.UsesEntityFramework)
            {
                _entityFrameworkHandler.AddEntityFrameworkSupport(efInfo, projectElement, result);
            }
            
            // Handle T4 Templates
            var t4Info = _t4TemplateHandler.DetectT4Templates(legacyProject);
            if (t4Info.HasT4Templates)
            {
                _t4TemplateHandler.MigrateT4Templates(t4Info, projectElement, result);
            }
            
            // Migrate build events before custom targets
            _buildEventMigrator.MigrateBuildEvents(legacyProject, projectElement, result);
            
            // Migrate complex build configurations
            MigrateComplexBuildConfigurations(legacyProject, projectElement, result);
            
            // Migrate custom targets with enhanced analyzer
            MigrateCustomTargetsWithAnalysis(legacyProject, projectElement, result);
            
            MigrateCustomTargetsAndImports(legacyProject, projectElement, result);

            if (!_options.DryRun)
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Add migration metadata comment for tracking
                sdkProject.Root?.AddFirst(new XComment($"Migrated by SdkMigrator on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"));

                // Save without XML declaration
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineChars = Environment.NewLine,
                    NewLineHandling = NewLineHandling.Replace
                };
                
                using (var writer = XmlWriter.Create(outputPath, settings))
                {
                    sdkProject.Save(writer);
                }
                _logger.LogInformation("Successfully migrated project to {OutputPath}", outputPath);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would migrate project to {OutputPath}", outputPath);
                _logger.LogDebug("[DRY RUN] Generated project content:\n{Content}", sdkProject.ToString());
            }
            
            // Add special handling notes from project type detection
            if (projectTypeInfo.RequiresSpecialHandling && projectTypeInfo.SpecialHandlingNotes.Any())
            {
                foreach (var note in projectTypeInfo.SpecialHandlingNotes)
                {
                    result.Warnings.Add($"[Project Type] {note}");
                }
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
        var projectName = Path.GetFileNameWithoutExtension(legacyProject.FullPath);

        // Handle single or multi-targeting
        if (_options.TargetFrameworks != null && _options.TargetFrameworks.Length > 0)
        {
            // Multi-targeting requested
            propertyGroup.Add(new XElement("TargetFrameworks", string.Join(";", _options.TargetFrameworks)));
            _logger.LogInformation("Using multi-targeting: {Frameworks}", string.Join(";", _options.TargetFrameworks));
            
            // Add warning if this is a library project
            var outputType = legacyProject.GetPropertyValue("OutputType");
            if (string.IsNullOrEmpty(outputType) || outputType.Equals("Library", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add("Multi-targeting enabled. Review conditional compilation and ensure all target frameworks are properly supported.");
            }
        }
        else
        {
            // Single target framework
            var targetFramework = GetTargetFramework(legacyProject);
            if (!string.IsNullOrEmpty(targetFramework))
            {
                propertyGroup.Add(new XElement("TargetFramework", targetFramework));
            }
        }

        // OutputType - only add if NOT "Library" (which is the default)
        var projectOutputType = legacyProject.GetPropertyValue("OutputType");
        if (!string.IsNullOrEmpty(projectOutputType) && 
            !projectOutputType.Equals("Library", StringComparison.OrdinalIgnoreCase))
        {
            propertyGroup.Add(new XElement("OutputType", projectOutputType));
            _logger.LogDebug("Added OutputType: {OutputType} (differs from default 'Library')", projectOutputType);
        }

        // RootNamespace - only add if different from project file name
        var rootNamespace = legacyProject.GetPropertyValue("RootNamespace");
        if (!string.IsNullOrEmpty(rootNamespace) && !rootNamespace.Equals(projectName, StringComparison.OrdinalIgnoreCase))
        {
            propertyGroup.Add(new XElement("RootNamespace", rootNamespace));
            _logger.LogDebug("Added RootNamespace: {RootNamespace} (differs from project name: {ProjectName})", rootNamespace, projectName);
        }

        // AssemblyName - only add if different from project file name
        var assemblyName = legacyProject.GetPropertyValue("AssemblyName");
        if (!string.IsNullOrEmpty(assemblyName) && !assemblyName.Equals(projectName, StringComparison.OrdinalIgnoreCase))
        {
            propertyGroup.Add(new XElement("AssemblyName", assemblyName));
            _logger.LogDebug("Added AssemblyName: {AssemblyName} (differs from project name: {ProjectName})", assemblyName, projectName);
        }

        // Properties that should be preserved if they differ from defaults
        var importantProperties = new[]
        {
            "LangVersion", // Only if restricting to older version (default is "latest")
            "Nullable",
            "GenerateDocumentationFile",
            "NoWarn",
            "TreatWarningsAsErrors",
            "WarningsAsErrors",
            "DefineConstants",
            "PlatformTarget",
            "AllowUnsafeBlocks"
            // Removed "Prefer32Bit" - irrelevant for libraries, only matters for .exe with AnyCPU
        };
        
        // ClickOnce properties
        var clickOnceProperties = new[]
        {
            "PublishUrl",
            "InstallUrl",
            "UpdateUrl",
            "SupportUrl",
            "ProductName",
            "PublisherName",
            "ApplicationRevision",
            "ApplicationVersion",
            "UseApplicationTrust",
            "CreateDesktopShortcut",
            "PublishWizardCompleted",
            "BootstrapperEnabled",
            "IsWebBootstrapper",
            "Install",
            "InstallFrom",
            "UpdateEnabled",
            "UpdateMode",
            "UpdateInterval",
            "UpdateIntervalUnits",
            "UpdatePeriodically",
            "UpdateRequired",
            "MapFileExtensions",
            "MinimumRequiredVersion",
            "CreateWebPageOnPublish",
            "WebPage",
            "TrustUrlParameters",
            "ErrorReportUrl",
            "TargetCulture",
            "SignManifests",
            "ManifestCertificateThumbprint",
            "ManifestKeyFile",
            "GenerateManifests",
            "SignAssembly",
            "AssemblyOriginatorKeyFile",
            "DelaySign"
        };

        foreach (var propName in importantProperties)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
            }
        }
        
        // Check if project uses ClickOnce
        var hasClickOnce = false;
        foreach (var propName in clickOnceProperties)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
                hasClickOnce = true;
                _logger.LogDebug("Migrated ClickOnce property: {PropertyName} = {Value}", propName, value);
            }
        }
        
        if (hasClickOnce)
        {
            // Ensure IsPublishable is set for ClickOnce
            if (propertyGroup.Element("IsPublishable") == null)
            {
                propertyGroup.Add(new XElement("IsPublishable", "true"));
            }
            
            result.Warnings.Add("ClickOnce deployment properties migrated. Important notes:");
            result.Warnings.Add("- Test publish functionality thoroughly after migration");
            result.Warnings.Add("- Ensure certificate files are accessible at the specified paths");
            result.Warnings.Add("- Update PublishUrl and InstallUrl if using relative paths");
            result.Warnings.Add("- Run 'dotnet publish -p:PublishProfile=ClickOnceProfile' to test");
        }
        
        // Handle special properties that might be needed
        // ProjectGuid - not needed in SDK-style projects, skip it
        // SignAssembly, AssemblyOriginatorKeyFile, DelaySign - keep if present for signing
        var signingProperties = new[] { "SignAssembly", "AssemblyOriginatorKeyFile", "DelaySign", "StrongNameKeyFile" };
        foreach (var propName in signingProperties)
        {
            var value = legacyProject.GetPropertyValue(propName);
            if (!string.IsNullOrEmpty(value))
            {
                propertyGroup.Add(new XElement(propName, value));
                _logger.LogDebug("Preserved signing property: {PropertyName}", propName);
            }
        }
        
        // IsPublishable - handle after ClickOnce and signing properties
        // Will be added below if needed

        foreach (var property in legacyProject.Properties)
        {
            if (LegacyProjectElements.PropertiesToRemove.Contains(property.Name) || 
                LegacyProjectElements.AssemblyPropertiesToExtract.Contains(property.Name))
            {
                result.RemovedElements.Add($"Property: {property.Name}");
                _logger.LogDebug("Removed legacy property: {PropertyName}", property.Name);
            }
        }
        
        // IsPublishable - only add for libraries if explicitly set to true (or if ClickOnce is used)
        // (default is true for exe, false for library)
        var isPublishable = legacyProject.GetPropertyValue("IsPublishable");
        bool isLibrary = string.IsNullOrEmpty(projectOutputType) || projectOutputType.Equals("Library", StringComparison.OrdinalIgnoreCase);
        bool needsIsPublishable = hasClickOnce || 
            (!string.IsNullOrEmpty(isPublishable) && isPublishable.Equals("true", StringComparison.OrdinalIgnoreCase) && isLibrary);
        
        if (needsIsPublishable && propertyGroup.Element("IsPublishable") == null)
        {
            propertyGroup.Add(new XElement("IsPublishable", "true"));
            _logger.LogDebug("Added IsPublishable: true (library project with explicit publish support or ClickOnce)");
        }

        return propertyGroup;
    }

    private bool IsContentForPackaging(string path)
    {
        // Common file types that are often included in packages
        var packagingExtensions = new[] { ".txt", ".md", ".json", ".xml", ".config", ".props", ".targets", ".ps1", ".psm1" };
        var extension = Path.GetExtension(path).ToLowerInvariant();
        
        // Check common packaging locations
        var directoryName = Path.GetDirectoryName(path)?.ToLowerInvariant() ?? "";
        var packagingDirectories = new[] { "content", "contentfiles", "build", "buildMultitargeting", "tools", "lib" };
        
        return packagingExtensions.Contains(extension) || 
               packagingDirectories.Any(d => directoryName.Contains(d));
    }
    
    private string GetPackagePath(string itemPath)
    {
        // Try to infer package path from item path
        var directoryName = Path.GetDirectoryName(itemPath)?.ToLowerInvariant() ?? "";
        
        if (directoryName.Contains("content"))
            return "content";
        if (directoryName.Contains("tools"))
            return "tools";
        if (directoryName.Contains("build"))
            return "build";
            
        // Default to content
        return "content";
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

    private string? GetTargetFrameworkFromProject(XElement projectElement)
    {
        var propertyGroups = projectElement.Elements("PropertyGroup");
        foreach (var pg in propertyGroups)
        {
            var targetFramework = pg.Element("TargetFramework")?.Value;
            if (!string.IsNullOrEmpty(targetFramework))
                return targetFramework;
                
            var targetFrameworks = pg.Element("TargetFrameworks")?.Value;
            if (!string.IsNullOrEmpty(targetFrameworks))
            {
                // Return the first framework for assembly resolution
                return targetFrameworks.Split(';')[0].Trim();
            }
        }
        return null;
    }
    
    private XElement MigrateProjectReferences(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectReferences = legacyProject.Items.Where(i => i.ItemType == "ProjectReference");
        var projectDir = Path.GetDirectoryName(legacyProject.FullPath)!;

        // Group references by condition to handle conditional references
        var refGroups = projectReferences.GroupBy(r => r.Xml.Condition ?? "");
        
        foreach (var group in refGroups)
        {
            var condition = group.Key;
            var currentItemGroup = string.IsNullOrEmpty(condition) ? itemGroup : new XElement("ItemGroup");
            
            if (!string.IsNullOrEmpty(condition))
            {
                currentItemGroup.Add(new XAttribute("Condition", condition));
            }
            
            foreach (var reference in group)
            {
                var includeValue = reference.EvaluatedInclude;
                var resolvedPath = ResolveProjectReferencePath(projectDir, includeValue, result);
                
                if (resolvedPath != includeValue)
                {
                    _logger.LogInformation("Fixed project reference path: {OldPath} -> {NewPath}", includeValue, resolvedPath);
                }
                
                var element = new XElement("ProjectReference",
                    new XAttribute("Include", resolvedPath));

                var metadataToPreserve = new[] { "Name", "Private", "SpecificVersion", "ReferenceOutputAssembly" };
                foreach (var metadata in metadataToPreserve)
                {
                    var value = reference.GetMetadataValue(metadata);
                    if (!string.IsNullOrEmpty(value))
                    {
                        element.Add(new XElement(metadata, value));
                    }
                }

                currentItemGroup.Add(element);
            }
            
            // If this was a conditional group, return it separately
            if (!string.IsNullOrEmpty(condition) && currentItemGroup.HasElements)
            {
                // We need to add conditional item groups to the project element directly
                // For now, add to the main itemGroup with a comment
                itemGroup.Add(new XComment($"Conditional reference group: {condition}"));
                foreach (var elem in currentItemGroup.Elements())
                {
                    var condElem = new XElement(elem);
                    condElem.Add(new XAttribute("Condition", condition));
                    itemGroup.Add(condElem);
                }
                result.Warnings.Add($"Conditional project reference detected: {condition}");
            }
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
    
    private XElement MigrateLinkedItems(Project legacyProject, MigrationResult result)
    {
        var itemGroup = new XElement("ItemGroup");
        var projectDir = Path.GetDirectoryName(legacyProject.FullPath)!;
        
        // Find all items with Link metadata that point outside the project directory
        var linkedItems = legacyProject.Items
            .Where(i => !IsEvaluationArtifact(i.ItemType))
            .Where(i => i.HasMetadata("Link") || 
                       (!Path.GetFullPath(Path.Combine(projectDir, i.EvaluatedInclude))
                        .StartsWith(projectDir, StringComparison.OrdinalIgnoreCase)))
            .ToList();
            
        foreach (var item in linkedItems)
        {
            // Skip MSBuild evaluation artifacts
            if (IsEvaluationArtifact(item.ItemType))
            {
                _logger.LogDebug("Skipping MSBuild evaluation artifact in linked items: {ItemType}", item.ItemType);
                continue;
            }
            
            // Skip if already handled in other methods
            if (item.ItemType == "Compile" && 
                LegacyProjectElements.ImplicitlyIncludedExtensions.Contains(Path.GetExtension(item.EvaluatedInclude)))
            {
                continue; // Already handled in MigrateCompileItems
            }
            
            var element = new XElement(item.ItemType,
                new XAttribute("Include", item.EvaluatedInclude));
                
            // Ensure Link metadata is preserved
            var linkValue = item.GetMetadataValue("Link");
            if (string.IsNullOrEmpty(linkValue))
            {
                // Generate Link metadata for files outside project directory
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, item.EvaluatedInclude));
                if (!fullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Use just the filename or a simplified path
                    linkValue = Path.GetFileName(item.EvaluatedInclude);
                    var dir = Path.GetDirectoryName(item.EvaluatedInclude);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var lastDir = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(lastDir))
                        {
                            linkValue = Path.Combine(lastDir, linkValue);
                        }
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(linkValue))
            {
                element.Add(new XElement("Link", linkValue));
            }
            
            // Copy other metadata
            CopyMetadata(item, element, "Link"); // Exclude Link since we already added it
            
            itemGroup.Add(element);
            _logger.LogDebug("Migrated linked item: {ItemType} {Include} -> {Link}", 
                item.ItemType, item.EvaluatedInclude, linkValue);
        }
        
        if (linkedItems.Any())
        {
            result.Warnings.Add($"Migrated {linkedItems.Count} linked files. Verify paths are correct after migration.");
        }
        
        return itemGroup;
    }
    
    private XElement MigrateWpfWinFormsItems(Project legacyProject)
    {
        var itemGroup = new XElement("ItemGroup");
        
        // Migrate WPF/WinForms specific items
        foreach (var itemType in LegacyProjectElements.WpfWinFormsItemTypes)
        {
            var items = legacyProject.Items
                .Where(i => i.ItemType == itemType)
                .Where(i => !IsEvaluationArtifact(i.ItemType));
            
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
        var sdk = DetermineSdk(legacyProject);
        var isPackable = !string.IsNullOrEmpty(legacyProject.GetPropertyValue("GeneratePackageOnBuild")) ||
                        !string.IsNullOrEmpty(legacyProject.GetPropertyValue("IsPackable"));
        
        var contentItems = legacyProject.Items
            .Where(i => i.ItemType == "Content")
            .Where(i => !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(i.ItemType))
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
            var packValue = item.GetMetadataValue("Pack");
            var packagePath = item.GetMetadataValue("PackagePath");
            
            // For web projects, Content is implicit
            if (sdk == "Microsoft.NET.Sdk.Web")
            {
                // Only need to explicitly include if it has special metadata
                if (!string.IsNullOrEmpty(copyToOutput) || !string.IsNullOrEmpty(packValue) || !string.IsNullOrEmpty(packagePath))
                {
                    var element = new XElement("Content",
                        new XAttribute("Include", item.EvaluatedInclude));
                    
                    if (!string.IsNullOrEmpty(copyToOutput))
                        element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
                    if (!string.IsNullOrEmpty(packValue))
                        element.Add(new XElement("Pack", packValue));
                    if (!string.IsNullOrEmpty(packagePath))
                        element.Add(new XElement("PackagePath", packagePath));
                    
                    CopyMetadata(item, element, "CopyToOutputDirectory", "Pack", "PackagePath");
                    itemGroup.Add(element);
                }
            }
            else
            {
                // For non-web SDKs, Content items need to be explicitly handled
                if (!string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never")
                {
                    var element = new XElement("None",
                        new XAttribute("Include", item.EvaluatedInclude));
                    
                    element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
                    CopyMetadata(item, element, "CopyToOutputDirectory");
                    
                    itemGroup.Add(element);
                    _logger.LogDebug("Migrated Content item as None with CopyToOutput: {Include}", item.EvaluatedInclude);
                }
                else if (isPackable && IsContentForPackaging(item.EvaluatedInclude))
                {
                    // This content item might be for packaging
                    var element = new XElement("None",
                        new XAttribute("Include", item.EvaluatedInclude));
                    
                    element.Add(new XElement("Pack", "true"));
                    if (!string.IsNullOrEmpty(packagePath))
                    {
                        element.Add(new XElement("PackagePath", packagePath));
                    }
                    else
                    {
                        // Infer package path from item path
                        var inferredPath = GetPackagePath(item.EvaluatedInclude);
                        if (!string.IsNullOrEmpty(inferredPath))
                        {
                            element.Add(new XElement("PackagePath", inferredPath));
                        }
                    }
                    
                    CopyMetadata(item, element, "Pack", "PackagePath");
                    itemGroup.Add(element);
                    _logger.LogDebug("Migrated Content item as None for packaging: {Include}", item.EvaluatedInclude);
                }
            }
        }
        
        var noneItems = legacyProject.Items
            .Where(i => i.ItemType == "None")
            .Where(i => !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(i.ItemType))
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
    
    private async Task<XElement> MigrateOtherItemsAsync(Project legacyProject, MigrationResult result, XElement projectElement, CancellationToken cancellationToken = default)
    {
        var itemGroup = new XElement("ItemGroup");
        var packageItemGroup = new XElement("ItemGroup");
        var addedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var convertedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get all packages that were already migrated from packages.config
        var existingPackages = result.MigratedPackages.ToList();
        
        // Get the SDK from the project element
        var sdk = projectElement.Attribute("Sdk")?.Value ?? "Microsoft.NET.Sdk";
        
        // Get the target framework for assembly resolution
        var targetFramework = GetTargetFrameworkFromProject(projectElement) ?? "net8.0";
        
        // Use NuGetAssetsResolver to get all assemblies provided by the migrated packages
        var projectDirectory = Path.GetDirectoryName(legacyProject.FullPath) ?? "";
        var resolutionResult = await _nugetAssetsResolver.ResolvePackageAssembliesAsync(
            existingPackages, targetFramework, projectDirectory, cancellationToken);
        
        _logger.LogInformation("Resolved {Count} assemblies from packages using {Method}. IsPartial: {IsPartial}", 
            resolutionResult.ResolvedAssemblies.Count, resolutionResult.ResolutionMethod, resolutionResult.IsPartialResolution);
        
        // Add any warnings from resolution to the result
        foreach (var warning in resolutionResult.Warnings)
        {
            result.Warnings.Add($"[Package Resolution] {warning}");
        }
        
        // Migrate assembly references that are not part of the implicit framework references
        var assemblyReferences = legacyProject.Items.Where(i => i.ItemType == "Reference");
        var removedReferences = new List<string>();
        
        foreach (var reference in assemblyReferences)
        {
            var referenceName = reference.EvaluatedInclude;
            
            // Extract just the assembly name without version info
            var assemblyName = referenceName.Split(',')[0].Trim();
            
            // First check if this assembly is already provided by a migrated package
            var isProvidedByPackage = resolutionResult.ResolvedAssemblies.Any(a => 
                a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
            
            if (isProvidedByPackage)
            {
                var providingPackage = resolutionResult.ResolvedAssemblies
                    .FirstOrDefault(a => a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
                
                _logger.LogInformation("Removing assembly reference '{AssemblyName}' - provided by package '{PackageId}' {Transitive}", 
                    assemblyName, 
                    providingPackage?.PackageId ?? "unknown",
                    providingPackage?.IsTransitive == true ? "(transitive)" : "");
                
                removedReferences.Add(assemblyName);
                
                // Collect hint path if this reference has one for cleanup
                var hintPath = reference.GetMetadataValue("HintPath");
                if (!string.IsNullOrEmpty(hintPath))
                {
                    result.ConvertedHintPaths.Add(hintPath);
                    _logger.LogDebug("Collected hint path for cleanup: {HintPath}", hintPath);
                }
                
                continue;
            }
            
            // Check if this assembly should be converted to a package reference
            var packageResolution = await _nugetResolver.ResolveAssemblyToPackageAsync(assemblyName, cancellationToken: cancellationToken);
            if (packageResolution != null)
            {
                // Collect hint path if this reference has one
                var hintPath = reference.GetMetadataValue("HintPath");
                if (!string.IsNullOrEmpty(hintPath))
                {
                    result.ConvertedHintPaths.Add(hintPath);
                    _logger.LogDebug("Collected hint path for cleanup: {HintPath}", hintPath);
                }
                // Add main package
                if (!addedPackages.Contains(packageResolution.PackageId))
                {
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", packageResolution.PackageId),
                        new XAttribute("Version", packageResolution.Version));
                    packageItemGroup.Add(packageElement);
                    addedPackages.Add(packageResolution.PackageId);
                    convertedAssemblies.Add(assemblyName); // Track that this assembly was converted
                    // Also track the full reference name in case it includes version info
                    convertedAssemblies.Add(referenceName);
                    
                    // Track all assemblies included in this package
                    foreach (var includedAssembly in packageResolution.IncludedAssemblies)
                    {
                        convertedAssemblies.Add(includedAssembly);
                        _logger.LogDebug("Package '{PackageName}' includes assembly '{AssemblyName}'", 
                            packageResolution.PackageId, includedAssembly);
                    }
                    
                    _logger.LogInformation("Converted assembly reference '{AssemblyName}' to package reference '{PackageName}' version {Version}", 
                        assemblyName, packageResolution.PackageId, packageResolution.Version);
                    
                    result.MigratedPackages.Add(new PackageReference 
                    { 
                        PackageId = packageResolution.PackageId, 
                        Version = packageResolution.Version,
                        IsTransitive = false
                    });
                }
                
                // Add additional packages if needed (e.g., test adapters)
                foreach (var additionalPackageId in packageResolution.AdditionalPackages)
                {
                    if (!addedPackages.Contains(additionalPackageId))
                    {
                        var additionalVersion = await _nugetResolver.GetLatestStableVersionAsync(additionalPackageId, cancellationToken);
                        if (additionalVersion != null)
                        {
                            var packageElement = new XElement("PackageReference",
                                new XAttribute("Include", additionalPackageId),
                                new XAttribute("Version", additionalVersion));
                            packageItemGroup.Add(packageElement);
                            addedPackages.Add(additionalPackageId);
                            _logger.LogInformation("Added additional package '{PackageName}' version {Version}", 
                                additionalPackageId, additionalVersion);
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(packageResolution.Notes))
                {
                    result.Warnings.Add($"Package migration note for '{assemblyName}': {packageResolution.Notes}");
                }
                
                continue;
            }
            
            // Skip references that are implicitly included in the framework or were already converted to packages
            var implicitFrameworkReferences = GetImplicitReferences(sdk, targetFramework);
            
            if (implicitFrameworkReferences.Contains(assemblyName) || convertedAssemblies.Contains(assemblyName))
            {
                if (implicitFrameworkReferences.Contains(assemblyName))
                {
                    removedReferences.Add($"{assemblyName} (SDK implicit)");
                    _logger.LogInformation("Removing assembly reference '{AssemblyName}' - implicitly included by SDK", assemblyName);
                }
                else
                {
                    _logger.LogDebug("Skipping already converted to package reference: {Reference}", assemblyName);
                }
                continue;
            }
            
            // Framework extensions and special references that need to be preserved
            var frameworkExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Windows.Forms", "System.Drawing", "System.Web", "System.Web.Extensions",
                "System.Configuration", "System.ServiceModel", "System.Runtime.Serialization",
                "System.ComponentModel.DataAnnotations"
            };
            
            if ((frameworkExtensions.Contains(assemblyName) || 
                assemblyName.StartsWith("Microsoft.VisualStudio", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Windows", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.ServiceModel", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("System.ComponentModel", StringComparison.OrdinalIgnoreCase)) &&
                !convertedAssemblies.Contains(assemblyName))
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
            .Where(i => !LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(i.ItemType))
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
        
        // Log summary of removed references
        if (removedReferences.Any())
        {
            _logger.LogInformation("Removed {Count} assembly references that are provided by packages:", removedReferences.Count);
            foreach (var removed in removedReferences)
            {
                _logger.LogInformation("  - {AssemblyName}", removed);
                result.RemovedElements.Add($"Assembly reference: {removed} (provided by package)");
            }
        }
        
        // Merge packageItemGroup with existing package references if they exist
        if (packageItemGroup.HasElements)
        {
            var existingPackageGroup = projectElement.Elements("ItemGroup")
                .FirstOrDefault(ig => ig.Elements("PackageReference").Any());
            
            if (existingPackageGroup != null)
            {
                // Add to existing package reference group
                foreach (var packageRef in packageItemGroup.Elements())
                {
                    existingPackageGroup.Add(packageRef);
                }
            }
            else
            {
                // Return the package item group separately
                return new XElement("MergedItemGroups", packageItemGroup, itemGroup);
            }
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
    
    private void MigrateComplexBuildConfigurations(Project legacyProject, XElement projectElement, MigrationResult result)
    {
        var configPropertyGroups = legacyProject.Xml.PropertyGroups
            .Where(pg => !string.IsNullOrEmpty(pg.Condition))
            .ToList();

        if (!configPropertyGroups.Any())
            return;

        _logger.LogInformation("Migrating {Count} conditional property groups", configPropertyGroups.Count);

        // Group by configuration
        var configGroups = new Dictionary<string, List<Microsoft.Build.Construction.ProjectPropertyGroupElement>>();
        
        foreach (var group in configPropertyGroups)
        {
            // Extract configuration name from condition
            var configMatch = System.Text.RegularExpressions.Regex.Match(
                group.Condition,
                @"'\$\(Configuration\)'(\s*==\s*|\s*\.Equals\s*\(\s*)'([^']+)'");
            
            if (configMatch.Success)
            {
                var configName = configMatch.Groups[2].Value;
                if (!configGroups.ContainsKey(configName))
                    configGroups[configName] = new List<Microsoft.Build.Construction.ProjectPropertyGroupElement>();
                configGroups[configName].Add(group);
            }
            else if (group.Condition.Contains("$(Configuration)", StringComparison.OrdinalIgnoreCase))
            {
                // Complex condition - preserve as-is
                var newPropGroup = new XElement("PropertyGroup",
                    new XAttribute("Condition", group.Condition));
                
                foreach (var prop in group.Properties)
                {
                    // Skip properties already in main PropertyGroup
                    if (!IsPropertyInMainGroup(prop.Name, projectElement))
                    {
                        newPropGroup.Add(new XElement(prop.Name, prop.Value));
                    }
                }
                
                if (newPropGroup.HasElements)
                {
                    projectElement.Add(newPropGroup);
                    _logger.LogDebug("Migrated complex conditional PropertyGroup: {Condition}", group.Condition);
                }
            }
        }

        // Process each configuration
        foreach (var (configName, groups) in configGroups)
        {
            var condition = $"'$(Configuration)' == '{configName}'";
            var configPropGroup = new XElement("PropertyGroup",
                new XAttribute("Condition", condition));
            
            // Merge all properties for this configuration
            var addedProps = new HashSet<string>();
            foreach (var group in groups)
            {
                foreach (var prop in group.Properties)
                {
                    // Skip if already added or in main group
                    if (!addedProps.Contains(prop.Name) && !IsPropertyInMainGroup(prop.Name, projectElement))
                    {
                        configPropGroup.Add(new XElement(prop.Name, prop.Value));
                        addedProps.Add(prop.Name);
                    }
                }
            }
            
            if (configPropGroup.HasElements)
            {
                projectElement.Add(configPropGroup);
                _logger.LogInformation("Migrated configuration-specific properties for: {Config}", configName);
            }
        }

        // Handle conditional ItemGroups
        var conditionalItemGroups = legacyProject.Xml.ItemGroups
            .Where(ig => !string.IsNullOrEmpty(ig.Condition))
            .ToList();
            
        foreach (var itemGroup in conditionalItemGroups)
        {
            var newItemGroup = new XElement("ItemGroup",
                new XAttribute("Condition", itemGroup.Condition));
                
            foreach (var item in itemGroup.Items)
            {
                // Skip MSBuild evaluation artifacts
                if (IsEvaluationArtifact(item.ItemType))
                {
                    _logger.LogDebug("Skipping MSBuild evaluation artifact: {ItemType}", item.ItemType);
                    continue;
                }
                
                var itemElement = new XElement(item.ItemType,
                    new XAttribute("Include", item.Include));
                    
                if (!string.IsNullOrEmpty(item.Exclude))
                    itemElement.Add(new XAttribute("Exclude", item.Exclude));
                    
                foreach (var metadata in item.Metadata)
                {
                    itemElement.Add(new XElement(metadata.Name, metadata.Value));
                }
                
                newItemGroup.Add(itemElement);
            }
            
            if (newItemGroup.HasElements)
            {
                projectElement.Add(newItemGroup);
                result.Warnings.Add($"Migrated conditional ItemGroup with condition: {itemGroup.Condition}");
            }
        }
    }

    private bool IsPropertyInMainGroup(string propertyName, XElement projectElement)
    {
        var mainPropGroup = projectElement.Elements("PropertyGroup")
            .FirstOrDefault(pg => pg.Attribute("Condition") == null);
            
        return mainPropGroup?.Element(propertyName) != null;
    }

    private void MigrateCustomTargetsWithAnalysis(Project legacyProject, XElement projectElement, MigrationResult result)
    {
        var targetAnalyses = _customTargetAnalyzer.AnalyzeTargets(legacyProject);
        
        foreach (var analysis in targetAnalyses)
        {
            var target = legacyProject.Xml.Targets.FirstOrDefault(t => t.Name == analysis.TargetName);
            if (target == null) continue;
            
            if (analysis.CanAutoMigrate)
            {
                var migratedTarget = _customTargetAnalyzer.MigrateTarget(target, analysis);
                if (migratedTarget != null)
                {
                    projectElement.Add(migratedTarget);
                    result.RemovedElements.Add($"Target: {analysis.TargetName} (auto-migrated)");
                    _logger.LogInformation("Auto-migrated custom target: {TargetName}", analysis.TargetName);
                }
            }
            else
            {
                // Add as removed with detailed guidance
                var removedTarget = new RemovedMSBuildElement
                {
                    ElementType = "Target",
                    Name = analysis.TargetName,
                    XmlContent = analysis.SuggestedCode ?? "[Unable to generate suggested code]",
                    Condition = analysis.Condition,
                    Reason = "Custom target requires manual migration",
                    SuggestedMigrationPath = analysis.ManualMigrationGuidance ?? "Review and migrate manually"
                };
                
                result.RemovedMSBuildElements.Add(removedTarget);
                result.Warnings.Add($"Custom target '{analysis.TargetName}' requires manual migration - see removed elements for guidance");
                _logger.LogWarning("Custom target requires manual migration: {TargetName}", analysis.TargetName);
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
            
            // Capture the import details
            var removedImport = new RemovedMSBuildElement
            {
                ElementType = "Import",
                Name = importPath,
                XmlContent = $"<Import Project=\"{import.Project}\"" + (string.IsNullOrEmpty(import.Condition) ? "" : $" Condition=\"{import.Condition}\"") + " />",
                Condition = import.Condition,
                Reason = "SDK-style projects use implicit imports"
            };
            
            // Add a warning and suggestion if this looks like a custom project import
            if (!string.IsNullOrEmpty(importPath) && 
                !importPath.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                !importPath.Contains("MSBuild", StringComparison.OrdinalIgnoreCase) &&
                !importPath.Contains("VisualStudio", StringComparison.OrdinalIgnoreCase) &&
                (importPath.StartsWith(".") || !importPath.Contains("$(")))
            {
                removedImport.SuggestedMigrationPath = "Move custom imports to Directory.Build.props or Directory.Build.targets";
                result.Warnings.Add($"Removed custom import '{importPath}' - move to Directory.Build.props/targets if needed");
            }
            else
            {
                removedImport.SuggestedMigrationPath = "No migration needed - handled by SDK";
            }
            
            result.RemovedMSBuildElements.Add(removedImport);
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
                
                // Capture the target content
                var removedTarget = new RemovedMSBuildElement
                {
                    ElementType = "Target",
                    Name = target.Name,
                    XmlContent = GetTargetXmlString(target),
                    Condition = target.Condition,
                    Reason = "Common build target handled by SDK"
                };
                
                // Provide specific migration guidance based on target name
                switch (target.Name.ToLower())
                {
                    case "beforebuild":
                    case "afterbuild":
                        removedTarget.SuggestedMigrationPath = "Move to Directory.Build.targets with BeforeTargets='Build' or AfterTargets='Build'";
                        break;
                    case "beforecompile":
                    case "aftercompile":
                        removedTarget.SuggestedMigrationPath = "Move to Directory.Build.targets with BeforeTargets='CoreCompile' or AfterTargets='CoreCompile'";
                        break;
                    case "beforepublish":
                    case "afterpublish":
                        removedTarget.SuggestedMigrationPath = "Move to Directory.Build.targets with BeforeTargets='Publish' or AfterTargets='Publish'";
                        break;
                    default:
                        removedTarget.SuggestedMigrationPath = "Use SDK extensibility points (BeforeTargets/AfterTargets attributes)";
                        break;
                }
                
                result.RemovedMSBuildElements.Add(removedTarget);
                result.Warnings.Add($"Removed '{target.Name}' target - {removedTarget.SuggestedMigrationPath}");
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
    
    private int CalculatePathSimilarity(string fullPath, List<string> originalSegments)
    {
        var pathSegments = fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var score = 0;
        
        // Check how many segments from the original path appear in the found path
        foreach (var segment in originalSegments)
        {
            if (pathSegments.Any(s => s.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            {
                score++;
            }
        }
        
        // Bonus points if segments appear in the same order
        var lastIndex = -1;
        foreach (var segment in originalSegments)
        {
            var index = Array.FindIndex(pathSegments, s => s.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (index > lastIndex)
            {
                score += 2;
                lastIndex = index;
            }
        }
        
        return score;
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
            
            // 4. Last resort - search the entire repository for the project file
            _logger.LogInformation("Project reference '{Path}' not found at expected location. Searching repository for '{FileName}'...", referencePath, fileName);
            
            // Find the repository root (look for .git directory or go up to a reasonable limit)
            var repoRoot = currentProjectDir;
            var searchDepth = 0;
            while (!Directory.Exists(Path.Combine(repoRoot, ".git")) && searchDepth < 10)
            {
                var parent = Path.GetDirectoryName(repoRoot);
                if (string.IsNullOrEmpty(parent) || parent == repoRoot)
                    break;
                repoRoot = parent;
                searchDepth++;
            }
            
            // If we didn't find .git, use a reasonable parent directory (up to 5 levels)
            if (!Directory.Exists(Path.Combine(repoRoot, ".git")) && searchDepth >= 10)
            {
                repoRoot = currentProjectDir;
                for (int i = 0; i < 5; i++)
                {
                    var parent = Path.GetDirectoryName(repoRoot);
                    if (string.IsNullOrEmpty(parent) || parent == repoRoot)
                        break;
                    repoRoot = parent;
                }
                _logger.LogDebug("No .git directory found, using parent directory as repository root: {Root}", repoRoot);
            }
            
            // Search from repository root
            try
            {
                _logger.LogDebug("Searching for project files in: {Root}", repoRoot);
                
                // Directories to exclude from search
                var excludedDirs = new[] { "bin", "obj", "packages", "node_modules", ".git", ".vs", "artifacts", "publish" };
                
                // First try exact filename match
                var allProjectFiles = Directory.GetFiles(repoRoot, fileName, SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !excludedDirs.Any(dir => f.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                
                // If no exact match, try with wildcards for common patterns
                if (!allProjectFiles.Any() && !fileName.Contains("*"))
                {
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                    var extension = Path.GetExtension(fileName);
                    
                    // Try common variations
                    var patterns = new[]
                    {
                        $"*{fileNameWithoutExtension}*{extension}",  // Any prefix/suffix
                        $"{fileNameWithoutExtension}.*proj",           // Any project type
                        $"*{fileNameWithoutExtension}.*proj"           // Any prefix and project type
                    };
                    
                    foreach (var pattern in patterns)
                    {
                        _logger.LogDebug("Trying pattern: {Pattern}", pattern);
                        var foundFiles = Directory.GetFiles(repoRoot, pattern, SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                            .Where(f => !excludedDirs.Any(dir => f.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                            
                        if (foundFiles.Any())
                        {
                            allProjectFiles.AddRange(foundFiles);
                        }
                    }
                    
                    // Remove duplicates
                    allProjectFiles = allProjectFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                
                _logger.LogDebug("Found {Count} potential project matches", allProjectFiles.Count);
                
                if (allProjectFiles.Count == 1)
                {
                    var relativePath = Path.GetRelativePath(currentProjectDir, allProjectFiles[0]);
                    _logger.LogInformation("Found project reference in repository: {OldPath} -> {NewPath}", referencePath, relativePath);
                    result.Warnings.Add($"Fixed project reference path: '{referencePath}' -> '{relativePath}'");
                    return relativePath.Replace('\\', Path.DirectorySeparatorChar);
                }
                else if (allProjectFiles.Count > 1)
                {
                    // Multiple matches - try to find the best match based on the original path segments
                    var originalSegments = referencePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(s => !s.Equals("..", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    var bestMatch = allProjectFiles
                        .Select(f => new
                        {
                            Path = f,
                            Score = CalculatePathSimilarity(f, originalSegments)
                        })
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault();
                    
                    if (bestMatch != null && bestMatch.Score > 0)
                    {
                        var relativePath = Path.GetRelativePath(currentProjectDir, bestMatch.Path);
                        _logger.LogInformation("Found best matching project reference in repository: {OldPath} -> {NewPath}", referencePath, relativePath);
                        result.Warnings.Add($"Fixed project reference path (best match): '{referencePath}' -> '{relativePath}'");
                        return relativePath.Replace('\\', Path.DirectorySeparatorChar);
                    }
                    
                    result.Warnings.Add($"Multiple projects named '{fileName}' found in repository - could not determine correct reference");
                    _logger.LogWarning("Multiple projects found with name {FileName}: {Paths}", fileName, string.Join(", ", allProjectFiles));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching repository for project file: {FileName}", fileName);
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
    
    private void MigrateNuSpecMetadata(XElement propertyGroup, NuSpecMetadata nuspec, MigrationResult result)
    {
        _logger.LogInformation("Migrating NuSpec metadata to MSBuild properties");
        
        // Always make the project packable when there's a nuspec
        AddOrUpdateProperty(propertyGroup, "IsPackable", "true");
        
        // Basic package metadata
        if (!string.IsNullOrEmpty(nuspec.Id))
            AddOrUpdateProperty(propertyGroup, "PackageId", nuspec.Id);
            
        if (!string.IsNullOrEmpty(nuspec.Version))
            AddOrUpdateProperty(propertyGroup, "PackageVersion", nuspec.Version);
            
        if (!string.IsNullOrEmpty(nuspec.Authors))
            AddOrUpdateProperty(propertyGroup, "Authors", nuspec.Authors);
            
        if (!string.IsNullOrEmpty(nuspec.Description))
            AddOrUpdateProperty(propertyGroup, "PackageDescription", nuspec.Description);
            
        if (!string.IsNullOrEmpty(nuspec.Copyright))
            AddOrUpdateProperty(propertyGroup, "Copyright", nuspec.Copyright);
            
        if (!string.IsNullOrEmpty(nuspec.ProjectUrl))
            AddOrUpdateProperty(propertyGroup, "PackageProjectUrl", nuspec.ProjectUrl);
            
        if (!string.IsNullOrEmpty(nuspec.LicenseUrl))
        {
            AddOrUpdateProperty(propertyGroup, "PackageLicenseUrl", nuspec.LicenseUrl);
            result.Warnings.Add("PackageLicenseUrl is deprecated. Consider using PackageLicenseExpression or PackageLicenseFile instead.");
        }
        
        if (!string.IsNullOrEmpty(nuspec.License))
        {
            if (nuspec.License.StartsWith("LICENSE_FILE:"))
            {
                var licenseFile = nuspec.License.Substring("LICENSE_FILE:".Length);
                AddOrUpdateProperty(propertyGroup, "PackageLicenseFile", licenseFile);
            }
            else
            {
                AddOrUpdateProperty(propertyGroup, "PackageLicenseExpression", nuspec.License);
            }
        }
        
        if (!string.IsNullOrEmpty(nuspec.IconUrl))
        {
            AddOrUpdateProperty(propertyGroup, "PackageIconUrl", nuspec.IconUrl);
            result.Warnings.Add("PackageIconUrl is deprecated. Consider using PackageIcon with an embedded icon file instead.");
        }
        
        if (!string.IsNullOrEmpty(nuspec.Icon))
            AddOrUpdateProperty(propertyGroup, "PackageIcon", nuspec.Icon);
            
        if (!string.IsNullOrEmpty(nuspec.Tags))
            AddOrUpdateProperty(propertyGroup, "PackageTags", nuspec.Tags);
            
        if (!string.IsNullOrEmpty(nuspec.ReleaseNotes))
            AddOrUpdateProperty(propertyGroup, "PackageReleaseNotes", nuspec.ReleaseNotes);
            
        if (nuspec.RequireLicenseAcceptance.HasValue)
            AddOrUpdateProperty(propertyGroup, "PackageRequireLicenseAcceptance", nuspec.RequireLicenseAcceptance.Value.ToString().ToLower());
            
        if (!string.IsNullOrEmpty(nuspec.RepositoryUrl))
            AddOrUpdateProperty(propertyGroup, "RepositoryUrl", nuspec.RepositoryUrl);
            
        if (!string.IsNullOrEmpty(nuspec.RepositoryType))
            AddOrUpdateProperty(propertyGroup, "RepositoryType", nuspec.RepositoryType);
            
        if (!string.IsNullOrEmpty(nuspec.RepositoryBranch))
            AddOrUpdateProperty(propertyGroup, "RepositoryBranch", nuspec.RepositoryBranch);
            
        if (!string.IsNullOrEmpty(nuspec.RepositoryCommit))
            AddOrUpdateProperty(propertyGroup, "RepositoryCommit", nuspec.RepositoryCommit);
            
        if (!string.IsNullOrEmpty(nuspec.Title))
            AddOrUpdateProperty(propertyGroup, "Title", nuspec.Title);
            
        if (!string.IsNullOrEmpty(nuspec.Summary))
        {
            AddOrUpdateProperty(propertyGroup, "PackageSummary", nuspec.Summary);
            result.Warnings.Add("PackageSummary is not commonly used in SDK-style projects. Consider using just PackageDescription.");
        }
        
        if (nuspec.DevelopmentDependency.HasValue && nuspec.DevelopmentDependency.Value)
            AddOrUpdateProperty(propertyGroup, "DevelopmentDependency", "true");
            
        if (nuspec.Serviceable.HasValue)
            AddOrUpdateProperty(propertyGroup, "Serviceable", nuspec.Serviceable.Value.ToString().ToLower());
            
        // Handle dependencies - they should already be in PackageReference format
        if (nuspec.Dependencies.Any())
        {
            result.Warnings.Add($"NuSpec contained {nuspec.Dependencies.Count} dependencies. Ensure these are properly represented as PackageReference items.");
        }
        
        // Handle files
        if (nuspec.Files.Any())
        {
            result.Warnings.Add($"NuSpec contained {nuspec.Files.Count} file entries. You may need to add corresponding Content/None items with Pack=\"true\" and PackagePath attributes.");
        }
        
        // Handle contentFiles
        if (nuspec.ContentFiles.Any())
        {
            result.Warnings.Add($"NuSpec contained {nuspec.ContentFiles.Count} contentFiles entries. These should be migrated to Content items with appropriate metadata.");
        }
        
        _logger.LogInformation("Successfully migrated NuSpec metadata to project file");
    }
    
    private void AddOrUpdateProperty(XElement propertyGroup, string name, string value)
    {
        var existing = propertyGroup.Elements(name).FirstOrDefault();
        if (existing != null)
        {
            existing.Value = value;
            _logger.LogDebug("Updated property {Name} to '{Value}'", name, value);
        }
        else
        {
            propertyGroup.Add(new XElement(name, value));
            _logger.LogDebug("Added property {Name} with value '{Value}'", name, value);
        }
    }
    
    private string GetTargetXmlString(Microsoft.Build.Construction.ProjectTargetElement target)
    {
        var attributes = new List<string> { $"Name=\"{target.Name}\"" };
        
        if (!string.IsNullOrEmpty(target.BeforeTargets))
            attributes.Add($"BeforeTargets=\"{target.BeforeTargets}\"");
        if (!string.IsNullOrEmpty(target.AfterTargets))
            attributes.Add($"AfterTargets=\"{target.AfterTargets}\"");
        if (!string.IsNullOrEmpty(target.DependsOnTargets))
            attributes.Add($"DependsOnTargets=\"{target.DependsOnTargets}\"");
        if (!string.IsNullOrEmpty(target.Condition))
            attributes.Add($"Condition=\"{target.Condition}\"");
            
        return $"<Target {string.Join(" ", attributes)}>...</Target>";
    }
    
    private bool IsEvaluationArtifact(string itemType)
    {
        // Check known evaluation artifacts
        if (LegacyProjectElements.MSBuildEvaluationArtifacts.Contains(itemType))
            return true;
            
        // Check patterns that indicate evaluation artifacts
        if (itemType.StartsWith("_", StringComparison.Ordinal)) // Internal MSBuild items often start with _
            return true;
            
        if (itemType.Contains("MSBuild", StringComparison.OrdinalIgnoreCase) && 
            !itemType.Equals("MSBuildAllProjects", StringComparison.OrdinalIgnoreCase)) // MSBuildAllProjects is sometimes used
            return true;
            
        if (itemType.EndsWith("Paths", StringComparison.OrdinalIgnoreCase) && 
            (itemType.Contains("Reference", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("Assembly", StringComparison.OrdinalIgnoreCase)))
            return true;
            
        if (itemType.Contains("Analyzer", StringComparison.OrdinalIgnoreCase) &&
            itemType.Contains("Config", StringComparison.OrdinalIgnoreCase))
            return true;
            
        return false;
    }
    
    private HashSet<string> GetImplicitReferences(string sdk, string targetFramework)
    {
        var implicitRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Basic .NET SDK implicit references (common to all SDKs)
        implicitRefs.Add("System");
        implicitRefs.Add("System.Core");
        implicitRefs.Add("System.Data");
        implicitRefs.Add("System.Xml");
        implicitRefs.Add("System.Xml.Linq");
        implicitRefs.Add("Microsoft.CSharp");
        implicitRefs.Add("mscorlib");
        
        // .NET Framework specific
        if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net3", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net2", StringComparison.OrdinalIgnoreCase))
        {
            implicitRefs.Add("System.Configuration");
            implicitRefs.Add("System.ServiceProcess");
            implicitRefs.Add("System.Net.Http");
            implicitRefs.Add("System.IO.Compression.FileSystem");
        }
        
        // SDK-specific implicit references
        if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            // Web SDK includes these implicitly
            implicitRefs.Add("System.Web");
            implicitRefs.Add("System.Net.Http");
            implicitRefs.Add("System.ComponentModel.DataAnnotations");
        }
        else if (sdk.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase))
        {
            // WindowsDesktop SDK includes WPF/WinForms references implicitly based on UseWPF/UseWindowsForms
            // These are handled by the SDK based on project properties
        }
        
        // .NET Core / .NET 5+ implicit references
        if (targetFramework.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net5", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net6", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net7", StringComparison.OrdinalIgnoreCase) ||
            targetFramework.StartsWith("net8", StringComparison.OrdinalIgnoreCase))
        {
            // Most System.* assemblies are implicitly included in .NET Core+
            implicitRefs.Add("System.Collections");
            implicitRefs.Add("System.Collections.Concurrent");
            implicitRefs.Add("System.Console");
            implicitRefs.Add("System.Diagnostics.Debug");
            implicitRefs.Add("System.Diagnostics.Tools");
            implicitRefs.Add("System.Diagnostics.Tracing");
            implicitRefs.Add("System.Globalization");
            implicitRefs.Add("System.IO");
            implicitRefs.Add("System.IO.Compression");
            implicitRefs.Add("System.Linq");
            implicitRefs.Add("System.Linq.Expressions");
            implicitRefs.Add("System.Net.Http");
            implicitRefs.Add("System.Net.Primitives");
            implicitRefs.Add("System.ObjectModel");
            implicitRefs.Add("System.Reflection");
            implicitRefs.Add("System.Reflection.Extensions");
            implicitRefs.Add("System.Reflection.Primitives");
            implicitRefs.Add("System.Resources.ResourceManager");
            implicitRefs.Add("System.Runtime");
            implicitRefs.Add("System.Runtime.Extensions");
            implicitRefs.Add("System.Runtime.InteropServices");
            implicitRefs.Add("System.Runtime.InteropServices.RuntimeInformation");
            implicitRefs.Add("System.Runtime.Numerics");
            implicitRefs.Add("System.Runtime.Serialization.Primitives");
            implicitRefs.Add("System.Text.Encoding");
            implicitRefs.Add("System.Text.Encoding.Extensions");
            implicitRefs.Add("System.Text.RegularExpressions");
            implicitRefs.Add("System.Threading");
            implicitRefs.Add("System.Threading.Tasks");
            implicitRefs.Add("System.Threading.Timer");
            implicitRefs.Add("System.Xml.ReaderWriter");
            implicitRefs.Add("System.Xml.XDocument");
        }
        
        return implicitRefs;
    }
}