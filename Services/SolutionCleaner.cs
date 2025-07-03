using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SdkMigrator.Services;

public class SolutionCleaner : ISolutionCleaner
{
    private readonly ILogger<SolutionCleaner> _logger;
    private readonly IBackupService _backupService;

    // Known .NET project type GUIDs that are considered "standard"
    private static readonly HashSet<string> StandardProjectTypeGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}", // C#
        "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}", // VB.NET
        "{F2A71F9B-5D33-465A-A702-920D77279786}", // F#
        "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}", // .NET Core/5+ SDK-style
        "{2150E333-8FDC-42A3-9474-1A3956D46DE8}", // Solution Folder
        "{60DC8134-EBA5-43B8-BCC9-BB4BC16C2548}", // WPF
        "{349C5851-65DF-11DA-9384-00065B846F21}", // Web Application
        "{603C0E0B-DB56-11DC-BE95-000D561079B0}", // ASP.NET MVC 1
        "{F85E285D-A4E0-4152-9332-AB1D724D3325}", // ASP.NET MVC 2
        "{E53F8FEA-EAE0-44A6-8774-FFD645390401}", // ASP.NET MVC 3
        "{E3E379DF-F4C6-4180-9B81-6769533ABE47}", // ASP.NET MVC 4
        "{349C5853-65DF-11DA-9384-00065B846F21}", // ASP.NET MVC 5
        "{C252FEB5-A946-4202-B1D4-9916A0590387}", // Windows Service
        "{786C830F-07A1-408B-BD7F-6EE04809D6DB}", // Portable Class Library
        "{A1591282-1198-4647-A2B1-27E5FF5F6F3B}", // Silverlight
        "{BC8A1FFA-BEE3-4634-8014-F334798102B3}", // Windows Store App
        "{14822709-B5A1-4724-98CA-57A101D1B079}", // Windows Phone
        "{A5A43C5B-DE2A-4C0C-9213-0A381AF9435A}", // UAP
        "{CC5FD16D-436D-48AD-A40C-5A424C6E3E79}", // Cloud Service
        "{3AC096D0-A1C2-E12C-1390-A8335801FDAB}", // Test Project
    };

    // Non-standard project types that might be removed with --remove-non-standard
    private static readonly HashSet<string> NonStandardProjectTypeGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}", // C++
        "{00D1A9C2-B5F0-4AF3-8072-F6C62B433612}", // SQL Server Database Project
        "{930C7802-8A8C-48F9-8165-68863BCCD9DD}", // WiX Installer
        "{54435603-DBB4-11D2-8724-00A0C9A8B90C}", // Visual Studio Installer
        "{4D628B5B-2FBC-4AA6-8C16-197242AEB884}", // SharePoint (C#)
        "{EC05E597-79D4-47F3-ADA0-324C4F7C7484}", // SharePoint (VB.NET)
        "{593B0543-81F6-4436-BA1E-4747859CAAE2}", // SharePoint (Workflow)
        "{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1}", // Xamarin.iOS
        "{EFBA0AD7-5A72-4C68-AF49-83D382785DCF}", // Xamarin.Android
    };

    public SolutionCleaner(ILogger<SolutionCleaner> logger, IBackupService backupService)
    {
        _logger = logger;
        _backupService = backupService;
    }

    public async Task<SolutionCleanResult> CleanSolutionAsync(
        string solutionPath,
        SolutionCleanOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new SolutionCleanResult
        {
            SolutionPath = solutionPath
        };

        try
        {
            if (!File.Exists(solutionPath))
            {
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");
            }

            options.ApplyFixAll();

            _logger.LogInformation("Starting solution cleanup for: {SolutionPath}", solutionPath);

            // Create backup if requested
            if (options.CreateBackup && !options.DryRun)
            {
                result.BackupPath = await CreateBackupAsync(solutionPath, cancellationToken);
                _logger.LogInformation("Created backup at: {BackupPath}", result.BackupPath);
            }

            // Read the solution file content
            var solutionContent = await File.ReadAllTextAsync(solutionPath, cancellationToken);
            var originalContent = solutionContent;

            // Parse the solution file for analysis
            var solution = SolutionFile.Parse(solutionPath);
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? ".";

            // Create our own project info dictionary with type GUIDs
            var projectInfos = ParseProjectTypeGuids(solutionContent);

            // Apply fixes based on options
            if (options.FixMissingProjects)
            {
                solutionContent = RemoveMissingProjects(solutionContent, solution, solutionDir, result, options.DryRun);
            }

            if (options.RemoveDuplicates)
            {
                solutionContent = RemoveDuplicateProjects(solutionContent, solution, result, options.DryRun);
            }

            if (options.RemoveNonStandardProjects)
            {
                solutionContent = RemoveNonStandardProjects(solutionContent, solution, projectInfos, result, options.DryRun);
            }

            if (options.RemoveSourceControlBindings)
            {
                solutionContent = RemoveSourceControlBindings(solutionContent, result, options.DryRun);
            }

            if (options.RemoveEmptyFolders)
            {
                solutionContent = RemoveEmptySolutionFolders(solutionContent, solution, result, options.DryRun);
            }

            if (options.FixConfigurations)
            {
                solutionContent = FixConfigurations(solutionContent, solution, result, options.DryRun);
            }

            // Validate GUIDs
            ValidateProjectGuids(solution, projectInfos, result);

            // Save changes if any were made and not in dry run mode
            if (solutionContent != originalContent && !options.DryRun)
            {
                _logger.LogInformation("Saving cleaned solution file...");
                await SaveSolutionAsync(solutionPath, solutionContent, cancellationToken);
                result.Success = true;
            }
            else if (options.DryRun && solutionContent != originalContent)
            {
                _logger.LogInformation("[DRY RUN] Would save changes to: {SolutionPath}", solutionPath);
                result.Success = true;
            }
            else
            {
                _logger.LogInformation("No changes needed for solution file");
                result.Success = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning solution file: {SolutionPath}", solutionPath);
            result.Errors.Add($"Error: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    private Dictionary<string, string> ParseProjectTypeGuids(string solutionContent)
    {
        var projectInfos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Pattern to match project entries
        var projectPattern = @"Project\(""({[^}]+})""\)\s*=\s*""[^""]+"",\s*""[^""]+"",\s*""({[^}]+})""";
        var matches = Regex.Matches(solutionContent, projectPattern);

        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 3)
            {
                var typeGuid = match.Groups[1].Value;
                var projectGuid = match.Groups[2].Value;
                projectInfos[projectGuid] = typeGuid;
            }
        }

        return projectInfos;
    }

    private string RemoveMissingProjects(string solutionContent, SolutionFile solution, string solutionDir, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        var projectsToRemove = new List<ProjectInSolution>();

        foreach (var project in solution.ProjectsInOrder)
        {
            // Skip solution folders
            if (project.ProjectType == SolutionProjectType.SolutionFolder)
                continue;

            var absolutePath = Path.GetFullPath(Path.Combine(solutionDir, project.RelativePath));

            if (!File.Exists(absolutePath))
            {
                _logger.LogWarning("Project file not found: {ProjectPath}", absolutePath);
                projectsToRemove.Add(project);
                result.RemovedProjects.Add($"{project.ProjectName} ({project.RelativePath})");
            }
        }

        foreach (var project in projectsToRemove)
        {
            // Remove the project entry - use a more flexible pattern
            var projectPattern = $@"Project\(""[^""]+""\)\s*=\s*""{Regex.Escape(project.ProjectName)}"",\s*""{Regex.Escape(project.RelativePath)}"",\s*""{project.ProjectGuid}""\s*\r?\n(?:.*?\r?\n)*?EndProject";
            modifiedContent = Regex.Replace(modifiedContent, projectPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // Remove project configurations
            var configPattern = $@"{Regex.Escape(project.ProjectGuid)}[^\r\n]+\r?\n";
            modifiedContent = Regex.Replace(modifiedContent, configPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        result.ProjectsRemoved = projectsToRemove.Count;

        if (projectsToRemove.Count > 0)
        {
            _logger.LogInformation("{Action} {Count} missing project(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                projectsToRemove.Count);
        }

        return modifiedContent;
    }

    private string RemoveDuplicateProjects(string solutionContent, SolutionFile solution, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        var seen = new Dictionary<string, ProjectInSolution>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<ProjectInSolution>();

        foreach (var project in solution.ProjectsInOrder)
        {
            var key = project.AbsolutePath.ToLowerInvariant();

            if (seen.ContainsKey(key))
            {
                _logger.LogWarning("Duplicate project found: {ProjectName} at {Path}",
                    project.ProjectName, project.RelativePath);
                duplicates.Add(project);
                result.RemovedDuplicates.Add($"{project.ProjectName} ({project.RelativePath})");
            }
            else
            {
                seen[key] = project;
            }
        }

        // Remove duplicates, keeping only the first occurrence
        foreach (var duplicate in duplicates)
        {
            // Find and remove the duplicate project entry (not the first one)
            var projectPattern = $@"Project\(""[^""]+""\)\s*=\s*""{Regex.Escape(duplicate.ProjectName)}"",\s*""{Regex.Escape(duplicate.RelativePath)}"",\s*""{duplicate.ProjectGuid}""\s*\r?\n(?:.*?\r?\n)*?EndProject";

            // Replace only the second occurrence
            var matches = Regex.Matches(modifiedContent, projectPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (matches.Count > 1)
            {
                // Remove from the end to avoid offset issues
                for (int i = matches.Count - 1; i >= 1; i--)
                {
                    modifiedContent = modifiedContent.Remove(matches[i].Index, matches[i].Length);
                }
            }
        }

        result.DuplicatesRemoved = duplicates.Count;

        if (duplicates.Count > 0)
        {
            _logger.LogInformation("{Action} {Count} duplicate project(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                duplicates.Count);
        }

        return modifiedContent;
    }

    private string RemoveNonStandardProjects(string solutionContent, SolutionFile solution, Dictionary<string, string> projectInfos, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        var projectsToRemove = new List<ProjectInSolution>();

        foreach (var project in solution.ProjectsInOrder)
        {
            if (project.ProjectType == SolutionProjectType.SolutionFolder)
                continue;

            // Get the project type GUID from our parsed data
            if (projectInfos.TryGetValue(project.ProjectGuid, out var projectTypeGuid))
            {
                if (NonStandardProjectTypeGuids.Contains(projectTypeGuid))
                {
                    _logger.LogWarning("Non-standard project type found: {ProjectName} ({ProjectType})",
                        project.ProjectName, projectTypeGuid);
                    projectsToRemove.Add(project);
                    result.RemovedProjects.Add($"{project.ProjectName} (Type: {GetProjectTypeName(projectTypeGuid)})");
                }
            }
        }

        foreach (var project in projectsToRemove)
        {
            // Remove the project entry
            var projectPattern = $@"Project\(""[^""]+""\)\s*=\s*""{Regex.Escape(project.ProjectName)}"",\s*""{Regex.Escape(project.RelativePath)}"",\s*""{project.ProjectGuid}""\s*\r?\n(?:.*?\r?\n)*?EndProject";
            modifiedContent = Regex.Replace(modifiedContent, projectPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // Remove project configurations
            var configPattern = $@"{Regex.Escape(project.ProjectGuid)}[^\r\n]+\r?\n";
            modifiedContent = Regex.Replace(modifiedContent, configPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        result.ProjectsRemoved += projectsToRemove.Count;

        if (projectsToRemove.Count > 0)
        {
            _logger.LogInformation("{Action} {Count} non-standard project(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                projectsToRemove.Count);
        }

        return modifiedContent;
    }

    private string RemoveSourceControlBindings(string solutionContent, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        var removedCount = 0;

        // Remove TeamFoundationVersionControl section
        var tfvcPattern = @"GlobalSection\(TeamFoundationVersionControl\)[^\r\n]*\r?\n(?:[^\r\n]+\r?\n)*?\s*EndGlobalSection\r?\n";
        if (Regex.IsMatch(modifiedContent, tfvcPattern, RegexOptions.Multiline))
        {
            modifiedContent = Regex.Replace(modifiedContent, tfvcPattern, "", RegexOptions.Multiline);
            result.RemovedSections.Add("GlobalSection(TeamFoundationVersionControl)");
            removedCount++;
        }

        // Remove SourceCodeControl section
        var sccPattern = @"GlobalSection\(SourceCodeControl\)[^\r\n]*\r?\n(?:[^\r\n]+\r?\n)*?\s*EndGlobalSection\r?\n";
        if (Regex.IsMatch(modifiedContent, sccPattern, RegexOptions.Multiline))
        {
            modifiedContent = Regex.Replace(modifiedContent, sccPattern, "", RegexOptions.Multiline);
            result.RemovedSections.Add("GlobalSection(SourceCodeControl)");
            removedCount++;
        }

        // Remove any SccProjectName, SccLocalPath, etc. from project entries
        var sccProjectPattern = @"^\s*Scc(?:ProjectName|LocalPath|AuxPath|Provider)[^\r\n]*\r?\n";
        var sccMatches = Regex.Matches(modifiedContent, sccProjectPattern, RegexOptions.Multiline);
        if (sccMatches.Count > 0)
        {
            modifiedContent = Regex.Replace(modifiedContent, sccProjectPattern, "", RegexOptions.Multiline);
            removedCount += sccMatches.Count;
        }

        result.SourceControlBindingsRemoved = removedCount;

        if (removedCount > 0)
        {
            _logger.LogInformation("{Action} {Count} source control binding(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                removedCount);
        }

        return modifiedContent;
    }

    private string RemoveEmptySolutionFolders(string solutionContent, SolutionFile solution, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        var foldersToRemove = new List<ProjectInSolution>();
        var folderContents = new Dictionary<string, HashSet<string>>();

        // Build parent-child relationships
        foreach (var project in solution.ProjectsInOrder)
        {
            if (project.ParentProjectGuid != null)
            {
                if (!folderContents.ContainsKey(project.ParentProjectGuid))
                {
                    folderContents[project.ParentProjectGuid] = new HashSet<string>();
                }
                folderContents[project.ParentProjectGuid].Add(project.ProjectGuid);
            }
        }

        // Find empty folders
        foreach (var project in solution.ProjectsInOrder)
        {
            if (project.ProjectType == SolutionProjectType.SolutionFolder)
            {
                if (!folderContents.ContainsKey(project.ProjectGuid) ||
                    folderContents[project.ProjectGuid].Count == 0)
                {
                    _logger.LogInformation("Found empty solution folder: {FolderName}", project.ProjectName);
                    foldersToRemove.Add(project);
                    result.RemovedFolders.Add(project.ProjectName);
                }
            }
        }

        foreach (var folder in foldersToRemove)
        {
            // Remove the folder entry
            var folderPattern = $@"Project\(""[^""]+""\)\s*=\s*""{Regex.Escape(folder.ProjectName)}"",\s*""{Regex.Escape(folder.RelativePath)}"",\s*""{folder.ProjectGuid}""\s*\r?\n(?:.*?\r?\n)*?EndProject";
            modifiedContent = Regex.Replace(modifiedContent, folderPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // Remove folder from nested projects
            var nestedPattern = $@"{Regex.Escape(folder.ProjectGuid)}\s*=\s*{folder.ParentProjectGuid ?? @"{[^}]+}"}\r?\n";
            modifiedContent = Regex.Replace(modifiedContent, nestedPattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        result.EmptyFoldersRemoved = foldersToRemove.Count;

        if (foldersToRemove.Count > 0)
        {
            _logger.LogInformation("{Action} {Count} empty solution folder(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                foldersToRemove.Count);
        }

        return modifiedContent;
    }

    private string FixConfigurations(string solutionContent, SolutionFile solution, SolutionCleanResult result, bool dryRun)
    {
        var modifiedContent = solutionContent;
        int orphanedRemoved = 0;
        int configurationsAdded = 0;

        // Get all project GUIDs currently in the solution
        var currentProjectGuids = new HashSet<string>(
            solution.ProjectsInOrder
                .Where(p => p.ProjectType != SolutionProjectType.SolutionFolder)
                .Select(p => p.ProjectGuid.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // Find and remove orphaned configurations
        var configSection = @"GlobalSection\(ProjectConfigurationPlatforms\)[^\r\n]*\r?\n((?:[^\r\n]+\r?\n)*?)\s*EndGlobalSection";
        var configMatch = Regex.Match(modifiedContent, configSection, RegexOptions.Multiline);

        if (configMatch.Success)
        {
            var configContent = configMatch.Groups[1].Value;
            var lines = configContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var newLines = new List<string>();

            foreach (var line in lines)
            {
                var guidMatch = Regex.Match(line, @"{([A-F0-9\-]+)}", RegexOptions.IgnoreCase);
                if (guidMatch.Success)
                {
                    var projectGuid = guidMatch.Groups[1].Value.ToUpperInvariant();
                    if (currentProjectGuids.Contains($"{{{projectGuid}}}"))
                    {
                        newLines.Add(line);
                    }
                    else
                    {
                        orphanedRemoved++;
                        result.FixedConfigurations.Add($"Removed orphaned config: {line.Trim()}");
                    }
                }
            }

            // Add missing configurations
            foreach (var project in solution.ProjectsInOrder.Where(p => p.ProjectType != SolutionProjectType.SolutionFolder))
            {
                foreach (var config in solution.SolutionConfigurations)
                {
                    var configName = $"{project.ProjectGuid}.{config.FullName}";
                    var buildKey = $"{configName}.Build.0";
                    var activeKey = $"{configName}.ActiveCfg";

                    var hasActive = newLines.Any(l => l.Contains(activeKey));
                    var hasBuild = newLines.Any(l => l.Contains(buildKey));

                    if (!hasActive)
                    {
                        newLines.Add($"\t\t{activeKey} = {config.FullName}");
                        configurationsAdded++;
                        result.AddedConfigurations.Add($"{project.ProjectName}: {config.FullName} (ActiveCfg)");
                    }

                    if (!hasBuild && project.ProjectType != SolutionProjectType.SolutionFolder)
                    {
                        newLines.Add($"\t\t{buildKey} = {config.FullName}");
                        configurationsAdded++;
                        result.AddedConfigurations.Add($"{project.ProjectName}: {config.FullName} (Build)");
                    }
                }
            }

            // Rebuild the configuration section
            var newConfigContent = string.Join("\r\n", newLines.Where(l => !string.IsNullOrWhiteSpace(l)));
            var newConfigSection = $"GlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n{newConfigContent}\r\n\tEndGlobalSection";
            modifiedContent = modifiedContent.Replace(configMatch.Value, newConfigSection);
        }

        result.ConfigurationsFixed = orphanedRemoved;
        result.ConfigurationsAdded = configurationsAdded;

        if (orphanedRemoved > 0)
        {
            _logger.LogInformation("{Action} {Count} orphaned configuration(s)",
                dryRun ? "[DRY RUN] Would remove" : "Removed",
                orphanedRemoved);
        }

        if (configurationsAdded > 0)
        {
            _logger.LogInformation("{Action} {Count} missing configuration(s)",
                dryRun ? "[DRY RUN] Would add" : "Added",
                configurationsAdded);
        }

        return modifiedContent;
    }

    private void ValidateProjectGuids(SolutionFile solution, Dictionary<string, string> projectInfos, SolutionCleanResult result)
    {
        var guidsSeen = new Dictionary<string, List<ProjectInSolution>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.ProjectsInOrder)
        {
            // Check for valid GUID format
            if (!Guid.TryParse(project.ProjectGuid.Trim('{', '}'), out _))
            {
                result.UnfixableIssues.Add(new SolutionIssue
                {
                    Type = SolutionIssueType.InvalidGuid,
                    Description = "Invalid project GUID format",
                    ProjectName = project.ProjectName,
                    ProjectPath = project.RelativePath,
                    Details = $"GUID: {project.ProjectGuid}"
                });
            }

            // Check for duplicate GUIDs
            if (!guidsSeen.ContainsKey(project.ProjectGuid))
            {
                guidsSeen[project.ProjectGuid] = new List<ProjectInSolution>();
            }
            guidsSeen[project.ProjectGuid].Add(project);

            // Check for unknown project types (warning only)
            if (projectInfos.TryGetValue(project.ProjectGuid, out var projectTypeGuid))
            {
                if (!StandardProjectTypeGuids.Contains(projectTypeGuid) &&
                    !NonStandardProjectTypeGuids.Contains(projectTypeGuid) &&
                    project.ProjectType != SolutionProjectType.SolutionFolder)
                {
                    result.Warnings.Add($"Unknown project type GUID: {projectTypeGuid} for project {project.ProjectName}");
                }
            }
        }

        // Report duplicate GUIDs
        foreach (var (guid, projects) in guidsSeen.Where(kvp => kvp.Value.Count > 1))
        {
            var projectList = string.Join(", ", projects.Select(p => p.ProjectName));
            result.UnfixableIssues.Add(new SolutionIssue
            {
                Type = SolutionIssueType.DuplicateGuid,
                Description = "Multiple projects share the same GUID",
                Details = $"GUID {guid} is used by: {projectList}"
            });
        }
    }

    private async Task<string> CreateBackupAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var backupSession = await _backupService.GetCurrentSessionAsync() ??
                           await _backupService.InitializeBackupAsync(Path.GetDirectoryName(solutionPath)!, cancellationToken);

        await _backupService.BackupFileAsync(backupSession, solutionPath, cancellationToken);

        var backupPath = $"{solutionPath}.{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Copy(solutionPath, backupPath, true);
        return backupPath;
    }

    private async Task SaveSolutionAsync(string path, string content, CancellationToken cancellationToken)
    {
        // First write to a temp file, then replace the original
        var tempPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);

            // Replace the original file
            File.Copy(tempPath, path, true);
            _logger.LogInformation("Solution file saved successfully");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string GetProjectTypeName(string projectTypeGuid)
    {
        return projectTypeGuid.ToUpperInvariant() switch
        {
            "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}" => "C++",
            "{00D1A9C2-B5F0-4AF3-8072-F6C62B433612}" => "SQL Server",
            "{930C7802-8A8C-48F9-8165-68863BCCD9DD}" => "WiX Installer",
            "{4D628B5B-2FBC-4AA6-8C16-197242AEB884}" => "SharePoint",
            "{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1}" => "Xamarin.iOS",
            "{EFBA0AD7-5A72-4C68-AF49-83D382785DCF}" => "Xamarin.Android",
            _ => "Unknown"
        };
    }
}