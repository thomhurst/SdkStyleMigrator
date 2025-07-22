using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// Handles specialized analysis and migration guidance for ASP.NET web projects
/// </summary>
public class WebProjectHandler : IWebProjectHandler
{
    private readonly ILogger<WebProjectHandler> _logger;

    public WebProjectHandler(ILogger<WebProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<WebMigrationAnalysis> AnalyzeWebProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing web project: {ProjectPath}", projectPath);

        var analysis = new WebMigrationAnalysis
        {
            ProjectPath = projectPath
        };

        try
        {
            var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;

            // Detect project type and patterns
            analysis.ProjectType = await DetectWebProjectTypeAsync(projectDirectory, cancellationToken);
            analysis.IsWebProject = analysis.ProjectType != WebProjectType.Unknown;

            if (!analysis.IsWebProject)
            {
                return analysis;
            }

            // Detect web patterns
            var patternResult = await DetectWebPatternsAsync(projectPath, cancellationToken);
            analysis.DetectedPatterns = await ConvertPatternsToWebPatternsAsync(patternResult, cancellationToken);

            // Generate recommendations
            analysis.Recommendations = await GenerateRecommendationsAsync(patternResult, analysis.ProjectType, cancellationToken);

            // Assess complexity
            analysis.Complexity = AssessComplexity(patternResult, analysis.DetectedPatterns);

            _logger.LogInformation("Web project analysis completed. Found {PatternCount} patterns", analysis.DetectedPatterns.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing web project: {ProjectPath}", projectPath);
        }

        return analysis;
    }

    public async Task<WebPatternDetectionResult> DetectWebPatternsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var result = new WebPatternDetectionResult();

        // Detect Global.asax
        await DetectGlobalAsaxAsync(projectDirectory, result, cancellationToken);

        // Detect App_Start folder
        await DetectAppStartFolderAsync(projectDirectory, result, cancellationToken);

        // Detect App_Code folder
        await DetectAppCodeFolderAsync(projectDirectory, result, cancellationToken);

        // Detect legacy web pages
        await DetectLegacyWebPagesAsync(projectDirectory, result, cancellationToken);

        // Detect bundling configuration
        await DetectBundlingConfigurationAsync(projectDirectory, result, cancellationToken);

        // Detect HTTP modules from web.config
        await DetectHttpModulesAsync(projectDirectory, result, cancellationToken);

        // Detect Web API configuration
        await DetectWebApiConfigurationAsync(projectDirectory, result, cancellationToken);

        return result;
    }

    public async Task<WebConfigurationMigrationGuidance> GenerateConfigurationGuidanceAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var guidance = new WebConfigurationMigrationGuidance();
        var patternResult = await DetectWebPatternsAsync(projectPath, cancellationToken);

        // Generate startup code for detected patterns
        if (patternResult.HasGlobalAsax && patternResult.GlobalAsaxAnalysis != null)
        {
            GenerateGlobalAsaxMigrationGuidance(patternResult.GlobalAsaxAnalysis, guidance);
        }

        if (patternResult.HasAppStartFolder)
        {
            GenerateAppStartMigrationGuidance(patternResult.AppStartFiles, guidance);
        }

        if (patternResult.HasBundlingConfiguration && patternResult.BundlingConfig != null)
        {
            GenerateBundlingMigrationGuidance(patternResult.BundlingConfig, guidance);
        }

        if (patternResult.HasWebApiConfiguration && patternResult.WebApiConfig != null)
        {
            GenerateWebApiMigrationGuidance(patternResult.WebApiConfig, guidance);
        }

        return guidance;
    }

    private async Task<WebProjectType> DetectWebProjectTypeAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        var hasWebForms = Directory.GetFiles(projectDirectory, "*.aspx", SearchOption.AllDirectories).Any() ||
                          Directory.GetFiles(projectDirectory, "*.master", SearchOption.AllDirectories).Any();

        var hasMvc = Directory.GetFiles(projectDirectory, "*.cshtml", SearchOption.AllDirectories).Any() ||
                     Directory.Exists(Path.Combine(projectDirectory, "Views"));

        var hasWebApi = Directory.GetFiles(projectDirectory, "*ApiController.cs", SearchOption.AllDirectories).Any();
        
        if (!hasWebApi)
        {
            var controllerFiles = Directory.GetFiles(projectDirectory, "*Controller.cs", SearchOption.AllDirectories);
            foreach (var controllerFile in controllerFiles)
            {
                if (await ContainsWebApiPatternsAsync(controllerFile, cancellationToken))
                {
                    hasWebApi = true;
                    break;
                }
            }
        }

        return (hasWebForms, hasMvc, hasWebApi) switch
        {
            (true, true, _) => WebProjectType.Mixed,
            (true, false, false) => WebProjectType.WebForms,
            (false, true, true) => WebProjectType.Mixed,
            (false, true, false) => WebProjectType.MvcFramework,
            (false, false, true) => WebProjectType.WebApi,
            _ => WebProjectType.Unknown
        };
    }

    private async Task<bool> ContainsWebApiPatternsAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return content.Contains("ApiController") || 
                   content.Contains("[Route(") || 
                   content.Contains("[HttpGet") ||
                   content.Contains("[HttpPost") ||
                   content.Contains("IHttpActionResult") ||
                   content.Contains("ActionResult<");
        }
        catch
        {
            return false;
        }
    }

    private async Task DetectGlobalAsaxAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var globalAsaxPath = Path.Combine(projectDirectory, "Global.asax");
        var globalAsaxCsPath = Path.Combine(projectDirectory, "Global.asax.cs");

        if (File.Exists(globalAsaxPath) || File.Exists(globalAsaxCsPath))
        {
            result.HasGlobalAsax = true;
            result.GlobalAsaxAnalysis = await AnalyzeGlobalAsaxAsync(globalAsaxCsPath, cancellationToken);
        }
    }

    private async Task<GlobalAsaxAnalysis> AnalyzeGlobalAsaxAsync(string globalAsaxCsPath, CancellationToken cancellationToken)
    {
        var analysis = new GlobalAsaxAnalysis
        {
            FilePath = globalAsaxCsPath
        };

        if (!File.Exists(globalAsaxCsPath))
        {
            return analysis;
        }

        try
        {
            var content = await File.ReadAllTextAsync(globalAsaxCsPath, cancellationToken);

            analysis.HasApplicationStart = content.Contains("Application_Start");
            analysis.HasApplicationEnd = content.Contains("Application_End");
            analysis.HasSessionStart = content.Contains("Session_Start");
            analysis.HasSessionEnd = content.Contains("Session_End");
            analysis.HasApplicationError = content.Contains("Application_Error");

            // Detect routing configuration
            if (content.Contains("RouteTable.Routes") || content.Contains("routes.MapRoute"))
            {
                analysis.RoutingConfiguration.Add("Legacy MVC routing detected in Global.asax");
            }

            // Detect filter registration
            if (content.Contains("GlobalFilters.Filters.Add"))
            {
                analysis.FilterRegistration.Add("Global filter registration detected");
            }

            // Detect DI container setup
            if (content.Contains("DependencyResolver.SetResolver") || content.Contains("Container.Register"))
            {
                analysis.DependencyInjectionSetup.Add("Legacy dependency injection container detected");
            }

            // Generate migration guidance
            GenerateGlobalAsaxMigrationSteps(analysis, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing Global.asax file: {FilePath}", globalAsaxCsPath);
        }

        return analysis;
    }

    private void GenerateGlobalAsaxMigrationSteps(GlobalAsaxAnalysis analysis, string content)
    {
        if (analysis.HasApplicationStart)
        {
            analysis.MigrationGuidance.Add("Move Application_Start logic to Program.cs or Startup.cs Configure method");
        }

        if (analysis.HasApplicationError)
        {
            analysis.MigrationGuidance.Add("Replace Application_Error with global exception handling middleware");
        }

        if (analysis.RoutingConfiguration.Any())
        {
            analysis.MigrationGuidance.Add("Move routing configuration to Program.cs with app.MapControllerRoute()");
        }

        if (analysis.FilterRegistration.Any())
        {
            analysis.MigrationGuidance.Add("Register global filters in Program.cs with services.AddMvc(options => options.Filters.Add(...))");
        }

        if (analysis.DependencyInjectionSetup.Any())
        {
            analysis.MigrationGuidance.Add("Replace custom DI container with built-in ASP.NET Core DI in Program.cs");
        }
    }

    private async Task DetectAppStartFolderAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var appStartPath = Path.Combine(projectDirectory, "App_Start");
        if (!Directory.Exists(appStartPath))
        {
            return;
        }

        result.HasAppStartFolder = true;
        var files = Directory.GetFiles(appStartPath, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var appStartFile = await AnalyzeAppStartFileAsync(file, cancellationToken);
            result.AppStartFiles.Add(appStartFile);
        }
    }

    private async Task<AppStartFile> AnalyzeAppStartFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        var appStartFile = new AppStartFile
        {
            FileName = fileName,
            FilePath = filePath,
            Type = DetermineAppStartFileType(fileName)
        };

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            switch (appStartFile.Type)
            {
                case AppStartFileType.RouteConfig:
                    AnalyzeRouteConfig(content, appStartFile);
                    break;
                case AppStartFileType.BundleConfig:
                    AnalyzeBundleConfig(content, appStartFile);
                    break;
                case AppStartFileType.FilterConfig:
                    AnalyzeFilterConfig(content, appStartFile);
                    break;
                case AppStartFileType.WebApiConfig:
                    AnalyzeWebApiConfig(content, appStartFile);
                    break;
                case AppStartFileType.IdentityConfig:
                    AnalyzeIdentityConfig(content, appStartFile);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing App_Start file: {FilePath}", filePath);
        }

        return appStartFile;
    }

    private AppStartFileType DetermineAppStartFileType(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "routeconfig.cs" => AppStartFileType.RouteConfig,
            "bundleconfig.cs" => AppStartFileType.BundleConfig,
            "filterconfig.cs" => AppStartFileType.FilterConfig,
            "webapiconfig.cs" => AppStartFileType.WebApiConfig,
            "identityconfig.cs" => AppStartFileType.IdentityConfig,
            "startup.auth.cs" => AppStartFileType.StartupAuth,
            _ => AppStartFileType.Other
        };
    }

    private void AnalyzeRouteConfig(string content, AppStartFile appStartFile)
    {
        if (content.Contains("routes.MapRoute"))
        {
            appStartFile.DetectedPatterns.Add("MVC route mappings detected");
            appStartFile.MigrationSteps.Add("Convert routes.MapRoute() calls to app.MapControllerRoute() in Program.cs");
        }

        if (content.Contains("routes.IgnoreRoute"))
        {
            appStartFile.DetectedPatterns.Add("Route ignore patterns detected");
            appStartFile.MigrationSteps.Add("Review ignored routes and implement equivalent logic in ASP.NET Core routing");
        }

        appStartFile.TargetLocation = "Program.cs - Configure routing";
    }

    private void AnalyzeBundleConfig(string content, AppStartFile appStartFile)
    {
        if (content.Contains("bundles.Add"))
        {
            appStartFile.DetectedPatterns.Add("Script/Style bundling configuration detected");
            appStartFile.MigrationSteps.Add("Replace System.Web.Optimization with modern bundling solution");
            appStartFile.MigrationSteps.Add("Consider using Webpack, Vite, or ASP.NET Core bundling");
        }

        if (content.Contains("StyleBundle") || content.Contains("ScriptBundle"))
        {
            appStartFile.DetectedPatterns.Add("Bundle definitions found");
        }

        appStartFile.TargetLocation = "wwwroot - Modern asset pipeline";
    }

    private void AnalyzeFilterConfig(string content, AppStartFile appStartFile)
    {
        if (content.Contains("GlobalFilters.Filters.Add"))
        {
            appStartFile.DetectedPatterns.Add("Global filter registration detected");
            appStartFile.MigrationSteps.Add("Register filters in Program.cs: services.AddMvc(options => options.Filters.Add(...))");
        }

        appStartFile.TargetLocation = "Program.cs - ConfigureServices";
    }

    private void AnalyzeWebApiConfig(string content, AppStartFile appStartFile)
    {
        if (content.Contains("config.Routes.MapHttpRoute"))
        {
            appStartFile.DetectedPatterns.Add("Web API routing configuration detected");
            appStartFile.MigrationSteps.Add("Replace with ASP.NET Core API controller routing");
        }

        if (content.Contains("config.Formatters"))
        {
            appStartFile.DetectedPatterns.Add("Custom formatters configuration detected");
            appStartFile.MigrationSteps.Add("Configure formatters in Program.cs: services.AddControllers(options => ...)");
        }

        if (content.Contains("config.MessageHandlers"))
        {
            appStartFile.DetectedPatterns.Add("Message handlers detected");
            appStartFile.MigrationSteps.Add("Replace with ASP.NET Core middleware or DelegatingHandler");
        }

        appStartFile.TargetLocation = "Program.cs - API configuration";
    }

    private void AnalyzeIdentityConfig(string content, AppStartFile appStartFile)
    {
        if (content.Contains("UserManager") || content.Contains("IdentityConfig"))
        {
            appStartFile.DetectedPatterns.Add("ASP.NET Identity configuration detected");
            appStartFile.MigrationSteps.Add("Migrate to ASP.NET Core Identity");
            appStartFile.MigrationSteps.Add("Update user management and authentication configuration");
        }

        appStartFile.TargetLocation = "Program.cs - Identity configuration";
    }

    private async Task DetectAppCodeFolderAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var appCodePath = Path.Combine(projectDirectory, "App_Code");
        if (!Directory.Exists(appCodePath))
        {
            return;
        }

        result.HasAppCodeFolder = true;
        result.AppCodeFiles = Directory.GetFiles(appCodePath, "*.cs", SearchOption.AllDirectories).ToList();
    }

    private async Task DetectLegacyWebPagesAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var webFormFiles = Directory.GetFiles(projectDirectory, "*.aspx", SearchOption.AllDirectories);
        var masterPageFiles = Directory.GetFiles(projectDirectory, "*.master", SearchOption.AllDirectories);
        var userControlFiles = Directory.GetFiles(projectDirectory, "*.ascx", SearchOption.AllDirectories);
        var webHandlerFiles = Directory.GetFiles(projectDirectory, "*.ashx", SearchOption.AllDirectories);
        var webServiceFiles = Directory.GetFiles(projectDirectory, "*.asmx", SearchOption.AllDirectories);

        var allFiles = webFormFiles.Concat(masterPageFiles).Concat(userControlFiles)
                                  .Concat(webHandlerFiles).Concat(webServiceFiles);

        if (allFiles.Any())
        {
            result.HasLegacyWebPages = true;
            
            foreach (var file in allFiles)
            {
                var legacyPage = new LegacyWebPage
                {
                    FilePath = file,
                    Type = DetermineLegacyWebPageType(file),
                    HasCodeBehind = HasCodeBehindFile(file),
                    CodeBehindPath = HasCodeBehindFile(file) ? GetCodeBehindPath(file) : null
                };

                GenerateLegacyPageMigrationNotes(legacyPage);
                result.LegacyWebPages.Add(legacyPage);
            }
        }
    }

    private LegacyWebPageType DetermineLegacyWebPageType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".aspx" => LegacyWebPageType.WebForm,
            ".master" => LegacyWebPageType.MasterPage,
            ".ascx" => LegacyWebPageType.UserControl,
            ".ashx" => LegacyWebPageType.WebHandler,
            ".asmx" => LegacyWebPageType.WebService,
            _ => LegacyWebPageType.WebForm
        };
    }

    private bool HasCodeBehindFile(string filePath)
    {
        var codeBehindPath = GetCodeBehindPath(filePath);
        return File.Exists(codeBehindPath);
    }

    private string GetCodeBehindPath(string filePath)
    {
        return filePath + ".cs";
    }

    private void GenerateLegacyPageMigrationNotes(LegacyWebPage legacyPage)
    {
        switch (legacyPage.Type)
        {
            case LegacyWebPageType.WebForm:
                legacyPage.MigrationNotes.Add("Consider migrating to Razor Pages or MVC Views");
                legacyPage.MigrationNotes.Add("Review server controls and postback patterns");
                break;
            case LegacyWebPageType.MasterPage:
                legacyPage.MigrationNotes.Add("Migrate to Razor Layout pages (_Layout.cshtml)");
                break;
            case LegacyWebPageType.UserControl:
                legacyPage.MigrationNotes.Add("Migrate to Razor Partial Views or View Components");
                break;
            case LegacyWebPageType.WebHandler:
                legacyPage.MigrationNotes.Add("Replace with ASP.NET Core middleware or minimal APIs");
                break;
            case LegacyWebPageType.WebService:
                legacyPage.MigrationNotes.Add("Migrate to ASP.NET Core Web API controllers");
                break;
        }
    }

    private async Task DetectBundlingConfigurationAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var bundleConfigPath = Path.Combine(projectDirectory, "App_Start", "BundleConfig.cs");
        if (!File.Exists(bundleConfigPath))
        {
            return;
        }

        result.HasBundlingConfiguration = true;
        result.BundlingConfig = await AnalyzeBundlingConfigurationAsync(bundleConfigPath, cancellationToken);
    }

    private async Task<BundlingConfiguration> AnalyzeBundlingConfigurationAsync(string bundleConfigPath, CancellationToken cancellationToken)
    {
        var config = new BundlingConfiguration
        {
            UsesSystemWebOptimization = true
        };

        try
        {
            var content = await File.ReadAllTextAsync(bundleConfigPath, cancellationToken);

            // Parse bundle definitions (simplified parsing)
            var scriptBundleRegex = new Regex(@"bundles\.Add\(new ScriptBundle\(""([^""]+)""\)", RegexOptions.Multiline);
            var styleBundleRegex = new Regex(@"bundles\.Add\(new StyleBundle\(""([^""]+)""\)", RegexOptions.Multiline);

            foreach (Match match in scriptBundleRegex.Matches(content))
            {
                config.ScriptBundles.Add(new BundleInfo
                {
                    VirtualPath = match.Groups[1].Value,
                    Type = BundleType.Script
                });
            }

            foreach (Match match in styleBundleRegex.Matches(content))
            {
                config.StyleBundles.Add(new BundleInfo
                {
                    VirtualPath = match.Groups[1].Value,
                    Type = BundleType.Style
                });
            }

            // Add modern alternatives
            config.ModernAlternatives.Add("Webpack with webpack.config.js");
            config.ModernAlternatives.Add("Vite for modern frontend tooling");
            config.ModernAlternatives.Add("ASP.NET Core bundling and minification");
            config.ModernAlternatives.Add("LibMan for client-side library management");

            // Add migration steps
            config.MigrationSteps.Add("Remove System.Web.Optimization NuGet package");
            config.MigrationSteps.Add("Create modern build pipeline (webpack/vite/etc.)");
            config.MigrationSteps.Add("Move assets to wwwroot directory");
            config.MigrationSteps.Add("Update references in Layout files");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing bundling configuration: {FilePath}", bundleConfigPath);
        }

        return config;
    }

    private async Task DetectHttpModulesAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var webConfigPath = Path.Combine(projectDirectory, "web.config");
        if (!File.Exists(webConfigPath))
        {
            return;
        }

        // This is a simplified implementation - would need full XML parsing for production
        try
        {
            var content = await File.ReadAllTextAsync(webConfigPath, cancellationToken);
            if (content.Contains("<httpModules>") || content.Contains("<modules>"))
            {
                result.HasCustomHttpModules = true;
                // Would parse actual modules here in production
                result.HttpModules.Add(new HttpModuleInfo
                {
                    Name = "Custom modules detected",
                    MigrationGuidance = { "Review web.config for custom HTTP modules and convert to ASP.NET Core middleware" }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing web.config for HTTP modules: {FilePath}", webConfigPath);
        }
    }

    private async Task DetectWebApiConfigurationAsync(string projectDirectory, WebPatternDetectionResult result, CancellationToken cancellationToken)
    {
        var webApiConfigPath = Path.Combine(projectDirectory, "App_Start", "WebApiConfig.cs");
        if (!File.Exists(webApiConfigPath))
        {
            return;
        }

        result.HasWebApiConfiguration = true;
        result.WebApiConfig = await AnalyzeWebApiConfigurationAsync(webApiConfigPath, cancellationToken);
    }

    private async Task<WebApiConfigurationInfo> AnalyzeWebApiConfigurationAsync(string webApiConfigPath, CancellationToken cancellationToken)
    {
        var config = new WebApiConfigurationInfo();

        try
        {
            var content = await File.ReadAllTextAsync(webApiConfigPath, cancellationToken);

            config.HasAttributeRouting = content.Contains("config.MapHttpAttributeRoutes");
            config.HasConventionalRouting = content.Contains("config.Routes.MapHttpRoute");
            config.HasCustomDependencyResolver = content.Contains("config.DependencyResolver");

            if (content.Contains("config.MessageHandlers"))
            {
                config.MessageHandlers.Add("Custom message handlers detected");
            }

            if (content.Contains("config.Filters"))
            {
                config.Filters.Add("Global Web API filters detected");
            }

            // Add migration steps
            config.MigrationSteps.Add("Replace Web API configuration with ASP.NET Core API controllers");
            config.MigrationSteps.Add("Use [Route] attributes for routing instead of MapHttpRoute");
            config.MigrationSteps.Add("Replace message handlers with ASP.NET Core middleware");
            config.MigrationSteps.Add("Use built-in DI instead of custom dependency resolver");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing Web API configuration: {FilePath}", webApiConfigPath);
        }

        return config;
    }

    private async Task<List<WebPattern>> ConvertPatternsToWebPatternsAsync(WebPatternDetectionResult patternResult, CancellationToken cancellationToken)
    {
        var patterns = new List<WebPattern>();

        if (patternResult.HasGlobalAsax)
        {
            patterns.Add(new WebPattern
            {
                Type = WebPatternType.GlobalAsax,
                Description = "Global.asax application lifecycle events detected",
                FilePath = patternResult.GlobalAsaxAnalysis?.FilePath ?? "",
                Risk = MigrationRiskLevel.Medium,
                MigrationNotes = patternResult.GlobalAsaxAnalysis?.MigrationGuidance ?? new List<string>()
            });
        }

        foreach (var appStartFile in patternResult.AppStartFiles)
        {
            patterns.Add(new WebPattern
            {
                Type = WebPatternType.AppStartConfiguration,
                Description = $"App_Start configuration file: {appStartFile.FileName}",
                FilePath = appStartFile.FilePath,
                Risk = MigrationRiskLevel.Medium,
                MigrationNotes = appStartFile.MigrationSteps
            });
        }

        if (patternResult.HasBundlingConfiguration)
        {
            patterns.Add(new WebPattern
            {
                Type = WebPatternType.BundlingMinification,
                Description = "System.Web.Optimization bundling configuration detected",
                FilePath = "App_Start/BundleConfig.cs",
                Risk = MigrationRiskLevel.Medium,
                MigrationNotes = patternResult.BundlingConfig?.MigrationSteps ?? new List<string>()
            });
        }

        return patterns;
    }

    private async Task<List<WebMigrationRecommendation>> GenerateRecommendationsAsync(
        WebPatternDetectionResult patternResult, 
        WebProjectType projectType, 
        CancellationToken cancellationToken)
    {
        var recommendations = new List<WebMigrationRecommendation>();

        // Global.asax recommendations
        if (patternResult.HasGlobalAsax)
        {
            recommendations.Add(new WebMigrationRecommendation
            {
                Title = "Migrate Global.asax Application Events",
                Description = "Move application lifecycle events from Global.asax to Program.cs",
                Risk = MigrationRiskLevel.Medium,
                Steps = {
                    "Create Program.cs with modern hosting model",
                    "Move Application_Start logic to ConfigureServices/Configure",
                    "Replace Application_Error with global exception handling middleware",
                    "Move routing configuration to endpoint routing"
                },
                RequiredPackages = { "Microsoft.AspNetCore.App" },
                CodeExample = @"
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(""/Home/Error"");
    app.UseHsts();
}

app.UseRouting();
app.MapControllerRoute(
    name: ""default"",
    pattern: ""{controller=Home}/{action=Index}/{id?}"");

app.Run();
"
            });
        }

        // App_Start recommendations
        if (patternResult.HasAppStartFolder)
        {
            recommendations.Add(new WebMigrationRecommendation
            {
                Title = "Migrate App_Start Configuration Files",
                Description = "Convert App_Start configuration to ASP.NET Core startup patterns",
                Risk = MigrationRiskLevel.High,
                Steps = {
                    "Review each App_Start file and understand its configuration",
                    "Move route configuration to Program.cs endpoint routing",
                    "Replace bundle configuration with modern asset pipeline",
                    "Convert filter registration to ASP.NET Core equivalents",
                    "Update Web API configuration for ASP.NET Core controllers"
                },
                RequiredPackages = { "Microsoft.AspNetCore.Mvc" }
            });
        }

        return recommendations;
    }

    private WebComplexityAssessment AssessComplexity(WebPatternDetectionResult patternResult, List<WebPattern> detectedPatterns)
    {
        var complexity = new WebComplexityAssessment
        {
            LegacyPatternCount = detectedPatterns.Count,
            CustomModuleCount = patternResult.HttpModules.Count
        };

        // Calculate complexity score
        var complexityScore = 0;
        complexityScore += patternResult.HasGlobalAsax ? 2 : 0;
        complexityScore += patternResult.AppStartFiles.Count;
        complexityScore += patternResult.LegacyWebPages.Count / 5; // Every 5 pages adds 1 point
        complexityScore += patternResult.HttpModules.Count * 2;
        complexityScore += patternResult.HasBundlingConfiguration ? 1 : 0;

        complexity.OverallComplexity = complexityScore switch
        {
            <= 3 => MigrationRiskLevel.Low,
            <= 7 => MigrationRiskLevel.Medium,
            _ => MigrationRiskLevel.High
        };

        complexity.ConfigurationComplexity = complexityScore;
        complexity.RequiresSignificantRefactoring = complexityScore > 7;
        
        complexity.EstimatedMigrationTime = complexityScore switch
        {
            <= 3 => TimeSpan.FromDays(1),
            <= 7 => TimeSpan.FromDays(3),
            <= 12 => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(14)
        };

        // Add complexity factors
        if (patternResult.HasGlobalAsax)
            complexity.ComplexityFactors.Add("Global.asax application lifecycle events");
        
        if (patternResult.HasAppStartFolder)
            complexity.ComplexityFactors.Add($"{patternResult.AppStartFiles.Count} App_Start configuration files");
        
        if (patternResult.HasLegacyWebPages && patternResult.LegacyWebPages.Count > 10)
            complexity.ComplexityFactors.Add($"{patternResult.LegacyWebPages.Count} legacy web pages");
        
        if (patternResult.HasCustomHttpModules)
            complexity.ComplexityFactors.Add("Custom HTTP modules");

        return complexity;
    }

    private void GenerateGlobalAsaxMigrationGuidance(GlobalAsaxAnalysis globalAsaxAnalysis, WebConfigurationMigrationGuidance guidance)
    {
        if (globalAsaxAnalysis.HasApplicationStart)
        {
            guidance.StartupCodeGeneration.Add(new StartupConfigurationCode
            {
                Section = "Application Startup",
                ConfigureServicesCode = @"
// Move initialization logic from Application_Start here
builder.Services.AddControllersWithViews();
",
                RequiredUsings = { "Microsoft.AspNetCore.Builder", "Microsoft.Extensions.DependencyInjection" }
            });
        }

        if (globalAsaxAnalysis.HasApplicationError)
        {
            guidance.StartupCodeGeneration.Add(new StartupConfigurationCode
            {
                Section = "Error Handling",
                ConfigureCode = @"
// Replace Application_Error with middleware
app.UseExceptionHandler(""/Error"");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
",
                RequiredUsings = { "Microsoft.AspNetCore.Diagnostics" }
            });
        }
    }

    private void GenerateAppStartMigrationGuidance(List<AppStartFile> appStartFiles, WebConfigurationMigrationGuidance guidance)
    {
        foreach (var file in appStartFiles)
        {
            switch (file.Type)
            {
                case AppStartFileType.RouteConfig:
                    guidance.StartupCodeGeneration.Add(new StartupConfigurationCode
                    {
                        Section = "Routing Configuration",
                        ConfigureCode = @"
app.UseRouting();
app.MapControllerRoute(
    name: ""default"",
    pattern: ""{controller=Home}/{action=Index}/{id?}"");
"
                    });
                    break;

                case AppStartFileType.BundleConfig:
                    guidance.ManualMigrationTasks.Add("Set up modern asset bundling (Webpack, Vite, or LibMan)");
                    guidance.ManualMigrationTasks.Add("Move static assets to wwwroot directory");
                    break;

                case AppStartFileType.FilterConfig:
                    guidance.StartupCodeGeneration.Add(new StartupConfigurationCode
                    {
                        Section = "Global Filters",
                        ConfigureServicesCode = @"
builder.Services.AddMvc(options =>
{
    // Add global filters here
    // options.Filters.Add<CustomActionFilter>();
});
"
                    });
                    break;
            }
        }
    }

    private void GenerateBundlingMigrationGuidance(BundlingConfiguration bundlingConfig, WebConfigurationMigrationGuidance guidance)
    {
        guidance.ManualMigrationTasks.Add("Replace System.Web.Optimization with modern bundling solution");
        guidance.ManualMigrationTasks.Add("Consider using Webpack, Vite, or ASP.NET Core bundling");
        guidance.ManualMigrationTasks.Add("Move JavaScript/CSS files to wwwroot directory");
        guidance.ManualMigrationTasks.Add("Update layout files to reference individual files or new bundles");

        guidance.RecommendedPackages.Add(new PackageRecommendation
        {
            PackageName = "Microsoft.AspNetCore.Mvc.TagHelpers",
            Reason = "For bundling and minification tag helpers",
            IsRequired = false,
            Alternatives = { "Webpack", "Vite", "LibMan" }
        });
    }

    private void GenerateWebApiMigrationGuidance(WebApiConfigurationInfo webApiConfig, WebConfigurationMigrationGuidance guidance)
    {
        guidance.StartupCodeGeneration.Add(new StartupConfigurationCode
        {
            Section = "Web API Configuration",
            ConfigureServicesCode = @"
builder.Services.AddControllers();
// Add any custom services, formatters, or options here
",
            ConfigureCode = @"
app.UseRouting();
app.MapControllers();
"
        });

        if (webApiConfig.HasCustomDependencyResolver)
        {
            guidance.ManualMigrationTasks.Add("Replace custom dependency resolver with ASP.NET Core built-in DI");
        }

        if (webApiConfig.MessageHandlers.Any())
        {
            guidance.ManualMigrationTasks.Add("Convert Web API message handlers to ASP.NET Core middleware");
        }
    }
}