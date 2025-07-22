using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class MigrationAnalyzer : IMigrationAnalyzer
{
    private readonly ILogger<MigrationAnalyzer> _logger;
    private readonly IProjectFileScanner _projectFileScanner;
    private readonly IProjectParser _projectParser;
    private readonly CustomTargetAnalyzer _customTargetAnalyzer;
    private readonly ProjectTypeDetector _projectTypeDetector;
    private readonly ServiceReferenceDetector _serviceReferenceDetector;
    private readonly INativeDependencyHandler _nativeDependencyHandler;

    public MigrationAnalyzer(
        ILogger<MigrationAnalyzer> logger,
        IProjectFileScanner projectFileScanner,
        IProjectParser projectParser,
        CustomTargetAnalyzer customTargetAnalyzer,
        ProjectTypeDetector projectTypeDetector,
        ServiceReferenceDetector serviceReferenceDetector,
        INativeDependencyHandler nativeDependencyHandler)
    {
        _logger = logger;
        _projectFileScanner = projectFileScanner;
        _projectParser = projectParser;
        _customTargetAnalyzer = customTargetAnalyzer;
        _projectTypeDetector = projectTypeDetector;
        _serviceReferenceDetector = serviceReferenceDetector;
        _nativeDependencyHandler = nativeDependencyHandler;
    }

    public async Task<MigrationAnalysis> AnalyzeProjectsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var analysis = new MigrationAnalysis
        {
            DirectoryPath = directoryPath,
            AnalysisDate = DateTime.UtcNow
        };

        _logger.LogInformation("Starting migration analysis for directory: {DirectoryPath}", directoryPath);

        var projectFiles = await _projectFileScanner.ScanForProjectFilesAsync(directoryPath, cancellationToken);
        var totalEffort = 0;
        var canProceed = true;

        foreach (var projectFile in projectFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var projectAnalysis = await AnalyzeProjectAsync(projectFile, cancellationToken);
                analysis.ProjectAnalyses.Add(projectAnalysis);

                totalEffort += projectAnalysis.EstimatedManualEffortHours;
                if (!projectAnalysis.CanMigrate)
                    canProceed = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing project {ProjectPath}", projectFile);
                analysis.ProjectAnalyses.Add(new ProjectAnalysis
                {
                    ProjectPath = projectFile,
                    ProjectName = Path.GetFileName(projectFile),
                    CanMigrate = false,
                    RiskLevel = MigrationRiskLevel.Critical,
                    Issues = new List<MigrationIssue>
                    {
                        new MigrationIssue
                        {
                            Category = "Analysis Error",
                            Description = $"Failed to analyze project: {ex.Message}",
                            Severity = MigrationIssueSeverity.Critical,
                            BlocksMigration = true
                        }
                    }
                });
                canProceed = false;
            }
        }

        // Calculate overall risk
        analysis.OverallRisk = CalculateOverallRisk(analysis.ProjectAnalyses);
        analysis.EstimatedManualEffortHours = totalEffort;
        analysis.CanProceedAutomatically = canProceed && analysis.OverallRisk != MigrationRiskLevel.Critical;

        // Add global recommendations
        AddGlobalRecommendations(analysis);

        _logger.LogInformation("Migration analysis complete. Projects: {Count}, Risk: {Risk}, Manual Effort: {Hours} hours",
            analysis.ProjectAnalyses.Count, analysis.OverallRisk, analysis.EstimatedManualEffortHours);

        return analysis;
    }

    public async Task<ProjectAnalysis> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Analyzing project: {ProjectPath}", projectPath);

        var analysis = new ProjectAnalysis
        {
            ProjectPath = projectPath,
            ProjectName = Path.GetFileName(projectPath),
            CanMigrate = true
        };

        try
        {
            var parsedProject = await _projectParser.ParseProjectAsync(projectPath, cancellationToken);
            var project = parsedProject.Project;

            // Check if already SDK-style
            if (!_projectParser.IsLegacyProject(project))
            {
                analysis.CanMigrate = true; // It's "migrated" in the sense that it's already in the desired format
                analysis.RiskLevel = MigrationRiskLevel.Low; // No risk since it's already migrated
                analysis.EstimatedManualEffortHours = 0;
                analysis.Issues.Add(new MigrationIssue
                {
                    Category = "Project Format",
                    Description = "Project is already in SDK-style format - no migration needed",
                    Severity = MigrationIssueSeverity.Info,
                    BlocksMigration = false
                });
                return analysis;
            }

            // Analyze project type
            var projectTypeInfo = _projectTypeDetector.DetectProjectType(project);
            analysis.ProjectType = projectTypeInfo.DetectedTypes.FirstOrDefault();
            if (!projectTypeInfo.CanMigrate)
            {
                analysis.CanMigrate = false;
                analysis.Issues.Add(new MigrationIssue
                {
                    Category = "Project Type",
                    Description = projectTypeInfo.MigrationBlocker!,
                    Severity = MigrationIssueSeverity.Critical,
                    BlocksMigration = true
                });
            }

            // Get target framework
            analysis.CurrentTargetFramework = project.GetPropertyValue("TargetFrameworkVersion") ?? "Unknown";

            // Analyze custom targets
            analysis.CustomTargets = _customTargetAnalyzer.AnalyzeTargets(project);
            AddCustomTargetIssues(analysis);

            // Analyze build configurations
            analysis.BuildConfigurations = AnalyzeBuildConfigurations(project);
            AddBuildConfigurationIssues(analysis);

            // Analyze packages
            analysis.Packages = await AnalyzePackagesAsync(project, cancellationToken);
            AddPackageIssues(analysis);

            // Analyze project references
            analysis.ProjectReferences = AnalyzeProjectReferences(project);
            AddProjectReferenceIssues(analysis);

            // Analyze special files
            analysis.SpecialFiles = await AnalyzeSpecialFilesAsync(project, cancellationToken);
            AddSpecialFileIssues(analysis);

            // Check for service references
            var serviceRefs = _serviceReferenceDetector.DetectServiceReferences(project);
            if (serviceRefs.HasServiceReferences)
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Category = "Service References",
                    Description = $"Found {serviceRefs.ServiceReferenceNames.Count} WCF service references that require manual migration",
                    Severity = MigrationIssueSeverity.Warning,
                    Resolution = "Use dotnet-svcutil to regenerate service proxies"
                });
                analysis.EstimatedManualEffortHours += serviceRefs.ServiceReferenceNames.Count * 2;
            }

            // Check for native dependencies
            var nativeDeps = _nativeDependencyHandler.DetectNativeDependencies(project);
            if (nativeDeps.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Category = "Native Dependencies",
                    Description = $"Found {nativeDeps.Count} native dependencies that need verification",
                    Severity = MigrationIssueSeverity.Warning,
                    Resolution = "Ensure native dependencies are correctly deployed with the application"
                });
            }

            // Calculate risk level
            analysis.RiskLevel = CalculateProjectRisk(analysis);

            // Estimate manual effort
            if (analysis.EstimatedManualEffortHours == 0)
            {
                analysis.EstimatedManualEffortHours = EstimateManualEffort(analysis);
            }

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project {ProjectPath}", projectPath);
            analysis.CanMigrate = false;
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Analysis Error",
                Description = ex.Message,
                Severity = MigrationIssueSeverity.Critical,
                BlocksMigration = true
            });
            analysis.RiskLevel = MigrationRiskLevel.Critical;
            return analysis;
        }
    }

    private List<BuildConfigurationAnalysis> AnalyzeBuildConfigurations(Project project)
    {
        var configurations = new List<BuildConfigurationAnalysis>();
        var configGroups = project.Xml.PropertyGroups
            .Where(pg => !string.IsNullOrEmpty(pg.Condition))
            .ToList();

        var foundConfigs = new HashSet<string>();

        foreach (var group in configGroups)
        {
            // Extract configuration name from condition
            var configMatch = System.Text.RegularExpressions.Regex.Match(
                group.Condition,
                @"'\$\(Configuration\)'(\s*==\s*|\s*\.Equals\s*\(\s*)'([^']+)'");

            if (configMatch.Success)
            {
                var configName = configMatch.Groups[2].Value;
                if (!foundConfigs.Contains(configName))
                {
                    foundConfigs.Add(configName);

                    var configAnalysis = new BuildConfigurationAnalysis
                    {
                        ConfigurationName = configName,
                        IsStandard = configName.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                                    configName.Equals("Release", StringComparison.OrdinalIgnoreCase),
                        HasComplexConditions = group.Condition.Contains("AND", StringComparison.OrdinalIgnoreCase) ||
                                             group.Condition.Contains("OR", StringComparison.OrdinalIgnoreCase)
                    };

                    // Get properties in this configuration
                    foreach (var prop in group.Properties)
                    {
                        configAnalysis.Properties.Add($"{prop.Name}={prop.Value}");
                    }

                    configurations.Add(configAnalysis);
                }
            }
        }

        // Also check for conditional items
        var conditionalItems = project.Xml.ItemGroups
            .Where(ig => !string.IsNullOrEmpty(ig.Condition))
            .ToList();

        foreach (var itemGroup in conditionalItems)
        {
            var configMatch = System.Text.RegularExpressions.Regex.Match(
                itemGroup.Condition,
                @"'\$\(Configuration\)'(\s*==\s*|\s*\.Equals\s*\(\s*)'([^']+)'");

            if (configMatch.Success)
            {
                var configName = configMatch.Groups[2].Value;
                var config = configurations.FirstOrDefault(c => c.ConfigurationName == configName);
                if (config != null)
                {
                    config.ConditionalItems.Add($"{itemGroup.Count} conditional items");
                }
            }
        }

        return configurations;
    }

    private async Task<List<PackageAnalysis>> AnalyzePackagesAsync(Project project, CancellationToken cancellationToken)
    {
        var packages = new List<PackageAnalysis>();

        // Check packages.config
        var projectDir = Path.GetDirectoryName(project.FullPath)!;
        var packagesConfigPath = Path.Combine(projectDir, "packages.config");

        if (File.Exists(packagesConfigPath))
        {
            try
            {
                var doc = XDocument.Load(packagesConfigPath);
                var packageElements = doc.Root?.Elements("package") ?? Enumerable.Empty<XElement>();

                foreach (var package in packageElements)
                {
                    var id = package.Attribute("id")?.Value;
                    var version = package.Attribute("version")?.Value;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        var analysis = new PackageAnalysis
                        {
                            PackageId = id,
                            Version = version
                        };

                        // Check for known problematic packages
                        CheckPackageCompatibility(analysis);
                        packages.Add(analysis);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading packages.config");
            }
        }

        // Check PackageReference items
        var packageRefs = project.Items.Where(i => i.ItemType == "PackageReference");
        foreach (var pkgRef in packageRefs)
        {
            var analysis = new PackageAnalysis
            {
                PackageId = pkgRef.EvaluatedInclude,
                Version = pkgRef.GetMetadataValue("Version") ?? "Unknown"
            };

            CheckPackageCompatibility(analysis);
            packages.Add(analysis);
        }

        return packages;
    }

    private void CheckPackageCompatibility(PackageAnalysis package)
    {
        // Known problematic packages
        var problematicPackages = new Dictionary<string, string>
        {
            ["Microsoft.Bcl.Build"] = "Not needed in SDK-style projects",
            ["Microsoft.Bcl.Targets"] = "Not needed in SDK-style projects",
            ["NuGet.Build.Tasks.Pack"] = "Built into SDK-style projects",
            ["Microsoft.Net.Compilers"] = "Compiler is included with SDK",
            ["Microsoft.CodeDom.Providers.DotNetCompilerPlatform"] = "Not needed with SDK",
            ["Antlr"] = "May have issues with SDK-style projects, verify after migration"
        };

        if (problematicPackages.TryGetValue(package.PackageId, out var notes))
        {
            package.HasKnownIssues = true;
            package.MigrationNotes = notes;
        }

        // Check for packages that need manual intervention
        if (package.PackageId.StartsWith("EnterpriseLibrary", StringComparison.OrdinalIgnoreCase))
        {
            package.RequiresManualIntervention = true;
            package.MigrationNotes = "Enterprise Library packages may need configuration migration";
        }
    }

    private List<ProjectReferenceAnalysis> AnalyzeProjectReferences(Project project)
    {
        var references = new List<ProjectReferenceAnalysis>();
        var projectDir = Path.GetDirectoryName(project.FullPath)!;

        foreach (var projRef in project.Items.Where(i => i.ItemType == "ProjectReference"))
        {
            var refPath = projRef.EvaluatedInclude;
            var fullPath = Path.GetFullPath(Path.Combine(projectDir, refPath));

            var analysis = new ProjectReferenceAnalysis
            {
                ReferencePath = refPath,
                Condition = projRef.Xml.Condition,
                PathExists = File.Exists(fullPath)
            };

            if (!analysis.PathExists)
            {
                analysis.NeedsPathCorrection = true;
                // Try to find the project
                var fileName = Path.GetFileName(refPath);
                var searchRoot = Path.GetDirectoryName(projectDir);
                if (!string.IsNullOrEmpty(searchRoot) && Directory.Exists(searchRoot))
                {
                    try
                    {
                        var foundFiles = Directory.GetFiles(searchRoot, fileName, SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                       f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (foundFiles.Count == 1)
                        {
                            analysis.SuggestedPath = Path.GetRelativePath(projectDir, foundFiles[0]);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error searching for project reference");
                    }
                }
            }

            references.Add(analysis);
        }

        return references;
    }

    private async Task<List<SpecialFileAnalysis>> AnalyzeSpecialFilesAsync(Project project, CancellationToken cancellationToken)
    {
        var specialFiles = new List<SpecialFileAnalysis>();
        var projectDir = Path.GetDirectoryName(project.FullPath)!;

        // Check for T4 templates
        var t4Files = Directory.GetFiles(projectDir, "*.tt", SearchOption.AllDirectories);
        foreach (var t4File in t4Files)
        {
            specialFiles.Add(new SpecialFileAnalysis
            {
                FilePath = Path.GetRelativePath(projectDir, t4File),
                FileType = SpecialFileType.T4Template,
                CanMigrate = true,
                MigrationApproach = "Add T4 SDK package reference",
                ManualSteps = "Verify T4 template generation after migration"
            });
        }

        // Check for Entity Framework migrations
        var migrationFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains("Migrations", StringComparison.OrdinalIgnoreCase) &&
                       (f.Contains("DbMigration", StringComparison.OrdinalIgnoreCase) ||
                        f.Contains("Migration.cs", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (migrationFiles.Any())
        {
            specialFiles.Add(new SpecialFileAnalysis
            {
                FilePath = "Migrations folder",
                FileType = SpecialFileType.EntityFrameworkMigration,
                CanMigrate = true,
                MigrationApproach = "EF migrations will be preserved",
                ManualSteps = "Verify EF tooling works after migration (Add-Migration, Update-Database)"
            });
        }

        // Check for .settings files
        var settingsFiles = Directory.GetFiles(projectDir, "*.settings", SearchOption.AllDirectories);
        foreach (var settingsFile in settingsFiles)
        {
            specialFiles.Add(new SpecialFileAnalysis
            {
                FilePath = Path.GetRelativePath(projectDir, settingsFile),
                FileType = SpecialFileType.SettingsFile,
                CanMigrate = true,
                MigrationApproach = "Settings files will be preserved with generators"
            });
        }

        // Check for strong name key files
        var snkFiles = Directory.GetFiles(projectDir, "*.snk", SearchOption.AllDirectories);
        foreach (var snkFile in snkFiles)
        {
            specialFiles.Add(new SpecialFileAnalysis
            {
                FilePath = Path.GetRelativePath(projectDir, snkFile),
                FileType = SpecialFileType.StrongNameKey,
                CanMigrate = true,
                MigrationApproach = "Strong name key will be referenced in project"
            });
        }

        return specialFiles;
    }

    private void AddCustomTargetIssues(ProjectAnalysis analysis)
    {
        var complexTargets = analysis.CustomTargets
            .Where(t => t.Complexity >= TargetComplexity.Complex && !t.CanAutoMigrate)
            .ToList();

        if (complexTargets.Any())
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Custom MSBuild Targets",
                Description = $"Found {complexTargets.Count} complex custom targets that require manual review",
                Severity = MigrationIssueSeverity.Warning,
                Resolution = "Review generated target code and adjust as needed"
            });

            analysis.EstimatedManualEffortHours += complexTargets.Count;

            foreach (var target in complexTargets)
            {
                analysis.ManualStepsRequired.Add($"Review and migrate target '{target.TargetName}'");
            }
        }

        var autoMigratable = analysis.CustomTargets.Count(t => t.CanAutoMigrate);
        if (autoMigratable > 0)
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Custom MSBuild Targets",
                Description = $"{autoMigratable} custom targets can be automatically migrated",
                Severity = MigrationIssueSeverity.Info
            });
        }
    }

    private void AddBuildConfigurationIssues(ProjectAnalysis analysis)
    {
        var nonStandardConfigs = analysis.BuildConfigurations
            .Where(c => !c.IsStandard)
            .ToList();

        if (nonStandardConfigs.Any())
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Build Configurations",
                Description = $"Found {nonStandardConfigs.Count} non-standard build configurations: {string.Join(", ", nonStandardConfigs.Select(c => c.ConfigurationName))}",
                Severity = MigrationIssueSeverity.Info,
                Resolution = "Custom configurations will be preserved"
            });
        }

        var complexConditions = analysis.BuildConfigurations
            .Where(c => c.HasComplexConditions)
            .ToList();

        if (complexConditions.Any())
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Build Configurations",
                Description = "Found complex conditional logic in build configurations",
                Severity = MigrationIssueSeverity.Warning,
                Resolution = "Review conditional logic after migration to ensure correctness"
            });

            analysis.EstimatedManualEffortHours += 1;
        }
    }

    private void AddPackageIssues(ProjectAnalysis analysis)
    {
        var problematicPackages = analysis.Packages
            .Where(p => p.HasKnownIssues || p.RequiresManualIntervention)
            .ToList();

        foreach (var package in problematicPackages)
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Package References",
                Description = $"Package '{package.PackageId}' {package.MigrationNotes}",
                Severity = package.RequiresManualIntervention ? MigrationIssueSeverity.Warning : MigrationIssueSeverity.Info,
                Resolution = package.RequiresManualIntervention ? "Manual configuration may be needed" : "Package will be handled automatically"
            });
        }

        if (problematicPackages.Any(p => p.RequiresManualIntervention))
        {
            analysis.EstimatedManualEffortHours += 1;
        }
    }

    private void AddProjectReferenceIssues(ProjectAnalysis analysis)
    {
        var brokenRefs = analysis.ProjectReferences
            .Where(r => !r.PathExists)
            .ToList();

        if (brokenRefs.Any())
        {
            foreach (var brokenRef in brokenRefs)
            {
                var severity = brokenRef.SuggestedPath != null ?
                    MigrationIssueSeverity.Warning : MigrationIssueSeverity.Error;

                analysis.Issues.Add(new MigrationIssue
                {
                    Category = "Project References",
                    Description = $"Project reference not found: {brokenRef.ReferencePath}",
                    Severity = severity,
                    Resolution = brokenRef.SuggestedPath != null ?
                        $"Suggested path: {brokenRef.SuggestedPath}" :
                        "Manual path correction required",
                    BlocksMigration = severity == MigrationIssueSeverity.Error
                });
            }

            if (brokenRefs.Any(r => r.SuggestedPath == null))
            {
                analysis.CanMigrate = false;
            }
        }

        var conditionalRefs = analysis.ProjectReferences
            .Where(r => !string.IsNullOrEmpty(r.Condition))
            .ToList();

        if (conditionalRefs.Any())
        {
            analysis.Issues.Add(new MigrationIssue
            {
                Category = "Project References",
                Description = $"Found {conditionalRefs.Count} conditional project references",
                Severity = MigrationIssueSeverity.Info,
                Resolution = "Conditional logic will be preserved"
            });
        }
    }

    private void AddSpecialFileIssues(ProjectAnalysis analysis)
    {
        foreach (var fileGroup in analysis.SpecialFiles.GroupBy(f => f.FileType))
        {
            var fileType = fileGroup.Key;
            var files = fileGroup.ToList();

            switch (fileType)
            {
                case SpecialFileType.T4Template:
                    analysis.Issues.Add(new MigrationIssue
                    {
                        Category = "T4 Templates",
                        Description = $"Found {files.Count} T4 templates",
                        Severity = MigrationIssueSeverity.Warning,
                        Resolution = "T4 SDK package will be added, verify generation works"
                    });
                    analysis.ManualStepsRequired.Add("Test T4 template generation");
                    analysis.EstimatedManualEffortHours += 1;
                    break;

                case SpecialFileType.EntityFrameworkMigration:
                    analysis.Issues.Add(new MigrationIssue
                    {
                        Category = "Entity Framework",
                        Description = "Entity Framework migrations detected",
                        Severity = MigrationIssueSeverity.Warning,
                        Resolution = "Verify EF tooling (Add-Migration, Update-Database) works after migration"
                    });
                    analysis.ManualStepsRequired.Add("Test EF migration commands");
                    analysis.EstimatedManualEffortHours += 1;
                    break;
            }
        }
    }

    private MigrationRiskLevel CalculateProjectRisk(ProjectAnalysis analysis)
    {
        var criticalIssues = analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Critical);
        var errorIssues = analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Error);
        var warningIssues = analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Warning);

        if (criticalIssues > 0 || !analysis.CanMigrate)
            return MigrationRiskLevel.Critical;

        if (errorIssues > 0)
            return MigrationRiskLevel.High;

        if (warningIssues > 3 || analysis.EstimatedManualEffortHours > 4)
            return MigrationRiskLevel.High;

        if (warningIssues > 0 || analysis.EstimatedManualEffortHours > 2)
            return MigrationRiskLevel.Medium;

        return MigrationRiskLevel.Low;
    }

    private MigrationRiskLevel CalculateOverallRisk(List<ProjectAnalysis> projectAnalyses)
    {
        if (projectAnalyses.Any(p => p.RiskLevel == MigrationRiskLevel.Critical))
            return MigrationRiskLevel.Critical;

        var highRiskCount = projectAnalyses.Count(p => p.RiskLevel == MigrationRiskLevel.High);
        if (highRiskCount > projectAnalyses.Count / 3)
            return MigrationRiskLevel.High;

        var mediumRiskCount = projectAnalyses.Count(p => p.RiskLevel >= MigrationRiskLevel.Medium);
        if (mediumRiskCount > projectAnalyses.Count / 2)
            return MigrationRiskLevel.Medium;

        return MigrationRiskLevel.Low;
    }

    private int EstimateManualEffort(ProjectAnalysis analysis)
    {
        var hours = 0;

        // Base effort per issue severity
        hours += analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Critical) * 4;
        hours += analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Error) * 2;
        hours += analysis.Issues.Count(i => i.Severity == MigrationIssueSeverity.Warning) * 1;

        // Add effort for complex targets
        hours += analysis.CustomTargets.Count(t => t.Complexity >= TargetComplexity.Complex && !t.CanAutoMigrate);

        // Add effort for special files
        if (analysis.SpecialFiles.Any(f => f.FileType == SpecialFileType.T4Template))
            hours += 1;

        if (analysis.SpecialFiles.Any(f => f.FileType == SpecialFileType.EntityFrameworkMigration))
            hours += 2;

        // Add base testing effort
        hours += 1;

        return Math.Max(hours, 1); // Minimum 1 hour
    }

    private void AddGlobalRecommendations(MigrationAnalysis analysis)
    {
        if (analysis.ProjectAnalyses.Any(p => p.CustomTargets.Any(t => !t.CanAutoMigrate)))
        {
            analysis.GlobalRecommendations.Add(
                "Review all custom MSBuild targets carefully. Consider moving common targets to Directory.Build.targets for reuse.");
        }

        if (analysis.ProjectAnalyses.Any(p => p.BuildConfigurations.Count > 2))
        {
            analysis.GlobalRecommendations.Add(
                "Multiple build configurations detected. Ensure your CI/CD pipeline is updated to use all configurations.");
        }

        if (analysis.ProjectAnalyses.Any(p => p.Issues.Any(i => i.Category == "Service References")))
        {
            analysis.GlobalRecommendations.Add(
                "WCF Service References found. Install dotnet-svcutil globally: dotnet tool install --global dotnet-svcutil");
        }

        if (analysis.EstimatedManualEffortHours > 8)
        {
            analysis.GlobalRecommendations.Add(
                "Significant manual effort required. Consider migrating projects incrementally and testing thoroughly.");
        }

        analysis.GlobalRecommendations.Add(
            "Run 'dotnet build' after migration to verify all projects compile correctly.");

        analysis.GlobalRecommendations.Add(
            "Update your .gitignore file to exclude SDK-style build outputs (bin/, obj/).");
    }
}