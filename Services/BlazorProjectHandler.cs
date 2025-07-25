using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class BlazorProjectHandler : IBlazorProjectHandler
{
    private readonly ILogger<BlazorProjectHandler> _logger;

    public BlazorProjectHandler(ILogger<BlazorProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<BlazorProjectInfo> DetectBlazorConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new BlazorProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Analyze project structure and determine .NET version
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Comprehensive package analysis for Blazor hosting models
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        await AnalyzeBlazorPackages(info, packageReferences, cancellationToken);

        // Detect render modes and .NET 8+ features
        await AnalyzeRenderModes(info, project, cancellationToken);

        // Analyze Razor components and pages
        await AnalyzeRazorComponents(info, cancellationToken);

        // Check for configuration files and hosting setup
        await AnalyzeConfigurationFiles(info, cancellationToken);

        // Detect PWA and static assets
        await AnalyzeStaticAssets(info, cancellationToken);

        // Analyze performance optimizations
        await AnalyzePerformanceOptimizations(info, project, cancellationToken);

        // Detect legacy patterns requiring migration
        await DetectLegacyPatterns(info, cancellationToken);

        // Analyze JavaScript interop complexity
        await AnalyzeJavaScriptInterop(info, project, cancellationToken);

        // Analyze component interactions and state management
        await AnalyzeComponentComplexity(info, cancellationToken);

        _logger.LogInformation("Detected Blazor project: Type={Type}, RenderMode={RenderMode}, .NET={NetVersion}, NeedsMigration={NeedsMigration}, JSInterop={JSInteropComplexity}",
            GetBlazorProjectType(info), info.GlobalRenderMode, info.IsNet8Plus ? "8+" : "Legacy", info.NeedsNet8Migration, 
            info.Properties.GetValueOrDefault("JSInteropComplexity", "Unknown"));

        return info;
    }

    public async Task MigrateBlazorProjectAsync(
        BlazorProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (info.NeedsNet8Migration)
            {
                // Migrate legacy Blazor project to .NET 8+ with modern render modes
                await MigrateToModernBlazor(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (info.IsNet8Plus)
            {
                // Modernize existing .NET 8+ Blazor project
                await ModernizeNet8BlazorProject(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Configure legacy Blazor project with best practices
                await ConfigureLegacyBlazorProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Apply common optimizations
            await ApplyBlazorOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully migrated Blazor project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate Blazor project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "Blazor migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void EnsureBlazorStaticAssetsIncluded(string projectDirectory, XElement projectElement)
    {
        var wwwrootPath = Path.Combine(projectDirectory, "wwwroot");
        if (!Directory.Exists(wwwrootPath))
            return;

        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Include all wwwroot content as static web assets
        var staticFiles = Directory.GetFiles(wwwrootPath, "*", SearchOption.AllDirectories);
        foreach (var file in staticFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, file);
            var fileName = Path.GetFileName(file);
            
            // Special handling for specific file types
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".json" when fileName == "manifest.json":
                    // PWA manifest - already handled in PWA migration
                    break;
                case ".js" when fileName.Contains("service-worker"):
                    // Service worker - already handled in PWA migration
                    break;
                default:
                    EnsureItemIncluded(itemGroup, "Content", relativePath);
                    break;
            }
        }

        // Ensure index.html is properly configured for Blazor WebAssembly
        var indexHtmlPath = Path.Combine(wwwrootPath, "index.html");
        if (File.Exists(indexHtmlPath))
        {
            EnsureItemIncluded(itemGroup, "Content", "wwwroot/index.html");
        }
    }

    public void MigratePwaConfiguration(string projectDirectory, XElement projectElement, BlazorProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Enable PWA features
        SetOrUpdateProperty(propertyGroup, "ServiceWorkerAssetsManifest", "service-worker-assets.js");

        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Include manifest.json
        if (!string.IsNullOrEmpty(info.ManifestJsonPath))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, info.ManifestJsonPath);
            EnsureItemIncluded(itemGroup, "Content", relativePath);
        }

        // Include service worker
        if (!string.IsNullOrEmpty(info.ServiceWorkerPath))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, info.ServiceWorkerPath);
            EnsureItemIncluded(itemGroup, "Content", relativePath);
        }

        _logger.LogInformation("Configured PWA features for Blazor project");
    }

    public void ConfigureBlazorProperties(XElement projectElement, BlazorProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Set target framework
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");

        if (info.IsWebAssembly)
        {
            // WebAssembly-specific properties
            SetOrUpdateProperty(propertyGroup, "BlazorWebAssemblyLoadAllGlobalizationData", "false");
            
            if (info.IsPwa)
            {
                SetOrUpdateProperty(propertyGroup, "ServiceWorkerAssetsManifest", "service-worker-assets.js");
            }
        }
        else if (info.IsServerSide)
        {
            // Server-side Blazor properties
            SetOrUpdateProperty(propertyGroup, "RazorLangVersion", "Latest");
        }

        // Common Blazor properties
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
    }

    private async Task DetectPwaFeatures(BlazorProjectInfo info, string wwwrootPath, CancellationToken cancellationToken)
    {
        // Check for manifest.json
        var manifestPath = Path.Combine(wwwrootPath, "manifest.json");
        if (File.Exists(manifestPath))
        {
            info.ManifestJsonPath = manifestPath;
            info.IsPwa = true;
        }

        // Check for service worker
        var serviceWorkerPaths = new[]
        {
            Path.Combine(wwwrootPath, "service-worker.js"),
            Path.Combine(wwwrootPath, "sw.js"),
            Path.Combine(wwwrootPath, "serviceworker.js")
        };

        foreach (var swPath in serviceWorkerPaths)
        {
            if (File.Exists(swPath))
            {
                info.ServiceWorkerPath = swPath;
                info.IsPwa = true;
                break;
            }
        }

        // Check for PWA icons in manifest.json
        if (!string.IsNullOrEmpty(info.ManifestJsonPath))
        {
            try
            {
                var manifestContent = await File.ReadAllTextAsync(info.ManifestJsonPath, cancellationToken);
                var manifest = System.Text.Json.JsonDocument.Parse(manifestContent);
                
                if (manifest.RootElement.TryGetProperty("icons", out var icons) && icons.GetArrayLength() > 0)
                {
                    info.IsPwa = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse manifest.json: {Error}", ex.Message);
            }
        }
    }

    private void DetectStaticAssets(BlazorProjectInfo info, string wwwrootPath)
    {
        var staticFiles = Directory.GetFiles(wwwrootPath, "*", SearchOption.AllDirectories);
        info.StaticAssets = staticFiles
            .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
            .ToList();
    }

    private async Task EnsureBlazorPackages(List<PackageReference> packageReferences, BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        if (info.IsWebAssembly)
        {
            // Ensure Microsoft.AspNetCore.Components.WebAssembly
            if (!packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Components.WebAssembly"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.AspNetCore.Components.WebAssembly",
                    Version = "8.0.8"
                });
            }

            // Ensure Microsoft.AspNetCore.Components.WebAssembly.DevServer for development
            if (!packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Components.WebAssembly.DevServer"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
                    Version = "8.0.8",
                    Metadata = { ["PrivateAssets"] = "all" }
                });
            }

            // For PWA projects
            if (info.IsPwa && !packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Components.WebAssembly.ServiceWorker"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.AspNetCore.Components.WebAssembly.ServiceWorker",
                    Version = "8.0.8"
                });
            }
        }
        else if (info.IsServerSide)
        {
            // Ensure Microsoft.AspNetCore.Components.Server
            if (!packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Components.Server"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.AspNetCore.Components.Server",
                    Version = "8.0.8"
                });
            }
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

    private async Task AnalyzeProjectStructure(BlazorProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Detect target framework and .NET version
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        
        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            var frameworks = targetFrameworks.Split(';');
            info.IsNet8Plus = frameworks.Any(f => f.StartsWith("net8.0") || f.StartsWith("net9.0"));
        }
        else if (!string.IsNullOrEmpty(targetFramework))
        {
            info.IsNet8Plus = targetFramework.StartsWith("net8.0") || targetFramework.StartsWith("net9.0");
        }

        // Check for wwwroot directory
        var wwwrootPath = Path.Combine(info.ProjectDirectory, "wwwroot");
        info.HasWwwroot = Directory.Exists(wwwrootPath);

        // Detect project output type and hosting model
        var outputType = project.GetPropertyValue("OutputType");
        var useBlazorWebAssembly = project.GetPropertyValue("UseBlazorWebAssembly");
        
        if (string.Equals(useBlazorWebAssembly, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.IsWebAssembly = true;
            info.HostingModel = "WebAssembly";
        }
        else if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
        {
            info.IsServerSide = true;
            info.HostingModel = "Server";
        }

        // Check for Blazor render mode configuration (.NET 8+)
        if (info.IsNet8Plus)
        {
            var renderMode = project.GetPropertyValue("BlazorRenderMode");
            if (!string.IsNullOrEmpty(renderMode))
            {
                info.GlobalRenderMode = renderMode;
            }
        }
    }

    private async Task AnalyzeBlazorPackages(BlazorProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Microsoft.AspNetCore.Components.WebAssembly":
                case "Microsoft.AspNetCore.Components.WebAssembly.DevServer":
                    info.IsWebAssembly = true;
                    info.HostingModel = "WebAssembly";
                    break;

                case "Microsoft.AspNetCore.Components.Server":
                    info.IsServerSide = true;
                    info.HostingModel = "Server";
                    break;

                case "Microsoft.AspNetCore.Components.WebAssembly.ServiceWorker":
                    info.IsPwa = true;
                    break;

                case "Microsoft.AspNetCore.Components.WebView.Maui":
                    info.IsHybridApp = true;
                    break;

                // Legacy patterns requiring migration
                case "Microsoft.AspNetCore.Blazor":
                case "Microsoft.AspNetCore.Blazor.Server":
                    info.LegacyPatterns.Add($"Legacy package: {packageId}");
                    info.NeedsNet8Migration = true;
                    break;
            }
        }

        // If no specific hosting model detected, analyze project structure
        if (string.IsNullOrEmpty(info.HostingModel))
        {
            // Check for Program.cs patterns
            var programCsPath = Path.Combine(info.ProjectDirectory, "Program.cs");
            if (File.Exists(programCsPath))
            {
                var content = await File.ReadAllTextAsync(programCsPath, cancellationToken);
                if (content.Contains("CreateBuilder") && content.Contains("AddRazorPages"))
                {
                    info.IsServerSide = true;
                    info.HostingModel = "Server";
                }
                else if (content.Contains("WebAssemblyHostBuilder"))
                {
                    info.IsWebAssembly = true;
                    info.HostingModel = "WebAssembly";
                }
            }
        }
    }

    private async Task AnalyzeRenderModes(BlazorProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        if (!info.IsNet8Plus)
            return;

        // Check global render mode configuration
        var renderModeProperty = project.GetPropertyValue("BlazorRenderMode");
        if (!string.IsNullOrEmpty(renderModeProperty))
        {
            info.GlobalRenderMode = renderModeProperty;
        }

        // Analyze App.razor or _Imports.razor for render mode attributes
        var appRazorPath = Path.Combine(info.ProjectDirectory, "App.razor");
        if (File.Exists(appRazorPath))
        {
            var content = await File.ReadAllTextAsync(appRazorPath, cancellationToken);
            
            if (content.Contains("@rendermode InteractiveServer"))
                info.UsesInteractiveServer = true;
            if (content.Contains("@rendermode InteractiveWebAssembly"))
                info.UsesInteractiveWebAssembly = true;
            if (content.Contains("@rendermode InteractiveAuto"))
                info.UsesAutoRenderMode = true;
            if (content.Contains("StreamRendering"))
                info.UsesStreamRendering = true;
        }

        // Check for enhanced navigation
        var mainLayoutPath = Path.Combine(info.ProjectDirectory, "Components", "Layout", "MainLayout.razor");
        if (File.Exists(mainLayoutPath))
        {
            var content = await File.ReadAllTextAsync(mainLayoutPath, cancellationToken);
            if (content.Contains("enhance-nav"))
                info.UsesEnhancedNavigation = true;
        }
    }

    private async Task AnalyzeRazorComponents(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        // Find all .razor files
        var razorFiles = Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories);
        
        foreach (var razorFile in razorFiles)
        {
            var relativePath = Path.GetRelativePath(info.ProjectDirectory, razorFile);
            var fileName = Path.GetFileName(razorFile);
            
            if (fileName.Equals("App.razor", StringComparison.OrdinalIgnoreCase))
            {
                info.HasRouterComponent = true;
            }
            else if (relativePath.Contains("Pages", StringComparison.OrdinalIgnoreCase))
            {
                info.Pages.Add(relativePath);
            }
            else if (relativePath.Contains("Layout", StringComparison.OrdinalIgnoreCase))
            {
                info.LayoutComponents.Add(relativePath);
            }
            else
            {
                info.RazorComponents.Add(relativePath);
            }

            // Analyze component for render modes
            try
            {
                var content = await File.ReadAllTextAsync(razorFile, cancellationToken);
                if (content.Contains("@rendermode"))
                {
                    if (content.Contains("InteractiveServer"))
                        info.ComponentRenderModes[relativePath] = "InteractiveServer";
                    else if (content.Contains("InteractiveWebAssembly"))
                        info.ComponentRenderModes[relativePath] = "InteractiveWebAssembly";
                    else if (content.Contains("InteractiveAuto"))
                        info.ComponentRenderModes[relativePath] = "InteractiveAuto";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze Razor component {File}: {Error}", razorFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeConfigurationFiles(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for appsettings.json
        var appSettingsPath = Path.Combine(info.ProjectDirectory, "appsettings.json");
        info.HasAppSettingsJson = File.Exists(appSettingsPath);

        // Check for blazor-specific config files
        var blazorConfigPath = Path.Combine(info.ProjectDirectory, "wwwroot", "blazor.config.js");
        info.HasBlazorConfigJs = File.Exists(blazorConfigPath);

        // Check for PWA-specific configuration files in wwwroot
        if (info.HasWwwroot)
        {
            var wwwrootPath = Path.Combine(info.ProjectDirectory, "wwwroot");
            await DetectPwaFeatures(info, wwwrootPath, cancellationToken);
        }
    }

    private async Task AnalyzeStaticAssets(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        if (!info.HasWwwroot)
            return;

        var wwwrootPath = Path.Combine(info.ProjectDirectory, "wwwroot");
        DetectStaticAssets(info, wwwrootPath);
    }

    private async Task AnalyzePerformanceOptimizations(BlazorProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for AOT compilation
        var runAotCompilation = project.GetPropertyValue("RunAOTCompilation");
        info.UsesAotCompilation = string.Equals(runAotCompilation, "true", StringComparison.OrdinalIgnoreCase);

        // Check for IL trimming
        var publishTrimmed = project.GetPropertyValue("PublishTrimmed");
        info.UsesILTrimming = string.Equals(publishTrimmed, "true", StringComparison.OrdinalIgnoreCase);

        // Check for SIMD support
        var enableSimd = project.GetPropertyValue("BlazorEnableTimeZoneSupport");
        info.UsesSIMD = string.Equals(enableSimd, "true", StringComparison.OrdinalIgnoreCase);

        // Check for Jiterpreter (Blazor WebAssembly .NET 8+)
        if (info.IsWebAssembly && info.IsNet8Plus)
        {
            var enableJiterpreter = project.GetPropertyValue("BlazorEnableJiterpreter");
            info.UsesJiterpreter = string.Equals(enableJiterpreter, "true", StringComparison.OrdinalIgnoreCase);
        }

        // Check for compression
        var enableCompression = project.GetPropertyValue("BlazorEnableCompression");
        info.UsesCompression = string.Equals(enableCompression, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DetectLegacyPatterns(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, sourceFile);

                // Detect legacy Blazor Server patterns
                if (content.Contains("services.AddServerSideBlazor()"))
                {
                    info.LegacyPatterns.Add($"Legacy server-side registration in {relativePath}");
                    info.NeedsNet8Migration = true;
                }

                // Detect legacy Blazor WebAssembly patterns
                if (content.Contains("WebAssemblyHostBuilder.CreateDefault"))
                {
                    info.LegacyPatterns.Add($"Legacy WebAssembly host builder in {relativePath}");
                    info.NeedsNet8Migration = true;
                }

                // Detect legacy component registration
                if (content.Contains("services.AddSingleton<WeatherForecastService>"))
                {
                    info.LegacyPatterns.Add($"Legacy service registration pattern in {relativePath}");
                }

                // Detect legacy authentication patterns
                if (content.Contains("AddAuthentication") && !content.Contains("AddAuthenticationCore"))
                {
                    info.LegacyPatterns.Add($"Legacy authentication setup in {relativePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze source file {File}: {Error}", sourceFile, ex.Message);
            }
        }

        // Check for legacy configuration patterns in Startup.cs
        var startupCsPath = Path.Combine(info.ProjectDirectory, "Startup.cs");
        if (File.Exists(startupCsPath))
        {
            info.LegacyPatterns.Add("Legacy Startup.cs file - should migrate to Program.cs");
            info.NeedsNet8Migration = true;
        }
    }

    private string GetBlazorProjectType(BlazorProjectInfo info)
    {
        if (info.IsHybridApp) return "Hybrid";
        if (info.IsWebAssembly) return "WebAssembly";
        if (info.IsServerSide) return "Server";
        return "Unknown";
    }

    private async Task MigrateToModernBlazor(BlazorProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating legacy Blazor project to .NET 8+ with modern render modes");

        // Update project SDK
        if (info.IsWebAssembly)
        {
            projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.BlazorWebAssembly");
        }
        else
        {
            projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Web");
        }

        // Update packages to modern versions
        await UpdateBlazorPackagesToModern(packageReferences, info, result, cancellationToken);

        // Configure modern Blazor properties
        await ConfigureModernBlazorProperties(info, projectElement, result, cancellationToken);

        // Create or update Program.cs with modern patterns
        await CreateModernProgramCs(info, result, cancellationToken);

        result.Warnings.Add("Blazor project migrated to .NET 8+ with modern render modes. Review generated configuration and test thoroughly.");
        result.Warnings.Add("Legacy patterns detected - review migration guidance for component updates.");
    }

    private async Task ModernizeNet8BlazorProject(BlazorProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing .NET 8+ Blazor project with latest best practices");

        // Update packages to latest versions
        await UpdateBlazorPackagesToLatest(packageReferences, info, result, cancellationToken);

        // Apply modern render mode optimizations
        await ApplyModernRenderModeOptimizations(info, projectElement, result, cancellationToken);

        result.Warnings.Add("Blazor project modernized with latest .NET 8+ features and optimizations.");
    }

    private async Task ConfigureLegacyBlazorProject(BlazorProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring legacy Blazor project with best practices");

        // Configure legacy project with modern SDK
        if (info.IsWebAssembly)
        {
            projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.BlazorWebAssembly");
        }
        else
        {
            projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Web");
        }

        // Update to supported .NET version
        await UpdateToSupportedNetVersion(info, projectElement, result, cancellationToken);

        result.Warnings.Add("Legacy Blazor project configured with modern SDK. Consider upgrading to .NET 8+ for enhanced features.");
    }

    private async Task ApplyBlazorOptimizations(BlazorProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        if (info.IsWebAssembly)
        {
            // WebAssembly-specific optimizations
            SetOrUpdateProperty(propertyGroup, "BlazorWebAssemblyLoadAllGlobalizationData", "false");
            SetOrUpdateProperty(propertyGroup, "BlazorEnableTimeZoneSupport", "false");
            
            if (info.IsNet8Plus)
            {
                SetOrUpdateProperty(propertyGroup, "BlazorEnableJiterpreter", "true");
                SetOrUpdateProperty(propertyGroup, "RunAOTCompilation", "false"); // Enable for production builds
            }
        }
        else if (info.IsServerSide)
        {
            // Server-side optimizations
            SetOrUpdateProperty(propertyGroup, "BlazorServerCircuitOptions.DetailedErrors", "false");
        }

        // Common optimizations
        SetOrUpdateProperty(propertyGroup, "PublishTrimmed", "true");
        SetOrUpdateProperty(propertyGroup, "TrimMode", "partial");
        
        result.Warnings.Add("Applied Blazor performance optimizations. Review and adjust for production requirements.");
    }

    private async Task UpdateBlazorPackagesToModern(List<PackageReference> packageReferences, BlazorProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var legacyPackages = new[]
        {
            "Microsoft.AspNetCore.Blazor",
            "Microsoft.AspNetCore.Blazor.Server",
            "Microsoft.AspNetCore.Blazor.HttpClientExtensions"
        };

        // Remove legacy packages
        packageReferences.RemoveAll(p => legacyPackages.Contains(p.PackageId));

        // Add modern packages
        await EnsureBlazorPackages(packageReferences, info, cancellationToken);
        
        result.Warnings.Add("Updated Blazor packages to modern versions. Review package compatibility.");
    }

    private async Task ConfigureModernBlazorProperties(BlazorProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Set modern .NET version
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");

        if (info.IsNet8Plus)
        {
            // Configure .NET 8+ render modes
            if (info.IsServerSide && info.IsWebAssembly)
            {
                SetOrUpdateProperty(propertyGroup, "BlazorRenderMode", "InteractiveAuto");
            }
            else if (info.IsServerSide)
            {
                SetOrUpdateProperty(propertyGroup, "BlazorRenderMode", "InteractiveServer");
            }
            else if (info.IsWebAssembly)
            {
                SetOrUpdateProperty(propertyGroup, "BlazorRenderMode", "InteractiveWebAssembly");
            }
        }
    }

    private async Task CreateModernProgramCs(BlazorProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var programCsPath = Path.Combine(info.ProjectDirectory, "Program.cs");
        
        if (!File.Exists(programCsPath))
        {
            var programContent = GenerateModernProgramCs(info);
            await File.WriteAllTextAsync(programCsPath, programContent, cancellationToken);
            result.Warnings.Add("Created modern Program.cs with .NET 8+ render modes. Review and customize as needed.");
        }
    }

    private string GenerateModernProgramCs(BlazorProjectInfo info)
    {
        if (info.IsWebAssembly)
        {
            return @"using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>(""#app"");
builder.RootComponents.Add<HeadOutlet>(""head::after"");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
";
        }
        else
        {
            return @"using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(""/Error"", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
";
        }
    }

    private async Task UpdateBlazorPackagesToLatest(List<PackageReference> packageReferences, BlazorProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var blazorPackages = packageReferences.Where(p => p.PackageId.Contains("AspNetCore.Components")).ToList();
        
        foreach (var package in blazorPackages)
        {
            if (package.Version != "8.0.8")
            {
                package.Version = "8.0.8";
                result.Warnings.Add($"Updated {package.PackageId} to version 8.0.8");
            }
        }
    }

    private async Task ApplyModernRenderModeOptimizations(BlazorProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Enable enhanced navigation and streaming rendering
        SetOrUpdateProperty(propertyGroup, "BlazorEnableEnhancedNavigation", "true");
        SetOrUpdateProperty(propertyGroup, "BlazorEnableStreamingRendering", "true");
        
        result.Warnings.Add("Applied modern Blazor render mode optimizations for enhanced performance.");
    }

    private async Task UpdateToSupportedNetVersion(BlazorProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault();
        if (propertyGroup != null)
        {
            var targetFramework = propertyGroup.Element("TargetFramework")?.Value;
            if (!string.IsNullOrEmpty(targetFramework) && !targetFramework.StartsWith("net8.0") && !targetFramework.StartsWith("net6.0"))
            {
                SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
                result.Warnings.Add("Updated target framework to .NET 8 for better Blazor support.");
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
                    item.Add(new XElement(kvp.Key, kvp.Value));
                }
            }
            
            itemGroup.Add(item);
        }
    }

    // JavaScript Interop Complexity Analysis
    private async Task AnalyzeJavaScriptInterop(BlazorProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Analyzing JavaScript interop complexity for Blazor project");

        var jsInteropUsage = new List<JSInteropInfo>();
        var jsModules = new List<string>();
        var jsLibraries = new List<string>();
        var complexityScore = 0;

        try
        {
            // Analyze IJSRuntime usage patterns
            await AnalyzeIJSRuntimeUsage(info, jsInteropUsage, cancellationToken);

            // Detect JavaScript module imports
            await DetectJavaScriptModules(info, jsModules, cancellationToken);

            // Analyze JavaScript files and libraries
            await AnalyzeJavaScriptFiles(info, jsLibraries, cancellationToken);

            // Detect JavaScript isolation patterns
            await DetectJavaScriptIsolation(info, cancellationToken);

            // Analyze JavaScript callback patterns
            await AnalyzeJavaScriptCallbacks(info, jsInteropUsage, cancellationToken);

            // Detect streaming and byte array transfers
            await AnalyzeStreamingInterop(info, jsInteropUsage, cancellationToken);

            // Check for JavaScript framework integrations
            await DetectJavaScriptFrameworks(info, jsLibraries, cancellationToken);

            // Analyze SignalR usage
            await AnalyzeSignalRUsage(info, cancellationToken);

            // Calculate complexity score
            complexityScore = CalculateJSInteropComplexity(info, jsInteropUsage, jsModules, jsLibraries);

            // Store analysis results
            StoreJSInteropAnalysisResults(info, jsInteropUsage, jsModules, jsLibraries, complexityScore);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze JavaScript interop: {Error}", ex.Message);
            info.Properties["JSInteropAnalysisError"] = ex.Message;
        }
    }

    private async Task AnalyzeIJSRuntimeUsage(BlazorProjectInfo info, List<JSInteropInfo> jsInteropUsage, CancellationToken cancellationToken)
    {
        var razorFiles = Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(200); // Limit for performance

        foreach (var file in razorFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for IJSRuntime injection
                if (content.Contains("@inject IJSRuntime") || content.Contains("IJSRuntime "))
                {
                    info.Properties["UsesIJSRuntime"] = "true";
                    
                    // Analyze specific JS interop patterns
                    var invokeAsyncMatches = Regex.Matches(content, @"InvokeAsync<([^>]+)>\s*\(\s*""([^""]+)""");
                    foreach (Match match in invokeAsyncMatches)
                    {
                        jsInteropUsage.Add(new JSInteropInfo
                        {
                            MethodName = match.Groups[2].Value,
                            ReturnType = match.Groups[1].Value,
                            SourceFile = file,
                            InteropType = "InvokeAsync"
                        });
                    }
                    
                    // Check for InvokeVoidAsync
                    var invokeVoidMatches = Regex.Matches(content, @"InvokeVoidAsync\s*\(\s*""([^""]+)""");
                    foreach (Match match in invokeVoidMatches)
                    {
                        jsInteropUsage.Add(new JSInteropInfo
                        {
                            MethodName = match.Groups[1].Value,
                            ReturnType = "void",
                            SourceFile = file,
                            InteropType = "InvokeVoidAsync"
                        });
                    }
                }
                
                // Check for IJSObjectReference (JavaScript isolation)
                if (content.Contains("IJSObjectReference"))
                {
                    info.Properties["UsesJSIsolation"] = "true";
                }
                
                // Check for IJSInProcessRuntime (Blazor WebAssembly only)
                if (content.Contains("IJSInProcessRuntime"))
                {
                    info.Properties["UsesInProcessInterop"] = "true";
                    info.Properties["RequiresWebAssembly"] = "true";
                }
                
                // Check for DotNetObjectReference
                if (content.Contains("DotNetObjectReference"))
                {
                    info.Properties["UsesDotNetObjectReference"] = "true";
                    info.Properties["HasJSCallbacks"] = "true";
                }
                
                // Check for JSInvokable attribute
                if (content.Contains("[JSInvokable"))
                {
                    info.Properties["HasJSInvokableMethods"] = "true";
                    
                    var jsInvokableMatches = Regex.Matches(content, @"\[JSInvokable(?:\(""([^""]+)""\))?\]\s*public\s+(?:async\s+)?(?:Task<)?(\w+)");
                    foreach (Match match in jsInvokableMatches)
                    {
                        jsInteropUsage.Add(new JSInteropInfo
                        {
                            MethodName = match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : "Default",
                            ReturnType = match.Groups[2].Value,
                            SourceFile = file,
                            InteropType = "JSInvokable",
                            IsCallback = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze JS interop in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task DetectJavaScriptModules(BlazorProjectInfo info, List<string> jsModules, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(100);

        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for JavaScript module imports
                var importMatches = Regex.Matches(content, @"import\s*\(\s*['""]\.?/([^'""]+\.js)['""]");
                foreach (Match match in importMatches)
                {
                    jsModules.Add(match.Groups[1].Value);
                }
                
                // Check for IJSObjectReference module pattern
                var moduleMatches = Regex.Matches(content, @"ImportAsync\s*\(\s*['""]([^'""]+)['""]");
                foreach (Match match in moduleMatches)
                {
                    var modulePath = match.Groups[1].Value;
                    if (!modulePath.EndsWith(".js"))
                        modulePath += ".js";
                    jsModules.Add(modulePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to detect JS modules in {File}: {Error}", file, ex.Message);
            }
        }
        
        if (jsModules.Any())
        {
            info.Properties["UsesJSModules"] = "true";
            info.Properties["JSModuleCount"] = jsModules.Count.ToString();
        }
    }

    private async Task AnalyzeJavaScriptFiles(BlazorProjectInfo info, List<string> jsLibraries, CancellationToken cancellationToken)
    {
        // Check wwwroot for JavaScript files
        var wwwrootPath = Path.Combine(info.ProjectDirectory, "wwwroot");
        if (Directory.Exists(wwwrootPath))
        {
            var jsFiles = Directory.GetFiles(wwwrootPath, "*.js", SearchOption.AllDirectories)
                .Where(f => !f.Contains("_framework") && !f.Contains(".min.js"))
                .ToList();
            
            foreach (var jsFile in jsFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(jsFile, cancellationToken);
                    var relativePath = Path.GetRelativePath(wwwrootPath, jsFile);
                    
                    // Analyze JavaScript complexity
                    var jsInfo = new JavaScriptFileInfo
                    {
                        FilePath = relativePath,
                        LineCount = content.Split('\n').Length,
                        HasExports = content.Contains("export ") || content.Contains("module.exports"),
                        UsesTypeScript = jsFile.EndsWith(".ts"),
                        HasBlazorInterop = content.Contains("DotNet.") || content.Contains("Blazor.") || content.Contains("window.blazor")
                    };
                    
                    // Check for JavaScript frameworks
                    if (content.Contains("React.") || content.Contains("useState"))
                        jsLibraries.Add("React");
                    if (content.Contains("Vue.") || content.Contains("createApp"))
                        jsLibraries.Add("Vue");
                    if (content.Contains("angular.") || content.Contains("@angular"))
                        jsLibraries.Add("Angular");
                    if (content.Contains("jQuery") || content.Contains("$()"))
                        jsLibraries.Add("jQuery");
                    
                    // Check for specific patterns
                    if (content.Contains("window.") || content.Contains("document."))
                    {
                        info.Properties["AccessesDOM"] = "true";
                    }
                    
                    if (content.Contains("fetch(") || content.Contains("XMLHttpRequest"))
                    {
                        info.Properties["MakesHttpRequests"] = "true";
                    }
                    
                    if (content.Contains("localStorage") || content.Contains("sessionStorage"))
                    {
                        info.Properties["UsesWebStorage"] = "true";
                    }
                    
                    if (content.Contains("addEventListener"))
                    {
                        info.Properties["RegistersEventListeners"] = "true";
                    }
                    
                    // Count Blazor-specific functions
                    var blazorFunctionCount = Regex.Matches(content, @"window\.(\w+)\s*=\s*(?:async\s+)?(?:function|\()").Count;
                    if (blazorFunctionCount > 0)
                    {
                        info.Properties["BlazorJSFunctionCount"] = blazorFunctionCount.ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze JS file {File}: {Error}", jsFile, ex.Message);
                }
            }
            
            info.Properties["TotalJSFiles"] = jsFiles.Count.ToString();
        }
        
        // Check for NPM/Node.js usage
        var packageJsonPath = Path.Combine(info.ProjectDirectory, "package.json");
        if (File.Exists(packageJsonPath))
        {
            info.Properties["UsesNpm"] = "true";
            try
            {
                var packageJson = await File.ReadAllTextAsync(packageJsonPath, cancellationToken);
                var json = JsonDocument.Parse(packageJson);
                
                // Check for build tools
                if (json.RootElement.TryGetProperty("devDependencies", out var devDeps))
                {
                    foreach (var dep in devDeps.EnumerateObject())
                    {
                        if (dep.Name.Contains("webpack") || dep.Name.Contains("vite") || dep.Name.Contains("rollup"))
                        {
                            info.Properties["UsesBundler"] = "true";
                            info.Properties["Bundler"] = dep.Name;
                        }
                        
                        if (dep.Name.Contains("typescript"))
                        {
                            info.Properties["UsesTypeScript"] = "true";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze package.json: {Error}", ex.Message);
            }
        }
    }

    private async Task DetectJavaScriptIsolation(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        var razorFiles = Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(50);

        foreach (var file in razorFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var componentName = Path.GetFileNameWithoutExtension(file);
                
                // Check for scoped JavaScript pattern
                var jsFilePath = Path.Combine(Path.GetDirectoryName(file), $"{componentName}.razor.js");
                if (File.Exists(jsFilePath))
                {
                    info.Properties["UsesComponentScopedJS"] = "true";
                    
                    // Analyze the scoped JS file
                    var jsContent = await File.ReadAllTextAsync(jsFilePath, cancellationToken);
                    if (jsContent.Contains("export function") || jsContent.Contains("export const"))
                    {
                        info.Properties["UsesModernJSModules"] = "true";
                    }
                }
                
                // Check for IJSObjectReference disposal
                if (content.Contains("DisposeAsync") && content.Contains("IJSObjectReference"))
                {
                    info.Properties["ProperlyDisposesJSReferences"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to detect JS isolation in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeJavaScriptCallbacks(BlazorProjectInfo info, List<JSInteropInfo> jsInteropUsage, CancellationToken cancellationToken)
    {
        var callbackCount = 0;
        var complexCallbacks = 0;
        
        foreach (var interop in jsInteropUsage.Where(j => j.IsCallback))
        {
            callbackCount++;
            
            // Check if callback involves complex data types
            if (interop.ReturnType.Contains("Task") || interop.ReturnType.Contains("List") || interop.ReturnType.Contains("Dictionary"))
            {
                complexCallbacks++;
            }
        }
        
        if (callbackCount > 0)
        {
            info.Properties["JSCallbackCount"] = callbackCount.ToString();
            info.Properties["ComplexCallbackCount"] = complexCallbacks.ToString();
            
            if (callbackCount > 10)
            {
                info.Warnings.Add("High number of JavaScript callbacks detected. Consider consolidating callback patterns.");
            }
        }
    }

    private async Task AnalyzeStreamingInterop(BlazorProjectInfo info, List<JSInteropInfo> jsInteropUsage, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(100);

        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for streaming patterns
                if (content.Contains("IJSStreamReference") || content.Contains("DotNetStreamReference"))
                {
                    info.Properties["UsesStreamingInterop"] = "true";
                    info.Properties["HasAdvancedInterop"] = "true";
                }
                
                // Check for byte array transfers
                if (Regex.IsMatch(content, @"InvokeAsync<byte\[\]>") || content.Contains("Uint8Array"))
                {
                    info.Properties["TransfersByteArrays"] = "true";
                    info.Properties["HasBinaryDataTransfer"] = "true";
                }
                
                // Check for large data warnings
                if (content.Contains("MaximumReceiveMessageSize"))
                {
                    info.Properties["ConfiguresMessageSize"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze streaming interop in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task DetectJavaScriptFrameworks(BlazorProjectInfo info, List<string> jsLibraries, CancellationToken cancellationToken)
    {
        // Check for common JS framework integrations
        var libmanJsonPath = Path.Combine(info.ProjectDirectory, "libman.json");
        if (File.Exists(libmanJsonPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(libmanJsonPath, cancellationToken);
                var json = JsonDocument.Parse(content);
                
                if (json.RootElement.TryGetProperty("libraries", out var libraries))
                {
                    foreach (var lib in libraries.EnumerateArray())
                    {
                        if (lib.TryGetProperty("library", out var libName))
                        {
                            var name = libName.GetString() ?? "";
                            if (name.Contains("bootstrap"))
                                jsLibraries.Add("Bootstrap");
                            if (name.Contains("jquery"))
                                jsLibraries.Add("jQuery");
                            if (name.Contains("chart"))
                                jsLibraries.Add("Chart.js");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze libman.json: {Error}", ex.Message);
            }
        }
        
        // Store detected libraries
        if (jsLibraries.Any())
        {
            var uniqueLibraries = jsLibraries.Distinct().ToList();
            info.Properties["JSLibraries"] = string.Join(";", uniqueLibraries);
            info.Properties["JSLibraryCount"] = uniqueLibraries.Count.ToString();
            
            // Store framework integrations separately
            var frameworkNames = new[] { "React", "Vue", "Angular", "jQuery", "Bootstrap", "Chart.js" };
            info.JSFrameworkIntegrations = uniqueLibraries
                .Where(lib => frameworkNames.Contains(lib))
                .ToList();
        }
    }

    private async Task AnalyzeSignalRUsage(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .Take(50);

        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                if (content.Contains("HubConnection") || content.Contains("@using Microsoft.AspNetCore.SignalR.Client"))
                {
                    info.Properties["UsesSignalR"] = "true";
                    info.Properties["HasRealTimeFeatures"] = "true";
                    
                    // Check for specific SignalR patterns
                    if (content.Contains("HubConnectionBuilder"))
                    {
                        info.Properties["SignalRClientSide"] = "true";
                    }
                    
                    if (content.Contains("Hub<") || content.Contains(": Hub"))
                    {
                        info.Properties["SignalRServerSide"] = "true";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze SignalR usage in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private int CalculateJSInteropComplexity(BlazorProjectInfo info, List<JSInteropInfo> jsInteropUsage, List<string> jsModules, List<string> jsLibraries)
    {
        var complexityScore = 0;
        
        // Basic interop usage
        if (info.Properties.ContainsKey("UsesIJSRuntime")) complexityScore += 2;
        
        // Advanced patterns
        if (info.Properties.ContainsKey("UsesJSIsolation")) complexityScore += 3;
        if (info.Properties.ContainsKey("UsesInProcessInterop")) complexityScore += 2;
        if (info.Properties.ContainsKey("UsesDotNetObjectReference")) complexityScore += 3;
        if (info.Properties.ContainsKey("HasJSInvokableMethods")) complexityScore += 2;
        
        // Module usage
        if (jsModules.Count > 0) complexityScore += 2;
        if (jsModules.Count > 5) complexityScore += 3;
        
        // JavaScript files
        var jsFileCount = int.Parse(info.Properties.GetValueOrDefault("TotalJSFiles", "0"));
        if (jsFileCount > 0) complexityScore += 2;
        if (jsFileCount > 10) complexityScore += 3;
        
        // Framework integration
        if (jsLibraries.Count > 0) complexityScore += 2;
        if (jsLibraries.Count > 3) complexityScore += 3;
        
        // Advanced features
        if (info.Properties.ContainsKey("UsesStreamingInterop")) complexityScore += 4;
        if (info.Properties.ContainsKey("TransfersByteArrays")) complexityScore += 3;
        if (info.Properties.ContainsKey("UsesSignalR")) complexityScore += 3;
        if (info.Properties.ContainsKey("UsesBundler")) complexityScore += 3;
        if (info.Properties.ContainsKey("UsesTypeScript")) complexityScore += 2;
        
        // Callback complexity
        var callbackCount = int.Parse(info.Properties.GetValueOrDefault("JSCallbackCount", "0"));
        if (callbackCount > 5) complexityScore += 2;
        if (callbackCount > 10) complexityScore += 3;
        
        // Total interop calls
        if (jsInteropUsage.Count > 10) complexityScore += 2;
        if (jsInteropUsage.Count > 25) complexityScore += 3;
        if (jsInteropUsage.Count > 50) complexityScore += 4;
        
        return complexityScore;
    }

    private void StoreJSInteropAnalysisResults(BlazorProjectInfo info, List<JSInteropInfo> jsInteropUsage, List<string> jsModules, List<string> jsLibraries, int complexityScore)
    {
        // Store summary statistics
        info.Properties["TotalJSInteropCalls"] = jsInteropUsage.Count.ToString();
        info.Properties["UniqueJSMethods"] = jsInteropUsage.Select(j => j.MethodName).Distinct().Count().ToString();
        info.Properties["JSModules"] = string.Join(";", jsModules.Distinct());
        
        // Store in strongly-typed properties
        info.UsesJavaScriptInterop = jsInteropUsage.Any();
        info.JavaScriptModules = jsModules.Distinct().ToList();
        info.JavaScriptLibraries = jsLibraries.Distinct().ToList();
        info.JSInteropComplexityScore = complexityScore;
        
        // Store pattern counts
        var patternCounts = jsInteropUsage
            .GroupBy(j => j.InteropType)
            .ToDictionary(g => g.Key, g => g.Count());
        info.JSInteropPatterns = patternCounts;
        
        // Determine complexity level
        info.Properties["JSInteropComplexity"] = complexityScore switch
        {
            0 => "None",
            < 10 => "Low",
            < 20 => "Medium",
            < 30 => "High",
            _ => "Very High"
        };
        
        info.Properties["JSInteropComplexityScore"] = complexityScore.ToString();
        
        // Add warnings based on analysis
        if (complexityScore >= 30)
        {
            info.Warnings.Add("Very high JavaScript interop complexity detected. Consider architectural review for maintainability.");
        }
        else if (complexityScore >= 20)
        {
            info.Warnings.Add("High JavaScript interop complexity. Ensure proper error handling and disposal patterns.");
        }
        
        if (info.Properties.ContainsKey("UsesInProcessInterop") && !info.IsWebAssembly)
        {
            info.Warnings.Add("IJSInProcessRuntime detected but project is not WebAssembly. This will cause runtime errors.");
        }
        
        if (!info.Properties.ContainsKey("ProperlyDisposesJSReferences") && info.Properties.ContainsKey("UsesJSIsolation"))
        {
            info.Warnings.Add("JavaScript isolation detected but disposal patterns not found. Ensure IJSObjectReference instances are properly disposed.");
        }
        
        if (jsLibraries.Count > 5)
        {
            info.Warnings.Add($"Multiple JavaScript libraries detected ({string.Join(", ", jsLibraries.Distinct())}). Consider consolidation for better performance.");
        }
        
        if (info.Properties.ContainsKey("TransfersByteArrays") && !info.Properties.ContainsKey("UsesStreamingInterop"))
        {
            info.Warnings.Add("Byte array transfers detected. Consider using streaming interop for large data transfers.");
        }
        
        // Render mode specific warnings
        if (info.UsesInteractiveServer && complexityScore > 20)
        {
            info.Warnings.Add("High JS interop complexity with Interactive Server mode. Consider WebAssembly for heavy client-side operations.");
        }
        
        if (info.UsesStaticSSR && jsInteropUsage.Any())
        {
            info.Warnings.Add("JavaScript interop detected with Static SSR. Ensure interop calls are properly initialized after hydration.");
        }
    }

    private async Task AnalyzeComponentComplexity(BlazorProjectInfo info, CancellationToken cancellationToken)
    {
        var componentCount = 0;
        var complexComponents = 0;
        var cascadingParameters = 0;
        var eventCallbacks = 0;
        
        var razorFiles = Directory.GetFiles(info.ProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .ToList();
        
        foreach (var file in razorFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                componentCount++;
                
                // Check for component complexity indicators
                var parameterCount = Regex.Matches(content, @"\[Parameter\]").Count;
                if (parameterCount > 5) complexComponents++;
                
                // Check for cascading parameters
                if (content.Contains("[CascadingParameter]"))
                {
                    cascadingParameters++;
                }
                
                // Check for event callbacks
                var eventCallbackMatches = Regex.Matches(content, @"EventCallback<?");
                eventCallbacks += eventCallbackMatches.Count;
                
                // Check for complex state management
                if (content.Contains("StateHasChanged") && content.Contains("InvokeAsync"))
                {
                    info.Properties["HasComplexStateManagement"] = "true";
                }
                
                // Check for render fragments
                if (content.Contains("RenderFragment"))
                {
                    info.Properties["UsesRenderFragments"] = "true";
                }
                
                // Check for virtualization
                if (content.Contains("Virtualize") || content.Contains("<Virtualize"))
                {
                    info.Properties["UsesVirtualization"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze component complexity in {File}: {Error}", file, ex.Message);
            }
        }
        
        // Store component analysis results
        info.Properties["TotalComponents"] = componentCount.ToString();
        info.Properties["ComplexComponents"] = complexComponents.ToString();
        info.Properties["CascadingParameterUsage"] = cascadingParameters.ToString();
        info.Properties["EventCallbackUsage"] = eventCallbacks.ToString();
        
        if (componentCount > 50)
        {
            info.Properties["LargeComponentBase"] = "true";
        }
        
        if (cascadingParameters > 5)
        {
            info.Warnings.Add("High cascading parameter usage detected. Consider using state management patterns for complex data flow.");
        }
    }

    // Helper classes for JavaScript interop analysis
    private class JSInteropInfo
    {
        public string MethodName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string InteropType { get; set; } = string.Empty;
        public bool IsCallback { get; set; }
    }

    private class JavaScriptFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public bool HasExports { get; set; }
        public bool UsesTypeScript { get; set; }
        public bool HasBlazorInterop { get; set; }
    }
}