using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.ProjectModel;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// Resolves assemblies provided by NuGet packages using project.assets.json
/// when possible, with fallback to direct package inspection.
/// </summary>
public class NuGetAssetsResolver
{
    private readonly ILogger<NuGetAssetsResolver> _logger;
    private readonly string _globalPackagesFolder;
    private readonly ConcurrentDictionary<string, HashSet<AssemblyInfo>> _cache = new();

    public NuGetAssetsResolver(ILogger<NuGetAssetsResolver> logger)
    {
        _logger = logger;
        
        // Get the global packages folder
        _globalPackagesFolder = Environment.GetEnvironmentVariable("NUGET_PACKAGES") 
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    /// <summary>
    /// Attempts to resolve all assemblies provided by the given packages using the most accurate method available.
    /// </summary>
    public async Task<AssemblyResolutionResult> ResolvePackageAssembliesAsync(
        List<Models.PackageReference> packages, 
        string targetFramework,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new AssemblyResolutionResult();
        
        // Try the high-fidelity approach first
        if (await TryResolveViaProjectAssetsJsonAsync(packages, targetFramework, projectDirectory, result, cancellationToken))
        {
            result.ResolutionMethod = "project.assets.json";
            _logger.LogInformation("Successfully resolved assemblies using project.assets.json (high fidelity)");
            return result;
        }

        // Fall back to direct package inspection
        _logger.LogWarning("Failed to resolve via project.assets.json, falling back to direct package inspection");
        await ResolveViaDirectPackageInspectionAsync(packages, targetFramework, result, cancellationToken);
        result.ResolutionMethod = "direct package inspection";
        result.IsPartialResolution = true;
        
        return result;
    }

    private async Task<bool> TryResolveViaProjectAssetsJsonAsync(
        List<Models.PackageReference> packages,
        string targetFramework,
        string projectDirectory,
        AssemblyResolutionResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create a temporary project file
            var tempProjectPath = Path.Combine(Path.GetTempPath(), $"SdkMigrator_{Guid.NewGuid()}.csproj");
            var tempObjPath = Path.Combine(Path.GetDirectoryName(tempProjectPath)!, "obj");
            
            try
            {
                // Generate minimal SDK-style project
                var projectContent = GenerateTemporaryProject(packages, targetFramework);
                await File.WriteAllTextAsync(tempProjectPath, projectContent, cancellationToken);
                
                // Run dotnet restore
                var restoreResult = await RunDotnetRestoreAsync(tempProjectPath, cancellationToken);
                if (!restoreResult.Success)
                {
                    _logger.LogWarning("dotnet restore failed: {Error}", restoreResult.Error);
                    result.Warnings.Add($"dotnet restore failed: {restoreResult.Error}");
                    return false;
                }

                // Parse project.assets.json
                var assetsJsonPath = Path.Combine(tempObjPath, "project.assets.json");
                if (!File.Exists(assetsJsonPath))
                {
                    _logger.LogWarning("project.assets.json not found at {Path}", assetsJsonPath);
                    return false;
                }

                var lockFile = LockFileUtilities.GetLockFile(assetsJsonPath, NullLogger.Instance);
                if (lockFile == null)
                {
                    _logger.LogWarning("Failed to parse project.assets.json");
                    return false;
                }

                // Extract assemblies from lock file
                var framework = NuGetFramework.Parse(targetFramework);
                var target = lockFile.GetTarget(framework, runtimeIdentifier: null);
                
                if (target == null)
                {
                    _logger.LogWarning("No target found for framework {Framework}", targetFramework);
                    return false;
                }

                foreach (var library in target.Libraries)
                {
                    // Skip if not a package type
                    if (library.Type != "package")
                        continue;

                    // Collect compile-time assemblies
                    foreach (var compileItem in library.CompileTimeAssemblies)
                    {
                        var assemblyPath = compileItem.Path;
                        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                        
                        result.ResolvedAssemblies.Add(new AssemblyInfo
                        {
                            Name = assemblyName,
                            PackageId = library.Name,
                            PackageVersion = library.Version.ToString(),
                            IsTransitive = !packages.Any(p => p.PackageId.Equals(library.Name, StringComparison.OrdinalIgnoreCase))
                        });
                    }

                    // Also collect runtime assemblies
                    foreach (var runtimeItem in library.RuntimeAssemblies)
                    {
                        var assemblyPath = runtimeItem.Path;
                        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                        
                        result.ResolvedAssemblies.Add(new AssemblyInfo
                        {
                            Name = assemblyName,
                            PackageId = library.Name,
                            PackageVersion = library.Version.ToString(),
                            IsTransitive = !packages.Any(p => p.PackageId.Equals(library.Name, StringComparison.OrdinalIgnoreCase))
                        });
                    }
                }

                _logger.LogInformation("Resolved {Count} assemblies from {PackageCount} packages (including transitive)",
                    result.ResolvedAssemblies.Count, target.Libraries.Count);
                
                return true;
            }
            finally
            {
                // Clean up temporary files
                try
                {
                    if (File.Exists(tempProjectPath))
                        File.Delete(tempProjectPath);
                    if (Directory.Exists(tempObjPath))
                        Directory.Delete(tempObjPath, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temporary files");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TryResolveViaProjectAssetsJsonAsync");
            result.Warnings.Add($"Exception during project.assets.json resolution: {ex.Message}");
            return false;
        }
    }

    private string GenerateTemporaryProject(List<Models.PackageReference> packages, string targetFramework)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine("  </PropertyGroup>");
        
        if (packages.Any())
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var package in packages)
            {
                sb.AppendLine($"    <PackageReference Include=\"{package.PackageId}\" Version=\"{package.Version}\" />");
            }
            sb.AppendLine("  </ItemGroup>");
        }
        
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private async Task<RestoreResult> RunDotnetRestoreAsync(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore \"{projectPath}\" --verbosity minimal",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath)
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) => 
            {
                if (args.Data != null)
                    outputBuilder.AppendLine(args.Data);
            };
            
            process.ErrorDataReceived += (sender, args) => 
            {
                if (args.Data != null)
                    errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process with timeout
            var completed = process.WaitForExit(30000); // 30 second timeout
            
            if (!completed)
            {
                try { process.Kill(); } catch { }
                return new RestoreResult 
                { 
                    Success = false, 
                    Error = "dotnet restore timed out after 30 seconds" 
                };
            }

            var exitCode = process.ExitCode;
            var error = errorBuilder.ToString();
            
            return new RestoreResult
            {
                Success = exitCode == 0,
                Error = exitCode != 0 ? (string.IsNullOrWhiteSpace(error) ? outputBuilder.ToString() : error) : null
            };
        }
        catch (Exception ex)
        {
            return new RestoreResult
            {
                Success = false,
                Error = $"Failed to execute dotnet restore: {ex.Message}"
            };
        }
    }

    private async Task ResolveViaDirectPackageInspectionAsync(
        List<Models.PackageReference> packages,
        string targetFramework,
        AssemblyResolutionResult result,
        CancellationToken cancellationToken)
    {
        var framework = NuGetFramework.Parse(targetFramework);
        
        foreach (var package in packages)
        {
            try
            {
                // Try to find the package in the global packages folder
                var packagePath = Path.Combine(_globalPackagesFolder, package.PackageId.ToLowerInvariant(), package.Version);
                
                if (!Directory.Exists(packagePath))
                {
                    _logger.LogWarning("Package not found in global packages folder: {Package} {Version}", 
                        package.PackageId, package.Version);
                    result.Warnings.Add($"Package {package.PackageId} {package.Version} not found in local cache");
                    continue;
                }

                var nupkgFile = Path.Combine(packagePath, $"{package.PackageId.ToLowerInvariant()}.{package.Version}.nupkg");
                if (!File.Exists(nupkgFile))
                {
                    // Sometimes the .nupkg is not in the extracted folder, just inspect the lib folder directly
                    await InspectExtractedPackageAsync(packagePath, package, framework, result);
                }
                else
                {
                    // Read the .nupkg file
                    using var reader = new PackageArchiveReader(nupkgFile);
                    await InspectPackageAsync(reader, package, framework, result, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inspecting package {Package}", package.PackageId);
                result.Warnings.Add($"Error inspecting package {package.PackageId}: {ex.Message}");
            }
        }
    }

    private async Task InspectPackageAsync(
        PackageArchiveReader reader,
        Models.PackageReference package,
        NuGetFramework targetFramework,
        AssemblyResolutionResult result,
        CancellationToken cancellationToken)
    {
        // Get lib items
        var libItems = await reader.GetLibItemsAsync(cancellationToken);
        var referenceItems = await reader.GetReferenceItemsAsync(cancellationToken);
        
        // Find the best matching framework
        var libFramework = GetBestMatchingFramework(libItems, targetFramework);
        var refFramework = GetBestMatchingFramework(referenceItems, targetFramework);
        
        // Collect assemblies from lib
        if (libFramework != null)
        {
            var items = libItems.FirstOrDefault(g => g.TargetFramework.Equals(libFramework))?.Items ?? Enumerable.Empty<string>();
            foreach (var item in items.Where(i => i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(item);
                result.ResolvedAssemblies.Add(new AssemblyInfo
                {
                    Name = assemblyName,
                    PackageId = package.PackageId,
                    PackageVersion = package.Version,
                    IsTransitive = false
                });
            }
        }
        
        // Collect assemblies from ref (compile-time references)
        if (refFramework != null)
        {
            var items = referenceItems.FirstOrDefault(g => g.TargetFramework.Equals(refFramework))?.Items ?? Enumerable.Empty<string>();
            foreach (var item in items.Where(i => i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(item);
                result.ResolvedAssemblies.Add(new AssemblyInfo
                {
                    Name = assemblyName,
                    PackageId = package.PackageId,
                    PackageVersion = package.Version,
                    IsTransitive = false
                });
            }
        }
    }

    private async Task InspectExtractedPackageAsync(
        string packagePath,
        Models.PackageReference package,
        NuGetFramework targetFramework,
        AssemblyResolutionResult result)
    {
        var libPath = Path.Combine(packagePath, "lib");
        if (!Directory.Exists(libPath))
            return;

        // Find the best matching framework folder
        var frameworkFolders = Directory.GetDirectories(libPath);
        string? bestMatch = null;
        var bestFramework = NuGetFramework.UnsupportedFramework;
        
        foreach (var folder in frameworkFolders)
        {
            var folderName = Path.GetFileName(folder);
            try
            {
                var framework = NuGetFramework.Parse(folderName);
                if (!framework.IsUnsupported && DefaultCompatibilityProvider.Instance.IsCompatible(targetFramework, framework))
                {
                    if (bestFramework.IsUnsupported || framework.Version > bestFramework.Version)
                    {
                        bestFramework = framework;
                        bestMatch = folder;
                    }
                }
            }
            catch
            {
                // Not a valid framework folder
            }
        }

        if (bestMatch != null)
        {
            var dlls = Directory.GetFiles(bestMatch, "*.dll");
            foreach (var dll in dlls)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dll);
                result.ResolvedAssemblies.Add(new AssemblyInfo
                {
                    Name = assemblyName,
                    PackageId = package.PackageId,
                    PackageVersion = package.Version,
                    IsTransitive = false
                });
            }
        }
    }

    private NuGetFramework? GetBestMatchingFramework(
        IEnumerable<FrameworkSpecificGroup> groups,
        NuGetFramework targetFramework)
    {
        var frameworks = groups.Select(g => g.TargetFramework).ToList();
        if (!frameworks.Any())
            return null;

        // Try to find exact match first
        var exactMatch = frameworks.FirstOrDefault(f => f.Equals(targetFramework));
        if (exactMatch != null)
            return exactMatch;

        // Find compatible frameworks
        var compatibleFrameworks = frameworks
            .Where(f => DefaultCompatibilityProvider.Instance.IsCompatible(targetFramework, f))
            .ToList();

        if (!compatibleFrameworks.Any())
            return null;

        // Return the highest version that's compatible
        return compatibleFrameworks
            .OrderByDescending(f => f.Version)
            .FirstOrDefault();
    }

    private class RestoreResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}

public class AssemblyResolutionResult
{
    public HashSet<AssemblyInfo> ResolvedAssemblies { get; } = new(AssemblyInfoComparer.Instance);
    public List<string> Warnings { get; } = new();
    public string ResolutionMethod { get; set; } = "unknown";
    public bool IsPartialResolution { get; set; }
}

public class AssemblyInfo
{
    public required string Name { get; set; }
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public bool IsTransitive { get; set; }
    public string? Culture { get; set; }
    public string? PublicKeyToken { get; set; }
    public Version? Version { get; set; }
    
    public string FullName
    {
        get
        {
            var parts = new List<string> { Name };
            if (Version != null)
                parts.Add($"Version={Version}");
            if (!string.IsNullOrEmpty(Culture))
                parts.Add($"Culture={Culture}");
            if (!string.IsNullOrEmpty(PublicKeyToken))
                parts.Add($"PublicKeyToken={PublicKeyToken}");
            return string.Join(", ", parts);
        }
    }
}

public class AssemblyInfoComparer : IEqualityComparer<AssemblyInfo>
{
    public static readonly AssemblyInfoComparer Instance = new();

    public bool Equals(AssemblyInfo? x, AssemblyInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;
        
        // Compare by name primarily, but also consider version if available
        return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
               (x.Version == null || y.Version == null || x.Version.Equals(y.Version));
    }

    public int GetHashCode(AssemblyInfo obj)
    {
        return obj.Name.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }
}

// Minimal NuGet logger implementation
internal class NullLogger : NuGet.Common.ILogger
{
    public static readonly NullLogger Instance = new();

    public void Log(NuGet.Common.LogLevel level, string data) { }
    public void Log(NuGet.Common.ILogMessage message) { }
    public Task LogAsync(NuGet.Common.LogLevel level, string data) => Task.CompletedTask;
    public Task LogAsync(NuGet.Common.ILogMessage message) => Task.CompletedTask;
    public void LogDebug(string data) { }
    public void LogVerbose(string data) { }
    public void LogInformation(string data) { }
    public void LogMinimal(string data) { }
    public void LogWarning(string data) { }
    public void LogError(string data) { }
    public void LogInformationSummary(string data) { }
}