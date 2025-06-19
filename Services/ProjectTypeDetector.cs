using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class ProjectTypeDetector
{
    private readonly ILogger<ProjectTypeDetector> _logger;
    
    // Common project type GUIDs
    private static readonly Dictionary<string, ProjectType> ProjectTypeGuids = new()
    {
        { "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}", ProjectType.CSharp },
        { "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}", ProjectType.VBNet },
        { "{F2A71F9B-5D33-465A-A702-920D77279786}", ProjectType.FSharp },
        { "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}", ProjectType.CPlusPlus },
        { "{60DC8134-EBA5-43B8-BCC9-BB4BC16C2548}", ProjectType.WPF },
        { "{C089C8C0-30E0-4E22-80C0-CE093F111A43}", ProjectType.WindowsPhone },
        { "{349C5851-65DF-11DA-9384-00065B846F21}", ProjectType.WebApplication },
        { "{E24C65DC-7377-472B-9ABA-BC803B73C61A}", ProjectType.WebSite },
        { "{3AC096D0-A1C2-E12C-1390-A8335801FDAB}", ProjectType.Test },
        { "{A1591282-1198-4647-A2B1-27E5FF5F6F3B}", ProjectType.Silverlight },
        { "{603C0E0B-DB56-11DC-BE95-000D561079B0}", ProjectType.AspNetMvc },
        { "{BC8A1FFA-BEE3-4634-8014-F334798102B3}", ProjectType.WindowsStore },
        { "{A5A43C5B-DE2A-4C0C-9213-0A381AF9435A}", ProjectType.UAP },
        { "{CC5FD16D-436D-48AD-A40C-5A424C6E3E79}", ProjectType.CloudService },
        { "{2150E333-8FDC-42A3-9474-1A3956D46DE8}", ProjectType.SolutionFolder }
    };

    public ProjectTypeDetector(ILogger<ProjectTypeDetector> logger)
    {
        _logger = logger;
    }

    public ProjectTypeInfo DetectProjectType(Project project)
    {
        var result = new ProjectTypeInfo
        {
            ProjectPath = project.FullPath
        };

        // Check project GUIDs
        var projectTypeGuidsProperty = project.GetPropertyValue("ProjectTypeGuids");
        if (!string.IsNullOrEmpty(projectTypeGuidsProperty))
        {
            var guids = projectTypeGuidsProperty.Split(';')
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrEmpty(g));
                
            foreach (var guid in guids)
            {
                if (ProjectTypeGuids.TryGetValue(guid.ToUpperInvariant(), out var projectType))
                {
                    result.DetectedTypes.Add(projectType);
                }
            }
        }

        // Check for C++/CLI
        if (result.DetectedTypes.Contains(ProjectType.CPlusPlus))
        {
            result.CanMigrate = false;
            result.MigrationBlocker = "C++/CLI projects cannot be migrated to SDK-style format";
            result.SuggestedSdk = null;
            return result;
        }

        // Detect by package references
        var packageRefs = project.Items
            .Where(i => i.ItemType == "PackageReference")
            .Select(i => i.EvaluatedInclude)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Azure Functions detection
        if (packageRefs.Contains("Microsoft.NET.Sdk.Functions") || 
            packageRefs.Contains("Microsoft.Azure.Functions.Extensions") ||
            packageRefs.Contains("Microsoft.Azure.WebJobs"))
        {
            result.DetectedTypes.Add(ProjectType.AzureFunctions);
            result.SuggestedSdk = "Microsoft.NET.Sdk";
            result.RequiresSpecialHandling = true;
            result.SpecialHandlingNotes.Add("Azure Functions projects require Microsoft.NET.Sdk with Microsoft.NET.Sdk.Functions PackageReference");
            return result;
        }

        // Worker Service detection
        if (packageRefs.Contains("Microsoft.Extensions.Hosting") ||
            packageRefs.Contains("Microsoft.Extensions.Hosting.WindowsServices"))
        {
            result.DetectedTypes.Add(ProjectType.WorkerService);
            result.SuggestedSdk = "Microsoft.NET.Sdk.Worker";
            return result;
        }

        // Test project detection
        if (packageRefs.Any(p => p.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                                p.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
                                p.Contains("mstest", StringComparison.OrdinalIgnoreCase)) ||
            packageRefs.Contains("Microsoft.NET.Test.Sdk"))
        {
            result.DetectedTypes.Add(ProjectType.Test);
            result.SuggestedSdk = "Microsoft.NET.Sdk";
            result.RequiredPackageReferences.Add("Microsoft.NET.Test.Sdk");
            return result;
        }

        // WPF/WinForms detection
        var hasWpfItems = project.Items.Any(i => 
            i.ItemType == "Page" || i.ItemType == "ApplicationDefinition" || 
            i.ItemType == "Resource" && i.EvaluatedInclude.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
            
        var hasWinFormsReferences = project.Items.Any(i => 
            i.ItemType == "Reference" && 
            (i.EvaluatedInclude.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.StartsWith("System.Drawing", StringComparison.OrdinalIgnoreCase)));
             
        if (hasWpfItems || hasWinFormsReferences)
        {
            if (hasWpfItems) result.DetectedTypes.Add(ProjectType.WPF);
            if (hasWinFormsReferences) result.DetectedTypes.Add(ProjectType.WinForms);
            
            // Check target framework to determine SDK
            var targetFramework = project.GetPropertyValue("TargetFrameworkVersion");
            if (targetFramework.StartsWith("v4") || targetFramework.StartsWith("net4"))
            {
                result.SuggestedSdk = "Microsoft.NET.Sdk.WindowsDesktop";
            }
            else
            {
                result.SuggestedSdk = "Microsoft.NET.Sdk";
                result.RequiredProperties["UseWPF"] = hasWpfItems ? "true" : null;
                result.RequiredProperties["UseWindowsForms"] = hasWinFormsReferences ? "true" : null;
            }
            return result;
        }

        // Web project detection
        var hasWebContent = project.Items.Any(i =>
            (i.ItemType == "Content" || i.ItemType == "None") &&
            (i.EvaluatedInclude.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.EndsWith(".ascx", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.Equals("web.config", StringComparison.OrdinalIgnoreCase)));
             
        var hasWebReferences = project.Items.Any(i =>
            i.ItemType == "Reference" &&
            (i.EvaluatedInclude.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.StartsWith("Microsoft.AspNet", StringComparison.OrdinalIgnoreCase)));
             
        if (hasWebContent || hasWebReferences)
        {
            result.DetectedTypes.Add(ProjectType.WebApplication);
            result.SuggestedSdk = "Microsoft.NET.Sdk.Web";
            return result;
        }

        // Default to library/console
        var outputType = project.GetPropertyValue("OutputType");
        if (outputType?.Equals("Exe", StringComparison.OrdinalIgnoreCase) == true ||
            outputType?.Equals("WinExe", StringComparison.OrdinalIgnoreCase) == true)
        {
            result.DetectedTypes.Add(ProjectType.Console);
        }
        else
        {
            result.DetectedTypes.Add(ProjectType.Library);
        }
        
        result.SuggestedSdk = "Microsoft.NET.Sdk";
        return result;
    }
}

public class ProjectTypeInfo
{
    public string ProjectPath { get; set; } = string.Empty;
    public List<ProjectType> DetectedTypes { get; set; } = new();
    public string? SuggestedSdk { get; set; }
    public bool CanMigrate { get; set; } = true;
    public string? MigrationBlocker { get; set; }
    public bool RequiresSpecialHandling { get; set; }
    public List<string> SpecialHandlingNotes { get; set; } = new();
    public List<string> RequiredPackageReferences { get; set; } = new();
    public Dictionary<string, string?> RequiredProperties { get; set; } = new();
}