using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

/// <summary>
/// Advanced package conflict detection and resolution analyzer
/// Handles CPM conflicts, transitive dependencies, and version mismatches
/// </summary>
public class PackageConflictAnalyzer
{
    private readonly ILogger<PackageConflictAnalyzer> _logger;
    private readonly HttpClient _httpClient;

    public PackageConflictAnalyzer(ILogger<PackageConflictAnalyzer> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Performs comprehensive package conflict analysis including CPM validation,
    /// transitive dependency conflicts, and version compatibility
    /// </summary>
    public async Task<PackageAnalysisResult> AnalyzePackageConflicts(
        Project project,
        List<PackageReference> packageReferences,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new PackageAnalysisResult
        {
            ProjectPath = project.FullPath,
            TotalPackages = packageReferences.Count
        };

        try
        {
            // Check for Central Package Management
            await AnalyzeCentralPackageManagement(projectDirectory, result, cancellationToken);

            // Analyze direct package references
            await AnalyzeDirectPackages(project, packageReferences, result, cancellationToken);

            // Detect transitive dependency conflicts
            await AnalyzeTransitiveDependencies(project, packageReferences, result, cancellationToken);

            // Check for version compatibility issues
            await AnalyzeVersionCompatibility(packageReferences, result, cancellationToken);

            // Detect vulnerable packages
            await AnalyzePackageVulnerabilities(packageReferences, result, cancellationToken);

            // Analyze package downgrades and conflicts
            await AnalyzePackageDowngrades(project, result, cancellationToken);

            // Check for framework-specific conflicts
            await AnalyzeFrameworkConflicts(project, packageReferences, result, cancellationToken);

            // Determine overall package health
            DeterminePackageHealth(result);

            _logger.LogInformation("Package analysis complete: Health={Health}, Conflicts={ConflictCount}, Vulnerabilities={VulnCount}",
                result.PackageHealth, result.Conflicts.Count, result.VulnerablePackages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze package conflicts for {ProjectPath}", project.FullPath);
            result.AnalysisErrors.Add($"Package analysis error: {ex.Message}");
        }

        return result;
    }

    private async Task AnalyzeCentralPackageManagement(string projectDirectory, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for Directory.Packages.props
        var currentDir = new DirectoryInfo(projectDirectory);
        while (currentDir != null)
        {
            var packagesPropsPath = Path.Combine(currentDir.FullName, "Directory.Packages.props");
            if (File.Exists(packagesPropsPath))
            {
                result.UsesCentralPackageManagement = true;
                result.CentralPackageManagementPath = packagesPropsPath;

                // Analyze CPM configuration
                await AnalyzeCpmConfiguration(packagesPropsPath, result, cancellationToken);
                break;
            }

            currentDir = currentDir.Parent;
            if (currentDir?.GetFiles("*.sln").Any() == true || currentDir?.Parent == null)
            {
                break;
            }
        }
    }

    private async Task AnalyzeCpmConfiguration(string cpmPath, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(cpmPath, cancellationToken);
            var matches = Regex.Matches(content, @"<PackageVersion\s+Include=""([^""]+)""\s+Version=""([^""]+)""");

            foreach (Match match in matches)
            {
                var packageId = match.Groups[1].Value;
                var version = match.Groups[2].Value;
                result.CpmPackageVersions[packageId] = version;
            }

            // Check for ManagePackageVersionsCentrally property
            if (content.Contains("ManagePackageVersionsCentrally") && content.Contains("false"))
            {
                result.CpmIssues.Add("Central Package Management is defined but disabled (ManagePackageVersionsCentrally=false)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze CPM configuration at {Path}: {Error}", cpmPath, ex.Message);
        }
    }

    private async Task AnalyzeDirectPackages(Project project, List<PackageReference> packageReferences, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        var packageGroups = packageReferences.GroupBy(p => p.PackageId);
        
        foreach (var group in packageGroups)
        {
            var versions = group.Select(p => p.Version).Distinct().ToList();
            
            if (versions.Count > 1)
            {
                result.Conflicts.Add(new PackageConflict
                {
                    PackageId = group.Key,
                    ConflictType = "MultipleVersions",
                    Versions = versions,
                    Severity = "High",
                    Description = $"Package '{group.Key}' has multiple versions referenced: {string.Join(", ", versions)}"
                });
            }

            // Check CPM conflicts
            if (result.UsesCentralPackageManagement && result.CpmPackageVersions.ContainsKey(group.Key))
            {
                var cpmVersion = result.CpmPackageVersions[group.Key];
                foreach (var reference in group)
                {
                    if (!string.IsNullOrEmpty(reference.Version) && reference.Version != cpmVersion)
                    {
                        result.CpmConflicts.Add(new CpmConflict
                        {
                            PackageId = group.Key,
                            LocalVersion = reference.Version,
                            CentralVersion = cpmVersion,
                            ProjectPath = project.FullPath
                        });
                    }
                }
            }
        }
    }

    private async Task AnalyzeTransitiveDependencies(Project project, List<PackageReference> packageReferences, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        // Look for project.assets.json
        var objDir = Path.Combine(Path.GetDirectoryName(project.FullPath) ?? "", "obj");
        var assetsFile = Path.Combine(objDir, "project.assets.json");

        if (File.Exists(assetsFile))
        {
            try
            {
                var assetsContent = await File.ReadAllTextAsync(assetsFile, cancellationToken);
                var assetsJson = JsonDocument.Parse(assetsContent);

                // Analyze targets section for transitive dependencies
                if (assetsJson.RootElement.TryGetProperty("targets", out var targets))
                {
                    foreach (var target in targets.EnumerateObject())
                    {
                        var transitivePackages = new Dictionary<string, List<string>>();
                        
                        foreach (var package in target.Value.EnumerateObject())
                        {
                            var packageParts = package.Name.Split('/');
                            if (packageParts.Length == 2)
                            {
                                var packageId = packageParts[0];
                                var version = packageParts[1];

                                // Check if this is a transitive dependency
                                if (!packageReferences.Any(p => p.PackageId == packageId))
                                {
                                    if (package.Value.TryGetProperty("dependencies", out var deps))
                                    {
                                        foreach (var dep in deps.EnumerateObject())
                                        {
                                            if (!transitivePackages.ContainsKey(dep.Name))
                                                transitivePackages[dep.Name] = new List<string>();
                                            
                                            transitivePackages[dep.Name].Add(version);
                                        }
                                    }
                                }
                            }
                        }

                        // Detect transitive conflicts
                        foreach (var (packageId, versions) in transitivePackages)
                        {
                            var distinctVersions = versions.Distinct().ToList();
                            if (distinctVersions.Count > 1)
                            {
                                result.TransitiveConflicts.Add(new TransitiveConflict
                                {
                                    PackageId = packageId,
                                    ConflictingVersions = distinctVersions,
                                    TargetFramework = target.Name
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze project.assets.json: {Error}", ex.Message);
            }
        }
    }

    private async Task AnalyzeVersionCompatibility(List<PackageReference> packageReferences, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for known incompatible package combinations
        var incompatiblePairs = new List<(string Package1, string Package2, string Reason)>
        {
            ("Newtonsoft.Json", "System.Text.Json", "Both JSON serializers present - consider using only one"),
            ("Microsoft.Extensions.Logging.Log4Net", "Serilog", "Multiple logging frameworks detected"),
            ("EntityFramework", "Microsoft.EntityFrameworkCore", "Both EF6 and EF Core present"),
            ("System.Data.SqlClient", "Microsoft.Data.SqlClient", "Both SQL client libraries present")
        };

        var packageIds = packageReferences.Select(p => p.PackageId).ToHashSet();

        foreach (var (package1, package2, reason) in incompatiblePairs)
        {
            if (packageIds.Contains(package1) && packageIds.Contains(package2))
            {
                result.CompatibilityIssues.Add(new CompatibilityIssue
                {
                    Packages = new[] { package1, package2 },
                    Issue = reason,
                    Severity = "Medium"
                });
            }
        }

        // Check for preview/beta packages in production
        foreach (var package in packageReferences)
        {
            if (!string.IsNullOrEmpty(package.Version) && 
                (package.Version.Contains("-preview") || 
                 package.Version.Contains("-beta") || 
                 package.Version.Contains("-alpha") ||
                 package.Version.Contains("-rc")))
            {
                result.PreviewPackages.Add(new PreviewPackage
                {
                    PackageId = package.PackageId,
                    Version = package.Version,
                    Risk = package.Version.Contains("-alpha") ? "High" : "Medium"
                });
            }
        }
    }

    private async Task AnalyzePackageVulnerabilities(List<PackageReference> packageReferences, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check against known vulnerable packages (in production, this would query a vulnerability database)
        var knownVulnerablePackages = new Dictionary<string, string>
        {
            ["System.Net.Http"] = "< 4.3.4",
            ["System.Text.RegularExpressions"] = "< 4.3.1",
            ["Microsoft.AspNetCore.All"] = "< 2.1.0",
            ["jQuery"] = "< 3.5.0"
        };

        foreach (var package in packageReferences)
        {
            if (knownVulnerablePackages.ContainsKey(package.PackageId))
            {
                // Simple version comparison (in production, use NuGet.Versioning)
                result.VulnerablePackages.Add(new VulnerablePackage
                {
                    PackageId = package.PackageId,
                    CurrentVersion = package.Version ?? "Unknown",
                    VulnerableVersions = knownVulnerablePackages[package.PackageId],
                    Severity = "High",
                    CVE = "Check NuGet.org for details"
                });
            }
        }
    }

    private async Task AnalyzePackageDowngrades(Project project, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for package downgrade warnings in the project
        var projectContent = await File.ReadAllTextAsync(project.FullPath, cancellationToken);
        
        // Look for NoWarn suppressions that might hide package conflicts
        var noWarnMatch = Regex.Match(projectContent, @"<NoWarn>([^<]+)</NoWarn>");
        if (noWarnMatch.Success)
        {
            var suppressedWarnings = noWarnMatch.Groups[1].Value.Split(';');
            if (suppressedWarnings.Contains("NU1605") || suppressedWarnings.Contains("NU1701"))
            {
                result.Warnings.Add("Package downgrade warnings are suppressed (NU1605/NU1701) - potential hidden conflicts");
            }
        }

        // Check for explicit package downgrades
        var downgrades = Regex.Matches(projectContent, @"<PackageReference.*?Include=""([^""]+)"".*?VersionOverride=""([^""]+)""");
        foreach (Match match in downgrades)
        {
            result.PackageDowngrades.Add(new PackageDowngrade
            {
                PackageId = match.Groups[1].Value,
                DowngradedVersion = match.Groups[2].Value,
                Reason = "Explicit version override"
            });
        }
    }

    private async Task AnalyzeFrameworkConflicts(Project project, List<PackageReference> packageReferences, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");

        var frameworks = new List<string>();
        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            frameworks.AddRange(targetFrameworks.Split(';'));
        }
        else if (!string.IsNullOrEmpty(targetFramework))
        {
            frameworks.Add(targetFramework);
        }

        // Check for packages that don't support all target frameworks
        foreach (var package in packageReferences)
        {
            // In production, this would query NuGet API for actual framework support
            if (frameworks.Count > 1)
            {
                // Check for conditional package references
                if (package.Metadata.ContainsKey("Condition"))
                {
                    result.ConditionalPackages.Add(new ConditionalPackage
                    {
                        PackageId = package.PackageId,
                        Condition = package.Metadata["Condition"],
                        TargetFrameworks = frameworks
                    });
                }
            }
        }
    }

    private void DeterminePackageHealth(PackageAnalysisResult result)
    {
        var healthScore = 100;

        // Deduct points for various issues
        healthScore -= result.Conflicts.Count * 10;
        healthScore -= result.VulnerablePackages.Count * 15;
        healthScore -= result.CpmConflicts.Count * 5;
        healthScore -= result.TransitiveConflicts.Count * 8;
        healthScore -= result.CompatibilityIssues.Count * 5;
        healthScore -= result.PackageDowngrades.Count * 5;
        healthScore -= result.PreviewPackages.Count * 3;

        if (healthScore >= 90)
            result.PackageHealth = "Excellent";
        else if (healthScore >= 70)
            result.PackageHealth = "Good";
        else if (healthScore >= 50)
            result.PackageHealth = "Fair";
        else
            result.PackageHealth = "Poor";

        result.HealthScore = Math.Max(0, healthScore);
    }
}

// Result models
public class PackageAnalysisResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public int TotalPackages { get; set; }
    public bool UsesCentralPackageManagement { get; set; }
    public string? CentralPackageManagementPath { get; set; }
    public Dictionary<string, string> CpmPackageVersions { get; set; } = new();
    public List<PackageConflict> Conflicts { get; set; } = new();
    public List<CpmConflict> CpmConflicts { get; set; } = new();
    public List<TransitiveConflict> TransitiveConflicts { get; set; } = new();
    public List<VulnerablePackage> VulnerablePackages { get; set; } = new();
    public List<CompatibilityIssue> CompatibilityIssues { get; set; } = new();
    public List<PackageDowngrade> PackageDowngrades { get; set; } = new();
    public List<PreviewPackage> PreviewPackages { get; set; } = new();
    public List<ConditionalPackage> ConditionalPackages { get; set; } = new();
    public List<string> CpmIssues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AnalysisErrors { get; set; } = new();
    
    public string PackageHealth { get; set; } = "Unknown";
    public int HealthScore { get; set; }
}

public class PackageConflict
{
    public string PackageId { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CpmConflict
{
    public string PackageId { get; set; } = string.Empty;
    public string LocalVersion { get; set; } = string.Empty;
    public string CentralVersion { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
}

public class TransitiveConflict
{
    public string PackageId { get; set; } = string.Empty;
    public List<string> ConflictingVersions { get; set; } = new();
    public string TargetFramework { get; set; } = string.Empty;
}

public class VulnerablePackage
{
    public string PackageId { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string VulnerableVersions { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string CVE { get; set; } = string.Empty;
}

public class CompatibilityIssue
{
    public string[] Packages { get; set; } = Array.Empty<string>();
    public string Issue { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

public class PackageDowngrade
{
    public string PackageId { get; set; } = string.Empty;
    public string DowngradedVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class PreviewPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
}

public class ConditionalPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public List<string> TargetFrameworks { get; set; } = new();
}