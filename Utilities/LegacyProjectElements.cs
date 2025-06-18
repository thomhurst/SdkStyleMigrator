namespace SdkMigrator.Utilities;

public static class LegacyProjectElements
{
    public static readonly HashSet<string> PropertiesToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectGuid",
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
}