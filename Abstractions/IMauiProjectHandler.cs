using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles MAUI and Xamarin project migration specifics
/// </summary>
public interface IMauiProjectHandler
{
    /// <summary>
    /// Detects MAUI/Xamarin configuration and platform targets
    /// </summary>
    Task<MauiProjectInfo> DetectMauiConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates MAUI/Xamarin specific elements to SDK-style format
    /// </summary>
    Task MigrateMauiProjectAsync(
        MauiProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles platform-specific folders and resources
    /// </summary>
    void MigratePlatformSpecificResources(string projectDirectory, XElement projectElement, MauiProjectInfo info);

    /// <summary>
    /// Converts Xamarin.Forms to MAUI configuration
    /// </summary>
    void ConvertXamarinFormsToMaui(XElement projectElement, MauiProjectInfo info);

    /// <summary>
    /// Migrates app icons, splash screens, and resources
    /// </summary>
    void MigrateAppResources(string projectDirectory, XElement projectElement);
}