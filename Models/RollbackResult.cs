namespace SdkMigrator.Models;

public class RollbackResult
{
    public bool Success { get; set; }
    public List<string> RestoredFiles { get; set; } = new();
    public List<string> DeletedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public BackupSession? BackupSession { get; set; }
}