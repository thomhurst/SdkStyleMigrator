using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;
using SdkMigrator.Abstractions;
using System.Text.RegularExpressions;

namespace SdkMigrator.Services;

public class BuildEventMigrator : IBuildEventMigrator
{
    private readonly ILogger<BuildEventMigrator> _logger;
    
    // Common build event patterns and their MSBuild task replacements
    private static readonly Dictionary<string, string> CommandPatterns = new()
    {
        { @"xcopy\s+/[ysiqreh]+\s*", "Use <Copy> task with SkipUnchangedFiles='true' and Retries='3'" },
        { @"copy\s+/[yb]+\s*", "Use <Copy> task" },
        { @"del\s+/[fsq]+\s*", "Use <Delete> task" },
        { @"rmdir\s+/[sq]+\s*", "Use <RemoveDir> task" },
        { @"mkdir\s+", "Use <MakeDir> task" },
        { @"echo\s+", "Use <Message> task with Importance='High'" },
        { @"call\s+", "Consider using <MSBuild> or <Exec> task" },
        { @"if\s+exist\s+", "Use Condition attribute on tasks" },
        { @"if\s+not\s+exist\s+", "Use Condition='!Exists(...)'" },
        { @"for\s+/[rfd]\s+", "Use <ItemGroup> with metadata and transforms" }
    };

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
            var preBuildTarget = CreateBuildEventTarget("PreBuild", preBuildEvent, true, result);
            newProjectRoot.Add(preBuildTarget);

            result.RemovedElements.Add("PreBuildEvent property (converted to Target)");
            _logger.LogInformation("Migrated PreBuildEvent to Target with BeforeTargets='PreBuildEvent'");
        }

        if (!string.IsNullOrWhiteSpace(postBuildEvent))
        {
            var postBuildTarget = CreateBuildEventTarget("PostBuild", postBuildEvent, false, result);

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

            newProjectRoot.Add(postBuildTarget);

            result.RemovedElements.Add("PostBuildEvent property (converted to Target)");
            _logger.LogInformation("Migrated PostBuildEvent to Target with AfterTargets='PostBuildEvent'");
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
                    .Select(cmd => cmd!)
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

    private XElement CreateBuildEventTarget(string targetName, string command, bool isPreBuild, MigrationResult result)
    {
        var target = new XElement("Target",
            new XAttribute("Name", targetName),
            new XAttribute(isPreBuild ? "BeforeTargets" : "AfterTargets", isPreBuild ? "PreBuildEvent" : "PostBuildEvent"));

        // Handle multi-line commands
        var commands = ParseMultiLineCommand(command);
        
        if (commands.Count == 1)
        {
            // Single command - analyze and add recommendations
            var analysis = AnalyzeCommand(commands[0]);
            if (analysis.HasRecommendations)
            {
                result.Warnings.Add($"{targetName}: {analysis.Recommendation}");
            }
            
            target.Add(new XElement("Exec", 
                new XAttribute("Command", NormalizeCommand(commands[0])),
                new XAttribute("WorkingDirectory", "$(MSBuildProjectDirectory)")));
        }
        else
        {
            // Multiple commands - create proper target with error handling
            foreach (var cmd in commands)
            {
                var analysis = AnalyzeCommand(cmd);
                if (analysis.HasRecommendations)
                {
                    result.Warnings.Add($"{targetName}: {analysis.Recommendation}");
                }

                var execElement = new XElement("Exec",
                    new XAttribute("Command", NormalizeCommand(cmd)),
                    new XAttribute("WorkingDirectory", "$(MSBuildProjectDirectory)"));

                // Add continue on error for non-critical commands
                if (analysis.IsContinuable)
                {
                    execElement.Add(new XAttribute("ContinueOnError", "true"));
                }

                target.Add(execElement);
            }
        }

        return target;
    }

    private List<string> ParseMultiLineCommand(string command)
    {
        var commands = new List<string>();
        
        // Split by common command separators, preserving quotes
        var lines = Regex.Split(command, @"(?<![""'])(?:&&|\r?\n)(?![""'])");
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                commands.Add(trimmed);
            }
        }

        return commands;
    }

    private (bool HasRecommendations, string Recommendation, bool IsContinuable) AnalyzeCommand(string command)
    {
        var recommendations = new List<string>();
        var isContinuable = false;

        // Check for common patterns
        foreach (var pattern in CommandPatterns)
        {
            if (Regex.IsMatch(command, pattern.Key, RegexOptions.IgnoreCase))
            {
                recommendations.Add(pattern.Value);
            }
        }

        // Check for path issues
        if (command.Contains("$(TargetPath)") || command.Contains("$(TargetDir)"))
        {
            recommendations.Add("Ensure output paths are compatible with SDK-style projects");
        }

        // Check for hardcoded paths
        if (Regex.IsMatch(command, @"[cC]:\\|\\\\"))
        {
            recommendations.Add("Replace hardcoded paths with MSBuild properties");
        }

        // Check for environment variables
        if (command.Contains("%") && command.IndexOf('%', command.IndexOf('%') + 1) > 0)
        {
            recommendations.Add("Consider using MSBuild properties instead of environment variables");
        }

        // Echo and non-critical commands can continue on error
        if (Regex.IsMatch(command, @"^\s*(echo|rem|::|@echo)", RegexOptions.IgnoreCase))
        {
            isContinuable = true;
        }

        var recommendation = recommendations.Any() 
            ? $"Command '{command.Substring(0, Math.Min(50, command.Length))}...' - {string.Join("; ", recommendations)}"
            : string.Empty;

        return (recommendations.Any(), recommendation, isContinuable);
    }

    private string NormalizeCommand(string command)
    {
        var normalized = command;
        
        // Convert common macros to MSBuild properties first (before path normalization)
        var macroReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "$(DevEnvDir)", "$(MSBuildExtensionsPath)/../.." },
            { "$(SolutionDir)", "$(MSBuildProjectDirectory)/.." },
            { "$(ConfigurationName)", "$(Configuration)" },
            { "$(PlatformName)", "$(Platform)" },
            { "$(TargetDir)", "$(OutputPath)" },
            { "$(TargetPath)", "$(OutputPath)$(TargetFileName)" },
            { "$(TargetName)", "$(AssemblyName)" },
            { "$(ProjectDir)", "$(MSBuildProjectDirectory)/" },
            { "$(ProjectPath)", "$(MSBuildProjectFullPath)" },
            { "$(ProjectName)", "$(MSBuildProjectName)" },
            { "$(ProjectExt)", "$(MSBuildProjectExtension)" }
        };

        foreach (var replacement in macroReplacements)
        {
            normalized = Regex.Replace(normalized, 
                Regex.Escape(replacement.Key), 
                replacement.Value.Replace("$", "$$"), 
                RegexOptions.IgnoreCase);
        }

        // Normalize path separators for cross-platform compatibility
        // But preserve them in quoted strings
        var parts = Regex.Split(normalized, @"(""[^""]*"")");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) // Not inside quotes
            {
                parts[i] = parts[i].Replace('\\', '/');
            }
            else // Inside quotes
            {
                // Still normalize inside quotes
                parts[i] = parts[i].Replace('\\', '/');
            }
        }
        normalized = string.Join("", parts);

        return normalized;
    }
}