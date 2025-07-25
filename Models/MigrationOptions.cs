namespace SdkMigrator.Models;

public class ProjectTypeFilters
{
    public bool IncludeWinForms { get; set; } = true;
    public bool IncludeWpf { get; set; } = true;
    public bool IncludeWeb { get; set; } = true;
    public bool IncludeTest { get; set; } = true;
    public bool IncludeClassLibrary { get; set; } = true;
    public bool IncludeConsole { get; set; } = true;
}

public class MigrationOptions
{
    public static MigrationOptions Default => new();
    
    public string DirectoryPath { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public string? OutputDirectory { get; set; }
    public string? TargetFramework { get; set; }
    public string[]? TargetFrameworks { get; set; } // For multi-targeting
    public bool CreateBackup { get; set; }
    public bool Force { get; set; }
    public int MaxDegreeOfParallelism { get; set; } = 1;
    public string LogLevel { get; set; } = "Information";
    public bool UseOfflineMode { get; set; }
    public string? NuGetConfigPath { get; set; }
    public bool EnableCentralPackageManagement { get; set; }
    public CpmVersionResolutionOptions CpmOptions { get; set; } = new();
    public bool DisableCache { get; set; }
    public int? CacheTTLMinutes { get; set; }
    public bool InteractiveImportSelection { get; set; }
    public ImportSelectionOptions ImportOptions { get; set; } = new();
    public bool InteractiveTargetSelection { get; set; }
    public TargetSelectionOptions TargetOptions { get; set; } = new();
    public ProjectTypeFilters ProjectTypeFilters { get; set; } = new();
}