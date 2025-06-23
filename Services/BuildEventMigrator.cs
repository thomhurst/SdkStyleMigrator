using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class BuildEventMigrator
{
    private readonly ILogger<BuildEventMigrator> _logger;

    public BuildEventMigrator(ILogger<BuildEventMigrator> logger)
    {
        _logger = logger;
    }

    public void MigrateBuildEvents(Project legacyProject, XElement newProjectRoot, MigrationResult result)
    {
        var preBuildEvent = legacyProject.GetPropertyValue("PreBuildEvent");
        var postBuildEvent = legacyProject.GetPropertyValue("PostBuildEvent");
        var runPostBuildEvent = legacyProject.GetPropertyValue("RunPostBuildEvent");

        if (!string.IsNullOrWhiteSpace(preBuildEvent))
        {
            var preBuildTarget = new XElement("Target",
                new XAttribute("Name", "PreBuild"),
                new XAttribute("BeforeTargets", "PreBuildEvent"),
                new XElement("Exec",
                    new XAttribute("Command", preBuildEvent)));

            newProjectRoot.Add(preBuildTarget);

            result.RemovedElements.Add("PreBuildEvent property (converted to Target)");
            _logger.LogInformation("Migrated PreBuildEvent to Target with BeforeTargets='PreBuildEvent'");

            // Add guidance
            result.Warnings.Add($"Pre-build event migrated to MSBuild target. Review the command for path and environment variable compatibility: {preBuildEvent}");
        }

        if (!string.IsNullOrWhiteSpace(postBuildEvent))
        {
            var postBuildTarget = new XElement("Target",
                new XAttribute("Name", "PostBuild"),
                new XAttribute("AfterTargets", "PostBuildEvent"));

            // Handle RunPostBuildEvent condition
            if (!string.IsNullOrWhiteSpace(runPostBuildEvent) && runPostBuildEvent != "Always")
            {
                var condition = runPostBuildEvent switch
                {
                    "OnBuildSuccess" => "'$(BuildingInsideVisualStudio)' != 'true' Or '$(RunPostBuildEvent)' == 'OnBuildSuccess'",
                    "OnOutputUpdated" => "'$(BuildingInsideVisualStudio)' != 'true' Or '$(RunPostBuildEvent)' == 'OnOutputUpdated'",
                    _ => null
                };

                if (condition != null)
                {
                    postBuildTarget.Add(new XAttribute("Condition", condition));
                }
            }

            postBuildTarget.Add(new XElement("Exec",
                new XAttribute("Command", postBuildEvent)));

            newProjectRoot.Add(postBuildTarget);

            result.RemovedElements.Add("PostBuildEvent property (converted to Target)");
            _logger.LogInformation("Migrated PostBuildEvent to Target with AfterTargets='PostBuildEvent'");

            // Add guidance
            result.Warnings.Add($"Post-build event migrated to MSBuild target. Review the command for path and environment variable compatibility: {postBuildEvent}");
        }

        // Look for custom targets with Exec tasks
        MigrateCustomExecTargets(legacyProject, newProjectRoot, result);
    }

    private void MigrateCustomExecTargets(Project legacyProject, XElement newProjectRoot, MigrationResult result)
    {
        foreach (var target in legacyProject.Xml.Targets)
        {
            var hasExecTasks = target.Children.Any(c => c.ElementName == "Exec");
            if (!hasExecTasks) continue;

            // Check if this is a common target that SDK handles
            var commonTargets = new[]
            {
                "BeforeBuild", "AfterBuild", "BeforeCompile", "AfterCompile",
                "BeforePublish", "AfterPublish", "BeforeResolveReferences", "AfterResolveReferences"
            };

            if (commonTargets.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Extract Exec commands and provide specific guidance
                var execCommands = target.Children
                    .Where(c => c.ElementName == "Exec")
                    .Select(e => (e as Microsoft.Build.Construction.ProjectTaskElement)?.GetParameter("Command"))
                    .Where(cmd => !string.IsNullOrEmpty(cmd))
                    .ToList();

                if (execCommands.Any())
                {
                    var migrationGuidance = new RemovedMSBuildElement
                    {
                        ElementType = "Target",
                        Name = target.Name,
                        XmlContent = GetTargetXml(target, execCommands),
                        Condition = target.Condition,
                        Reason = "Common build target with Exec tasks",
                        SuggestedMigrationPath = GetMigrationExample(target.Name, execCommands)
                    };

                    result.RemovedMSBuildElements.Add(migrationGuidance);
                    result.Warnings.Add($"Target '{target.Name}' contained Exec commands that need manual migration. See removed elements for guidance.");
                }
            }
        }
    }

    private string GetTargetXml(Microsoft.Build.Construction.ProjectTargetElement target, List<string> execCommands)
    {
        var xml = $"<Target Name=\"{target.Name}\"";
        if (!string.IsNullOrEmpty(target.Condition))
            xml += $" Condition=\"{target.Condition}\"";
        xml += ">\n";

        foreach (var cmd in execCommands)
        {
            xml += $"  <Exec Command=\"{cmd}\" />\n";
        }

        xml += "</Target>";
        return xml;
    }

    private string GetMigrationExample(string targetName, List<string> execCommands)
    {
        var newTargetName = $"Custom{targetName}";
        var beforeOrAfter = targetName.StartsWith("Before") ? "BeforeTargets" : "AfterTargets";
        var sdkTarget = targetName switch
        {
            "BeforeBuild" or "AfterBuild" => "Build",
            "BeforeCompile" or "AfterCompile" => "CoreCompile",
            "BeforePublish" or "AfterPublish" => "Publish",
            "BeforeResolveReferences" or "AfterResolveReferences" => "ResolveReferences",
            _ => "Build"
        };

        var example = $"<Target Name=\"{newTargetName}\" {beforeOrAfter}=\"{sdkTarget}\">\n";
        foreach (var cmd in execCommands)
        {
            example += $"  <Exec Command=\"{cmd}\" />\n";
        }
        example += "</Target>\n\n";
        example += "Note: Review commands for:\n";
        example += "- Path separators (use / or MSBuild properties)\n";
        example += "- Environment variables\n";
        example += "- Working directory assumptions\n";
        example += "- Consider replacing with MSBuild tasks (Copy, Delete, etc.)";

        return example;
    }
}