using System.Collections;
using System.Xml;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class CentralPackageManagementGenerator : ICentralPackageManagementGenerator
{
    private readonly ILogger<CentralPackageManagementGenerator> _logger;
    private readonly IAuditService _auditService;
    private readonly MigrationOptions _options;
    private readonly CpmVersionResolver _versionResolver;
    private readonly CpmPackageClassifier _packageClassifier;
    private readonly ExistingCpmDetector _existingCpmDetector;

    public CentralPackageManagementGenerator(
        ILogger<CentralPackageManagementGenerator> logger,
        IAuditService auditService,
        MigrationOptions options,
        CpmVersionResolver versionResolver,
        CpmPackageClassifier packageClassifier,
        ExistingCpmDetector existingCpmDetector)
    {
        _logger = logger;
        _auditService = auditService;
        _options = options;
        _versionResolver = versionResolver;
        _packageClassifier = packageClassifier;
        _existingCpmDetector = existingCpmDetector;
    }

    public async Task<CentralPackageManagementResult> GenerateDirectoryPackagesPropsAsync(
        string solutionDirectory,
        IEnumerable<MigrationResult> migrationResults,
        CancellationToken cancellationToken = default)
    {
        var result = new CentralPackageManagementResult();

        try
        {
            _logger.LogInformation("Analyzing packages for Central Package Management...");

            // Check for existing CPM setup
            var existingCpm = _existingCpmDetector.DetectExistingCpm(solutionDirectory);
            
            if (existingCpm.HasExistingCpm)
            {
                _logger.LogInformation("Found existing Directory.Packages.props at {Path} with {PackageCount} packages", 
                    existingCpm.DirectoryPackagesPropsPath, existingCpm.ExistingPackages.Count);
            }

            // Collect all package references from migration results
            var newPackages = migrationResults
                .Where(r => r.Success)
                .SelectMany(r => r.MigratedPackages)
                .Where(p => !p.IsTransitive)
                .GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Merge with existing packages
            var allPackages = MergeWithExistingPackages(newPackages, existingCpm);

            if (!allPackages.Any())
            {
                _logger.LogInformation("No packages found to centralize");
                result.Success = true;
                return result;
            }

            // Collect all target frameworks from migration results
            var allTargetFrameworks = migrationResults
                .Where(r => r.Success && r.TargetFrameworks != null)
                .SelectMany(r => r.TargetFrameworks!)
                .Distinct()
                .ToList();

            result.PackageCount = allPackages.Count;

            // Check for version conflicts using enhanced resolution
            foreach (var packageGroup in allPackages)
            {
                var versions = packageGroup.Select(p => p.Version).Distinct().ToList();
                if (versions.Count > 1)
                {
                    var resolution = _versionResolver.ResolveVersionConflict(
                        packageGroup.Key, 
                        versions, 
                        allTargetFrameworks, 
                        _options.CpmOptions);

                    var conflict = new CpmPackageVersionConflict
                    {
                        PackageId = packageGroup.Key,
                        Versions = versions,
                        ResolvedVersion = resolution.ResolvedVersion,
                        ResolutionReason = resolution.ResolutionReason,
                        Strategy = resolution.Strategy,
                        TargetFrameworks = allTargetFrameworks,
                        HasWarnings = resolution.HasWarnings,
                        Warnings = resolution.Warnings
                    };
                    result.VersionConflicts.Add(conflict);

                    var logLevel = resolution.HasWarnings ? LogLevel.Warning : LogLevel.Information;
                    _logger.Log(logLevel, "Version conflict for package {PackageId}: {Versions}. Resolved to {ResolvedVersion} using {Strategy}",
                        packageGroup.Key, string.Join(", ", versions), conflict.ResolvedVersion, resolution.Strategy);

                    if (resolution.HasWarnings)
                    {
                        foreach (var warning in resolution.Warnings)
                        {
                            _logger.LogWarning("CPM Warning for {PackageId}: {Warning}", packageGroup.Key, warning);
                        }
                    }
                }
            }

            // Classify packages for better organization
            var packageClassifications = allPackages
                .ToDictionary(packageGroup => packageGroup.Key, packageGroup =>
                {
                    var versions = packageGroup.Select(p => p.Version).Distinct().ToList();
                    var resolvedVersion = versions.Count == 1 ? versions[0] :
                        result.VersionConflicts.First(c => c.PackageId == packageGroup.Key).ResolvedVersion;
                    
                    return _packageClassifier.ClassifyPackage(packageGroup.Key, resolvedVersion, allTargetFrameworks);
                });

            // Generate Directory.Packages.props with organized package groups
            var directoryPackagesProps = new XDocument(
                new XComment("Central Package Management - Generated by SdkMigrator"),
                new XElement("Project",
                    new XElement("PropertyGroup",
                        new XElement("ManagePackageVersionsCentrally", "true"),
                        new XElement("CentralPackageTransitivePinningEnabled", "true"))));

            var root = directoryPackagesProps.Root!;

            // Group packages by type for better organization
            var packageTypes = packageClassifications.Values
                .GroupBy(c => c.PackageType)
                .OrderBy(g => packageClassifications.Values.Where(v => v.PackageType == g.Key).Min(v => v.Priority));

            foreach (var packageTypeGroup in packageTypes)
            {
                var packagesInGroup = packageTypeGroup.Where(c => !c.IsGlobalReference).ToList();
                var globalPackagesInGroup = packageTypeGroup.Where(c => c.IsGlobalReference).ToList();

                // Add regular package references grouped by type
                if (packagesInGroup.Any())
                {
                    var groupComment = GetPackageGroupComment(packageTypeGroup.Key);
                    root.Add(new XComment(groupComment));
                    
                    root.Add(new XElement("ItemGroup",
                        packagesInGroup
                            .OrderBy(c => c.PackageId)
                            .Select(classification => new XElement("PackageVersion",
                                new XAttribute("Include", classification.PackageId),
                                new XAttribute("Version", classification.Version)))));
                }

                // Add global package references (analyzers, build tools) 
                if (globalPackagesInGroup.Any())
                {
                    var globalGroupComment = GetGlobalPackageGroupComment(packageTypeGroup.Key);
                    root.Add(new XComment(globalGroupComment));
                    
                    root.Add(new XElement("ItemGroup",
                        globalPackagesInGroup
                            .OrderBy(c => c.PackageId)
                            .Select(classification => new XElement("GlobalPackageReference",
                                new XAttribute("Include", classification.PackageId),
                                new XAttribute("Version", classification.Version)))));
                }
            }

            var outputPath = Path.Combine(solutionDirectory, "Directory.Packages.props");

            if (!_options.DryRun)
            {
                // Check if file already exists
                if (File.Exists(outputPath))
                {
                    _logger.LogWarning("Directory.Packages.props already exists at {Path}. Creating backup.", outputPath);
                    File.Copy(outputPath, $"{outputPath}.backup", overwrite: true);
                }

                // Save without XML declaration
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineChars = Environment.NewLine,
                    NewLineHandling = NewLineHandling.Replace
                };

                using (var writer = XmlWriter.Create(outputPath, settings))
                {
                    directoryPackagesProps.Save(writer);
                }
                _logger.LogInformation("Created Directory.Packages.props at {Path}", outputPath);

                // Audit the creation
                await _auditService.LogFileCreationAsync(new FileCreationAudit
                {
                    FilePath = outputPath,
                    FileSize = new FileInfo(outputPath).Length,
                    CreationType = "Central Package Management configuration",
                    FileHash = await FileHashCalculator.CalculateHashAsync(outputPath, cancellationToken)
                }, cancellationToken);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would create Directory.Packages.props at {Path}", outputPath);
                _logger.LogDebug("[DRY RUN] Content:\n{Content}", directoryPackagesProps.ToString());
            }

            result.DirectoryPackagesPropsPath = outputPath;
            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Directory.Packages.props");
            result.Errors.Add(ex.Message);
            result.Success = false;
        }

        return result;
    }

    public async Task<bool> RemoveVersionsFromProjectsAsync(
        IEnumerable<string> projectFiles,
        CancellationToken cancellationToken = default)
    {
        var success = true;
        var updatedCount = 0;

        foreach (var projectFile in projectFiles)
        {
            try
            {
                if (!File.Exists(projectFile))
                {
                    _logger.LogWarning("Project file not found: {ProjectFile}", projectFile);
                    continue;
                }

                var doc = XDocument.Load(projectFile);
                var modified = false;

                // Find all PackageReference elements with Version attribute
                var packageReferences = doc.Descendants("PackageReference")
                    .Where(pr => pr.Attribute("Version") != null)
                    .ToList();

                foreach (var packageRef in packageReferences)
                {
                    // Skip if it has VersionOverride (intentional override)
                    if (packageRef.Attribute("VersionOverride") != null)
                        continue;

                    packageRef.Attribute("Version")?.Remove();
                    modified = true;

                    _logger.LogDebug("Removed Version from PackageReference {Package} in {Project}",
                        packageRef.Attribute("Include")?.Value, projectFile);
                }

                if (modified)
                {
                    if (!_options.DryRun)
                    {
                        // Save without XML declaration
                        var settings = new XmlWriterSettings
                        {
                            OmitXmlDeclaration = true,
                            Indent = true,
                            NewLineChars = Environment.NewLine,
                            NewLineHandling = NewLineHandling.Replace
                        };

                        using (var writer = XmlWriter.Create(projectFile, settings))
                        {
                            doc.Save(writer);
                        }
                        updatedCount++;
                        _logger.LogInformation("Updated {ProjectFile} to use Central Package Management", projectFile);
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would update {ProjectFile} to use Central Package Management", projectFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update project file: {ProjectFile}", projectFile);
                success = false;
            }
        }

        _logger.LogInformation("Updated {Count} project files for Central Package Management", updatedCount);
        return success;
    }

    private string ResolveVersionConflict(List<string> versions)
    {
        // Simple strategy: pick the highest version
        // In real scenarios, might want more sophisticated resolution
        return versions
            .OrderByDescending(v => v, new VersionComparer())
            .First();
    }

    private string GetPackageGroupComment(CpmPackageType packageType)
    {
        return packageType switch
        {
            CpmPackageType.MicrosoftRuntime => "Microsoft Runtime and Framework Packages",
            CpmPackageType.Runtime => "Runtime Packages",
            CpmPackageType.ThirdPartyRuntime => "Third-Party Runtime Packages",
            CpmPackageType.Testing => "Testing Framework Packages",
            CpmPackageType.BuildTool => "Build Tools and MSBuild Packages",
            CpmPackageType.DevelopmentOnly => "Development and Design-Time Packages",
            _ => "Other Packages"
        };
    }

    private string GetGlobalPackageGroupComment(CpmPackageType packageType)
    {
        return packageType switch
        {
            CpmPackageType.Analyzer => "Code Analysis and Static Analysis Tools (Applied Globally)",
            CpmPackageType.BuildTool => "Build Tools Applied Globally",
            _ => "Global Package References"
        };
    }

    private bool IsAnalyzerPackage(string packageId)
    {
        var analyzerPackages = new[]
        {
            "StyleCop.Analyzers",
            "SonarAnalyzer.CSharp",
            "Microsoft.CodeAnalysis.NetAnalyzers",
            "Microsoft.CodeAnalysis.FxCopAnalyzers",
            "Roslynator.Analyzers"
        };

        return analyzerPackages.Any(ap => packageId.StartsWith(ap, StringComparison.OrdinalIgnoreCase)) ||
               packageId.EndsWith(".Analyzers", StringComparison.OrdinalIgnoreCase);
    }

    private void CollectTransitiveProjectDependencies(ProjectInfo project, Dictionary<string, ProjectInfo> projectGraph, HashSet<string> visited)
    {
        if (!visited.Add(project.FilePath))
            return;

        foreach (var refPath in project.ProjectReferences)
        {
            if (projectGraph.TryGetValue(refPath, out var referencedProject))
            {
                project.AllProjectDependencies.Add(refPath);

                // Recursively collect transitive dependencies
                CollectTransitiveProjectDependencies(referencedProject, projectGraph, visited);

                // Add transitive dependencies
                project.AllProjectDependencies.UnionWith(referencedProject.AllProjectDependencies);
            }
        }
    }

    private class ProjectInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public HashSet<string> DirectPackageReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ProjectReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllProjectDependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Try to parse as semantic versions
            if (Version.TryParse(x, out var vx) && Version.TryParse(y, out var vy))
            {
                return vx.CompareTo(vy);
            }

            // Fallback to string comparison
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<CleanCpmResult> CleanUnusedPackagesAsync(
        string directoryPath,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var result = new CleanCpmResult { Success = true };

        try
        {
            // Find Directory.Packages.props
            var packagesPropsPath = Path.Combine(directoryPath, "Directory.Packages.props");
            if (!File.Exists(packagesPropsPath))
            {
                // Try parent directories up to 3 levels
                var searchDir = directoryPath;
                for (int i = 0; i < 3; i++)
                {
                    var parent = Directory.GetParent(searchDir);
                    if (parent == null) break;

                    searchDir = parent.FullName;
                    packagesPropsPath = Path.Combine(searchDir, "Directory.Packages.props");
                    if (File.Exists(packagesPropsPath))
                    {
                        _logger.LogInformation("Found Directory.Packages.props at: {Path}", packagesPropsPath);
                        break;
                    }
                }

                if (!File.Exists(packagesPropsPath))
                {
                    result.Success = false;
                    result.Error = "Directory.Packages.props not found";
                    return result;
                }
            }

            // Load Directory.Packages.props
            var packagesDoc = XDocument.Load(packagesPropsPath);
            var packageVersionElements = packagesDoc.Descendants("PackageVersion").ToList();

            if (!packageVersionElements.Any())
            {
                _logger.LogInformation("No PackageVersion elements found in Directory.Packages.props");
                return result;
            }

            // Find all project files in the directory and subdirectories
            var projectFiles = Directory.GetFiles(directoryPath, "*.*proj", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".obj", StringComparison.OrdinalIgnoreCase) &&
                           !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) &&
                           !f.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                           !f.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                           !f.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                           !f.Contains(".legacy.") &&
                           !Path.GetFileName(f).Contains(".legacy.") &&
                           !f.Contains("_sdkmigrator_backup_"))
                .ToList();

            _logger.LogInformation("Found {Count} project files to analyze", projectFiles.Count);

            // Build project dependency graph and collect all referenced packages
            var projectGraph = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
            var referencedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First pass: Load all projects and their direct dependencies
            foreach (var projectFile in projectFiles)
            {
                try
                {
                    var projectDoc = XDocument.Load(projectFile);
                    var projectInfo = new ProjectInfo { FilePath = projectFile };

                    // Get package references (both Include and Update attributes)
                    var packageRefs = projectDoc.Descendants("PackageReference")
                        .Select(pr => pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .ToList();

                    projectInfo.DirectPackageReferences.UnionWith(packageRefs!);

                    // Get project references
                    var projectRefs = projectDoc.Descendants("ProjectReference")
                        .Select(pr => pr.Attribute("Include")?.Value)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Select(path => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, path!)))
                        .Where(fullPath => File.Exists(fullPath))
                        .ToList();

                    projectInfo.ProjectReferences.UnionWith(projectRefs);
                    projectGraph[projectFile] = projectInfo;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read project file: {ProjectFile}", projectFile);
                }
            }

            // Collect all directly referenced packages (no transitive project dependencies)
            foreach (var project in projectGraph.Values)
            {
                // Only add direct package references from each project
                if (project.DirectPackageReferences.Any())
                {
                    _logger.LogDebug("Project {Project} has {Count} direct package references: {Packages}",
                        Path.GetFileName(project.FilePath),
                        project.DirectPackageReferences.Count,
                        string.Join(", ", project.DirectPackageReferences));
                }
                referencedPackages.UnionWith(project.DirectPackageReferences);
            }

            _logger.LogInformation("Found {Count} unique package references directly referenced in projects", referencedPackages.Count);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("All directly referenced packages: {Packages}", string.Join(", ", referencedPackages.OrderBy(p => p)));
            }

            // Log detailed analysis if in debug mode
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var project in projectGraph.Values)
                {
                    _logger.LogDebug("Project {Project}:", Path.GetFileName(project.FilePath));
                    _logger.LogDebug("  Direct packages: {Packages}", string.Join(", ", project.DirectPackageReferences));
                }
            }

            // Find unused packages
            var unusedPackageElements = new List<XElement>();
            var keptPackages = new Dictionary<string, List<string>>(); // package -> list of projects using it

            foreach (var packageElement in packageVersionElements)
            {
                var packageId = packageElement.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(packageId))
                    continue;

                if (!referencedPackages.Contains(packageId))
                {
                    unusedPackageElements.Add(packageElement);
                    result.RemovedPackages.Add(packageId);
                    _logger.LogInformation("Found unused package: {PackageId}", packageId);
                }
                else
                {
                    // Track which projects use this package for logging
                    var usedBy = new List<string>();
                    foreach (var project in projectGraph.Values)
                    {
                        if (project.DirectPackageReferences.Contains(packageId))
                        {
                            usedBy.Add(Path.GetFileName(project.FilePath));
                        }
                    }

                    if (usedBy.Any())
                    {
                        keptPackages[packageId] = usedBy;
                    }
                }
            }

            // Log kept packages if in debug mode
            if (_logger.IsEnabled(LogLevel.Debug) && keptPackages.Any())
            {
                _logger.LogDebug("Packages kept in Directory.Packages.props:");
                foreach (var (packageId, usedBy) in keptPackages.OrderBy(kvp => kvp.Key))
                {
                    _logger.LogDebug("  {Package}: used by {Projects}", packageId, string.Join(", ", usedBy.Take(3)));
                    if (usedBy.Count > 3)
                    {
                        _logger.LogDebug("    ...and {Count} more projects", usedBy.Count - 3);
                    }
                }
            }

            if (unusedPackageElements.Any() && !dryRun)
            {
                // Create backup
                if (_options.CreateBackup)
                {
                    var backupPath = packagesPropsPath + ".backup";
                    File.Copy(packagesPropsPath, backupPath, overwrite: true);
                    _logger.LogInformation("Created backup: {BackupPath}", backupPath);
                }

                // Remove unused packages
                foreach (var element in unusedPackageElements)
                {
                    element.Remove();
                }

                // Clean up empty ItemGroups
                var emptyItemGroups = packagesDoc.Descendants("ItemGroup")
                    .Where(ig => !ig.HasElements && !ig.HasAttributes)
                    .ToList();
                foreach (var ig in emptyItemGroups)
                {
                    ig.Remove();
                }

                // Save the cleaned file
                // Save without XML declaration
                var saveSettings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineChars = Environment.NewLine,
                    NewLineHandling = NewLineHandling.Replace
                };

                using (var writer = XmlWriter.Create(packagesPropsPath, saveSettings))
                {
                    packagesDoc.Save(writer);
                }
                _logger.LogInformation("Updated Directory.Packages.props - removed {Count} unused packages", unusedPackageElements.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning Directory.Packages.props");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    private List<IGrouping<string, PackageReference>> MergeWithExistingPackages(
        List<IGrouping<string, PackageReference>> newPackages, 
        ExistingCpmInfo existingCpm)
    {
        var mergedPackages = new Dictionary<string, List<PackageReference>>(StringComparer.OrdinalIgnoreCase);

        // Add all new packages first
        foreach (var packageGroup in newPackages)
        {
            mergedPackages[packageGroup.Key] = packageGroup.ToList();
        }

        // Process existing packages
        if (existingCpm.HasExistingCpm)
        {
            foreach (var existingPackage in existingCpm.ExistingPackages)
            {
                if (!mergedPackages.ContainsKey(existingPackage.PackageId))
                {
                    // Create a synthetic PackageReference for existing packages not in migration
                    var syntheticPackage = new PackageReference
                    {
                        PackageId = existingPackage.PackageId,
                        Version = existingPackage.Version,
                        IsExisting = true // Flag to identify existing packages
                    };

                    mergedPackages[existingPackage.PackageId] = new List<PackageReference> { syntheticPackage };
                    
                    _logger.LogInformation("Preserving existing CPM package: {PackageId} v{Version}", 
                        existingPackage.PackageId, existingPackage.Version);
                }
                else
                {
                    // Package exists in both - resolve version conflict by choosing higher version
                    var newVersions = mergedPackages[existingPackage.PackageId].Select(p => p.Version).Distinct().ToList();
                    if (!newVersions.Contains(existingPackage.Version))
                    {
                        // Compare versions and use the higher one
                        var allVersions = newVersions.Concat(new[] { existingPackage.Version }).ToList();
                        var versionComparer = new VersionComparer();
                        var highestVersion = allVersions.OrderByDescending(v => v, versionComparer).First();
                        
                        if (highestVersion == existingPackage.Version)
                        {
                            // Existing version is higher, replace migration packages with existing
                            var syntheticPackage = new PackageReference
                            {
                                PackageId = existingPackage.PackageId,
                                Version = existingPackage.Version,
                                IsExisting = true
                            };
                            mergedPackages[existingPackage.PackageId] = new List<PackageReference> { syntheticPackage };
                            
                            _logger.LogInformation("Package {PackageId} version conflict resolved: existing version {ExistingVersion} is higher than migration versions ({NewVersions}). Using existing version.", 
                                existingPackage.PackageId, existingPackage.Version, string.Join(", ", newVersions));
                        }
                        else
                        {
                            _logger.LogInformation("Package {PackageId} version conflict resolved: migration version {HighestVersion} is higher than existing version {ExistingVersion}. Using migration version.", 
                                existingPackage.PackageId, highestVersion, existingPackage.Version);
                        }
                    }
                }
            }
        }

        // Convert back to IGrouping format
        return mergedPackages.Select(kvp => 
            new PackageGrouping(kvp.Key, kvp.Value)).Cast<IGrouping<string, PackageReference>>().ToList();
    }

    private class PackageGrouping : IGrouping<string, PackageReference>
    {
        public string Key { get; }
        private readonly List<PackageReference> _packages;

        public PackageGrouping(string key, List<PackageReference> packages)
        {
            Key = key;
            _packages = packages;
        }

        public IEnumerator<PackageReference> GetEnumerator() => _packages.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}