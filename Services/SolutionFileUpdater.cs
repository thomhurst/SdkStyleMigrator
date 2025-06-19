using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class SolutionFileUpdater : ISolutionFileUpdater
{
    private readonly ILogger<SolutionFileUpdater> _logger;
    private readonly MigrationOptions _options;
    
    private static readonly Regex ProjectLineRegex = new(
        @"^Project\(""{(?<TypeGuid>[A-F0-9\-]+)}""\)\s*=\s*""(?<Name>[^""]+)""\s*,\s*""(?<Path>[^""]+)""\s*,\s*""{(?<ProjectGuid>[A-F0-9\-]+)}""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public SolutionFileUpdater(ILogger<SolutionFileUpdater> logger, MigrationOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task<SolutionUpdateResult> UpdateSolutionFilesAsync(
        string rootDirectory, 
        Dictionary<string, string> projectMappings, 
        CancellationToken cancellationToken = default)
    {
        var result = new SolutionUpdateResult();
        
        var solutionFiles = Directory.GetFiles(rootDirectory, "*.sln", SearchOption.AllDirectories);
        
        if (solutionFiles.Length == 0)
        {
            _logger.LogInformation("No solution files found in {Directory}", rootDirectory);
            result.Success = true;
            return result;
        }
        
        foreach (var solutionFile in solutionFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            try
            {
                result.SolutionPath = solutionFile;
                await UpdateSolutionFileAsync(solutionFile, projectMappings, result, cancellationToken);
                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update solution file: {SolutionFile}", solutionFile);
                result.Success = false;
                result.Errors.Add($"Failed to update {solutionFile}: {ex.Message}");
            }
        }
        
        return result;
    }
    
    private async Task UpdateSolutionFileAsync(
        string solutionPath, 
        Dictionary<string, string> projectMappings,
        SolutionUpdateResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating solution file: {SolutionPath}", solutionPath);
        
        var solutionContent = await File.ReadAllTextAsync(solutionPath, cancellationToken);
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var updated = false;
        var updatedContent = solutionContent;
        
        var matches = ProjectLineRegex.Matches(solutionContent);
        
        foreach (Match match in matches)
        {
            var projectPath = match.Groups["Path"].Value;
            var absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDir, projectPath));
            
            if (projectMappings.TryGetValue(absoluteProjectPath, out var newProjectPath))
            {
                var newRelativePath = Path.GetRelativePath(solutionDir, newProjectPath).Replace('\\', '/');
                
                if (Path.DirectorySeparatorChar == '\\')
                {
                    newRelativePath = newRelativePath.Replace('/', '\\');
                }
                
                var oldLine = match.Value;
                var newLine = oldLine.Replace($"\"{projectPath}\"", $"\"{newRelativePath}\"");
                
                if (oldLine != newLine)
                {
                    updatedContent = updatedContent.Replace(oldLine, newLine);
                    updated = true;
                    result.UpdatedProjects.Add(projectPath);
                    
                    _logger.LogInformation("Updated project reference in solution: {OldPath} -> {NewPath}", 
                        projectPath, newRelativePath);
                }
            }
        }
        
        if (updated)
        {
            if (!_options.DryRun)
            {
                if (_options.CreateBackup)
                {
                    var backupPath = $"{solutionPath}.legacy";
                    File.Copy(solutionPath, backupPath, overwrite: true);
                    _logger.LogDebug("Created solution backup at {BackupPath}", backupPath);
                }
                
                await File.WriteAllTextAsync(solutionPath, updatedContent, cancellationToken);
                _logger.LogInformation("Successfully updated solution file: {SolutionPath}", solutionPath);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would update solution file: {SolutionPath}", solutionPath);
            }
        }
        else
        {
            _logger.LogInformation("No updates needed for solution file: {SolutionPath}", solutionPath);
        }
    }
}