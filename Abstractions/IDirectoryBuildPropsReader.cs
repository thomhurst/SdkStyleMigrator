namespace SdkMigrator.Abstractions;

/// <summary>
/// Reads and analyzes Directory.Build.props and Directory.Build.targets files
/// to understand inherited properties and avoid duplication during migration.
/// </summary>
public interface IDirectoryBuildPropsReader
{
    /// <summary>
    /// Finds and loads all Directory.Build.props files that apply to a project.
    /// </summary>
    /// <param name="projectPath">Path to the project file</param>
    /// <returns>Dictionary of property names to values that are inherited</returns>
    Dictionary<string, string> GetInheritedProperties(string projectPath);

    /// <summary>
    /// Checks if a Directory.Build.targets file exists in the hierarchy.
    /// </summary>
    /// <param name="projectPath">Path to the project file</param>
    /// <returns>True if Directory.Build.targets exists in the hierarchy</returns>
    bool HasDirectoryBuildTargets(string projectPath);

    /// <summary>
    /// Gets all Directory.Packages.props properties if Central Package Management is enabled.
    /// </summary>
    /// <param name="projectPath">Path to the project file</param>
    /// <returns>Set of package IDs that are centrally managed</returns>
    HashSet<string> GetCentrallyManagedPackages(string projectPath);
}