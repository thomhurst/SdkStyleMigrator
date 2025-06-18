using Microsoft.Build.Evaluation;
using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

public interface IPackageReferenceMigrator
{
    Task<IEnumerable<PackageReference>> MigratePackagesAsync(Project project, CancellationToken cancellationToken = default);
}