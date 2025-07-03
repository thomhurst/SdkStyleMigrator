using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;

namespace SdkMigrator.Services;

public class LocalPackageFilesCleaner : ILocalPackageFilesCleaner
{
    private readonly ILogger<LocalPackageFilesCleaner> _logger;
    private readonly IBackupService _backupService;
    private readonly IAuditService _auditService;
    private readonly MigrationOptions _options;

    // Common package-related file extensions to clean
    private readonly HashSet<string> _packageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".xml", ".config", ".targets", ".props",
        ".pri", ".resources.dll", ".runtimeconfig.json", ".deps.json"
    };

    // Common package folder patterns
    private readonly string[] _packageFolderPatterns = new[]
    {
        "packages", "lib", "libs", "references", "Dependencies", "ExternalDependencies",
        "ThirdParty", "3rdParty", "Binaries", "bin/packages"
    };

    public LocalPackageFilesCleaner(
        ILogger<LocalPackageFilesCleaner> logger,
        IBackupService backupService,
        IAuditService auditService,
        MigrationOptions options)
    {
        _logger = logger;
        _backupService = backupService;
        _auditService = auditService;
        _options = options;
    }

    public async Task<LocalPackageCleanupResult> CleanLocalPackageFilesAsync(
        string projectDirectory,
        List<PackageReference> packageReferences,
        List<string> hintPaths,
        CancellationToken cancellationToken = default)
    {
        var result = new LocalPackageCleanupResult();

        _logger.LogInformation("Starting cleanup of local package files in {Directory}", projectDirectory);

        // Build a set of package names for quick lookup
        var packageNames = new HashSet<string>(
            packageReferences.Select(p => p.PackageId),
            StringComparer.OrdinalIgnoreCase);

        // Also include common variations
        var packageVariations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packageNames)
        {
            packageVariations.Add(package);
            packageVariations.Add(package.Replace(".", ""));
            packageVariations.Add(package.Replace(".", "_"));
            packageVariations.Add(package.Replace(".", "-"));
        }

        // Process hint paths first
        foreach (var hintPath in hintPaths.Where(h => !string.IsNullOrEmpty(h)))
        {
            await ProcessHintPath(projectDirectory, hintPath, packageVariations, result, cancellationToken);
        }

        // Find and clean package directories
        await CleanPackageDirectories(projectDirectory, packageVariations, result, cancellationToken);

        // Clean up orphaned package files in project directory
        await CleanOrphanedPackageFiles(projectDirectory, packageVariations, result, cancellationToken);

        _logger.LogInformation("Local package cleanup completed. Cleaned {FileCount} files, freed {Bytes} bytes",
            result.CleanedFiles.Count, result.TotalBytesFreed);

        return result;
    }

    private async Task ProcessHintPath(
        string projectDirectory,
        string hintPath,
        HashSet<string> packageNames,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = Path.IsPathRooted(hintPath)
                ? hintPath
                : Path.GetFullPath(Path.Combine(projectDirectory, hintPath));

            if (!File.Exists(fullPath))
                return;

            // Check if this file is from a package
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileNameWithoutExtension(fullPath);

            if (IsPackageRelatedFile(fullPath, fileName, directory, packageNames))
            {
                await CleanFile(fullPath, fileName, "HintPath reference replaced by PackageReference", result, cancellationToken);

                // Also clean related files (XML docs, PDB, config, etc.)
                await CleanRelatedFiles(fullPath, fileName, result, cancellationToken);

                // If the directory is now empty, clean it too
                await CleanEmptyDirectory(directory, result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing hint path: {HintPath}", hintPath);
            result.Errors.Add($"Error processing hint path {hintPath}: {ex.Message}");
        }
    }

    private bool IsPackageRelatedFile(string filePath, string fileName, string? directory, HashSet<string> packageNames)
    {
        // Check if file is in a known package directory
        if (!string.IsNullOrEmpty(directory))
        {
            var dirName = Path.GetFileName(directory);
            if (_packageFolderPatterns.Any(pattern =>
                directory.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Check if filename matches any package name
        foreach (var packageName in packageNames)
        {
            if (fileName.StartsWith(packageName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check parent directories for package folders
        var parentDir = directory;
        while (!string.IsNullOrEmpty(parentDir))
        {
            if (_packageFolderPatterns.Any(pattern =>
                parentDir.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            parentDir = Path.GetDirectoryName(parentDir);
        }

        return false;
    }

    private async Task CleanFile(
        string filePath,
        string packageName,
        string reason,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return;

            var fileSize = fileInfo.Length;
            var fileType = Path.GetExtension(filePath).TrimStart('.');

            if (!_options.DryRun)
            {
                // Backup the file
                var backupSession = await _backupService.GetCurrentSessionAsync();
                if (_options.CreateBackup && backupSession != null)
                {
                    await _backupService.BackupFileAsync(backupSession, filePath, cancellationToken);
                }

                // Audit the deletion
                var beforeHash = await FileHashCalculator.CalculateHashAsync(filePath, cancellationToken);

                File.Delete(filePath);

                await _auditService.LogFileDeletionAsync(new FileDeletionAudit
                {
                    FilePath = filePath,
                    BeforeHash = beforeHash,
                    FileSize = fileSize,
                    DeletionReason = reason
                }, cancellationToken);

                _logger.LogInformation("Deleted {FileType} file: {File} ({Size} bytes)",
                    fileType, filePath, fileSize);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would delete {FileType} file: {File} ({Size} bytes)",
                    fileType, filePath, fileSize);
            }

            result.CleanedFiles.Add(new CleanedFile
            {
                FilePath = filePath,
                FileSize = fileSize,
                FileType = fileType.ToUpperInvariant(),
                AssociatedPackage = packageName,
                Reason = reason
            });

            result.TotalBytesFreed += fileSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean file: {File}", filePath);
            result.Errors.Add($"Failed to clean {filePath}: {ex.Message}");
        }
    }

    private async Task CleanRelatedFiles(
        string primaryFilePath,
        string fileName,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(primaryFilePath);
        if (string.IsNullOrEmpty(directory))
            return;

        var baseFileName = Path.GetFileNameWithoutExtension(primaryFilePath);

        // Look for related files
        var relatedPatterns = new[]
        {
            $"{baseFileName}.xml",           // XML documentation
            $"{baseFileName}.pdb",           // Debug symbols
            $"{baseFileName}.config",        // Config files
            $"{baseFileName}.*.dll",         // Satellite assemblies
            $"{baseFileName}.pri",           // Package resource index
            $"{baseFileName}.deps.json",     // Dependencies file
            $"{baseFileName}.runtimeconfig.json" // Runtime config
        };

        foreach (var pattern in relatedPatterns)
        {
            try
            {
                var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    if (file != primaryFilePath) // Don't re-process the primary file
                    {
                        await CleanFile(file, fileName, "Related package file", result, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for related files with pattern: {Pattern}", pattern);
            }
        }
    }

    private async Task CleanPackageDirectories(
        string projectDirectory,
        HashSet<string> packageNames,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        // Look for common package directories
        foreach (var pattern in _packageFolderPatterns)
        {
            var searchPaths = new[]
            {
                Path.Combine(projectDirectory, pattern),
                Path.Combine(projectDirectory, "..", pattern),
                Path.Combine(projectDirectory, "..", "..", pattern)
            };

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    await ProcessPackageDirectory(searchPath, packageNames, result, cancellationToken);
                }
            }
        }
    }

    private async Task ProcessPackageDirectory(
        string packageDirectory,
        HashSet<string> packageNames,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Look for package subdirectories
            var subdirs = Directory.GetDirectories(packageDirectory);

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);

                // Check if this directory matches a package name
                foreach (var packageName in packageNames)
                {
                    if (dirName.StartsWith(packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        // This is likely a package directory
                        await CleanPackageFolder(subdir, packageName, result, cancellationToken);
                        break;
                    }
                }
            }

            // Clean the main package directory if it's empty
            await CleanEmptyDirectory(packageDirectory, result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing package directory: {Directory}", packageDirectory);
        }
    }

    private async Task CleanPackageFolder(
        string folderPath,
        string packageName,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all files in the package folder
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (_packageFileExtensions.Contains(Path.GetExtension(file)))
                {
                    await CleanFile(file, packageName, $"Package folder for {packageName}", result, cancellationToken);
                }
            }

            // Clean empty directories
            await CleanEmptyDirectoriesRecursive(folderPath, result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning package folder: {Folder}", folderPath);
            result.Errors.Add($"Error cleaning package folder {folderPath}: {ex.Message}");
        }
    }

    private async Task CleanOrphanedPackageFiles(
        string projectDirectory,
        HashSet<string> packageNames,
        LocalPackageCleanupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Look for DLLs and related files in the project directory
            foreach (var extension in _packageFileExtensions)
            {
                var files = Directory.GetFiles(projectDirectory, $"*{extension}", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);

                    if (IsPackageRelatedFile(file, fileName, projectDirectory, packageNames))
                    {
                        await CleanFile(file, fileName, "Orphaned package file", result, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning orphaned package files");
            result.Errors.Add($"Error cleaning orphaned files: {ex.Message}");
        }
    }

    private Task CleanEmptyDirectory(string? directory, LocalPackageCleanupResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Task.CompletedTask;

        try
        {
            // Don't delete if it has any files or subdirectories
            if (!Directory.GetFiles(directory).Any() && !Directory.GetDirectories(directory).Any())
            {
                if (!_options.DryRun)
                {
                    Directory.Delete(directory);
                    _logger.LogInformation("Deleted empty directory: {Directory}", directory);
                }
                else
                {
                    _logger.LogInformation("[DRY RUN] Would delete empty directory: {Directory}", directory);
                }

                result.CleanedDirectories.Add(directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete empty directory: {Directory}", directory);
        }

        return Task.CompletedTask;
    }

    private async Task CleanEmptyDirectoriesRecursive(string directory, LocalPackageCleanupResult result, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return;

        // Clean subdirectories first
        foreach (var subdir in Directory.GetDirectories(directory))
        {
            await CleanEmptyDirectoriesRecursive(subdir, result, cancellationToken);
        }

        // Then clean this directory if empty
        await CleanEmptyDirectory(directory, result, cancellationToken);
    }

    public async Task<bool> CleanPackagesFolderAsync(string solutionDirectory, CancellationToken cancellationToken = default)
    {
        var packagesPath = Path.Combine(solutionDirectory, "packages");

        if (!Directory.Exists(packagesPath))
        {
            _logger.LogDebug("No packages folder found at {Path}", packagesPath);
            return true;
        }

        try
        {
            // Check if any projects still use packages.config
            var remainingPackagesConfigs = Directory.GetFiles(
                solutionDirectory,
                "packages.config",
                SearchOption.AllDirectories)
                .Where(f => !f.Contains(".migration_backup", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (remainingPackagesConfigs.Any())
            {
                _logger.LogWarning("Cannot clean packages folder - {Count} projects still use packages.config",
                    remainingPackagesConfigs.Count);
                return false;
            }

            if (!_options.DryRun)
            {
                // Calculate total size before deletion
                var totalSize = GetDirectorySize(packagesPath);

                // Backup if needed
                var backupSession = await _backupService.GetCurrentSessionAsync();
                if (_options.CreateBackup && backupSession != null)
                {
                    // We'll just log this since backing up entire packages folder might be huge
                    _logger.LogInformation("Deleting packages folder: {Path} ({Size} bytes)", packagesPath, totalSize);
                }

                // Delete the folder
                Directory.Delete(packagesPath, recursive: true);

                _logger.LogInformation("Successfully deleted packages folder, freed {Size} bytes", totalSize);
            }
            else
            {
                var totalSize = GetDirectorySize(packagesPath);
                _logger.LogInformation("[DRY RUN] Would delete packages folder: {Path} ({Size} bytes)",
                    packagesPath, totalSize);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean packages folder");
            return false;
        }
    }

    public async Task<bool> CleanPackagesConfigAsync(
        string projectDirectory,
        bool migrationSuccessful,
        CancellationToken cancellationToken = default)
    {
        if (!migrationSuccessful)
        {
            _logger.LogDebug("Skipping packages.config cleanup - migration was not successful");
            return false;
        }

        var packagesConfigPath = Path.Combine(projectDirectory, "packages.config");
        if (!File.Exists(packagesConfigPath))
        {
            _logger.LogDebug("No packages.config found in {Directory}", projectDirectory);
            return true;
        }

        try
        {
            if (!_options.DryRun)
            {
                // Backup the file before deletion
                var backupSession = await _backupService.GetCurrentSessionAsync();
                if (_options.CreateBackup && backupSession != null)
                {
                    await _backupService.BackupFileAsync(backupSession, packagesConfigPath, cancellationToken);
                }

                // Calculate file info before deletion for audit
                var fileInfo = new FileInfo(packagesConfigPath);
                var beforeHash = await FileHashCalculator.CalculateHashAsync(packagesConfigPath, cancellationToken);

                File.Delete(packagesConfigPath);

                // Audit the deletion
                await _auditService.LogFileDeletionAsync(new FileDeletionAudit
                {
                    FilePath = packagesConfigPath,
                    BeforeHash = beforeHash,
                    FileSize = fileInfo.Length,
                    DeletionReason = "Removed obsolete packages.config after migration to PackageReference"
                }, cancellationToken);
                _logger.LogInformation("Cleaned packages.config file: {Path}", packagesConfigPath);
            }
            else
            {
                _logger.LogInformation("[DRY RUN] Would delete packages.config: {Path}", packagesConfigPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean packages.config file: {Path}", packagesConfigPath);
            return false;
        }
    }

    public async Task<LocalPackageCleanupResult> CleanLegacyProjectArtifactsAsync(
        string projectDirectory,
        bool assemblyInfoMigrated,
        CancellationToken cancellationToken = default)
    {
        var result = new LocalPackageCleanupResult();

        _logger.LogDebug("Cleaning legacy project artifacts in {Directory}", projectDirectory);

        var artifactsToClean = new List<string>();

        // Project user files
        var userFiles = Directory.GetFiles(projectDirectory, "*.csproj.user", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectDirectory, "*.vbproj.user", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(projectDirectory, "*.fsproj.user", SearchOption.TopDirectoryOnly))
            .ToList();

        artifactsToClean.AddRange(userFiles);

        // AssemblyInfo.cs if it was migrated to project properties
        if (assemblyInfoMigrated)
        {
            var assemblyInfoPath = Path.Combine(projectDirectory, "Properties", "AssemblyInfo.cs");
            if (File.Exists(assemblyInfoPath))
            {
                artifactsToClean.Add(assemblyInfoPath);
            }
        }

        // NuGet-related legacy files (excluding packages.config which has its own cleanup method)
        var nugetFiles = new[]
        {
            Path.Combine(projectDirectory, "repositories.config"),
            Path.Combine(projectDirectory, "nuget.exe")
        }.Where(File.Exists);

        artifactsToClean.AddRange(nugetFiles);

        foreach (var artifactPath in artifactsToClean)
        {
            await CleanFile(artifactPath, Path.GetFileName(artifactPath), "Legacy project artifact", result, cancellationToken);
        }

        _logger.LogInformation("Cleaned {Count} legacy project artifacts in {Directory}",
            result.CleanedFiles.Count, projectDirectory);

        return result;
    }

    public async Task<LocalPackageCleanupResult> CleanConfigTransformationFilesAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new LocalPackageCleanupResult();

        _logger.LogDebug("Cleaning configuration transformation files in {Directory}", projectDirectory);

        var transformationPatterns = new[]
        {
            "web.config.transform",
            "app.config.transform",
            "web.*.config", // web.Debug.config, web.Release.config, etc.
            "app.*.config"  // app.Debug.config, app.Release.config, etc.
        };

        var transformFiles = new List<string>();

        foreach (var pattern in transformationPatterns)
        {
            try
            {
                var matchingFiles = Directory.GetFiles(projectDirectory, pattern, SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith("web.config", StringComparison.OrdinalIgnoreCase) &&
                               !f.EndsWith("app.config", StringComparison.OrdinalIgnoreCase)) // Keep main config files
                    .ToList();

                transformFiles.AddRange(matchingFiles);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for transformation files with pattern {Pattern}", pattern);
            }
        }

        foreach (var transformFile in transformFiles.Distinct())
        {
            await CleanFile(transformFile, Path.GetFileName(transformFile), "Configuration transformation file", result, cancellationToken);
        }

        _logger.LogInformation("Cleaned {Count} configuration transformation files in {Directory}",
            result.CleanedFiles.Count, projectDirectory);

        return result;
    }

    private long GetDirectorySize(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }
}