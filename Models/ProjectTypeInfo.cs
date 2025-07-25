namespace SdkMigrator.Models;

/// <summary>
/// Base class for project type-specific information
/// </summary>
public abstract class ProjectTypeInfo
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// Azure Functions project configuration information
/// </summary>
public class FunctionsProjectInfo : ProjectTypeInfo
{
    public string FunctionsVersion { get; set; } = "v4";
    public bool HasHostJson { get; set; }
    public bool HasLocalSettingsJson { get; set; }
    public bool HasProgramCs { get; set; }
    public bool HasStartupCs { get; set; }
    public List<string> FunctionJsonFiles { get; set; } = new();
    public Dictionary<string, string> ExtensionBundles { get; set; } = new();
    
    // Model detection
    public bool UsesIsolatedModel { get; set; }
    public bool HasInProcessPackages { get; set; }
    public bool HasIsolatedWorkerPackages { get; set; }
    public bool NeedsIsolatedModelMigration { get; set; }
    public bool IsExecutableProject { get; set; }
    
    // Configuration analysis
    public string ConfiguredWorkerRuntime { get; set; } = string.Empty;
    public bool ConfiguredForIsolatedRuntime { get; set; }
    public bool ConfiguredForInProcessRuntime { get; set; }
    public bool HasCustomLoggingConfig { get; set; }
    public Dictionary<string, string> LocalSettingsValues { get; set; } = new();
    
    // Code analysis
    public List<string> InProcessFunctionMethods { get; set; } = new();
    public List<string> IsolatedFunctionMethods { get; set; } = new();
    public bool UsesLegacyWebJobsPatterns { get; set; }
    public List<string> DetectedTriggerTypes { get; set; } = new();
    
    // Extension tracking
    public List<string> IsolatedWorkerExtensions { get; set; } = new();
    public List<string> InProcessExtensions { get; set; } = new();
}

/// <summary>
/// MAUI/Xamarin project configuration information
/// </summary>
public class MauiProjectInfo : ProjectTypeInfo
{
    // Project type detection
    public bool IsXamarinForms { get; set; }
    public bool IsMauiProject { get; set; }
    public bool IsXamarinAndroid { get; set; }
    public bool IsXamariniOS { get; set; }
    public bool NeedsXamarinFormsMigration { get; set; }
    
    // Platform and framework configuration
    public List<string> TargetPlatforms { get; set; } = new();
    public Dictionary<string, string> PlatformVersions { get; set; } = new();
    public string MauiVersion { get; set; } = string.Empty;
    public string NetVersion { get; set; } = "net8.0";
    
    // Project structure
    public bool HasPlatformsFolder { get; set; }
    public bool HasLegacyPlatformProjects { get; set; }
    public bool HasSingleProject { get; set; }
    
    // Resources and assets
    public List<string> ResourceFiles { get; set; } = new();
    public string? AppIconPath { get; set; }
    public string? SplashScreenPath { get; set; }
    public List<string> FontFiles { get; set; } = new();
    public List<string> ImageFiles { get; set; } = new();
    
    // Legacy Xamarin patterns requiring migration
    public List<string> LegacyXamarinPatterns { get; set; } = new();
    public List<string> CustomRenderers { get; set; } = new();
    public List<string> CustomHandlers { get; set; } = new();
    public List<string> DependencyServices { get; set; } = new();
    public List<string> PlatformSpecificCode { get; set; } = new();
    
    // Configuration and features
    public bool UsesAotCompilation { get; set; }
    public bool UsesMauiEssentials { get; set; }
    public bool UsesCommunityToolkit { get; set; }
    public Dictionary<string, string> MauiFeatures { get; set; } = new();
    
    // Package analysis
    public List<string> IncompatiblePackages { get; set; } = new();
    public List<string> MigrationRequiredPackages { get; set; } = new();
}

/// <summary>
/// Blazor project configuration information (WebAssembly, Server, Hybrid)
/// </summary>
public class BlazorProjectInfo : ProjectTypeInfo
{
    // Project type detection
    public bool IsWebAssembly { get; set; }
    public bool IsServerSide { get; set; }
    public bool IsHybridApp { get; set; }
    public bool IsNet8Plus { get; set; }
    
    // .NET 8+ Render modes
    public string GlobalRenderMode { get; set; } = string.Empty;
    public bool UsesStaticSSR { get; set; }
    public bool UsesInteractiveServer { get; set; }
    public bool UsesInteractiveWebAssembly { get; set; }
    public bool UsesAutoRenderMode { get; set; }
    public bool UsesStreamRendering { get; set; }
    public bool UsesEnhancedNavigation { get; set; }
    
    // PWA capabilities
    public bool IsPwa { get; set; }
    public bool HasWwwroot { get; set; }
    public string? ServiceWorkerPath { get; set; }
    public string? ManifestJsonPath { get; set; }
    public List<string> StaticAssets { get; set; } = new();
    
    // Performance optimizations
    public bool UsesJiterpreter { get; set; }
    public bool UsesAotCompilation { get; set; }
    public bool UsesSIMD { get; set; }
    public bool UsesILTrimming { get; set; }
    public bool UsesCompression { get; set; }
    
    // Component analysis
    public List<string> RazorComponents { get; set; } = new();
    public List<string> Pages { get; set; } = new();
    public List<string> LayoutComponents { get; set; } = new();
    public Dictionary<string, string> ComponentRenderModes { get; set; } = new();
    
    // Configuration files
    public bool HasAppSettingsJson { get; set; }
    public bool HasBlazorConfigJs { get; set; }
    public bool HasRouterComponent { get; set; }
    
    // Legacy patterns
    public List<string> LegacyPatterns { get; set; } = new();
    public bool NeedsNet8Migration { get; set; }
    
    // Hosting and deployment
    public string HostingModel { get; set; } = string.Empty;
    public List<string> StaticWebAssets { get; set; } = new();
    public Dictionary<string, string> BuildOptimizations { get; set; } = new();
    
    // JavaScript interop analysis
    public bool UsesJavaScriptInterop { get; set; }
    public List<string> JavaScriptModules { get; set; } = new();
    public List<string> JavaScriptLibraries { get; set; } = new();
    public int JSInteropComplexityScore { get; set; }
    public Dictionary<string, int> JSInteropPatterns { get; set; } = new();
    public List<string> JSFrameworkIntegrations { get; set; } = new();
}

/// <summary>
/// Worker Service project configuration information
/// </summary>
public class WorkerServiceInfo : ProjectTypeInfo
{
    public List<string> HostedServices { get; set; } = new();
    public bool HasAppSettingsJson { get; set; }
    public bool HasDependencyInjection { get; set; }
    public List<string> ConfigurationFiles { get; set; } = new();
}

/// <summary>
/// gRPC Service project configuration information
/// </summary>
public class GrpcProjectInfo : ProjectTypeInfo
{
    public List<string> ProtoFiles { get; set; } = new();
    public Dictionary<string, string> ProtoReferences { get; set; } = new();
    public bool HasGrpcWeb { get; set; }
    public bool HasReflection { get; set; }
}

/// <summary>
/// UWP project configuration information
/// </summary>
public class UwpProjectInfo : ProjectTypeInfo
{
    public string? PackageManifestPath { get; set; }
    public string? CertificatePath { get; set; }
    public List<string> Assets { get; set; } = new();
    public string MinimumPlatformVersion { get; set; } = string.Empty;
    public string TargetPlatformVersion { get; set; } = string.Empty;
    public Dictionary<string, string> Capabilities { get; set; } = new();
}

/// <summary>
/// Database project configuration information
/// </summary>
public class DatabaseProjectInfo : ProjectTypeInfo
{
    public string DatabaseType { get; set; } = "SqlServer";
    public List<string> SqlFiles { get; set; } = new();
    public List<string> SchemaFiles { get; set; } = new();
    public List<string> DatabaseReferences { get; set; } = new();
    public Dictionary<string, string> DeploymentSettings { get; set; } = new();
    public bool CanMigrate { get; set; } = true;
}

/// <summary>
/// Docker project configuration information
/// </summary>
public class DockerProjectInfo : ProjectTypeInfo
{
    public List<string> ComposeFiles { get; set; } = new();
    public List<string> Dockerfiles { get; set; } = new();
    public Dictionary<string, string> Services { get; set; } = new();
    public bool IsOrchestrationProject { get; set; }
}

/// <summary>
/// Shared project configuration information
/// </summary>
public class SharedProjectInfo : ProjectTypeInfo
{
    public List<string> SourceFiles { get; set; } = new();
    public List<string> ReferencingProjects { get; set; } = new();
    public bool CanConvertToClassLibrary { get; set; } = true;
    public string? RecommendedConversionPath { get; set; }
}

/// <summary>
/// Office/VSTO project configuration information
/// </summary>
public class OfficeProjectInfo : ProjectTypeInfo
{
    public string OfficeApplication { get; set; } = string.Empty; // Word, Excel, PowerPoint, etc.
    public string VstoVersion { get; set; } = string.Empty;
    public List<string> InteropReferences { get; set; } = new();
    public string? DeploymentManifestPath { get; set; }
    public string? RibbonXmlPath { get; set; }
    public List<string> CustomTaskPanes { get; set; } = new();
    public bool HasClickOnceDeployment { get; set; }
    public bool CanMigrate { get; set; } = true;
}

/// <summary>
/// Migration guidance for Docker projects
/// </summary>
public class DockerMigrationGuidance
{
    public bool RequiresManualMigration { get; set; }
    public List<string> RecommendedActions { get; set; } = new();
    public List<string> AlternativeApproaches { get; set; } = new();
}

/// <summary>
/// Migration guidance for shared projects
/// </summary>
public class SharedProjectMigrationGuidance
{
    public bool ShouldConvertToClassLibrary { get; set; }
    public string RecommendedTargetFramework { get; set; } = string.Empty;
    public List<string> RequiredChanges { get; set; } = new();
    public List<string> ReferencingProjectUpdates { get; set; } = new();
}