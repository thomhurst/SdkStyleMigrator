using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface ITransitiveDependencyDetector : IDisposable
{
    Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences, 
        CancellationToken cancellationToken = default);
        
    Task<IEnumerable<PackageReference>> DetectTransitiveDependenciesAsync(
        IEnumerable<PackageReference> packageReferences,
        string? projectDirectory,
        CancellationToken cancellationToken = default);
}