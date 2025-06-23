using System.Collections.Generic;

namespace SdkMigrator.Models;

public class MigrationAnalysis
{
    public string DirectoryPath { get; set; } = string.Empty;
    public DateTime AnalysisDate { get; set; } = DateTime.UtcNow;
    public List<ProjectAnalysis> ProjectAnalyses { get; set; } = new();
    public List<string> GlobalWarnings { get; set; } = new();
    public List<string> GlobalRecommendations { get; set; } = new();
    public MigrationRiskLevel OverallRisk { get; set; }
    public int EstimatedManualEffortHours { get; set; }
    public bool CanProceedAutomatically { get; set; }
}

public class ProjectAnalysis
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public ProjectType ProjectType { get; set; }
    public string CurrentTargetFramework { get; set; } = string.Empty;
    public bool CanMigrate { get; set; }
    public MigrationRiskLevel RiskLevel { get; set; }

    // Detailed findings
    public List<CustomTargetAnalysis> CustomTargets { get; set; } = new();
    public List<BuildConfigurationAnalysis> BuildConfigurations { get; set; } = new();
    public List<PackageAnalysis> Packages { get; set; } = new();
    public List<ProjectReferenceAnalysis> ProjectReferences { get; set; } = new();
    public List<SpecialFileAnalysis> SpecialFiles { get; set; } = new();

    // Issues and recommendations
    public List<MigrationIssue> Issues { get; set; } = new();
    public List<string> ManualStepsRequired { get; set; } = new();
    public int EstimatedManualEffortHours { get; set; }
}

public class CustomTargetAnalysis
{
    public string TargetName { get; set; } = string.Empty;
    public string? BeforeTargets { get; set; }
    public string? AfterTargets { get; set; }
    public string? DependsOnTargets { get; set; }
    public string? Condition { get; set; }
    public List<string> Tasks { get; set; } = new();
    public bool CanAutoMigrate { get; set; }
    public string? AutoMigrationApproach { get; set; }
    public string? ManualMigrationGuidance { get; set; }
    public string? SuggestedCode { get; set; }
    public TargetComplexity Complexity { get; set; }
}

public class BuildConfigurationAnalysis
{
    public string ConfigurationName { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public List<string> Properties { get; set; } = new();
    public List<string> ConditionalItems { get; set; } = new();
    public bool IsStandard { get; set; } // Debug/Release
    public bool HasComplexConditions { get; set; }
}

public class PackageAnalysis
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool HasKnownIssues { get; set; }
    public string? MigrationNotes { get; set; }
    public bool RequiresManualIntervention { get; set; }
}

public class ProjectReferenceAnalysis
{
    public string ReferencePath { get; set; } = string.Empty;
    public string? Condition { get; set; }
    public bool PathExists { get; set; }
    public bool NeedsPathCorrection { get; set; }
    public string? SuggestedPath { get; set; }
}

public class SpecialFileAnalysis
{
    public string FilePath { get; set; } = string.Empty;
    public SpecialFileType FileType { get; set; }
    public bool CanMigrate { get; set; }
    public string? MigrationApproach { get; set; }
    public string? ManualSteps { get; set; }
}

public class MigrationIssue
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MigrationIssueSeverity Severity { get; set; }
    public string? Resolution { get; set; }
    public bool BlocksMigration { get; set; }
}

public enum MigrationRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum TargetComplexity
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}

public enum SpecialFileType
{
    T4Template,
    EntityFrameworkMigration,
    ServiceReference,
    WebConfig,
    AppConfig,
    StrongNameKey,
    NuSpec,
    ResxWithDesigner,
    SettingsFile,
    WpfXaml,
    WinFormsDesigner
}

public enum MigrationIssueSeverity
{
    Info,
    Warning,
    Error,
    Critical
}