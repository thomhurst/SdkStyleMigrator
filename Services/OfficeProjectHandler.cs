using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class OfficeProjectHandler : IOfficeProjectHandler
{
    private readonly ILogger<OfficeProjectHandler> _logger;

    public OfficeProjectHandler(ILogger<OfficeProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<OfficeProjectInfo> DetectOfficeConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new OfficeProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive project structure analysis
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Comprehensive Office interop and COM reference analysis
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        var references = project.AllEvaluatedItems
            .Where(item => item.ItemType == "Reference" || item.ItemType == "COMReference")
            .ToList();

        await AnalyzeOfficeInteropReferences(info, references, cancellationToken);

        // Analyze NuGet packages for Office development
        await AnalyzeOfficePackages(info, packageReferences, cancellationToken);

        // Comprehensive VSTO and Office Add-in detection
        await DetectVstoConfiguration(info, cancellationToken);

        // Detect Office customizations and UI elements
        await DetectOfficeCustomizations(info, cancellationToken);

        // Analyze deployment and security configurations
        await AnalyzeDeploymentAndSecurity(info, cancellationToken);

        // Check for modern Office development patterns
        await AnalyzeModernOfficePatterns(info, cancellationToken);

        // Detect legacy patterns requiring migration
        await DetectLegacyPatterns(info, cancellationToken);

        // Analyze migration opportunities to modern Office development
        await AnalyzeMigrationOpportunities(info, cancellationToken);

        // Assess code quality and maintainability
        await AnalyzeCodeQuality(info, cancellationToken);

        _logger.LogInformation("Detected Office project: Type={Type}, Application={App}, VSTO={Version}, AddInType={AddInType}, CanMigrate={CanMigrate}, ModernPatterns={Modern}",
            GetOfficeProjectType(info), info.OfficeApplication, info.VstoVersion, GetAddInType(info), info.CanMigrate, HasModernPatterns(info));

        return info;
    }

    public async Task MigrateOfficeProjectAsync(
        OfficeProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine optimal migration strategy based on comprehensive analysis
            if (CanMigrateToWebAddIn(info))
            {
                // Provide guidance for migrating to modern Office Web Add-ins
                await ProvideWebAddInMigrationGuidance(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (CanMigrateToVstoModern(info))
            {
                // Modernize existing VSTO project with latest patterns
                await MigrateToModernVsto(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (CanMigrateToComAddIn(info))
            {
                // Migrate to COM Add-in with modern .NET
                await MigrateToModernComAddIn(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Provide legacy project modernization guidance
                await ModernizeLegacyOfficeProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Apply common Office development optimizations
            await ApplyOfficeOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully processed Office project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate Office project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "Office migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void ConfigureOfficeInteropReferences(XElement projectElement, OfficeProjectInfo info)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Add Office interop references
        foreach (var interopRef in info.InteropReferences)
        {
            EnsureItemIncluded(itemGroup, "Reference", interopRef, new Dictionary<string, string>
            {
                ["EmbedInteropTypes"] = "False"
            });
        }

        // Add VSTO runtime reference
        if (!string.IsNullOrEmpty(info.VstoVersion))
        {
            EnsureItemIncluded(itemGroup, "Reference", "Microsoft.Office.Tools.Common", new Dictionary<string, string>
            {
                ["EmbedInteropTypes"] = "False"
            });
        }
    }

    public void MigrateDeploymentConfiguration(string projectDirectory, XElement projectElement, OfficeProjectInfo info)
    {
        if (!info.HasClickOnceDeployment)
            return;

        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Configure ClickOnce deployment
        SetOrUpdateProperty(propertyGroup, "IsWebBootstrapper", "false");
        SetOrUpdateProperty(propertyGroup, "UseApplicationTrust", "false");
        SetOrUpdateProperty(propertyGroup, "BootstrapperEnabled", "true");

        if (!string.IsNullOrEmpty(info.DeploymentManifestPath))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, info.DeploymentManifestPath);
            SetOrUpdateProperty(propertyGroup, "ManifestPath", relativePath);
        }
    }

    public void MigrateOfficeCustomizations(string projectDirectory, XElement projectElement, OfficeProjectInfo info)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");

        // Include ribbon XML
        if (!string.IsNullOrEmpty(info.RibbonXmlPath))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, info.RibbonXmlPath);
            EnsureItemIncluded(itemGroup, "EmbeddedResource", relativePath);
        }

        // Include custom task panes
        foreach (var taskPane in info.CustomTaskPanes)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, taskPane);
            EnsureItemIncluded(itemGroup, "Compile", relativePath);
        }
    }

    public bool CanMigrateToModernFormat(OfficeProjectInfo info)
    {
        // VSTO projects have limited migration capability
        // Modern approach is to use Office Add-ins with web technologies
        return !string.IsNullOrEmpty(info.OfficeApplication) && 
               !info.HasClickOnceDeployment && 
               info.InteropReferences.Count < 5;
    }

    // Comprehensive analysis methods
    private async Task AnalyzeProjectStructure(OfficeProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Get target framework
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        
        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            var frameworks = targetFrameworks.Split(';');
            info.Properties["TargetFrameworks"] = targetFrameworks;
            info.Properties["IsNet8Plus"] = frameworks.Any(f => f.StartsWith("net8.0") || f.StartsWith("net9.0")).ToString();
        }
        else if (!string.IsNullOrEmpty(targetFramework))
        {
            info.Properties["TargetFramework"] = targetFramework;
            info.Properties["IsNet8Plus"] = (targetFramework.StartsWith("net8.0") || targetFramework.StartsWith("net9.0")).ToString();
        }

        // Check output type
        var outputType = project.GetPropertyValue("OutputType");
        info.Properties["OutputType"] = outputType ?? "Library";

        // Check for Office-specific properties
        var officeVersion = project.GetPropertyValue("OfficeVersion");
        if (!string.IsNullOrEmpty(officeVersion))
        {
            info.Properties["OfficeVersion"] = officeVersion;
        }

        // Check for VSTO properties
        var vstoVersion = project.GetPropertyValue("VSTOVersion");
        if (!string.IsNullOrEmpty(vstoVersion))
        {
            info.VstoVersion = vstoVersion;
            info.Properties["VSTOVersion"] = vstoVersion;
        }
    }

    private async Task AnalyzeOfficeInteropReferences(OfficeProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> references, CancellationToken cancellationToken)
    {
        var detectedApplications = new HashSet<string>();
        var comReferences = new Dictionary<string, string>();

        foreach (var reference in references)
        {
            var include = reference.EvaluatedInclude;
            var type = reference.ItemType;

            // Analyze Office Interop references
            if (include.Contains("Microsoft.Office.Interop"))
            {
                info.InteropReferences.Add(include);

                if (include.Contains("Word"))
                {
                    detectedApplications.Add("Word");
                    info.Properties["SupportsWord"] = "true";
                }
                else if (include.Contains("Excel"))
                {
                    detectedApplications.Add("Excel");
                    info.Properties["SupportsExcel"] = "true";
                }
                else if (include.Contains("PowerPoint"))
                {
                    detectedApplications.Add("PowerPoint");
                    info.Properties["SupportsPowerPoint"] = "true";
                }
                else if (include.Contains("Outlook"))
                {
                    detectedApplications.Add("Outlook");
                    info.Properties["SupportsOutlook"] = "true";
                }
                else if (include.Contains("Access"))
                {
                    detectedApplications.Add("Access");
                    info.Properties["SupportsAccess"] = "true";
                }
                else if (include.Contains("Visio"))
                {
                    detectedApplications.Add("Visio");
                    info.Properties["SupportsVisio"] = "true";
                }
                else if (include.Contains("Project"))
                {
                    detectedApplications.Add("Project");
                    info.Properties["SupportsProject"] = "true";
                }
            }

            // Analyze COM references
            if (type == "COMReference")
            {
                var guid = reference.GetMetadataValue("Guid");
                var versionMajor = reference.GetMetadataValue("VersionMajor");
                var versionMinor = reference.GetMetadataValue("VersionMinor");
                
                if (!string.IsNullOrEmpty(guid))
                {
                    comReferences[include] = $"{guid} v{versionMajor}.{versionMinor}";
                }
            }
        }

        // Set primary application
        if (detectedApplications.Any())
        {
            info.OfficeApplication = detectedApplications.First();
            info.Properties["DetectedApplications"] = string.Join(";", detectedApplications);
        }

        if (comReferences.Any())
        {
            info.Properties["ComReferenceCount"] = comReferences.Count.ToString();
        }
    }

    private async Task AnalyzeOfficePackages(OfficeProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Microsoft.Office.Interop.Excel":
                case "Microsoft.Office.Interop.Word":
                case "Microsoft.Office.Interop.PowerPoint":
                case "Microsoft.Office.Interop.Outlook":
                    info.Properties["UsesInteropPackages"] = "true";
                    break;

                case "Microsoft.VSTO.Runtime":
                case "Microsoft.Office.Tools":
                case "Microsoft.Office.Tools.Common":
                case "Microsoft.Office.Tools.Excel":
                case "Microsoft.Office.Tools.Word":
                case "Microsoft.Office.Tools.Outlook":
                    info.Properties["UsesVSTORuntime"] = "true";
                    if (!string.IsNullOrEmpty(version))
                        info.VstoVersion = version;
                    break;

                case "NetOffice.Core":
                case "NetOffice.Excel":
                case "NetOffice.Word":
                case "NetOffice.PowerPoint":
                case "NetOffice.Outlook":
                    info.Properties["UsesNetOffice"] = "true";
                    break;

                case "Microsoft.Graph":
                case "Microsoft.Graph.Core":
                    info.Properties["UsesGraphAPI"] = "true";
                    break;

                case "DocumentFormat.OpenXml":
                    info.Properties["UsesOpenXML"] = "true";
                    break;

                case "ClosedXML":
                    info.Properties["UsesClosedXML"] = "true";
                    break;

                case "EPPlus":
                    info.Properties["UsesEPPlus"] = "true";
                    break;
            }
        }
    }

    private async Task DetectVstoConfiguration(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var projectContent = await File.ReadAllTextAsync(info.ProjectPath, cancellationToken);
            
            // Enhanced VSTO version detection
            if (projectContent.Contains("Microsoft.Office.Tools"))
            {
                var vstoVersionMatch = Regex.Match(projectContent, @"Microsoft\.Office\.Tools.*?Version=([0-9\.]+)");
                if (vstoVersionMatch.Success)
                {
                    info.VstoVersion = vstoVersionMatch.Groups[1].Value;
                }
                else
                {
                    // Fallback version detection
                    if (projectContent.Contains("v4.0"))
                        info.VstoVersion = "4.0";
                    else if (projectContent.Contains("v3.0"))
                        info.VstoVersion = "3.0";
                    else if (projectContent.Contains("v2.0"))
                        info.VstoVersion = "2.0";
                }
            }

            // Check for ClickOnce deployment
            info.HasClickOnceDeployment = projectContent.Contains("ClickOnce") || 
                                         projectContent.Contains("PublishUrl") ||
                                         projectContent.Contains("Install");

            // Check for Office security and trust configuration
            if (projectContent.Contains("InclusionThresholdSetting") || projectContent.Contains("TrustUrlParameters"))
            {
                info.Properties["HasSecurityConfiguration"] = "true";
            }

            // Check for application manifest
            if (projectContent.Contains("ApplicationManifest") || projectContent.Contains("app.manifest"))
            {
                info.Properties["HasApplicationManifest"] = "true";
            }

            // Check for document-level vs application-level
            if (projectContent.Contains("ThisDocument") || projectContent.Contains("ThisWorkbook") || 
                projectContent.Contains("ThisPresentation") || projectContent.Contains("ThisAddIn"))
            {
                if (projectContent.Contains("ThisAddIn"))
                    info.Properties["AddInLevel"] = "Application";
                else
                    info.Properties["AddInLevel"] = "Document";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect VSTO configuration: {Error}", ex.Message);
        }
    }

    private async Task DetectOfficeCustomizations(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var customizations = new List<string>();

            // Find ribbon XML files
            var ribbonFiles = Directory.GetFiles(info.ProjectDirectory, "*Ribbon*.xml", SearchOption.AllDirectories);
            foreach (var ribbonFile in ribbonFiles)
            {
                info.RibbonXmlPath = ribbonFile;
                customizations.Add($"Ribbon XML: {Path.GetFileName(ribbonFile)}");
            }

            // Find ribbon designer files
            var ribbonDesignerFiles = Directory.GetFiles(info.ProjectDirectory, "*Ribbon*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".Designer.") && !f.Contains("AssemblyInfo"))
                .ToList();
            
            foreach (var ribbonFile in ribbonDesignerFiles)
            {
                var content = await File.ReadAllTextAsync(ribbonFile, cancellationToken);
                if (content.Contains("RibbonBase") || content.Contains("IRibbonExtensibility"))
                {
                    customizations.Add($"Ribbon Code: {Path.GetFileName(ribbonFile)}");
                }
            }

            // Find custom task panes
            var taskPaneFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
            foreach (var taskPaneFile in taskPaneFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(taskPaneFile, cancellationToken);
                    if (content.Contains("CustomTaskPane") || content.Contains("TaskPane") || 
                        content.Contains("UserControl") && content.Contains("Office"))
                    {
                        info.CustomTaskPanes.Add(taskPaneFile);
                        customizations.Add($"Task Pane: {Path.GetFileName(taskPaneFile)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze task pane file {File}: {Error}", taskPaneFile, ex.Message);
                }
            }

            // Find form regions (Outlook specific)
            var formRegionFiles = Directory.GetFiles(info.ProjectDirectory, "*FormRegion*.cs", SearchOption.AllDirectories);
            foreach (var formRegionFile in formRegionFiles)
            {
                customizations.Add($"Form Region: {Path.GetFileName(formRegionFile)}");
            }

            // Find action panes
            var actionPaneFiles = Directory.GetFiles(info.ProjectDirectory, "*ActionPane*.cs", SearchOption.AllDirectories);
            foreach (var actionPaneFile in actionPaneFiles)
            {
                customizations.Add($"Action Pane: {Path.GetFileName(actionPaneFile)}");
            }

            if (customizations.Any())
            {
                info.Properties["OfficeCustomizations"] = string.Join("; ", customizations);
                info.Properties["CustomizationCount"] = customizations.Count.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect Office customizations: {Error}", ex.Message);
        }
    }

    private async Task AnalyzeDeploymentAndSecurity(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            // Find deployment manifest
            var manifestFiles = Directory.GetFiles(info.ProjectDirectory, "*.manifest", SearchOption.AllDirectories);
            if (manifestFiles.Any())
            {
                info.DeploymentManifestPath = manifestFiles.First();
                info.Properties["HasDeploymentManifest"] = "true";
            }

            // Find application manifest
            var appManifestFiles = Directory.GetFiles(info.ProjectDirectory, "app.manifest", SearchOption.AllDirectories);
            if (appManifestFiles.Any())
            {
                info.Properties["HasApplicationManifest"] = "true";
            }

            // Check for certificate files
            var certificateFiles = Directory.GetFiles(info.ProjectDirectory, "*.pfx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(info.ProjectDirectory, "*.snk", SearchOption.AllDirectories))
                .ToList();
            
            if (certificateFiles.Any())
            {
                info.Properties["HasCertificates"] = "true";
                info.Properties["CertificateCount"] = certificateFiles.Count.ToString();
            }

            // Check for setup projects
            var setupFiles = Directory.GetFiles(info.ProjectDirectory, "*.vdproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(info.ProjectDirectory, "*.wixproj", SearchOption.AllDirectories))
                .ToList();
            
            if (setupFiles.Any())
            {
                info.Properties["HasSetupProject"] = "true";
            }

            // Analyze project file for security settings
            var projectContent = await File.ReadAllTextAsync(info.ProjectPath, cancellationToken);
            if (projectContent.Contains("SignManifests") && projectContent.Contains("true"))
            {
                info.Properties["SignsManifests"] = "true";
            }

            if (projectContent.Contains("SignAssembly") && projectContent.Contains("true"))
            {
                info.Properties["SignsAssembly"] = "true";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze deployment and security: {Error}", ex.Message);
        }
    }

    private async Task AnalyzeModernOfficePatterns(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        var modernPatternCount = 0;

        // Check for async/await patterns
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles.Take(20))
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);

                if (content.Contains("async Task") || content.Contains("await "))
                {
                    info.Properties["UsesAsyncPatterns"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("Microsoft.Graph") || content.Contains("GraphServiceClient"))
                {
                    info.Properties["UsesGraphAPI"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("HttpClient") || content.Contains("RestClient"))
                {
                    info.Properties["UsesModernHttpClients"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("IServiceCollection") || content.Contains("DependencyInjection"))
                {
                    info.Properties["UsesDependencyInjection"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("IConfiguration") || content.Contains("IOptions"))
                {
                    info.Properties["UsesModernConfiguration"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("ILogger") || content.Contains("LogInformation"))
                {
                    info.Properties["UsesModernLogging"] = "true";
                    modernPatternCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze modern patterns in {File}: {Error}", sourceFile, ex.Message);
            }
        }

        info.Properties["ModernPatternCount"] = modernPatternCount.ToString();
        info.Properties["HasModernPatterns"] = (modernPatternCount >= 2).ToString();
    }

    private async Task DetectLegacyPatterns(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        var legacyPatterns = new List<string>();

        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles.Take(20))
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                var fileName = Path.GetFileName(sourceFile);

                // Check for legacy VSTO patterns
                if (content.Contains("System.Runtime.InteropServices") && content.Contains("Marshal.ReleaseComObject"))
                {
                    legacyPatterns.Add($"Manual COM object release in {fileName}");
                }

                if (content.Contains("Microsoft.Office.Interop") && content.Contains("Missing.Value"))
                {
                    legacyPatterns.Add($"Legacy Missing.Value usage in {fileName}");
                }

                if (content.Contains("Application.EnableEvents = false"))
                {
                    legacyPatterns.Add($"Event suppression pattern in {fileName}");
                }

                if (content.Contains("System.Windows.Forms") && !content.Contains("WPF"))
                {
                    legacyPatterns.Add($"WinForms UI in {fileName}");
                }

                // Check for deprecated Office APIs
                if (content.Contains("FileDialog") || content.Contains("GetOpenFilename"))
                {
                    legacyPatterns.Add($"Legacy file dialog usage in {fileName}");
                }

                if (content.Contains("Globals.") && content.Contains("Application"))
                {
                    legacyPatterns.Add($"Global object usage in {fileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze legacy patterns in {File}: {Error}", sourceFile, ex.Message);
            }
        }

        if (legacyPatterns.Any())
        {
            info.Properties["LegacyPatterns"] = string.Join("; ", legacyPatterns.Take(5));
            info.Properties["LegacyPatternCount"] = legacyPatterns.Count.ToString();
        }
    }

    private async Task AnalyzeMigrationOpportunities(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        // Assess migration feasibility
        info.CanMigrate = await AssessMigrationFeasibility(info, cancellationToken);

        var opportunities = new List<string>();

        // Web Add-in migration opportunity
        if (CanMigrateToWebAddIn(info))
        {
            opportunities.Add("Web Add-in migration (JavaScript/TypeScript)");
            info.Properties["CanMigrateToWebAddIn"] = "true";
        }

        // Modern VSTO migration
        if (CanMigrateToVstoModern(info))
        {
            opportunities.Add("Modern VSTO with .NET 8+");
            info.Properties["CanMigrateToModernVSTO"] = "true";
        }

        // COM Add-in modernization
        if (CanMigrateToComAddIn(info))
        {
            opportunities.Add("Modern COM Add-in");
            info.Properties["CanMigrateToComAddIn"] = "true";
        }

        // Office.js compatibility
        if (info.Properties.ContainsKey("UsesGraphAPI") || !info.Properties.ContainsKey("LegacyPatterns"))
        {
            opportunities.Add("Office.js compatibility layer");
            info.Properties["OfficeJsCompatible"] = "true";
        }

        if (opportunities.Any())
        {
            info.Properties["MigrationOpportunities"] = string.Join("; ", opportunities);
        }
    }

    private async Task AnalyzeCodeQuality(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        var qualityIssues = new List<string>();
        var qualityScore = 100;

        // Check for proper COM object disposal
        if (info.Properties.ContainsKey("LegacyPatterns") && 
            info.Properties["LegacyPatterns"].Contains("Manual COM object release"))
        {
            qualityIssues.Add("Manual COM object disposal detected - memory leak risk");
            qualityScore -= 15;
        }

        // Check complexity
        var customizationCount = int.Parse(info.Properties.GetValueOrDefault("CustomizationCount", "0"));
        if (customizationCount > 10)
        {
            qualityIssues.Add("High customization complexity may complicate migration");
            qualityScore -= 20;
        }
        else if (customizationCount > 5)
        {
            qualityScore -= 10;
        }

        // Check for modern patterns
        if (!info.Properties.ContainsKey("HasModernPatterns") || 
            info.Properties["HasModernPatterns"] != "true")
        {
            qualityIssues.Add("Limited modern .NET patterns - consider modernization");
            qualityScore -= 15;
        }

        // Check for security issues
        if (!info.Properties.ContainsKey("HasCertificates"))
        {
            qualityIssues.Add("Missing code signing certificates - deployment security concern");
            qualityScore -= 10;
        }

        info.Properties["QualityScore"] = qualityScore.ToString();
        if (qualityIssues.Any())
        {
            info.Properties["QualityIssues"] = string.Join("; ", qualityIssues);
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

    // Migration methods
    private async Task ProvideWebAddInMigrationGuidance(OfficeProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Providing Office Web Add-in migration guidance");

        result.Warnings.Add("Office Web Add-in Migration Recommended:");
        result.Warnings.Add("1. Create new Office Add-in project using Yeoman generator or Visual Studio template");
        result.Warnings.Add("2. Migrate business logic to TypeScript/JavaScript using Office.js APIs");
        result.Warnings.Add("3. Replace Office Interop calls with equivalent Office.js methods");
        result.Warnings.Add("4. Convert UI elements (ribbons, task panes) to web-based HTML/CSS/JS");
        result.Warnings.Add("5. Use Microsoft Graph APIs for data access and external integrations");
        result.Warnings.Add("6. Test across all supported Office platforms (Web, Desktop, Mobile)");
        
        if (info.Properties.ContainsKey("OfficeCustomizations"))
        {
            result.Warnings.Add($"Custom UI elements to migrate: {info.Properties["OfficeCustomizations"]}");
        }
    }

    private async Task MigrateToModernVsto(OfficeProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating to modern VSTO with .NET 8+ patterns");

        // Update to modern .NET
        await ConfigureModernOfficeProperties(info, projectElement, result, cancellationToken);

        // Update packages to modern versions
        await UpdateOfficePackagesToModern(packageReferences, info, result, cancellationToken);

        result.Warnings.Add("VSTO project updated to modern .NET patterns.");
        result.Warnings.Add("Consider migrating to Office Web Add-ins for better cross-platform support.");
    }

    private async Task MigrateToModernComAddIn(OfficeProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating to modern COM Add-in");

        // Configure COM Add-in properties
        await ConfigureComAddInProperties(info, projectElement, result, cancellationToken);

        result.Warnings.Add("COM Add-in configured for modern .NET development.");
        result.Warnings.Add("Ensure proper COM registration and security considerations.");
    }

    private async Task ModernizeLegacyOfficeProject(OfficeProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing legacy Office project");

        // Apply basic modernization
        await ApplyBasicModernization(info, projectElement, result, cancellationToken);

        result.Warnings.Add("Legacy Office project partially modernized.");
        result.Warnings.Add("Full migration to modern Office development patterns recommended.");
        result.Warnings.Add("Consider evaluating Office Web Add-ins or modern VSTO approaches.");
    }

    private async Task ApplyOfficeOptimizations(OfficeProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Performance optimizations
        SetOrUpdateProperty(propertyGroup, "Optimize", "true");
        SetOrUpdateProperty(propertyGroup, "LangVersion", "latest");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        
        // Security optimizations
        SetOrUpdateProperty(propertyGroup, "TreatWarningsAsErrors", "true");
        SetOrUpdateProperty(propertyGroup, "WarningsAsErrors", "");
        SetOrUpdateProperty(propertyGroup, "WarningsNotAsErrors", "CS8600;CS8601;CS8602;CS8618");
        
        result.Warnings.Add("Applied Office development optimizations and security enhancements.");
    }

    private async Task ConfigureModernOfficeProperties(OfficeProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Set modern .NET version
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0-windows");
        SetOrUpdateProperty(propertyGroup, "UseWindowsForms", "true");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        
        // VSTO-specific properties
        if (!string.IsNullOrEmpty(info.VstoVersion))
        {
            SetOrUpdateProperty(propertyGroup, "UseVSTO", "true");
        }
    }

    private async Task ConfigureComAddInProperties(OfficeProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // COM Add-in specific properties
        SetOrUpdateProperty(propertyGroup, "RegisterForComInterop", "true");
        SetOrUpdateProperty(propertyGroup, "ComVisible", "true");
        SetOrUpdateProperty(propertyGroup, "ClassInterface", "None");
    }

    private async Task ApplyBasicModernization(OfficeProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Basic modernization
        SetOrUpdateProperty(propertyGroup, "LangVersion", "latest");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
    }

    private async Task UpdateOfficePackagesToModern(List<PackageReference> packageReferences, OfficeProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        // Update Office-related packages to modern versions
        var officePackages = packageReferences.Where(p => 
            p.PackageId.StartsWith("Microsoft.Office") ||
            p.PackageId.StartsWith("Microsoft.VSTO")).ToList();

        foreach (var package in officePackages)
        {
            if (package.PackageId.StartsWith("Microsoft.Office.Tools"))
                package.Version = "10.0.0";
            else if (package.PackageId.StartsWith("Microsoft.VSTO"))
                package.Version = "10.0.0";
        }

        result.Warnings.Add("Updated Office packages to modern versions where available.");
    }

    // Helper methods for migration assessment
    private async Task<bool> AssessMigrationFeasibility(OfficeProjectInfo info, CancellationToken cancellationToken)
    {
        // Basic feasibility check
        return !string.IsNullOrEmpty(info.OfficeApplication) && 
               (!info.HasClickOnceDeployment || info.Properties.ContainsKey("HasModernPatterns"));
    }

    private bool CanMigrateToWebAddIn(OfficeProjectInfo info)
    {
        // Web Add-in migration is feasible if:
        // 1. Limited COM object usage
        // 2. No heavy interop requirements
        // 3. UI is mostly standard Office interactions
        var comReferenceCount = int.Parse(info.Properties.GetValueOrDefault("ComReferenceCount", "0"));
        var customizationCount = int.Parse(info.Properties.GetValueOrDefault("CustomizationCount", "0"));
        
        return comReferenceCount <= 3 && customizationCount <= 8 && 
               !info.Properties.ContainsKey("LegacyPatterns");
    }

    private bool CanMigrateToVstoModern(OfficeProjectInfo info)
    {
        // Modern VSTO migration is feasible if:
        // 1. Already using VSTO runtime
        // 2. Limited legacy patterns
        // 3. Standard Office application target
        return info.Properties.ContainsKey("UsesVSTORuntime") &&
               !string.IsNullOrEmpty(info.OfficeApplication) &&
               (!info.Properties.ContainsKey("LegacyPatternCount") || 
                int.Parse(info.Properties["LegacyPatternCount"]) <= 5);
    }

    private bool CanMigrateToComAddIn(OfficeProjectInfo info)
    {
        // COM Add-in migration is feasible for application-level customizations
        return info.Properties.GetValueOrDefault("AddInLevel") == "Application" &&
               !info.HasClickOnceDeployment;
    }

    private string GetOfficeProjectType(OfficeProjectInfo info)
    {
        if (info.Properties.ContainsKey("UsesVSTORuntime") && info.Properties["UsesVSTORuntime"] == "true")
            return "VSTO";
        if (info.Properties.ContainsKey("UsesNetOffice") && info.Properties["UsesNetOffice"] == "true")
            return "NetOffice";
        if (info.Properties.ContainsKey("UsesInteropPackages") && info.Properties["UsesInteropPackages"] == "true")
            return "Interop";
        if (info.Properties.ContainsKey("ComReferenceCount"))
            return "COM";
        return "Legacy";
    }

    private string GetAddInType(OfficeProjectInfo info)
    {
        var addInLevel = info.Properties.GetValueOrDefault("AddInLevel", "Unknown");
        if (addInLevel == "Application")
            return "Application-Level";
        if (addInLevel == "Document")
            return "Document-Level";
        return "Unknown";
    }

    private bool HasModernPatterns(OfficeProjectInfo info)
    {
        return info.Properties.ContainsKey("HasModernPatterns") && 
               info.Properties["HasModernPatterns"] == "true";
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
}