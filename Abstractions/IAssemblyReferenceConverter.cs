using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IAssemblyReferenceConverter
{
    /// <summary>
    /// Analyzes legacy assembly references (<Reference> items) and determines corresponding NuGet PackageReferences
    /// required for the target framework. Also returns references that could not be converted.
    /// </summary>
    /// <param name="legacyProject">The legacy MSBuild project.</param>
    /// <param name="targetFramework">The target framework moniker (e.g., "net6.0") for the new SDK-style project.</param>
    /// <param name="existingPackages">Packages already converted from packages.config to avoid duplicate processing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing both converted package references and unconverted assembly references.</returns>
    Task<ReferenceConversionResult> ConvertReferencesAsync(
        Project legacyProject,
        string targetFramework,
        IEnumerable<PackageReference> existingPackages,
        CancellationToken cancellationToken = default);
}