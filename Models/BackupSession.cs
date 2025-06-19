namespace SdkMigrator.Models;

public class BackupSession
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string RootDirectory { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public List<BackupFileInfo> BackedUpFiles { get; set; } = new();
    public string ToolVersion { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public Dictionary<string, string> MigrationParameters { get; set; } = new();
}

public class BackupFileInfo
{
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string OriginalHash { get; set; } = string.Empty;
    public DateTime BackupTime { get; set; }
    public long FileSize { get; set; }
}