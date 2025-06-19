namespace SdkMigrator.Models;

public class MigrationReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalProjectsFound { get; set; }
    public int TotalProjectsMigrated { get; set; }
    public int TotalProjectsFailed { get; set; }
    public List<MigrationResult> Results { get; set; } = new();
    
    public TimeSpan Duration => EndTime - StartTime;
}