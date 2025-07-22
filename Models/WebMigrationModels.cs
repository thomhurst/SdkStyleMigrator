namespace SdkMigrator.Models;

/// <summary>
/// Results of web project migration analysis
/// </summary>
public class WebMigrationAnalysis
{
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsWebProject { get; set; }
    public WebProjectType ProjectType { get; set; }
    public List<WebPattern> DetectedPatterns { get; set; } = new();
    public List<WebMigrationRecommendation> Recommendations { get; set; } = new();
    public List<WebConfigurationIssue> ConfigurationIssues { get; set; } = new();
    public WebComplexityAssessment Complexity { get; set; } = new();
}

/// <summary>
/// Results of web pattern detection
/// </summary>
public class WebPatternDetectionResult
{
    public bool HasGlobalAsax { get; set; }
    public GlobalAsaxAnalysis? GlobalAsaxAnalysis { get; set; }
    public bool HasAppStartFolder { get; set; }
    public List<AppStartFile> AppStartFiles { get; set; } = new();
    public bool HasAppCodeFolder { get; set; }
    public List<string> AppCodeFiles { get; set; } = new();
    public bool HasLegacyWebPages { get; set; }
    public List<LegacyWebPage> LegacyWebPages { get; set; } = new();
    public bool HasBundlingConfiguration { get; set; }
    public BundlingConfiguration? BundlingConfig { get; set; }
    public bool HasCustomHttpModules { get; set; }
    public List<HttpModuleInfo> HttpModules { get; set; } = new();
    public bool HasWebApiConfiguration { get; set; }
    public WebApiConfigurationInfo? WebApiConfig { get; set; }
}

/// <summary>
/// Web configuration migration guidance
/// </summary>
public class WebConfigurationMigrationGuidance
{
    public List<ConfigurationMigrationStep> MigrationSteps { get; set; } = new();
    public List<StartupConfigurationCode> StartupCodeGeneration { get; set; } = new();
    public List<PackageRecommendation> RecommendedPackages { get; set; } = new();
    public List<string> ManualMigrationTasks { get; set; } = new();
    public List<string> BreakingChanges { get; set; } = new();
}

/// <summary>
/// Types of web projects
/// </summary>
public enum WebProjectType
{
    WebForms,
    MvcFramework,
    WebApi,
    Mixed,
    WebSite,
    Unknown
}

/// <summary>
/// Detected web patterns
/// </summary>
public class WebPattern
{
    public WebPatternType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public MigrationRiskLevel Risk { get; set; }
    public List<string> MigrationNotes { get; set; } = new();
}

/// <summary>
/// Types of web patterns
/// </summary>
public enum WebPatternType
{
    GlobalAsax,
    AppStartConfiguration,
    AppCodeFiles,
    LegacyWebPages,
    HttpModules,
    HttpHandlers,
    BundlingMinification,
    WebApiConfiguration,
    MvcConfiguration,
    CustomAuthentication,
    SessionStateProvider,
    CustomConfigurationSection
}

/// <summary>
/// Global.asax file analysis
/// </summary>
public class GlobalAsaxAnalysis
{
    public string FilePath { get; set; } = string.Empty;
    public bool HasApplicationStart { get; set; }
    public bool HasApplicationEnd { get; set; }
    public bool HasSessionStart { get; set; }
    public bool HasSessionEnd { get; set; }
    public bool HasApplicationError { get; set; }
    public List<string> RoutingConfiguration { get; set; } = new();
    public List<string> FilterRegistration { get; set; } = new();
    public List<string> DependencyInjectionSetup { get; set; } = new();
    public List<string> CustomInitialization { get; set; } = new();
    public List<string> MigrationGuidance { get; set; } = new();
}

/// <summary>
/// App_Start file information
/// </summary>
public class AppStartFile
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public AppStartFileType Type { get; set; }
    public List<string> DetectedPatterns { get; set; } = new();
    public List<string> MigrationSteps { get; set; } = new();
    public string TargetLocation { get; set; } = string.Empty;
}

/// <summary>
/// Types of App_Start files
/// </summary>
public enum AppStartFileType
{
    RouteConfig,
    BundleConfig,
    FilterConfig,
    WebApiConfig,
    IdentityConfig,
    StartupAuth,
    Other
}

/// <summary>
/// Legacy web page information
/// </summary>
public class LegacyWebPage
{
    public string FilePath { get; set; } = string.Empty;
    public LegacyWebPageType Type { get; set; }
    public bool HasCodeBehind { get; set; }
    public string? CodeBehindPath { get; set; }
    public List<string> MigrationNotes { get; set; } = new();
}

/// <summary>
/// Types of legacy web pages
/// </summary>
public enum LegacyWebPageType
{
    WebForm,
    MasterPage,
    UserControl,
    WebHandler,
    WebService
}

/// <summary>
/// Bundling configuration analysis
/// </summary>
public class BundlingConfiguration
{
    public bool UsesSystemWebOptimization { get; set; }
    public List<BundleInfo> ScriptBundles { get; set; } = new();
    public List<BundleInfo> StyleBundles { get; set; } = new();
    public List<string> ModernAlternatives { get; set; } = new();
    public List<string> MigrationSteps { get; set; } = new();
}

/// <summary>
/// Bundle information
/// </summary>
public class BundleInfo
{
    public string VirtualPath { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public BundleType Type { get; set; }
}

/// <summary>
/// Types of bundles
/// </summary>
public enum BundleType
{
    Script,
    Style
}

/// <summary>
/// HTTP module information
/// </summary>
public class HttpModuleInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? AssemblyName { get; set; }
    public List<string> MigrationGuidance { get; set; } = new();
    public string? MiddlewareEquivalent { get; set; }
}

/// <summary>
/// Web API configuration information
/// </summary>
public class WebApiConfigurationInfo
{
    public bool HasAttributeRouting { get; set; }
    public bool HasConventionalRouting { get; set; }
    public List<string> ConfiguredRoutes { get; set; } = new();
    public List<string> MessageHandlers { get; set; } = new();
    public List<string> Filters { get; set; } = new();
    public bool HasCustomDependencyResolver { get; set; }
    public List<string> MigrationSteps { get; set; } = new();
}

/// <summary>
/// Web migration recommendation
/// </summary>
public class WebMigrationRecommendation
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MigrationRiskLevel Risk { get; set; }
    public List<string> Steps { get; set; } = new();
    public List<string> RequiredPackages { get; set; } = new();
    public string? CodeExample { get; set; }
    public List<string> AdditionalResources { get; set; } = new();
}

/// <summary>
/// Web configuration issue
/// </summary>
public class WebConfigurationIssue
{
    public string Section { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public MigrationRiskLevel Severity { get; set; }
    public bool RequiresManualMigration { get; set; }
}

/// <summary>
/// Web complexity assessment
/// </summary>
public class WebComplexityAssessment
{
    public MigrationRiskLevel OverallComplexity { get; set; }
    public int LegacyPatternCount { get; set; }
    public int CustomModuleCount { get; set; }
    public int ConfigurationComplexity { get; set; }
    public bool RequiresSignificantRefactoring { get; set; }
    public TimeSpan EstimatedMigrationTime { get; set; }
    public List<string> ComplexityFactors { get; set; } = new();
}

/// <summary>
/// Configuration migration step
/// </summary>
public class ConfigurationMigrationStep
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FromConfiguration { get; set; } = string.Empty;
    public string ToConfiguration { get; set; } = string.Empty;
    public string? CodeExample { get; set; }
    public bool IsAutomated { get; set; }
}

/// <summary>
/// Startup configuration code generation
/// </summary>
public class StartupConfigurationCode
{
    public string Section { get; set; } = string.Empty;
    public string ConfigureServicesCode { get; set; } = string.Empty;
    public string ConfigureCode { get; set; } = string.Empty;
    public List<string> RequiredUsings { get; set; } = new();
    public List<string> RequiredPackages { get; set; } = new();
}

/// <summary>
/// Package recommendation for web migration
/// </summary>
public class PackageRecommendation
{
    public string PackageName { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public List<string> Alternatives { get; set; } = new();
}