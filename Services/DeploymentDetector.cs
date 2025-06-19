using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class DeploymentDetector
{
    private readonly ILogger<DeploymentDetector> _logger;
    
    private static readonly string[] ClickOnceProperties = new[]
    {
        "PublishUrl",
        "InstallUrl", 
        "UpdateUrl",
        "ApplicationVersion",
        "ApplicationRevision",
        "UpdateEnabled",
        "UpdateMode",
        "UpdateInterval",
        "UpdateIntervalUnits",
        "UpdatePeriodically",
        "UpdateRequired",
        "MapFileExtensions",
        "InstallFrom",
        "MinimumRequiredVersion",
        "PublisherName",
        "SuiteName",
        "CreateWebPageOnPublish",
        "WebPage",
        "TrustUrlParameters",
        "CreateDesktopShortcut",
        "PublishWizardCompleted",
        "BootstrapperEnabled",
        "IsWebBootstrapper",
        "UseApplicationTrust",
        "PublishProvider",
        "PublishSingleFile",
        "TargetZone",
        "GenerateManifests",
        "SignManifests",
        "ManifestCertificateThumbprint",
        "ManifestKeyFile",
        "SignAssembly",
        "DelaySign",
        "AssemblyOriginatorKeyFile"
    };

    public DeploymentDetector(ILogger<DeploymentDetector> logger)
    {
        _logger = logger;
    }

    public DeploymentInfo DetectDeploymentMethod(Project project)
    {
        var result = new DeploymentInfo();
        
        // Check for ClickOnce
        var clickOnceProps = new Dictionary<string, string>();
        foreach (var prop in ClickOnceProperties)
        {
            var value = project.GetPropertyValue(prop);
            if (!string.IsNullOrEmpty(value))
            {
                clickOnceProps[prop] = value;
            }
        }
        
        if (clickOnceProps.Any())
        {
            result.UsesClickOnce = true;
            result.ClickOnceProperties = clickOnceProps;
            
            _logger.LogWarning("ClickOnce deployment detected with {Count} properties", clickOnceProps.Count);
            
            // Determine ClickOnce configuration
            if (clickOnceProps.TryGetValue("UpdateEnabled", out var updateEnabled) && 
                updateEnabled.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result.ClickOnceFeatures.Add("Auto-update enabled");
            }
            
            if (clickOnceProps.TryGetValue("IsWebBootstrapper", out var isWeb) &&
                isWeb.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result.ClickOnceFeatures.Add("Web bootstrapper");
            }
            
            if (clickOnceProps.ContainsKey("MinimumRequiredVersion"))
            {
                result.ClickOnceFeatures.Add("Minimum version requirement");
            }
            
            if (clickOnceProps.TryGetValue("SignManifests", out var signManifests) &&
                signManifests.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result.ClickOnceFeatures.Add("Signed manifests");
            }
        }
        
        // Check for other deployment indicators
        if (project.Items.Any(i => i.ItemType == "BootstrapperPackage"))
        {
            result.HasBootstrapperPackages = true;
        }
        
        if (project.Items.Any(i => i.ItemType == "PublishFile"))
        {
            result.HasPublishFiles = true;
        }
        
        // Check for WiX or other installer projects
        if (project.Items.Any(i => i.ItemType == "Reference" && 
            i.EvaluatedInclude.Contains("WixToolset", StringComparison.OrdinalIgnoreCase)))
        {
            result.UsesWiX = true;
        }
        
        return result;
    }
    
    public void AddDeploymentWarnings(DeploymentInfo deploymentInfo, MigrationResult result)
    {
        if (deploymentInfo.UsesClickOnce)
        {
            var warning = new StringBuilder();
            warning.AppendLine("⚠️ CRITICAL: ClickOnce Deployment Detected");
            warning.AppendLine();
            warning.AppendLine("This project uses ClickOnce deployment, which has LIMITED support in .NET 5+:");
            warning.AppendLine("- No programmatic access to ApplicationDeployment APIs");
            warning.AppendLine("- Limited to Visual Studio publish (no MSBuild publish)");
            warning.AppendLine("- Requires .NET Framework launcher for .NET Core apps");
            warning.AppendLine();
            warning.AppendLine("Detected ClickOnce features:");
            foreach (var feature in deploymentInfo.ClickOnceFeatures)
            {
                warning.AppendLine($"  - {feature}");
            }
            warning.AppendLine();
            warning.AppendLine("RECOMMENDED ALTERNATIVES:");
            warning.AppendLine("1. MSIX (Recommended by Microsoft)");
            warning.AppendLine("   - Modern packaging format");
            warning.AppendLine("   - Auto-update support");
            warning.AppendLine("   - Works with .NET 5+");
            warning.AppendLine("   - See: https://docs.microsoft.com/windows/msix/");
            warning.AppendLine();
            warning.AppendLine("2. Self-contained deployment");
            warning.AppendLine("   - Single file executables");
            warning.AppendLine("   - No runtime dependencies");
            warning.AppendLine("   - Use: dotnet publish -r win-x64 --self-contained");
            warning.AppendLine();
            warning.AppendLine("3. Third-party solutions");
            warning.AppendLine("   - Squirrel.Windows");
            warning.AppendLine("   - AutoUpdater.NET");
            warning.AppendLine();
            warning.AppendLine("ACTION REQUIRED: ClickOnce properties will be removed during migration.");
            warning.AppendLine("You must implement an alternative deployment strategy.");
            
            result.Warnings.Add(warning.ToString());
            
            // Add removed properties
            foreach (var prop in deploymentInfo.ClickOnceProperties)
            {
                result.RemovedElements.Add($"ClickOnce property: {prop.Key} = {prop.Value}");
            }
        }
        
        if (deploymentInfo.HasBootstrapperPackages)
        {
            result.Warnings.Add("Bootstrapper packages detected. These are not supported in SDK-style projects. Consider using self-contained deployment or MSIX prerequisites.");
        }
        
        if (deploymentInfo.UsesWiX)
        {
            result.Warnings.Add("WiX installer detected. Ensure WiX v4+ for .NET 5+ compatibility.");
        }
    }
}

public class DeploymentInfo
{
    public bool UsesClickOnce { get; set; }
    public Dictionary<string, string> ClickOnceProperties { get; set; } = new();
    public List<string> ClickOnceFeatures { get; set; } = new();
    public bool HasBootstrapperPackages { get; set; }
    public bool HasPublishFiles { get; set; }
    public bool UsesWiX { get; set; }
}