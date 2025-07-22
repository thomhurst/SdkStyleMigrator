using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles specialized analysis and migration guidance for ASP.NET web projects
/// </summary>
public interface IWebProjectHandler
{
    /// <summary>
    /// Analyzes a web project for migration patterns and provides guidance
    /// </summary>
    Task<WebMigrationAnalysis> AnalyzeWebProjectAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects legacy web patterns that need special handling
    /// </summary>
    Task<WebPatternDetectionResult> DetectWebPatternsAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates migration guidance for web-specific configurations
    /// </summary>
    Task<WebConfigurationMigrationGuidance> GenerateConfigurationGuidanceAsync(string projectPath, CancellationToken cancellationToken = default);
}