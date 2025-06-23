using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class CustomTargetAnalyzer
{
    private readonly ILogger<CustomTargetAnalyzer> _logger;

    // Common target patterns that can be auto-migrated
    private static readonly Dictionary<string, TargetMigrationPattern> KnownPatterns = new()
    {
        ["CopyFiles"] = new TargetMigrationPattern
        {
            TaskTypes = new[] { "Copy" },
            MigrationApproach = "Convert to Content/None items with CopyToOutputDirectory",
            CanAutoMigrate = true
        },
        ["DeleteFiles"] = new TargetMigrationPattern
        {
            TaskTypes = new[] { "Delete" },
            MigrationApproach = "Convert to Delete task in appropriate target",
            CanAutoMigrate = true
        },
        ["RunTool"] = new TargetMigrationPattern
        {
            TaskTypes = new[] { "Exec" },
            MigrationApproach = "Preserve as custom target with updated BeforeTargets/AfterTargets",
            CanAutoMigrate = true
        },
        ["GenerateCode"] = new TargetMigrationPattern
        {
            TaskTypes = new[] { "Exec", "WriteLinesToFile", "GenerateResource" },
            MigrationApproach = "Preserve as custom target, consider T4 or source generators",
            CanAutoMigrate = false
        }
    };

    public CustomTargetAnalyzer(ILogger<CustomTargetAnalyzer> logger)
    {
        _logger = logger;
    }

    public List<CustomTargetAnalysis> AnalyzeTargets(Project project)
    {
        var analyses = new List<CustomTargetAnalysis>();

        foreach (var target in project.Xml.Targets)
        {
            var analysis = AnalyzeTarget(target);
            analyses.Add(analysis);
        }

        return analyses;
    }

    private CustomTargetAnalysis AnalyzeTarget(ProjectTargetElement target)
    {
        var analysis = new CustomTargetAnalysis
        {
            TargetName = target.Name,
            BeforeTargets = target.BeforeTargets,
            AfterTargets = target.AfterTargets,
            DependsOnTargets = target.DependsOnTargets,
            Condition = target.Condition
        };

        // Analyze tasks within the target
        foreach (var child in target.Children)
        {
            if (child is ProjectTaskElement task)
            {
                analysis.Tasks.Add($"{task.Name}: {GetTaskSummary(task)}");
            }
        }

        // Determine complexity
        analysis.Complexity = DetermineComplexity(target);

        // Check if it's a standard target that SDK handles
        if (IsStandardTarget(target.Name))
        {
            analysis.CanAutoMigrate = false;
            analysis.ManualMigrationGuidance = GetStandardTargetMigrationGuidance(target.Name);
            analysis.SuggestedCode = GenerateSdkStyleTarget(target);
        }
        else
        {
            // Check against known patterns
            var pattern = IdentifyPattern(target);
            if (pattern != null)
            {
                analysis.CanAutoMigrate = pattern.CanAutoMigrate;
                analysis.AutoMigrationApproach = pattern.MigrationApproach;
                if (!pattern.CanAutoMigrate)
                {
                    analysis.ManualMigrationGuidance = pattern.MigrationApproach;
                    analysis.SuggestedCode = GenerateSdkStyleTarget(target);
                }
            }
            else
            {
                // Unknown pattern
                analysis.CanAutoMigrate = false;
                analysis.ManualMigrationGuidance = "Custom target requires manual review and migration";
                analysis.SuggestedCode = GenerateSdkStyleTarget(target);
            }
        }

        return analysis;
    }

    private string GetTaskSummary(ProjectTaskElement task)
    {
        var summary = new StringBuilder();

        // Get key parameters
        var command = task.GetParameter("Command");
        var files = task.GetParameter("SourceFiles") ?? task.GetParameter("Files");
        var destination = task.GetParameter("DestinationFolder") ?? task.GetParameter("DestinationFiles");

        if (!string.IsNullOrEmpty(command))
        {
            summary.Append($"Command='{TruncateString(command, 50)}'");
        }
        if (!string.IsNullOrEmpty(files))
        {
            if (summary.Length > 0) summary.Append(", ");
            summary.Append($"Files='{TruncateString(files, 30)}'");
        }
        if (!string.IsNullOrEmpty(destination))
        {
            if (summary.Length > 0) summary.Append(", ");
            summary.Append($"Destination='{TruncateString(destination, 30)}'");
        }

        return summary.ToString();
    }

    private string TruncateString(string str, int maxLength)
    {
        if (str.Length <= maxLength) return str;
        return str.Substring(0, maxLength - 3) + "...";
    }

    private TargetComplexity DetermineComplexity(ProjectTargetElement target)
    {
        var taskCount = target.Children.OfType<ProjectTaskElement>().Count();
        var hasComplexConditions = target.Children.Any(c => !string.IsNullOrWhiteSpace(c.Condition));
        var hasPropertyGroups = target.Children.Any(c => c.ElementName == "PropertyGroup");
        var hasItemGroups = target.Children.Any(c => c.ElementName == "ItemGroup");

        if (taskCount == 0)
            return TargetComplexity.Simple;

        if (taskCount == 1 && !hasComplexConditions && !hasPropertyGroups && !hasItemGroups)
            return TargetComplexity.Simple;

        if (taskCount <= 3 && !hasPropertyGroups && !hasItemGroups)
            return TargetComplexity.Moderate;

        if (taskCount > 5 || (hasPropertyGroups && hasItemGroups))
            return TargetComplexity.VeryComplex;

        return TargetComplexity.Complex;
    }

    private bool IsStandardTarget(string targetName)
    {
        var standardTargets = new[]
        {
            "BeforeBuild", "AfterBuild", "BeforeCompile", "AfterCompile",
            "BeforePublish", "AfterPublish", "BeforeResolveReferences", "AfterResolveReferences",
            "BeforeClean", "AfterClean", "CoreCompile", "Compile", "Build"
        };

        return standardTargets.Contains(targetName, StringComparer.OrdinalIgnoreCase);
    }

    private string GetStandardTargetMigrationGuidance(string targetName)
    {
        return targetName.ToLower() switch
        {
            "beforebuild" or "afterbuild" =>
                "Use a custom target with BeforeTargets='Build' or AfterTargets='Build'",
            "beforecompile" or "aftercompile" =>
                "Use a custom target with BeforeTargets='CoreCompile' or AfterTargets='CoreCompile'",
            "beforepublish" or "afterpublish" =>
                "Use a custom target with BeforeTargets='Publish' or AfterTargets='Publish'",
            "beforeresolvereferences" or "afterresolvereferences" =>
                "Use a custom target with BeforeTargets='ResolveReferences' or AfterTargets='ResolveReferences'",
            _ => "This target is handled by the SDK, create a custom target with appropriate BeforeTargets/AfterTargets"
        };
    }

    private TargetMigrationPattern? IdentifyPattern(ProjectTargetElement target)
    {
        var tasks = target.Children.OfType<ProjectTaskElement>().ToList();
        if (!tasks.Any()) return null;

        // Check for simple copy pattern
        if (tasks.All(t => t.Name == "Copy"))
        {
            return KnownPatterns["CopyFiles"];
        }

        // Check for delete pattern
        if (tasks.All(t => t.Name == "Delete"))
        {
            return KnownPatterns["DeleteFiles"];
        }

        // Check for exec pattern
        if (tasks.All(t => t.Name == "Exec"))
        {
            return KnownPatterns["RunTool"];
        }

        // Check for code generation pattern
        if (tasks.Any(t => t.Name == "Exec" || t.Name == "WriteLinesToFile" || t.Name == "GenerateResource"))
        {
            return KnownPatterns["GenerateCode"];
        }

        return null;
    }

    private string GenerateSdkStyleTarget(ProjectTargetElement target)
    {
        var sb = new StringBuilder();

        // Generate a new target name if it's a standard one
        var targetName = IsStandardTarget(target.Name) ? $"Custom{target.Name}" : target.Name;

        sb.AppendLine($"<Target Name=\"{targetName}\"");

        // Add appropriate BeforeTargets/AfterTargets
        if (IsStandardTarget(target.Name))
        {
            var sdkTarget = GetSdkTargetName(target.Name);
            if (target.Name.StartsWith("Before", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"        BeforeTargets=\"{sdkTarget}\"");
            }
            else if (target.Name.StartsWith("After", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"        AfterTargets=\"{sdkTarget}\"");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                sb.AppendLine($"        BeforeTargets=\"{target.BeforeTargets}\"");
            if (!string.IsNullOrEmpty(target.AfterTargets))
                sb.AppendLine($"        AfterTargets=\"{target.AfterTargets}\"");
        }

        if (!string.IsNullOrEmpty(target.DependsOnTargets))
            sb.AppendLine($"        DependsOnTargets=\"{target.DependsOnTargets}\"");

        if (!string.IsNullOrEmpty(target.Condition))
            sb.AppendLine($"        Condition=\"{target.Condition}\"");

        sb.AppendLine(">");

        // Add tasks
        foreach (var child in target.Children)
        {
            if (child is ProjectTaskElement task)
            {
                sb.AppendLine($"  <{task.Name}");
                foreach (var param in task.Parameters)
                {
                    sb.AppendLine($"    {param.Key}=\"{param.Value}\"");
                }
                if (!string.IsNullOrEmpty(task.Condition))
                {
                    sb.AppendLine($"    Condition=\"{task.Condition}\"");
                }
                sb.AppendLine("  />");
            }
        }

        sb.AppendLine("</Target>");

        return sb.ToString();
    }

    private string GetSdkTargetName(string legacyTargetName)
    {
        return legacyTargetName.ToLower() switch
        {
            "beforebuild" or "afterbuild" => "Build",
            "beforecompile" or "aftercompile" => "CoreCompile",
            "beforepublish" or "afterpublish" => "Publish",
            "beforeresolvereferences" or "afterresolvereferences" => "ResolveReferences",
            "beforeclean" or "afterclean" => "Clean",
            _ => "Build"
        };
    }

    public XElement? MigrateTarget(ProjectTargetElement target, CustomTargetAnalysis analysis)
    {
        if (!analysis.CanAutoMigrate)
            return null;

        var targetName = IsStandardTarget(target.Name) ? $"Custom{target.Name}" : target.Name;
        var newTarget = new XElement("Target", new XAttribute("Name", targetName));

        // Set appropriate BeforeTargets/AfterTargets
        if (IsStandardTarget(target.Name))
        {
            var sdkTarget = GetSdkTargetName(target.Name);
            if (target.Name.StartsWith("Before", StringComparison.OrdinalIgnoreCase))
            {
                newTarget.Add(new XAttribute("BeforeTargets", sdkTarget));
            }
            else if (target.Name.StartsWith("After", StringComparison.OrdinalIgnoreCase))
            {
                newTarget.Add(new XAttribute("AfterTargets", sdkTarget));
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(target.BeforeTargets))
                newTarget.Add(new XAttribute("BeforeTargets", target.BeforeTargets));
            if (!string.IsNullOrEmpty(target.AfterTargets))
                newTarget.Add(new XAttribute("AfterTargets", target.AfterTargets));
        }

        if (!string.IsNullOrEmpty(target.DependsOnTargets))
            newTarget.Add(new XAttribute("DependsOnTargets", target.DependsOnTargets));

        if (!string.IsNullOrEmpty(target.Condition))
            newTarget.Add(new XAttribute("Condition", target.Condition));

        // Migrate tasks
        foreach (var child in target.Children)
        {
            if (child is ProjectTaskElement task)
            {
                var newTask = new XElement(task.Name);
                foreach (var param in task.Parameters)
                {
                    newTask.Add(new XAttribute(param.Key, param.Value));
                }
                if (!string.IsNullOrEmpty(task.Condition))
                {
                    newTask.Add(new XAttribute("Condition", task.Condition));
                }
                newTarget.Add(newTask);
            }
        }

        return newTarget;
    }
}

public class TargetMigrationPattern
{
    public string[] TaskTypes { get; set; } = Array.Empty<string>();
    public string MigrationApproach { get; set; } = string.Empty;
    public bool CanAutoMigrate { get; set; }
}