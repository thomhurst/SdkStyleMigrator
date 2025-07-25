using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class UwpProjectHandler : IUwpProjectHandler
{
    private readonly ILogger<UwpProjectHandler> _logger;

    public UwpProjectHandler(ILogger<UwpProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<UwpProjectInfo> DetectUwpConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new UwpProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive project analysis
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Analyze UWP packages and dependencies
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        await AnalyzeUwpPackages(info, packageReferences, cancellationToken);

        // Comprehensive manifest and asset detection
        await DetectUwpAssets(info, cancellationToken);

        // Analyze capabilities and permissions
        await AnalyzeCapabilitiesAndPermissions(info, cancellationToken);

        // Detect XAML and UI patterns
        await AnalyzeXamlAndUiPatterns(info, cancellationToken);

        // Check for WinUI 3 migration opportunities
        await AnalyzeWinUI3MigrationOpportunities(info, cancellationToken);

        // Detect deployment and packaging patterns
        await AnalyzeDeploymentPatterns(info, cancellationToken);

        // Check for performance and modern patterns
        await AnalyzeModernPatterns(info, cancellationToken);

        _logger.LogInformation("Detected UWP project: Type={ProjectType}, MinVersion={MinVersion}, TargetVersion={TargetVersion}, CanMigrateToWinUI={CanMigrate}, HasModernPatterns={HasModern}",
            GetUwpProjectType(info), info.MinimumPlatformVersion, info.TargetPlatformVersion, CanMigrateToWinUI3(info), HasModernPatterns(info));

        return info;
    }

    public async Task MigrateUwpProjectAsync(
        UwpProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine optimal migration strategy based on project analysis
            if (CanMigrateToWinUI3(info))
            {
                // Provide WinUI 3 migration guidance and preparation
                await PrepareWinUI3Migration(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (CanMigrateToMaui(info))
            {
                // Provide MAUI migration guidance for cross-platform scenarios
                await PrepareMauiMigration(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Modernize existing UWP project with latest best practices
                await ModernizeUwpProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Apply common UWP optimizations and best practices
            await ApplyUwpOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully processed UWP project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate UWP project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "UWP migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void EnsureUwpAssetsIncluded(string projectDirectory, XElement projectElement)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Include Package.appxmanifest
        var manifestPath = Path.Combine(projectDirectory, "Package.appxmanifest");
        if (File.Exists(manifestPath))
        {
            EnsureItemIncluded(itemGroup, "AppxManifest", "Package.appxmanifest");
        }

        // Include assets folder
        var assetsPath = Path.Combine(projectDirectory, "Assets");
        if (Directory.Exists(assetsPath))
        {
            var assetFiles = Directory.GetFiles(assetsPath, "*", SearchOption.AllDirectories);
            foreach (var assetFile in assetFiles)
            {
                var relativePath = Path.GetRelativePath(projectDirectory, assetFile);
                EnsureItemIncluded(itemGroup, "Content", relativePath);
            }
        }
    }

    public void MigratePackagingConfiguration(XElement projectElement, UwpProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Set packaging properties
        if (!string.IsNullOrEmpty(info.CertificatePath))
        {
            SetOrUpdateProperty(propertyGroup, "PackageCertificateKeyFile", info.CertificatePath);
        }

        // Configure app packaging
        SetOrUpdateProperty(propertyGroup, "GenerateAppInstallerFile", "False");
        SetOrUpdateProperty(propertyGroup, "AppxAutoIncrementPackageRevision", "True");
    }

    public void ConfigureUwpProperties(XElement projectElement, UwpProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Set platform versions if available
        if (!string.IsNullOrEmpty(info.MinimumPlatformVersion))
        {
            SetOrUpdateProperty(propertyGroup, "TargetPlatformMinVersion", info.MinimumPlatformVersion);
        }

        if (!string.IsNullOrEmpty(info.TargetPlatformVersion))
        {
            SetOrUpdateProperty(propertyGroup, "TargetPlatformVersion", info.TargetPlatformVersion);
        }

        // Set default values if not present
        if (string.IsNullOrEmpty(info.MinimumPlatformVersion))
        {
            SetOrUpdateProperty(propertyGroup, "TargetPlatformMinVersion", "10.0.17763.0");
        }

        if (string.IsNullOrEmpty(info.TargetPlatformVersion))
        {
            SetOrUpdateProperty(propertyGroup, "TargetPlatformVersion", "10.0.19041.0");
        }
    }

    private async Task DetectUwpAssets(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            // Comprehensive Package.appxmanifest analysis
            await AnalyzePackageManifest(info, cancellationToken);

            // Find and analyze certificate files
            await DetectCertificates(info, cancellationToken);

            // Comprehensive assets detection and categorization
            await DetectAndCategorizeAssets(info, cancellationToken);

            // Check for additional UWP-specific files
            await DetectAdditionalUwpFiles(info, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect UWP assets: {Error}", ex.Message);
        }
    }

    private async Task AnalyzePackageManifest(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(info.ProjectDirectory, "Package.appxmanifest");
        if (!File.Exists(manifestPath))
            return;

        info.PackageManifestPath = manifestPath;

        try
        {
            var content = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(content);

            // Extract package identity
            var identityNode = xmlDoc.SelectSingleNode("//Package/Identity");
            if (identityNode?.Attributes != null)
            {
                info.Properties["PackageName"] = identityNode.Attributes["Name"]?.Value ?? "";
                info.Properties["PackageVersion"] = identityNode.Attributes["Version"]?.Value ?? "";
                info.Properties["Architecture"] = identityNode.Attributes["ProcessorArchitecture"]?.Value ?? "";
            }

            // Extract target device family
            var deviceFamilyNode = xmlDoc.SelectSingleNode("//Package/Dependencies/TargetDeviceFamily");
            if (deviceFamilyNode?.Attributes != null)
            {
                info.Properties["DeviceFamily"] = deviceFamilyNode.Attributes["Name"]?.Value ?? "";
                info.MinimumPlatformVersion = deviceFamilyNode.Attributes["MinVersion"]?.Value ?? "";
                info.TargetPlatformVersion = deviceFamilyNode.Attributes["MaxVersionTested"]?.Value ?? "";
            }

            // Extract application details
            var appNode = xmlDoc.SelectSingleNode("//Package/Applications/Application");
            if (appNode?.Attributes != null)
            {
                info.Properties["ApplicationId"] = appNode.Attributes["Id"]?.Value ?? "";
                info.Properties["EntryPoint"] = appNode.Attributes["EntryPoint"]?.Value ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to parse Package.appxmanifest: {Error}", ex.Message);
        }
    }

    private async Task DetectCertificates(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        var certificateFiles = Directory.GetFiles(info.ProjectDirectory, "*.pfx", SearchOption.TopDirectoryOnly);
        if (certificateFiles.Any())
        {
            info.CertificatePath = Path.GetRelativePath(info.ProjectDirectory, certificateFiles.First());
            
            // Check for temporary certificates
            if (Path.GetFileName(certificateFiles.First()).Contains("_TemporaryKey"))
            {
                info.Properties["HasTemporaryCertificate"] = "true";
            }
        }
    }

    private async Task DetectAndCategorizeAssets(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        var assetsPath = Path.Combine(info.ProjectDirectory, "Assets");
        if (!Directory.Exists(assetsPath))
            return;

        var assetFiles = Directory.GetFiles(assetsPath, "*", SearchOption.AllDirectories);
        info.Assets = assetFiles.Select(f => Path.GetRelativePath(info.ProjectDirectory, f)).ToList();

        // Categorize assets
        var appIcons = assetFiles.Where(f => Path.GetFileName(f).Contains("AppIcon", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(f).Contains("StoreLogo", StringComparison.OrdinalIgnoreCase)).ToList();
        
        var splashScreens = assetFiles.Where(f => Path.GetFileName(f).Contains("SplashScreen", StringComparison.OrdinalIgnoreCase)).ToList();
        
        var tiles = assetFiles.Where(f => Path.GetFileName(f).Contains("Tile", StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetFileName(f).Contains("Logo", StringComparison.OrdinalIgnoreCase)).ToList();

        info.Properties["AppIconCount"] = appIcons.Count.ToString();
        info.Properties["SplashScreenCount"] = splashScreens.Count.ToString();
        info.Properties["TileCount"] = tiles.Count.ToString();
    }

    private async Task DetectAdditionalUwpFiles(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for priconfig.xml
        var priconfigPath = Path.Combine(info.ProjectDirectory, "priconfig.xml");
        if (File.Exists(priconfigPath))
        {
            info.Properties["HasResourceConfiguration"] = "true";
        }

        // Check for Package.StoreAssociation.xml
        var storeAssociationPath = Path.Combine(info.ProjectDirectory, "Package.StoreAssociation.xml");
        if (File.Exists(storeAssociationPath))
        {
            info.Properties["HasStoreAssociation"] = "true";
        }

        // Check for app.config or other configuration files
        var configFiles = Directory.GetFiles(info.ProjectDirectory, "*.config", SearchOption.TopDirectoryOnly);
        if (configFiles.Any())
        {
            info.Properties["HasConfigFiles"] = "true";
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

    // New comprehensive analysis methods
    private async Task AnalyzeProjectStructure(UwpProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Get platform versions from project
        info.MinimumPlatformVersion = project.GetPropertyValue("TargetPlatformMinVersion");
        info.TargetPlatformVersion = project.GetPropertyValue("TargetPlatformVersion");
        
        // Analyze target framework
        var targetFramework = project.GetPropertyValue("TargetFramework");
        if (!string.IsNullOrEmpty(targetFramework))
        {
            info.TargetFramework = targetFramework;
        }

        // Check for UWP-specific properties
        var useWinUI = project.GetPropertyValue("UseWinUI");
        if (string.Equals(useWinUI, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.Properties["UsesWinUI"] = "true";
        }

        var packageReference = project.GetPropertyValue("PackageReference");
        var useWinUILatest = project.GetPropertyValue("UseWinUI3");
        if (string.Equals(useWinUILatest, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.Properties["UsesWinUI3"] = "true";
        }

        // Check output type
        var outputType = project.GetPropertyValue("OutputType");
        info.Properties["OutputType"] = outputType ?? "AppContainer";
    }

    private async Task AnalyzeUwpPackages(UwpProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Microsoft.WindowsAppSDK":
                case "Microsoft.WinUI":
                    info.Properties["UsesWinUI3"] = "true";
                    info.Properties["WinUI3Version"] = version ?? "";
                    break;

                case "Microsoft.Toolkit.Win32.UI.Controls":
                case "Microsoft.UI.Xaml":
                    info.Properties["UsesWinUI"] = "true";
                    break;

                case "Microsoft.NETCore.UniversalWindowsPlatform":
                    info.Properties["UwpCoreVersion"] = version ?? "";
                    break;

                case "Win2D.UWP":
                case "Win2D.WinUI":
                    info.Properties["UsesWin2D"] = "true";
                    break;

                case "Microsoft.ApplicationInsights":
                    info.Properties["UsesTelemetry"] = "true";
                    break;

                case "Microsoft.Extensions.DependencyInjection":
                case "Microsoft.Extensions.Hosting":
                    info.Properties["UsesModernDI"] = "true";
                    break;

                // MVVM frameworks
                case "Microsoft.Toolkit.Mvvm":
                case "CommunityToolkit.Mvvm":
                    info.Properties["UsesMvvm"] = "true";
                    break;

                case "Prism.Unity":
                case "Prism.DryIoc":
                    info.Properties["UsesPrism"] = "true";
                    break;
            }
        }
    }

    private async Task AnalyzeCapabilitiesAndPermissions(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(info.PackageManifestPath) || !File.Exists(info.PackageManifestPath))
            return;

        try
        {
            var content = await File.ReadAllTextAsync(info.PackageManifestPath, cancellationToken);
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(content);

            // Extract capabilities
            var capabilityNodes = xmlDoc.SelectNodes("//Package/Capabilities/Capability");
            if (capabilityNodes != null)
            {
                foreach (XmlNode node in capabilityNodes)
                {
                    var capabilityName = node.Attributes?["Name"]?.Value;
                    if (!string.IsNullOrEmpty(capabilityName))
                    {
                        info.Capabilities[capabilityName] = "true";
                    }
                }
            }

            // Extract device capabilities
            var deviceCapabilityNodes = xmlDoc.SelectNodes("//Package/Capabilities/DeviceCapability");
            if (deviceCapabilityNodes != null)
            {
                foreach (XmlNode node in deviceCapabilityNodes)
                {
                    var capabilityName = node.Attributes?["Name"]?.Value;
                    if (!string.IsNullOrEmpty(capabilityName))
                    {
                        info.Capabilities[$"Device_{capabilityName}"] = "true";
                    }
                }
            }

            // Analyze security implications
            if (info.Capabilities.ContainsKey("internetClient"))
                info.Properties["RequiresNetwork"] = "true";
            
            if (info.Capabilities.ContainsKey("Device_location"))
                info.Properties["RequiresLocation"] = "true";
                
            if (info.Capabilities.ContainsKey("Device_webcam") || info.Capabilities.ContainsKey("Device_microphone"))
                info.Properties["RequiresMediaCapture"] = "true";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze capabilities: {Error}", ex.Message);
        }
    }

    private async Task AnalyzeXamlAndUiPatterns(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        var xamlFiles = Directory.GetFiles(info.ProjectDirectory, "*.xaml", SearchOption.AllDirectories);
        var codeFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        info.Properties["XamlFileCount"] = xamlFiles.Length.ToString();
        
        foreach (var xamlFile in xamlFiles.Take(10)) // Analyze first 10 XAML files for performance
        {
            try
            {
                var content = await File.ReadAllTextAsync(xamlFile, cancellationToken);
                
                // Detect UI frameworks and patterns
                if (content.Contains("winui:") || content.Contains("microsoft.ui.xaml"))
                    info.Properties["UsesWinUI"] = "true";
                    
                if (content.Contains("NavigationView") || content.Contains("Frame"))
                    info.Properties["UsesNavigation"] = "true";
                    
                if (content.Contains("Binding") || content.Contains("x:Bind"))
                    info.Properties["UsesDataBinding"] = "true";
                    
                if (content.Contains("UserControl"))
                    info.Properties["HasUserControls"] = "true";
                    
                if (content.Contains("ContentDialog") || content.Contains("Popup"))
                    info.Properties["UsesDialogs"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze XAML file {File}: {Error}", xamlFile, ex.Message);
            }
        }
        
        // Analyze code-behind patterns
        foreach (var codeFile in codeFiles.Take(20)) // Analyze first 20 code files
        {
            try
            {
                var content = await File.ReadAllTextAsync(codeFile, cancellationToken);
                
                if (content.Contains("INotifyPropertyChanged") || content.Contains("ObservableObject"))
                    info.Properties["UsesMvvm"] = "true";
                    
                if (content.Contains("async Task") || content.Contains("await "))
                    info.Properties["UsesAsyncPatterns"] = "true";
                    
                if (content.Contains("HttpClient") || content.Contains("WebRequest"))
                    info.Properties["RequiresNetwork"] = "true";
                    
                if (content.Contains("ApplicationData") || content.Contains("LocalSettings"))
                    info.Properties["UsesLocalStorage"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze code file {File}: {Error}", codeFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeWinUI3MigrationOpportunities(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        // Check if project can be migrated to WinUI 3
        var canMigrate = true;
        var migrationBlockers = new List<string>();
        
        // Check minimum platform version
        if (!string.IsNullOrEmpty(info.MinimumPlatformVersion))
        {
            if (Version.TryParse(info.MinimumPlatformVersion.Replace("10.0.", "").Split('.')[0], out var minVersion))
            {
                if (minVersion.Major < 17763) // Windows 10 version 1809
                {
                    migrationBlockers.Add("Minimum platform version too low for WinUI 3 (requires 10.0.17763.0 or higher)");
                    canMigrate = false;
                }
            }
        }
        
        // Check for UWP-specific APIs that don't exist in WinUI 3
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles.Take(20))
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                
                // Check for problematic UWP APIs
                if (content.Contains("Windows.ApplicationModel.Background"))
                    migrationBlockers.Add("Uses Background Tasks (different implementation in WinUI 3)");
                    
                if (content.Contains("Windows.System.Launcher"))
                    migrationBlockers.Add("Uses System Launcher (needs PackageId in WinUI 3)");
                    
                if (content.Contains("Windows.ApplicationModel.DataTransfer"))
                    migrationBlockers.Add("Uses DataTransfer APIs (limited support in WinUI 3)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze migration blockers in {File}: {Error}", sourceFile, ex.Message);
            }
        }
        
        info.Properties["CanMigrateToWinUI3"] = canMigrate.ToString();
        info.Properties["MigrationBlockerCount"] = migrationBlockers.Count.ToString();
        
        if (migrationBlockers.Any())
        {
            info.Properties["MigrationBlockers"] = string.Join("; ", migrationBlockers);
        }
    }

    private async Task AnalyzeDeploymentPatterns(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for MSIX packaging
        var waprojFiles = Directory.GetFiles(info.ProjectDirectory, "*.wapproj", SearchOption.AllDirectories);
        if (waprojFiles.Any())
        {
            info.Properties["HasPackagingProject"] = "true";
        }
        
        // Check for Store association
        if (info.Properties.ContainsKey("HasStoreAssociation") && info.Properties["HasStoreAssociation"] == "true")
        {
            info.Properties["ConfiguredForStore"] = "true";
        }
        
        // Check for enterprise deployment patterns
        var enterpriseFiles = Directory.GetFiles(info.ProjectDirectory, "*.appinstaller", SearchOption.AllDirectories);
        if (enterpriseFiles.Any())
        {
            info.Properties["SupportsEnterpriseDeployment"] = "true";
        }
    }

    private async Task AnalyzeModernPatterns(UwpProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for modern .NET patterns
        var modernPatternCount = 0;
        
        if (info.Properties.ContainsKey("UsesModernDI") && info.Properties["UsesModernDI"] == "true")
            modernPatternCount++;
            
        if (info.Properties.ContainsKey("UsesAsyncPatterns") && info.Properties["UsesAsyncPatterns"] == "true")
            modernPatternCount++;
            
        if (info.Properties.ContainsKey("UsesMvvm") && info.Properties["UsesMvvm"] == "true")
            modernPatternCount++;
            
        if (info.Properties.ContainsKey("UsesWinUI3") && info.Properties["UsesWinUI3"] == "true")
            modernPatternCount++;
        
        info.Properties["ModernPatternCount"] = modernPatternCount.ToString();
        info.Properties["HasModernPatterns"] = (modernPatternCount >= 2).ToString();
    }

    // Migration methods
    private async Task PrepareWinUI3Migration(UwpProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Preparing UWP to WinUI 3 migration guidance");

        result.Warnings.Add("UWP to WinUI 3 Migration Plan:");
        result.Warnings.Add("1. Create new WinUI 3 project using 'Blank App, Packaged (WinUI 3 in UWP)' template");
        result.Warnings.Add("2. Copy XAML files and update namespace references (Windows.UI.Xaml → Microsoft.UI.Xaml)");
        result.Warnings.Add("3. Update C# using statements and API calls");
        result.Warnings.Add("4. Review and update Package.appxmanifest for WinUI 3 compatibility");
        result.Warnings.Add("5. Test all functionality thoroughly, especially background tasks and system integrations");
        
        if (info.Properties.ContainsKey("MigrationBlockers"))
        {
            result.Warnings.Add($"Migration Blockers: {info.Properties["MigrationBlockers"]}");
        }
        
        // Generate WinUI 3 package references
        await GenerateWinUI3PackageReferences(packageReferences, info, result, cancellationToken);
    }

    private async Task PrepareMauiMigration(UwpProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Preparing UWP to MAUI migration guidance");

        result.Warnings.Add("UWP to .NET MAUI Migration Plan:");
        result.Warnings.Add("1. Create new .NET MAUI project targeting Windows platform");
        result.Warnings.Add("2. Migrate business logic and view models to MAUI");
        result.Warnings.Add("3. Convert UWP-specific XAML to MAUI XAML patterns");
        result.Warnings.Add("4. Replace UWP APIs with MAUI equivalents or platform-specific implementations");
        result.Warnings.Add("5. Consider cross-platform opportunities for broader device support");
        
        if (info.Properties.ContainsKey("RequiresNetwork") && info.Properties["RequiresNetwork"] == "true")
        {
            result.Warnings.Add("Network capabilities detected - ensure HttpClient usage is compatible with MAUI");
        }
    }

    private async Task ModernizeUwpProject(UwpProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing UWP project with best practices");

        // Update to latest UWP packages
        await UpdateUwpPackagesToLatest(packageReferences, info, result, cancellationToken);

        // Apply modern UWP patterns
        await ApplyModernUwpPatterns(info, projectElement, result, cancellationToken);

        result.Warnings.Add("UWP project modernized with latest packages and patterns.");
        result.Warnings.Add("Consider planning migration to WinUI 3 or MAUI for future development.");
    }

    private async Task ApplyUwpOptimizations(UwpProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Performance optimizations
        SetOrUpdateProperty(propertyGroup, "UseDotNetNativeToolchain", "true");
        SetOrUpdateProperty(propertyGroup, "EnableTypeInfoReflection", "false");
        
        // Modern compilation settings
        SetOrUpdateProperty(propertyGroup, "LangVersion", "latest");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        
        result.Warnings.Add("Applied UWP performance optimizations and modern compilation settings.");
    }

    private async Task GenerateWinUI3PackageReferences(List<PackageReference> packageReferences, UwpProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        result.Warnings.Add("Recommended WinUI 3 packages:");
        result.Warnings.Add("- Microsoft.WindowsAppSDK (latest stable)");
        result.Warnings.Add("- Microsoft.Windows.SDK.BuildTools (if needed)");
        
        if (info.Properties.ContainsKey("UsesMvvm") && info.Properties["UsesMvvm"] == "true")
        {
            result.Warnings.Add("- CommunityToolkit.Mvvm (for MVVM patterns)");
        }
        
        if (info.Properties.ContainsKey("UsesWin2D") && info.Properties["UsesWin2D"] == "true")
        {
            result.Warnings.Add("- Win2D.WinUI (for graphics)");
        }
    }

    private async Task UpdateUwpPackagesToLatest(List<PackageReference> packageReferences, UwpProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var uwpPackages = packageReferences.Where(p => 
            p.PackageId.StartsWith("Microsoft.NETCore.UniversalWindowsPlatform") ||
            p.PackageId.StartsWith("Microsoft.UI.Xaml") ||
            p.PackageId.StartsWith("Microsoft.Toolkit")).ToList();
        
        foreach (var package in uwpPackages)
        {
            if (package.PackageId == "Microsoft.NETCore.UniversalWindowsPlatform")
                package.Version = "6.2.14";
            else if (package.PackageId.StartsWith("Microsoft.UI.Xaml"))
                package.Version = "2.8.6";
        }
    }

    private async Task ApplyModernUwpPatterns(UwpProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Enable modern C# features
        SetOrUpdateProperty(propertyGroup, "LangVersion", "latest");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        
        // Optimize for modern deployment
        SetOrUpdateProperty(propertyGroup, "AppxBundle", "Always");
        SetOrUpdateProperty(propertyGroup, "AppxBundlePlatforms", "x86|x64|ARM|ARM64");
    }

    // Helper methods
    private bool CanMigrateToWinUI3(UwpProjectInfo info)
    {
        return info.Properties.ContainsKey("CanMigrateToWinUI3") && 
               info.Properties["CanMigrateToWinUI3"] == "true";
    }

    private bool CanMigrateToMaui(UwpProjectInfo info)
    {
        // MAUI migration is viable if the app doesn't heavily rely on UWP-specific features
        return !info.Properties.ContainsKey("RequiresMediaCapture") &&
               !info.Capabilities.ContainsKey("Device_webcam") &&
               info.Properties.GetValueOrDefault("ModernPatternCount", "0") != "0";
    }

    private string GetUwpProjectType(UwpProjectInfo info)
    {
        if (info.Properties.ContainsKey("UsesWinUI3") && info.Properties["UsesWinUI3"] == "true")
            return "WinUI3-Ready";
        if (info.Properties.ContainsKey("UsesWinUI") && info.Properties["UsesWinUI"] == "true")
            return "WinUI-Enhanced";
        if (info.Properties.ContainsKey("HasModernPatterns") && info.Properties["HasModernPatterns"] == "true")
            return "Modern-UWP";
        return "Legacy-UWP";
    }

    private bool HasModernPatterns(UwpProjectInfo info)
    {
        return info.Properties.ContainsKey("HasModernPatterns") && 
               info.Properties["HasModernPatterns"] == "true";
    }

    private static void EnsureItemIncluded(XElement itemGroup, string itemType, string include)
    {
        var existingItem = itemGroup.Elements(itemType)
            .FirstOrDefault(e => e.Attribute("Include")?.Value == include);

        if (existingItem == null)
        {
            itemGroup.Add(new XElement(itemType, new XAttribute("Include", include)));
        }
    }
}