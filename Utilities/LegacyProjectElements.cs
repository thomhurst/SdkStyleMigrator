namespace SdkMigrator.Utilities;

public static class LegacyProjectElements
{
    public static readonly HashSet<string> PropertiesToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        // "ProjectGuid", // Now preserved for solution compatibility
        "ProjectTypeGuids", 
        "TargetFrameworkProfile",
        "FileAlignment",
        "AppDesignerFolder",
        "RootNamespace",
        "AssemblyName",
        "SchemaVersion",
        "ProductVersion",
        "FileVersion",
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
    
    // Properties to preserve (even though not strictly needed in SDK-style)
    public static readonly HashSet<string> PropertiesToPreserve = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectGuid", // Needed for solution file compatibility
        "SignAssembly",
        "AssemblyOriginatorKeyFile",
        "DelaySign",
        "StrongNameKeyFile"
    };

    // Assembly properties that can be moved to Directory.Build.props
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

    // Common assembly info file patterns
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

    public static readonly HashSet<string> ImportsToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "$(MSBuildToolsPath)\\Microsoft.CSharp.targets",
        "$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets",
        "$(MSBuildBinPath)\\Microsoft.CSharp.targets",
        "$(MSBuildBinPath)\\Microsoft.VisualBasic.targets",
        "$(VSToolsPath)\\TeamTest\\Microsoft.TestTools.targets",
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
        "FlavorProperties"
    };
    
    // WPF/WinForms specific item types that need special handling
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
    
    // File extensions that are implicitly included in SDK-style projects
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