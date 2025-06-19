namespace SdkMigrator.Models;

public class SolutionUpdateResult
{
    public string SolutionPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<string> UpdatedProjects { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}