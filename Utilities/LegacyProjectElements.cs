namespace SdkMigrator.Utilities;

public static class LegacyProjectElements
{
    public static readonly HashSet<string> PropertiesToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectGuid", // Not needed in SDK-style projects
        "ProjectTypeGuids",
        "TargetFrameworkProfile",
        "FileAlignment",
        "AppDesignerFolder",
        "SchemaVersion",
        "ProductVersion",
        "FileVersion",
        "OldToolsVersion",
        "UpgradeBackupLocation",
        "Prefer32Bit", // Irrelevant for libraries, only matters for exe
        "DebugSymbols",
        "DebugType",
        "Optimize",
        "OutputPath",
        "IntermediateOutputPath",
        "ErrorReport",
        "WarningLevel"
        // Note: RootNamespace and AssemblyName removed from here - handled conditionally
        // Note: ClickOnce properties removed from here - handled separately
    };

    // Properties handled conditionally elsewhere - this list is now empty
    // Signing properties are handled in SdkStyleProjectGenerator
    public static readonly HashSet<string> PropertiesToPreserve = new(StringComparer.OrdinalIgnoreCase)
    {
        // Empty - all properties are now handled conditionally
    };

    public static readonly HashSet<string> AssemblyPropertiesToExtract = new(StringComparer.OrdinalIgnoreCase)
    {
        "Company",
        "Product",
        "Copyright",
        "Trademark",
        "AssemblyVersion",
        "FileVersion",
        "AssemblyTitle",
        "AssemblyDescription",
        "AssemblyConfiguration",
        "AssemblyCompany",
        "AssemblyProduct",
        "AssemblyCopyright",
        "AssemblyTrademark",
        "ComVisible",
        "Guid",
        "NeutralResourcesLanguage"
    };

    public static readonly string[] AssemblyInfoFilePatterns = new[]
    {
        "AssemblyInfo.cs",
        "AssemblyInfo.vb",
        "GlobalAssemblyInfo.cs",
        "GlobalAssemblyInfo.vb",
        "SharedAssemblyInfo.cs",
        "SharedAssemblyInfo.vb",
        "CommonAssemblyInfo.cs",
        "CommonAssemblyInfo.vb"
    };

    // Specific imports that should always be removed (exact matches)
    // The IsVisualStudioSpecificImport method in SdkStyleProjectGenerator provides
    // additional keyword-based detection for more comprehensive removal
    public static readonly HashSet<string> ImportsToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "$(MSBuildToolsPath)\\Microsoft.CSharp.targets",
        "$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets",
        "$(MSBuildBinPath)\\Microsoft.CSharp.targets",
        "$(MSBuildBinPath)\\Microsoft.VisualBasic.targets",
        "$(MSBuildExtensionsPath)\\$(MSBuildToolsVersion)\\Microsoft.Common.props"
    };

    public static readonly HashSet<string> ProblematicTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "BeforeBuild",
        "AfterBuild"
    };

    public static readonly HashSet<string> ItemsToConvert = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reference",
        "ProjectReference",
        "PackageReference"
    };

    public static readonly HashSet<string> ItemsToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "BootstrapperPackage",
        "AppDesigner",
        "VisualStudio",
        "FlavorProperties",
        "VSToolsPath",
        "VisualStudioVersion",
        "FileUpgradeFlags",
        "OldToolsVersion",
        "UpgradeBackupLocation",
        "PublishUrl",
        "Install",
        "InstallFrom",
        "UpdateEnabled",
        "UpdateMode",
        "UpdateInterval",
        "UpdateIntervalUnits",
        "UpdatePeriodically",
        "UpdateRequired",
        "MapFileExtensions",
        "ApplicationRevision",
        "ApplicationVersion",
        "UseApplicationTrust",
        "BootstrapperEnabled"
    };

    // MSBuild evaluation artifacts that should never be copied to the migrated project
    public static readonly HashSet<string> MSBuildEvaluationArtifacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "SourceRoot",
        "GlobalAnalyzerConfigFiles",
        "AllDirectoriesAbove",
        "PropertyPageSchema",
        "PotentialEditorConfigFiles",
        "EditorConfigFiles",
        "ReferencePath",
        "ReferenceCopyLocalPaths",
        "RuntimeCopyLocalItems",
        "AnalyzerConfigFiles",
        "_GeneratedEditorConfigFiles",
        "_GlobalAnalyzerConfigFiles",
        "CollectedAnalyzerConfigFiles",
        "FinalAnalyzerConfigFiles",
        "MergedAnalyzerConfigFiles",
        "ActiveDebugFramework",
        "ProjectCapability",
        "CompilerVisibleProperty",
        "CompilerVisibleItemMetadata",
        "SupportedTargetFramework",
        "SdkSupportedTargetPlatformIdentifier",
        "SdkSupportedTargetPlatformVersion",
        "TargetPlatformIdentifier",
        "TargetPlatformVersion",
        "TargetPlatformMoniker",
        "_TargetFrameworkVersionWithoutV",
        "_DebugSymbolsProduced",
        "_DocumentationFileProduced",
        "_TargetPathItem",
        "_SourceItemsToCopyToOutputDirectory",
        "_SourceItemsToCopyToOutputDirectoryAlways",
        "_ResolvedProjectReferencePaths",
        "_MSBuildProjectReferenceExistent",
        "_ProjectReferencesWithExecutableExtensions",
        "CollectPackageReferences",
        "CollectPackageDownloads",
        "CollectCentralPackageVersions",
        "ImplicitConfigurationDefine",
        "AspNetCompilerPath",
        "RazorCompilerPath",
        "_AllDirectoriesAbove",
        "_DirectoriesAbove",
        "_ConfigurationFiles",
        "_ResolveComReferenceCache",
        "FileWrites",
        "FileWritesShareable",
        "SuggestedBindingRedirects",
        "IntermediateAssembly",
        "_DeploymentManifestEntryPoint",
        "ApplicationManifest",
        "_DeploymentManifestIconFile",
        "_ClickOnceDependencies",
        "_ClickOncePrerequisites",
        "_UnmanagedRegistrationCache",
        "_ResolveAssemblyReferenceResolvedFiles",
        "_PublishFiles",
        "_SatelliteAssemblyResourceFiles",
        "ReferenceSatellitePaths",
        "_OutputPathItem",
        "AppConfigFileDestination",
        "CopyUpToDateMarker",
        "_DebugSymbolsIntermediatePath",
        "_DebugSymbolsOutputPath",
        "_ExplicitReference",
        "PackageConflictOverrides",
        "DebugSymbolsProjectOutputGroupOutput",
        "BuiltProjectOutputGroupKeyOutput",
        "DeployManifest",
        "_ApplicationManifestFinal",
        "AppDesigner"
    };

    public static readonly HashSet<string> WpfWinFormsItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationDefinition",
        "Page",
        "Resource",
        "XamlAppdef",
        "DesignData",
        "DesignDataWithDesignTimeCreatableTypes",
        "EntityDeploy",
        "FontDefinition",
        "SplashScreen"
    };

    public static readonly HashSet<string> ImplicitlyIncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".vb",
        ".resx",
        ".settings",
        ".cshtml",
        ".vbhtml",
        ".razor"
    };
}