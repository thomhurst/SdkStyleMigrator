namespace SdkMigrator.Models;

public class TestProjectInfo
{
    public string ProjectPath { get; set; } = string.Empty;
    public List<string> DetectedFrameworks { get; set; } = new();
    public List<string> RunSettingsFiles { get; set; } = new();
    public List<string> TestSettingsFiles { get; set; } = new();
    public List<string> TestPlaylistFiles { get; set; } = new();
    public List<string> CodeCoverageTools { get; set; } = new();
    public List<string> FeatureFiles { get; set; } = new();
}