namespace SdkMigrator.Models;

public class ProjectTypeFilters
{
    // Desktop
    public bool IncludeWinForms { get; set; } = true;
    public bool IncludeWpf { get; set; } = true;
    
    // Web
    public bool IncludeWeb { get; set; } = true;
    public bool IncludeBlazor { get; set; } = true;
    
    // Cloud/Services
    public bool IncludeAzureFunctions { get; set; } = true;
    public bool IncludeWorkerService { get; set; } = true;
    public bool IncludeGrpc { get; set; } = true;
    
    // Mobile/Cross-platform
    public bool IncludeMaui { get; set; } = true;
    public bool IncludeUwp { get; set; } = true;
    
    // Standard project types
    public bool IncludeTest { get; set; } = true;
    public bool IncludeClassLibrary { get; set; } = true;
    public bool IncludeConsole { get; set; } = true;
    
    // Language-specific
    public bool IncludeFSharp { get; set; } = true;
    public bool IncludeVbNet { get; set; } = true;
    
    // Special/Legacy
    public bool IncludeDatabase { get; set; } = true;
    public bool IncludeOfficeAddIn { get; set; } = true;
    public bool IncludeDocker { get; set; } = true;
    public bool IncludeShared { get; set; } = false; // Default to false as these need special handling
    public bool IncludeLegacyUnsupported { get; set; } = false; // Default to false for safety
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