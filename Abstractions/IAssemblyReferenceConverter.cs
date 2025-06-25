using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IAssemblyReferenceConverter
{
    /// <summary>
    /// Analyzes legacy assembly references (<Reference> items) and determines corresponding NuGet PackageReferences
    /// required for the target framework.
    /// </summary>
    /// <param name="legacyProject">The legacy MSBuild project.</param>
    /// <param name="targetFramework">The target framework moniker (e.g., "net6.0") for the new SDK-style project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of unique PackageReference models.</returns>
    Task<IEnumerable<PackageReference>> ConvertReferencesAsync(
        Project legacyProject,
        string targetFramework,
        CancellationToken cancellationToken = default);
}