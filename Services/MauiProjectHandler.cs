using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class MauiProjectHandler : IMauiProjectHandler
{
    private readonly ILogger<MauiProjectHandler> _logger;
    private bool _generateModernProgramCs = false;

    public MauiProjectHandler(ILogger<MauiProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<MauiProjectInfo> DetectMauiConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new MauiProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive package analysis
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        await AnalyzePackageReferences(info, packageReferences, cancellationToken);

        // Detect project structure and platform configuration
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Detect Xamarin.Forms legacy patterns that need migration
        await DetectLegacyXamarinPatterns(info, cancellationToken);

        // Analyze current MAUI configuration
        await AnalyzeMauiConfiguration(info, project, cancellationToken);

        // Detect custom renderers vs handlers
        await DetectCustomRenderersAndHandlers(info, cancellationToken);

        // Analyze resource structure and platform assets
        await AnalyzeResourceStructure(info, cancellationToken);

        // Check for platform-specific configurations
        await AnalyzePlatformConfigurations(info, cancellationToken);

        // Analyze native library bindings and P/Invoke
        await AnalyzeNativeLibraryBindings(info, project, cancellationToken);

        // Detect platform-specific build configurations
        await AnalyzePlatformBuildConfigurations(info, project, cancellationToken);

        _logger.LogInformation("Detected MAUI/Xamarin project: Type={ProjectType}, NeedsXamarinMigration={NeedsMigration}, TargetFrameworks={Frameworks}, LegacyPatterns={LegacyCount}, NativeLibs={NativeLibCount}",
            info.IsMauiProject ? "MAUI" : (info.IsXamarinForms ? "Xamarin.Forms" : "Unknown"), 
            info.NeedsXamarinFormsMigration, 
            string.Join(";", info.TargetPlatforms), 
            info.LegacyXamarinPatterns.Count,
            info.Properties.GetValueOrDefault("NativeLibraryCount", "0"));

        return info;
    }

    public async Task MigrateMauiProjectAsync(
        MauiProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (info.NeedsXamarinFormsMigration)
            {
                // Comprehensive Xamarin.Forms to MAUI migration
                await PerformXamarinToMauiMigration(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (info.IsMauiProject)
            {
                // Modernize existing MAUI project
                await ModernizeExistingMauiProject(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Configure new MAUI project from scratch
                await ConfigureNewMauiProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Apply common MAUI optimizations and best practices
            await ApplyMauiOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully migrated MAUI project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate MAUI project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "MAUI migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void MigratePlatformSpecificResources(string projectDirectory, XElement projectElement, MauiProjectInfo info)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Handle Platforms folder structure
        if (info.HasPlatformsFolder)
        {
            var platformsPath = Path.Combine(projectDirectory, "Platforms");
            var platformFolders = Directory.GetDirectories(platformsPath);

            foreach (var platformFolder in platformFolders)
            {
                var platformName = Path.GetFileName(platformFolder);
                var platformFiles = Directory.GetFiles(platformFolder, "*", SearchOption.AllDirectories);

                foreach (var file in platformFiles)
                {
                    var relativePath = Path.GetRelativePath(projectDirectory, file);
                    var extension = Path.GetExtension(file).ToLowerInvariant();

                    switch (extension)
                    {
                        case ".cs":
                            EnsureItemIncluded(itemGroup, "Compile", relativePath);
                            break;
                        case ".xml":
                        case ".axml":
                        case ".storyboard":
                        case ".xib":
                            EnsureItemIncluded(itemGroup, "MauiXaml", relativePath);
                            break;
                        default:
                            EnsureItemIncluded(itemGroup, "None", relativePath);
                            break;
                    }
                }
            }
        }

        // Handle legacy platform-specific files
        MigrateLegacyPlatformFiles(projectDirectory, itemGroup);
    }

    public void ConvertXamarinFormsToMaui(XElement projectElement, MauiProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Update target frameworks from Xamarin to MAUI
        SetOrUpdateProperty(propertyGroup, "SingleProject", "true");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");

        // Remove Xamarin-specific properties
        RemoveProperty(propertyGroup, "AndroidManifest");
        RemoveProperty(propertyGroup, "AndroidResgenFile");
        RemoveProperty(propertyGroup, "AndroidUseIntermediateDesignerFile");

        // Add MAUI-specific properties
        SetOrUpdateProperty(propertyGroup, "MauiVersion", "8.0.90");

        _logger.LogInformation("Converted Xamarin.Forms project to MAUI format");
    }

    public void MigrateAppResources(string projectDirectory, XElement projectElement)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");

        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Handle app icon
        var appIconPaths = new[]
        {
            Path.Combine(projectDirectory, "Resources", "AppIcon", "appicon.svg"),
            Path.Combine(projectDirectory, "appicon.svg"),
            Path.Combine(projectDirectory, "icon.png")
        };

        foreach (var iconPath in appIconPaths)
        {
            if (File.Exists(iconPath))
            {
                var relativePath = Path.GetRelativePath(projectDirectory, iconPath);
                EnsureItemIncluded(itemGroup, "MauiIcon", relativePath);
                break;
            }
        }

        // Handle splash screen
        var splashPaths = new[]
        {
            Path.Combine(projectDirectory, "Resources", "Splash", "splash.svg"),
            Path.Combine(projectDirectory, "splash.svg"),
            Path.Combine(projectDirectory, "splash.png")
        };

        foreach (var splashPath in splashPaths)
        {
            if (File.Exists(splashPath))
            {
                var relativePath = Path.GetRelativePath(projectDirectory, splashPath);
                EnsureItemIncluded(itemGroup, "MauiSplashScreen", relativePath);
                break;
            }
        }

        // Handle fonts
        var fontsPath = Path.Combine(projectDirectory, "Resources", "Fonts");
        if (Directory.Exists(fontsPath))
        {
            var fontFiles = Directory.GetFiles(fontsPath, "*", SearchOption.AllDirectories);
            foreach (var fontFile in fontFiles)
            {
                var relativePath = Path.GetRelativePath(projectDirectory, fontFile);
                EnsureItemIncluded(itemGroup, "MauiFont", relativePath);
            }
        }

        // Handle images
        var imagesPath = Path.Combine(projectDirectory, "Resources", "Images");
        if (Directory.Exists(imagesPath))
        {
            var imageFiles = Directory.GetFiles(imagesPath, "*", SearchOption.AllDirectories);
            foreach (var imageFile in imageFiles)
            {
                var relativePath = Path.GetRelativePath(projectDirectory, imageFile);
                EnsureItemIncluded(itemGroup, "MauiImage", relativePath);
            }
        }
    }

    private async Task DetectResourceFiles(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        var resourcesPath = Path.Combine(info.ProjectDirectory, "Resources");
        if (!Directory.Exists(resourcesPath))
            return;

        var resourceFiles = Directory.GetFiles(resourcesPath, "*", SearchOption.AllDirectories);
        info.ResourceFiles = resourceFiles
            .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
            .ToList();
    }

    private void DetectAppAssets(MauiProjectInfo info)
    {
        // Look for app icon
        var iconPaths = new[]
        {
            Path.Combine(info.ProjectDirectory, "Resources", "AppIcon", "appicon.svg"),
            Path.Combine(info.ProjectDirectory, "appicon.svg"),
            Path.Combine(info.ProjectDirectory, "icon.png")
        };

        info.AppIconPath = iconPaths.FirstOrDefault(File.Exists);

        // Look for splash screen
        var splashPaths = new[]
        {
            Path.Combine(info.ProjectDirectory, "Resources", "Splash", "splash.svg"),
            Path.Combine(info.ProjectDirectory, "splash.svg"),
            Path.Combine(info.ProjectDirectory, "splash.png")
        };

        info.SplashScreenPath = splashPaths.FirstOrDefault(File.Exists);
    }

    private void MigrateLegacyPlatformFiles(string projectDirectory, XElement itemGroup)
    {
        // Handle Android-specific files
        var androidFiles = Directory.GetFiles(projectDirectory, "*.Android.*", SearchOption.AllDirectories);
        foreach (var file in androidFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, file);
            EnsureItemIncluded(itemGroup, "Compile", relativePath, new Dictionary<string, string>
            {
                ["Condition"] = "'$(TargetFramework)' == 'net8.0-android'"
            });
        }

        // Handle iOS-specific files
        var iosFiles = Directory.GetFiles(projectDirectory, "*.iOS.*", SearchOption.AllDirectories);
        foreach (var file in iosFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, file);
            EnsureItemIncluded(itemGroup, "Compile", relativePath, new Dictionary<string, string>
            {
                ["Condition"] = "'$(TargetFramework)' == 'net8.0-ios'"
            });
        }
    }

    private async Task EnsureMauiPackages(List<PackageReference> packageReferences, MauiProjectInfo info, CancellationToken cancellationToken)
    {
        // Remove Xamarin.Forms packages if converting
        if (info.IsXamarinForms)
        {
            packageReferences.RemoveAll(p => p.PackageId.StartsWith("Xamarin.Forms"));
            packageReferences.RemoveAll(p => p.PackageId.StartsWith("Xamarin.Essentials"));
        }

        // Ensure Microsoft.Maui.Controls
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Maui.Controls"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Maui.Controls",
                Version = "8.0.90"
            });
        }

        // Ensure Microsoft.Maui.Controls.Compatibility (for Xamarin.Forms migration)
        if (info.IsXamarinForms && !packageReferences.Any(p => p.PackageId == "Microsoft.Maui.Controls.Compatibility"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Maui.Controls.Compatibility",
                Version = "8.0.90"
            });
        }
    }

    private static void SetOrUpdateProperty(XElement propertyGroup, string name, string value)
    {
        var existingProperty = propertyGroup.Element(name);
        if (existingProperty != null)
        {
            existingProperty.Value = value;
        }
        else
        {
            propertyGroup.Add(new XElement(name, value));
        }
    }

    private static void RemoveProperty(XElement propertyGroup, string name)
    {
        propertyGroup.Element(name)?.Remove();
    }

    private async Task AnalyzePackageReferences(MauiProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Microsoft.Maui.Controls":
                case "Microsoft.Maui.Graphics":
                case "Microsoft.Maui.Essentials":
                    info.IsMauiProject = true;
                    if (!string.IsNullOrEmpty(version))
                        info.MauiVersion = version;
                    break;

                case "Xamarin.Forms":
                    info.IsXamarinForms = true;
                    info.NeedsXamarinFormsMigration = true;
                    break;

                case "Xamarin.Android.Support.V4":
                case "Xamarin.Android.Support.V7.AppCompat":
                case "Xamarin.AndroidX.Migration":
                    info.IsXamarinAndroid = true;
                    info.NeedsXamarinFormsMigration = true;
                    break;

                case "Xamarin.iOS":
                    info.IsXamariniOS = true;
                    info.NeedsXamarinFormsMigration = true;
                    break;

                case "CommunityToolkit.Maui":
                case "CommunityToolkit.Mvvm":
                    info.UsesCommunityToolkit = true;
                    break;

                case "Microsoft.Maui.Authentication.WebView":
                case "Microsoft.Maui.Controls.Maps":
                    info.MauiFeatures[packageId] = version ?? "";
                    break;

                // Packages that need migration or replacement
                case var pkg when pkg.StartsWith("Xamarin.") && !pkg.StartsWith("Xamarin.AndroidX"):
                    info.IncompatiblePackages.Add(packageId);
                    break;
            }
        }

        // If no MAUI packages found but Xamarin packages exist, mark for migration
        if (!info.IsMauiProject && (info.IsXamarinForms || info.IsXamarinAndroid || info.IsXamariniOS))
        {
            info.NeedsXamarinFormsMigration = true;
        }
    }

    private async Task AnalyzeProjectStructure(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for single project structure (MAUI style)
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            info.TargetPlatforms = targetFrameworks.Split(';')
                .Select(tf => tf.Trim())
                .Where(tf => !string.IsNullOrEmpty(tf))
                .ToList();
            
            info.HasSingleProject = info.TargetPlatforms.Count > 1;
        }
        else
        {
            var targetFramework = project.GetPropertyValue("TargetFramework");
            if (!string.IsNullOrEmpty(targetFramework))
            {
                info.TargetPlatforms.Add(targetFramework);
            }
        }

        // Check for Platforms folder (modern MAUI structure)
        var platformsPath = Path.Combine(info.ProjectDirectory, "Platforms");
        info.HasPlatformsFolder = Directory.Exists(platformsPath);

        // Check for legacy platform-specific projects in parent directory
        var parentDir = Directory.GetParent(info.ProjectDirectory)?.FullName;
        if (!string.IsNullOrEmpty(parentDir))
        {
            var projectName = Path.GetFileNameWithoutExtension(info.ProjectPath);
            var androidProject = Path.Combine(parentDir, $"{projectName}.Android");
            var iosProject = Path.Combine(parentDir, $"{projectName}.iOS");
            
            info.HasLegacyPlatformProjects = Directory.Exists(androidProject) || Directory.Exists(iosProject);
        }

        // Determine .NET version
        foreach (var platform in info.TargetPlatforms)
        {
            if (platform.StartsWith("net9.0"))
            {
                info.NetVersion = "net9.0";
                break;
            }
            else if (platform.StartsWith("net8.0"))
            {
                info.NetVersion = "net8.0";
            }
        }
    }

    private async Task DetectLegacyXamarinPatterns(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, sourceFile);

                // Detect custom renderers
                if (content.Contains(": Renderer<") || content.Contains(": ViewRenderer<"))
                {
                    info.CustomRenderers.Add(relativePath);
                    info.LegacyXamarinPatterns.Add($"Custom Renderer in {relativePath}");
                }

                // Detect dependency services
                if (content.Contains("[assembly: Dependency") || content.Contains("DependencyService.Get"))
                {
                    info.DependencyServices.Add(relativePath);
                    info.LegacyXamarinPatterns.Add($"DependencyService usage in {relativePath}");
                }

                // Detect platform-specific code patterns
                if (content.Contains("#if __ANDROID__") || content.Contains("#if __IOS__"))
                {
                    info.PlatformSpecificCode.Add(relativePath);
                    info.LegacyXamarinPatterns.Add($"Platform-specific conditionals in {relativePath}");
                }

                // Detect legacy Xamarin.Forms imports
                if (content.Contains("using Xamarin.Forms"))
                {
                    info.LegacyXamarinPatterns.Add($"Xamarin.Forms namespace in {relativePath}");
                }

                // Detect MAUI handlers (modern replacement for renderers)
                if (content.Contains(": Microsoft.Maui.Handlers") || content.Contains(": IElementHandler"))
                {
                    info.CustomHandlers.Add(relativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze source file {File}: {Error}", sourceFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeMauiConfiguration(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for MAUI-specific properties
        var useMaui = project.GetPropertyValue("UseMaui");
        if (string.Equals(useMaui, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.IsMauiProject = true;
        }

        // Check for AOT compilation
        var enableAot = project.GetPropertyValue("EnableAotAnalyzer");
        if (string.Equals(enableAot, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.UsesAotCompilation = true;
        }

        // Check for MAUI Essentials usage
        var mauiProgram = Path.Combine(info.ProjectDirectory, "MauiProgram.cs");
        if (File.Exists(mauiProgram))
        {
            var content = await File.ReadAllTextAsync(mauiProgram, cancellationToken);
            if (content.Contains("UseMauiEssentials"))
            {
                info.UsesMauiEssentials = true;
            }
        }
    }

    private async Task DetectCustomRenderersAndHandlers(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        // Already detected in DetectLegacyXamarinPatterns, but let's add more comprehensive detection
        var renderersPath = Path.Combine(info.ProjectDirectory, "Renderers");
        if (Directory.Exists(renderersPath))
        {
            var rendererFiles = Directory.GetFiles(renderersPath, "*.cs", SearchOption.AllDirectories);
            foreach (var file in rendererFiles)
            {
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, file);
                if (!info.CustomRenderers.Contains(relativePath))
                {
                    info.CustomRenderers.Add(relativePath);
                }
            }
        }

        var handlersPath = Path.Combine(info.ProjectDirectory, "Handlers");
        if (Directory.Exists(handlersPath))
        {
            var handlerFiles = Directory.GetFiles(handlersPath, "*.cs", SearchOption.AllDirectories);
            foreach (var file in handlerFiles)
            {
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, file);
                if (!info.CustomHandlers.Contains(relativePath))
                {
                    info.CustomHandlers.Add(relativePath);
                }
            }
        }
    }

    private async Task AnalyzeResourceStructure(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        var resourcesPath = Path.Combine(info.ProjectDirectory, "Resources");
        if (Directory.Exists(resourcesPath))
        {
            // Analyze MAUI resource structure
            await AnalyzeMauiResources(info, resourcesPath, cancellationToken);
        }
        else
        {
            // Check for legacy Xamarin resource patterns
            await DetectLegacyResources(info, cancellationToken);
        }
    }

    private async Task AnalyzeMauiResources(MauiProjectInfo info, string resourcesPath, CancellationToken cancellationToken)
    {
        // App Icon
        var appIconPath = Path.Combine(resourcesPath, "AppIcon");
        if (Directory.Exists(appIconPath))
        {
            var iconFiles = Directory.GetFiles(appIconPath, "*", SearchOption.AllDirectories);
            info.AppIconPath = iconFiles.FirstOrDefault(f => f.EndsWith(".svg") || f.EndsWith(".png"));
        }

        // Splash Screen
        var splashPath = Path.Combine(resourcesPath, "Splash");
        if (Directory.Exists(splashPath))
        {
            var splashFiles = Directory.GetFiles(splashPath, "*", SearchOption.AllDirectories);
            info.SplashScreenPath = splashFiles.FirstOrDefault(f => f.EndsWith(".svg") || f.EndsWith(".png"));
        }

        // Fonts
        var fontsPath = Path.Combine(resourcesPath, "Fonts");
        if (Directory.Exists(fontsPath))
        {
            info.FontFiles = Directory.GetFiles(fontsPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
                .ToList();
        }

        // Images
        var imagesPath = Path.Combine(resourcesPath, "Images");
        if (Directory.Exists(imagesPath))
        {
            info.ImageFiles = Directory.GetFiles(imagesPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
                .ToList();
        }

        // All resource files
        info.ResourceFiles = Directory.GetFiles(resourcesPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
            .ToList();
    }

    private async Task DetectLegacyResources(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        // Look for legacy Xamarin.Forms resource patterns
        var legacyResourcePaths = new[]
        {
            Path.Combine(info.ProjectDirectory, "Images"),
            Path.Combine(info.ProjectDirectory, "Assets"),
            Path.Combine(info.ProjectDirectory, "Resources")
        };

        foreach (var path in legacyResourcePaths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
                    .ToList();
                
                info.ResourceFiles.AddRange(files);
                
                // Try to detect app icon and splash from legacy structure
                if (string.IsNullOrEmpty(info.AppIconPath))
                {
                    info.AppIconPath = files.FirstOrDefault(f => f.Contains("icon", StringComparison.OrdinalIgnoreCase));
                }
                
                if (string.IsNullOrEmpty(info.SplashScreenPath))
                {
                    info.SplashScreenPath = files.FirstOrDefault(f => f.Contains("splash", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    private async Task AnalyzePlatformConfigurations(MauiProjectInfo info, CancellationToken cancellationToken)
    {
        if (info.HasPlatformsFolder)
        {
            var platformsPath = Path.Combine(info.ProjectDirectory, "Platforms");
            var platforms = Directory.GetDirectories(platformsPath);
            
            foreach (var platform in platforms)
            {
                var platformName = Path.GetFileName(platform);
                var configFiles = Directory.GetFiles(platform, "*", SearchOption.AllDirectories);
                
                info.PlatformVersions[platformName] = $"{configFiles.Length} files";
            }
        }
    }

    private async Task PerformXamarinToMauiMigration(MauiProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing comprehensive Xamarin.Forms to .NET MAUI migration");

        // Set MAUI SDK
        projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Maui");

        // Configure project properties for MAUI
        await ConfigureMauiProjectProperties(info, projectElement, result, cancellationToken);

        // Migrate packages from Xamarin to MAUI equivalents
        await MigrateXamarinPackagesToMaui(packageReferences, info, result, cancellationToken);

        // Create MauiProgram.cs if it doesn't exist
        await CreateMauiProgramCs(info, result, cancellationToken);

        result.Warnings.Add("Xamarin.Forms to MAUI migration completed. Review generated migration guidance and test thoroughly.");
        result.Warnings.Add("Custom renderers need to be converted to MAUI handlers manually.");
        result.Warnings.Add("DependencyService usage should be replaced with dependency injection.");
    }

    private async Task ModernizeExistingMauiProject(MauiProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing MAUI project to latest best practices");

        // Update to latest .NET version
        await UpdateToLatestNetVersion(info, projectElement, result, cancellationToken);

        // Update MAUI packages to latest versions
        await UpdateMauiPackagesToLatest(packageReferences, info, result, cancellationToken);

        result.Warnings.Add("MAUI project modernized with latest best practices and optimizations.");
    }

    private async Task ConfigureNewMauiProject(MauiProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring new MAUI project with best practices");

        // Set MAUI SDK
        projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Maui");

        // Configure for modern MAUI
        await ConfigureMauiProjectProperties(info, projectElement, result, cancellationToken);

        // Add essential MAUI packages
        await AddEssentialMauiPackages(packageReferences, info, result, cancellationToken);

        result.Warnings.Add("New MAUI project configured with modern best practices.");
    }

    private async Task ConfigureMauiProjectProperties(MauiProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Essential MAUI properties
        SetOrUpdateProperty(propertyGroup, "TargetFrameworks", 
            info.NetVersion == "net9.0" ? 
            "net9.0-android;net9.0-ios;net9.0-maccatalyst;net9.0-windows10.0.19041.0" :
            "net8.0-android;net8.0-ios;net8.0-maccatalyst;net8.0-windows10.0.19041.0");
        
        SetOrUpdateProperty(propertyGroup, "OutputType", "Exe");
        SetOrUpdateProperty(propertyGroup, "UseMaui", "true");
        SetOrUpdateProperty(propertyGroup, "SingleProject", "true");

        // Modern .NET features
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");

        // MAUI-specific optimizations
        SetOrUpdateProperty(propertyGroup, "EnableMauiCssSelector", "true");
    }

    private async Task MigrateXamarinPackagesToMaui(List<PackageReference> packageReferences, MauiProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var packageMigrations = new Dictionary<string, string>
        {
            ["Xamarin.Forms"] = "Microsoft.Maui.Controls",
            ["Xamarin.Essentials"] = "Microsoft.Maui.Essentials",
            ["Xamarin.Forms.Maps"] = "Microsoft.Maui.Controls.Maps",
            ["Xamarin.CommunityToolkit"] = "CommunityToolkit.Maui",
            ["Xamarin.Forms.Visual.Material"] = "" // Built into MAUI
        };

        var packagesToRemove = new List<PackageReference>();
        var packagesToAdd = new List<PackageReference>();

        foreach (var package in packageReferences)
        {
            if (packageMigrations.TryGetValue(package.PackageId, out var replacement))
            {
                packagesToRemove.Add(package);
                
                if (!string.IsNullOrEmpty(replacement) && 
                    !packageReferences.Any(p => p.PackageId == replacement))
                {
                    packagesToAdd.Add(new PackageReference
                    {
                        PackageId = replacement,
                        Version = "8.0.90"
                    });
                }
                
                result.Warnings.Add($"Migrated package: {package.PackageId} → {replacement}");
            }
        }

        // Remove old packages
        foreach (var package in packagesToRemove)
        {
            packageReferences.Remove(package);
        }

        // Add new packages
        packageReferences.AddRange(packagesToAdd);

        // Add essential MAUI packages
        await AddEssentialMauiPackages(packageReferences, info, result, cancellationToken);
    }

    private async Task AddEssentialMauiPackages(List<PackageReference> packageReferences, MauiProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var essentialPackages = new[]
        {
            ("Microsoft.Maui.Controls", "8.0.90"),
            ("Microsoft.Maui.Controls.Compatibility", "8.0.90"), // For easier Xamarin.Forms migration
            ("Microsoft.Extensions.Logging.Debug", "8.0.0")
        };

        foreach (var (packageId, version) in essentialPackages)
        {
            if (!packageReferences.Any(p => p.PackageId == packageId))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = packageId,
                    Version = version
                });
            }
        }
    }

    private async Task CreateMauiProgramCs(MauiProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        if (!_generateModernProgramCs)
        {
            _logger.LogInformation("Skipping MauiProgram.cs generation as GenerateModernProgramCs is disabled");
            return;
        }
        
        var mauiProgramPath = Path.Combine(info.ProjectDirectory, "MauiProgram.cs");
        
        if (!File.Exists(mauiProgramPath))
        {
            var mauiProgramContent = $@"using Microsoft.Extensions.Logging;

namespace {Path.GetFileNameWithoutExtension(info.ProjectPath)};

public static class MauiProgram
{{
    public static MauiApp CreateMauiApp()
    {{
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {{
                fonts.AddFont(""OpenSans-Regular.ttf"", ""OpenSansRegular"");
            }});

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }}
}}
";
            await File.WriteAllTextAsync(mauiProgramPath, mauiProgramContent, cancellationToken);
            result.Warnings.Add("Created MauiProgram.cs with modern configuration. Review and customize as needed.");
            _logger.LogInformation("Created MauiProgram.cs for MAUI project");
        }
    }

    private async Task ApplyMauiOptimizations(MauiProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Performance optimizations
        SetOrUpdateProperty(propertyGroup, "PublishTrimmed", "true");
        SetOrUpdateProperty(propertyGroup, "TrimMode", "partial");
        
        result.Warnings.Add("Applied MAUI performance optimizations. Monitor app size and startup performance.");
    }

    private async Task UpdateToLatestNetVersion(MauiProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        if (info.NetVersion != "net9.0" && info.NetVersion != "net8.0")
        {
            var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault();
            if (propertyGroup != null)
            {
                var targetFrameworks = propertyGroup.Element("TargetFrameworks")?.Value;
                if (!string.IsNullOrEmpty(targetFrameworks))
                {
                    var updatedFrameworks = targetFrameworks.Replace("net6.0", "net8.0").Replace("net7.0", "net8.0");
                    SetOrUpdateProperty(propertyGroup, "TargetFrameworks", updatedFrameworks);
                    result.Warnings.Add("Updated target frameworks to .NET 8 for better performance and support.");
                }
            }
        }
    }

    private async Task UpdateMauiPackagesToLatest(List<PackageReference> packageReferences, MauiProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var mauiPackages = packageReferences.Where(p => p.PackageId.StartsWith("Microsoft.Maui")).ToList();
        
        foreach (var package in mauiPackages)
        {
            if (package.Version != "8.0.90")
            {
                package.Version = "8.0.90";
                result.Warnings.Add($"Updated {package.PackageId} to version 8.0.90");
            }
        }
    }

    private static void EnsureItemIncluded(XElement itemGroup, string itemType, string include, Dictionary<string, string>? metadata = null)
    {
        var existingItem = itemGroup.Elements(itemType)
            .FirstOrDefault(e => e.Attribute("Include")?.Value == include);

        if (existingItem == null)
        {
            var item = new XElement(itemType, new XAttribute("Include", include));
            
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    if (kvp.Key == "Condition")
                    {
                        item.SetAttributeValue("Condition", kvp.Value);
                    }
                    else
                    {
                        item.Add(new XElement(kvp.Key, kvp.Value));
                    }
                }
            }
            
            itemGroup.Add(item);
        }
    }

    // Native library binding analysis methods
    private async Task AnalyzeNativeLibraryBindings(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Analyzing native library bindings for MAUI project");

        var nativeLibraries = new List<NativeLibraryInfo>();
        var bindingProjects = new List<string>();
        var pinvokeUsage = new List<PInvokeInfo>();

        try
        {
            // Detect native libraries in project
            await DetectNativeLibraries(info, project, nativeLibraries, cancellationToken);

            // Detect Xamarin binding projects
            await DetectBindingProjects(info, bindingProjects, cancellationToken);

            // Analyze P/Invoke usage
            await AnalyzePInvokeUsage(info, pinvokeUsage, cancellationToken);

            // Analyze platform-specific native references
            await AnalyzePlatformNativeReferences(info, project, cancellationToken);

            // Detect embedded native libraries
            await DetectEmbeddedNativeLibraries(info, project, cancellationToken);

            // Analyze native dependency chains
            await AnalyzeNativeDependencyChains(info, nativeLibraries, cancellationToken);

            // Store analysis results
            StoreNativeLibraryAnalysisResults(info, nativeLibraries, bindingProjects, pinvokeUsage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze native library bindings: {Error}", ex.Message);
            info.Properties["NativeLibraryAnalysisError"] = ex.Message;
        }
    }

    private async Task DetectNativeLibraries(MauiProjectInfo info, Project project, List<NativeLibraryInfo> nativeLibraries, CancellationToken cancellationToken)
    {
        // Check for native library references in project
        var nativeReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "NativeReference" || 
                          item.ItemType == "ObjcBindingNativeLibrary" ||
                          item.ItemType == "AndroidNativeLibrary" ||
                          item.ItemType == "EmbeddedNativeLibrary")
            .ToList();

        foreach (var nativeRef in nativeReferences)
        {
            var libInfo = new NativeLibraryInfo
            {
                LibraryPath = nativeRef.EvaluatedInclude,
                ItemType = nativeRef.ItemType,
                Platform = DeterminePlatformFromItemType(nativeRef.ItemType),
                Metadata = new Dictionary<string, string>()
            };

            // Extract metadata
            foreach (var metadata in nativeRef.Metadata)
            {
                libInfo.Metadata[metadata.Name] = metadata.EvaluatedValue;
            }

            // Check library type
            var extension = Path.GetExtension(libInfo.LibraryPath).ToLowerInvariant();
            libInfo.LibraryType = extension switch
            {
                ".a" => "Static Library (iOS/macOS)",
                ".framework" => "Framework (iOS/macOS)",
                ".dylib" => "Dynamic Library (macOS)",
                ".so" => "Shared Object (Android/Linux)",
                ".dll" => "Dynamic Link Library (Windows)",
                ".lib" => "Static Library (Windows)",
                ".aar" => "Android Archive",
                ".jar" => "Java Archive",
                _ => "Unknown"
            };

            nativeLibraries.Add(libInfo);
        }

        // Check for libraries in specific folders
        var platformFolders = new[]
        {
            ("Platforms/Android/libs", "Android"),
            ("Platforms/iOS/libs", "iOS"),
            ("Platforms/MacCatalyst/libs", "MacCatalyst"),
            ("Platforms/Windows/libs", "Windows"),
            ("runtimes", "Multi-platform")
        };

        foreach (var (folder, platform) in platformFolders)
        {
            var libPath = Path.Combine(info.ProjectDirectory, folder);
            if (Directory.Exists(libPath))
            {
                var libs = Directory.GetFiles(libPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsNativeLibrary(f))
                    .ToList();

                foreach (var lib in libs)
                {
                    nativeLibraries.Add(new NativeLibraryInfo
                    {
                        LibraryPath = lib,
                        Platform = platform,
                        LibraryType = GetLibraryTypeFromPath(lib),
                        IsEmbedded = true
                    });
                }
            }
        }

        info.Properties["NativeLibraryCount"] = nativeLibraries.Count.ToString();
    }

    private async Task DetectBindingProjects(MauiProjectInfo info, List<string> bindingProjects, CancellationToken cancellationToken)
    {
        // Look for Xamarin binding projects in solution
        var solutionDir = FindSolutionDirectory(info.ProjectDirectory);
        if (!string.IsNullOrEmpty(solutionDir))
        {
            var projectFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
            
            foreach (var projFile in projectFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(projFile, cancellationToken);
                    
                    // Check for binding project indicators
                    if (content.Contains("Xamarin.iOS.ObjCBinding") ||
                        content.Contains("Xamarin.Android.Binding") ||
                        content.Contains("XamarinBuildAndroidAarRestore") ||
                        content.Contains("ObjcBindingNativeLibrary") ||
                        content.Contains("<IsBindingProject>true</IsBindingProject>"))
                    {
                        bindingProjects.Add(projFile);
                        
                        // Analyze binding project
                        await AnalyzeBindingProject(info, projFile, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze potential binding project {File}: {Error}", projFile, ex.Message);
                }
            }
        }

        if (bindingProjects.Any())
        {
            info.Properties["HasBindingProjects"] = "true";
            info.Properties["BindingProjectCount"] = bindingProjects.Count.ToString();
        }
    }

    private async Task AnalyzePInvokeUsage(MauiProjectInfo info, List<PInvokeInfo> pinvokeUsage, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(100); // Limit for performance

        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for DllImport attributes
                var dllImportMatches = Regex.Matches(content, @"\[DllImport\s*\(\s*""([^""]+)""[^\]]*\]\s*(?:.*\s+)?(\w+)\s*\(");
                
                foreach (Match match in dllImportMatches)
                {
                    var libraryName = match.Groups[1].Value;
                    var methodName = match.Groups[2].Value;
                    
                    pinvokeUsage.Add(new PInvokeInfo
                    {
                        LibraryName = libraryName,
                        MethodName = methodName,
                        SourceFile = file,
                        Platform = DeterminePlatformFromLibraryName(libraryName)
                    });
                }

                // Check for LibraryImport (.NET 7+)
                if (content.Contains("[LibraryImport"))
                {
                    info.Properties["UsesLibraryImport"] = "true";
                    var libraryImportMatches = Regex.Matches(content, @"\[LibraryImport\s*\(\s*""([^""]+)""[^\]]*\]");
                    
                    foreach (Match match in libraryImportMatches)
                    {
                        pinvokeUsage.Add(new PInvokeInfo
                        {
                            LibraryName = match.Groups[1].Value,
                            SourceFile = file,
                            IsLibraryImport = true,
                            Platform = DeterminePlatformFromLibraryName(match.Groups[1].Value)
                        });
                    }
                }

                // Check for unsafe code
                if (content.Contains("unsafe ") || content.Contains("unsafe{"))
                {
                    info.Properties["UsesUnsafeCode"] = "true";
                }

                // Check for function pointers
                if (content.Contains("delegate*"))
                {
                    info.Properties["UsesFunctionPointers"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze P/Invoke in {File}: {Error}", file, ex.Message);
            }
        }

        if (pinvokeUsage.Any())
        {
            info.Properties["UsesPInvoke"] = "true";
            info.Properties["PInvokeCount"] = pinvokeUsage.Count.ToString();
            info.Properties["PInvokeLibraries"] = string.Join(";", pinvokeUsage.Select(p => p.LibraryName).Distinct());
        }
    }

    private async Task AnalyzePlatformNativeReferences(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Android-specific native references
        var androidLibraries = project.AllEvaluatedItems
            .Where(item => item.ItemType == "AndroidAarLibrary" || 
                          item.ItemType == "AndroidJavaLibrary" ||
                          item.ItemType == "AndroidLibrary")
            .ToList();

        if (androidLibraries.Any())
        {
            info.Properties["AndroidNativeLibraryCount"] = androidLibraries.Count.ToString();
            info.Properties["HasAndroidNativeLibraries"] = "true";
            
            // Check for specific Android native patterns
            foreach (var lib in androidLibraries)
            {
                if (lib.EvaluatedInclude.EndsWith(".aar"))
                {
                    info.Properties["HasAndroidAarLibraries"] = "true";
                }
                if (lib.EvaluatedInclude.EndsWith(".jar"))
                {
                    info.Properties["HasAndroidJarLibraries"] = "true";
                }
            }
        }

        // iOS-specific native references
        var iosLibraries = project.AllEvaluatedItems
            .Where(item => item.ItemType == "NativeReference" && 
                          (item.EvaluatedInclude.EndsWith(".a") || 
                           item.EvaluatedInclude.EndsWith(".framework")))
            .ToList();

        if (iosLibraries.Any())
        {
            info.Properties["iOSNativeLibraryCount"] = iosLibraries.Count.ToString();
            info.Properties["HasiOSNativeLibraries"] = "true";
            
            // Check for frameworks
            var frameworks = iosLibraries.Where(l => l.EvaluatedInclude.EndsWith(".framework")).ToList();
            if (frameworks.Any())
            {
                info.Properties["iOSFrameworks"] = string.Join(";", frameworks.Select(f => Path.GetFileNameWithoutExtension(f.EvaluatedInclude)));
            }
        }

        // Windows-specific native references
        var windowsLibraries = project.AllEvaluatedItems
            .Where(item => (item.ItemType == "Reference" || item.ItemType == "NativeReference") && 
                          (item.EvaluatedInclude.EndsWith(".dll") || 
                           item.EvaluatedInclude.EndsWith(".lib")))
            .Where(item => !item.EvaluatedInclude.Contains("Microsoft.") && 
                          !item.EvaluatedInclude.Contains("System."))
            .ToList();

        if (windowsLibraries.Any())
        {
            info.Properties["WindowsNativeLibraryCount"] = windowsLibraries.Count.ToString();
            info.Properties["HasWindowsNativeLibraries"] = "true";
        }
    }

    private async Task DetectEmbeddedNativeLibraries(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for embedded resources that might be native libraries
        var embeddedResources = project.AllEvaluatedItems
            .Where(item => item.ItemType == "EmbeddedResource")
            .Where(item => IsNativeLibrary(item.EvaluatedInclude))
            .ToList();

        if (embeddedResources.Any())
        {
            info.Properties["HasEmbeddedNativeLibraries"] = "true";
            info.Properties["EmbeddedNativeLibraryCount"] = embeddedResources.Count.ToString();
        }

        // Check for Content or None items that might be native libraries
        var contentLibraries = project.AllEvaluatedItems
            .Where(item => item.ItemType == "Content" || item.ItemType == "None")
            .Where(item => IsNativeLibrary(item.EvaluatedInclude))
            .Where(item => item.GetMetadataValue("CopyToOutputDirectory") != "")
            .ToList();

        if (contentLibraries.Any())
        {
            info.Properties["HasContentNativeLibraries"] = "true";
            info.Properties["ContentNativeLibraryCount"] = contentLibraries.Count.ToString();
        }
    }

    private async Task AnalyzeNativeDependencyChains(MauiProjectInfo info, List<NativeLibraryInfo> nativeLibraries, CancellationToken cancellationToken)
    {
        // Analyze potential dependency chains and compatibility issues
        var platforms = nativeLibraries.Select(l => l.Platform).Distinct().ToList();
        
        if (platforms.Count > 1)
        {
            info.Properties["HasMultiPlatformNativeLibraries"] = "true";
            info.Properties["NativeLibraryPlatforms"] = string.Join(";", platforms);
        }

        // Check for architecture mismatches
        var architectures = new HashSet<string>();
        foreach (var lib in nativeLibraries)
        {
            if (lib.Metadata.TryGetValue("Architecture", out var arch))
            {
                architectures.Add(arch);
            }
            else
            {
                // Try to infer from path
                if (lib.LibraryPath.Contains("x64") || lib.LibraryPath.Contains("x86_64"))
                    architectures.Add("x64");
                else if (lib.LibraryPath.Contains("x86"))
                    architectures.Add("x86");
                else if (lib.LibraryPath.Contains("arm64"))
                    architectures.Add("arm64");
                else if (lib.LibraryPath.Contains("arm"))
                    architectures.Add("arm");
            }
        }

        if (architectures.Count > 0)
        {
            info.Properties["NativeLibraryArchitectures"] = string.Join(";", architectures);
            
            // Check for missing architectures for target platforms
            if (info.TargetPlatforms.Contains("ios") && !architectures.Contains("arm64"))
            {
                info.Warnings.Add("iOS target detected but no arm64 native libraries found");
            }
            
            if (info.TargetPlatforms.Contains("android") && 
                !(architectures.Contains("arm64") || architectures.Contains("x86_64")))
            {
                info.Warnings.Add("Android target detected but no 64-bit native libraries found (required for Google Play)");
            }
        }

        // Check for runtime package dependencies
        var runtimesFolder = Path.Combine(info.ProjectDirectory, "runtimes");
        if (Directory.Exists(runtimesFolder))
        {
            var runtimeIdentifiers = Directory.GetDirectories(runtimesFolder)
                .Select(d => Path.GetFileName(d))
                .ToList();
            
            if (runtimeIdentifiers.Any())
            {
                info.Properties["HasRuntimeSpecificLibraries"] = "true";
                info.Properties["RuntimeIdentifiers"] = string.Join(";", runtimeIdentifiers);
            }
        }
    }

    private async Task AnalyzeBindingProject(MauiProjectInfo info, string bindingProjectPath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(bindingProjectPath, cancellationToken);
            var bindingInfo = new Dictionary<string, string>();
            
            // Extract binding metadata
            var bindingType = "Unknown";
            if (content.Contains("Xamarin.iOS.ObjCBinding"))
                bindingType = "iOS";
            else if (content.Contains("Xamarin.Android.Binding"))
                bindingType = "Android";
            
            bindingInfo["Type"] = bindingType;
            bindingInfo["ProjectPath"] = bindingProjectPath;
            
            // Check for API definition files
            if (content.Contains("ObjcBindingApiDefinition"))
            {
                bindingInfo["HasApiDefinition"] = "true";
            }
            
            if (content.Contains("ObjcBindingCoreSource"))
            {
                bindingInfo["HasStructsAndEnums"] = "true";
            }
            
            // Check for transforms
            if (content.Contains("TransformFile"))
            {
                bindingInfo["HasTransforms"] = "true";
            }
            
            // Store binding project info
            var bindingKey = $"BindingProject_{Path.GetFileNameWithoutExtension(bindingProjectPath)}";
            foreach (var kvp in bindingInfo)
            {
                info.Properties[$"{bindingKey}_{kvp.Key}"] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze binding project {Path}: {Error}", bindingProjectPath, ex.Message);
        }
    }

    private void StoreNativeLibraryAnalysisResults(
        MauiProjectInfo info, 
        List<NativeLibraryInfo> nativeLibraries, 
        List<string> bindingProjects, 
        List<PInvokeInfo> pinvokeUsage)
    {
        // Summary statistics
        info.Properties["TotalNativeLibraries"] = nativeLibraries.Count.ToString();
        info.Properties["TotalBindingProjects"] = bindingProjects.Count.ToString();
        info.Properties["TotalPInvokeCalls"] = pinvokeUsage.Count.ToString();
        
        // Complexity assessment
        var complexityScore = 0;
        if (nativeLibraries.Count > 0) complexityScore += 2;
        if (nativeLibraries.Count > 5) complexityScore += 3;
        if (bindingProjects.Count > 0) complexityScore += 3;
        if (pinvokeUsage.Count > 0) complexityScore += 2;
        if (pinvokeUsage.Count > 10) complexityScore += 3;
        if (info.Properties.ContainsKey("UsesUnsafeCode")) complexityScore += 2;
        if (info.Properties.ContainsKey("HasMultiPlatformNativeLibraries")) complexityScore += 3;
        
        info.Properties["NativeLibraryComplexity"] = complexityScore switch
        {
            0 => "None",
            < 5 => "Low",
            < 10 => "Medium",
            < 15 => "High",
            _ => "Very High"
        };
        
        // Migration warnings
        if (nativeLibraries.Count > 0 || bindingProjects.Count > 0 || pinvokeUsage.Count > 0)
        {
            info.Warnings.Add($"Project uses native libraries/bindings. Manual review required for: " +
                            $"{nativeLibraries.Count} native libraries, " +
                            $"{bindingProjects.Count} binding projects, " +
                            $"{pinvokeUsage.Count} P/Invoke calls");
        }
        
        if (info.Properties.ContainsKey("UsesLibraryImport"))
        {
            info.Warnings.Add("Project uses [LibraryImport] attribute (source-generated P/Invoke). Ensure .NET 7+ target.");
        }
        
        if (bindingProjects.Count > 0)
        {
            info.Warnings.Add("Xamarin binding projects detected. Consider migrating to .NET for iOS/Android binding projects.");
        }
        
        // Platform-specific warnings
        if (info.Properties.ContainsKey("HasAndroidAarLibraries"))
        {
            info.Warnings.Add("Android AAR libraries detected. Ensure they are compatible with .NET for Android.");
        }
        
        if (info.Properties.ContainsKey("iOSFrameworks"))
        {
            info.Warnings.Add($"iOS frameworks detected: {info.Properties["iOSFrameworks"]}. Verify framework compatibility with .NET for iOS.");
        }
    }

    private async Task AnalyzePlatformBuildConfigurations(MauiProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for platform-specific build configurations
        var configurations = project.ConditionedProperties.Where(p => p.Key == "Configuration")
            .SelectMany(p => p.Value)
            .Distinct()
            .ToList();
        
        var platformConfigs = configurations.Where(c => 
            c.Contains("iPhone") || c.Contains("Android") || 
            c.Contains("iOS") || c.Contains("Windows"))
            .ToList();
        
        if (platformConfigs.Any())
        {
            info.Properties["HasPlatformSpecificConfigurations"] = "true";
            info.Properties["PlatformConfigurations"] = string.Join(";", platformConfigs);
        }
        
        // Check for RuntimeIdentifiers
        var runtimeIdentifiers = project.GetPropertyValue("RuntimeIdentifiers");
        if (!string.IsNullOrEmpty(runtimeIdentifiers))
        {
            info.Properties["RuntimeIdentifiers"] = runtimeIdentifiers;
            
            // Check if all required RIDs are present for native libraries
            if (info.Properties.ContainsKey("NativeLibraryPlatforms"))
            {
                var requiredRids = GetRequiredRuntimeIdentifiers(info.Properties["NativeLibraryPlatforms"]);
                var currentRids = runtimeIdentifiers.Split(';');
                var missingRids = requiredRids.Except(currentRids).ToList();
                
                if (missingRids.Any())
                {
                    info.Warnings.Add($"Missing RuntimeIdentifiers for native libraries: {string.Join(", ", missingRids)}");
                }
            }
        }
    }

    // Helper methods
    private bool IsNativeLibrary(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".so" or ".a" or ".dylib" or ".framework" => true,
            ".dll" when !path.Contains("Microsoft.") && !path.Contains("System.") => true,
            ".lib" or ".aar" or ".jar" => true,
            _ => false
        };
    }

    private string GetLibraryTypeFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".a" => "Static Library",
            ".so" => "Shared Object",
            ".dylib" => "Dynamic Library",
            ".framework" => "Framework",
            ".dll" => "Dynamic Link Library",
            ".lib" => "Import Library",
            ".aar" => "Android Archive",
            ".jar" => "Java Archive",
            _ => "Unknown"
        };
    }

    private string DeterminePlatformFromItemType(string itemType)
    {
        return itemType switch
        {
            "AndroidNativeLibrary" => "Android",
            "AndroidAarLibrary" => "Android",
            "AndroidJavaLibrary" => "Android",
            "ObjcBindingNativeLibrary" => "iOS",
            "NativeReference" => "iOS/macOS",
            _ => "Unknown"
        };
    }

    private string DeterminePlatformFromLibraryName(string libraryName)
    {
        var lower = libraryName.ToLowerInvariant();
        
        if (lower.Contains("android") || lower.EndsWith(".so"))
            return "Android";
        if (lower.Contains("ios") || lower.Contains("foundation") || lower.Contains("uikit"))
            return "iOS";
        if (lower.Contains("kernel32") || lower.Contains("user32") || lower.Contains("advapi32"))
            return "Windows";
        if (lower.Contains("libc") || lower.Contains("libdl"))
            return "Linux";
        if (lower.Contains("system") || lower.EndsWith(".dylib"))
            return "macOS";
        
        return "Cross-platform";
    }

    private string FindSolutionDirectory(string projectDirectory)
    {
        var currentDir = new DirectoryInfo(projectDirectory);
        
        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any())
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        
        return string.Empty;
    }

    private List<string> GetRequiredRuntimeIdentifiers(string platforms)
    {
        var rids = new List<string>();
        var platformList = platforms.Split(';');
        
        foreach (var platform in platformList)
        {
            switch (platform.ToLowerInvariant())
            {
                case "android":
                    rids.AddRange(new[] { "android-arm64", "android-x64", "android-arm", "android-x86" });
                    break;
                case "ios":
                    rids.AddRange(new[] { "ios-arm64", "iossimulator-arm64", "iossimulator-x64" });
                    break;
                case "windows":
                    rids.AddRange(new[] { "win-x64", "win-x86", "win-arm64" });
                    break;
                case "macos":
                    rids.AddRange(new[] { "osx-x64", "osx-arm64" });
                    break;
            }
        }
        
        return rids.Distinct().ToList();
    }

    // Helper classes for native library analysis
    private class NativeLibraryInfo
    {
        public string LibraryPath { get; set; } = string.Empty;
        public string LibraryType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public bool IsEmbedded { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    private class PInvokeInfo
    {
        public string LibraryName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public bool IsLibraryImport { get; set; }
    }
    
    public void SetGenerateModernProgramCs(bool enabled)
    {
        _generateModernProgramCs = enabled;
        _logger.LogInformation("GenerateModernProgramCs set to: {Enabled}", enabled);
    }
}