using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class PostMigrationValidator : IPostMigrationValidator
{
    private readonly ILogger<PostMigrationValidator> _logger;
    private readonly IProjectParser _projectParser;
    private readonly MigrationOptions _options;

    public PostMigrationValidator(
        ILogger<PostMigrationValidator> logger,
        IProjectParser projectParser,
        MigrationOptions options)
    {
        _logger = logger;
        _projectParser = projectParser;
        _options = options;
    }

    public async Task<PostMigrationValidationResult> ValidateProjectAsync(
        string projectPath,
        MigrationResult migrationResult,
        CancellationToken cancellationToken = default)
    {
        var result = new PostMigrationValidationResult
        {
            ProjectPath = projectPath,
            IsValid = true
        };

        try
        {
            if (!File.Exists(projectPath))
            {
                result.IsValid = false;
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Critical,
                    Category = "File",
                    Message = "Project file not found",
                    SuggestedFix = "Ensure the migration completed successfully"
                });
                return result;
            }

            // Load and validate XML structure
            var doc = XDocument.Load(projectPath);
            ValidateXmlStructure(doc, result);

            // Load as MSBuild project for deeper validation
            var projectCollection = new ProjectCollection();
            try
            {
                var project = new Project(projectPath, null, null, projectCollection);

                // Validate SDK attribute
                ValidateSdkAttribute(doc, result);

                // Validate target framework
                ValidateTargetFramework(project, result);

                // Validate package references
                await ValidatePackageReferencesAsync(project, migrationResult, result, cancellationToken);

                // Validate removed elements were properly handled
                ValidateRemovedElements(project, migrationResult, result);

                // Validate project structure
                ValidateProjectStructure(project, result);

                // Validate output paths
                ValidateOutputPaths(project, result);

                // Validate dependencies
                ValidateDependencies(project, result);

                // Check for common migration issues
                CheckCommonMigrationIssues(project, doc, result);
            }
            finally
            {
                projectCollection.UnloadAllProjects();
                projectCollection.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating project {ProjectPath}", projectPath);
            result.IsValid = false;
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Critical,
                Category = "Validation",
                Message = $"Failed to validate project: {ex.Message}",
                SuggestedFix = "Check the project file for syntax errors"
            });
        }

        // Set IsValid based on critical/error issues
        result.IsValid = !result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Critical ||
            i.Severity == ValidationSeverity.Error);

        return result;
    }

    public async Task<PostMigrationValidationReport> ValidateSolutionAsync(
        string solutionDirectory,
        IEnumerable<MigrationResult> migrationResults,
        CancellationToken cancellationToken = default)
    {
        var report = new PostMigrationValidationReport();
        var validationTasks = new List<Task<PostMigrationValidationResult>>();

        foreach (var migrationResult in migrationResults.Where(r => r.Success))
        {
            var projectPath = migrationResult.OutputPath ?? migrationResult.ProjectPath;
            validationTasks.Add(ValidateProjectAsync(projectPath, migrationResult, cancellationToken));
        }

        var results = await Task.WhenAll(validationTasks);

        report.ProjectResults.AddRange(results);
        report.TotalProjects = results.Length;
        report.ValidProjects = results.Count(r => r.IsValid);
        report.ProjectsWithIssues = results.Count(r => r.Issues.Any());

        // Aggregate issues by category and severity
        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                if (!report.IssuesByCategory.ContainsKey(issue.Category))
                    report.IssuesByCategory[issue.Category] = 0;
                report.IssuesByCategory[issue.Category]++;

                if (!report.IssuesBySeverity.ContainsKey(issue.Severity))
                    report.IssuesBySeverity[issue.Severity] = 0;
                report.IssuesBySeverity[issue.Severity]++;
            }
        }

        LogValidationReport(report);
        return report;
    }

    private void ValidateXmlStructure(XDocument doc, PostMigrationValidationResult result)
    {
        if (doc.Root == null || doc.Root.Name != "Project")
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Critical,
                Category = "Structure",
                Message = "Invalid root element - expected 'Project'",
                SuggestedFix = "Ensure the project file has a valid SDK-style structure"
            });
            return;
        }

        // Check for legacy project structure
        if (doc.Root.Attribute("ToolsVersion") != null)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Category = "Structure",
                Message = "Project still contains ToolsVersion attribute",
                SuggestedFix = "Remove the ToolsVersion attribute from the Project element"
            });
        }

        if (doc.Root.Attribute("xmlns") != null)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Category = "Structure",
                Message = "Project contains xmlns attribute which is not needed in SDK-style projects",
                SuggestedFix = "Remove the xmlns attribute from the Project element"
            });
        }
    }

    private void ValidateSdkAttribute(XDocument doc, PostMigrationValidationResult result)
    {
        var sdkAttribute = doc.Root?.Attribute("Sdk");
        if (sdkAttribute == null || string.IsNullOrWhiteSpace(sdkAttribute.Value))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Critical,
                Category = "SDK",
                Message = "Missing or empty SDK attribute",
                SuggestedFix = "Add appropriate SDK attribute (e.g., Sdk=\"Microsoft.NET.Sdk\")"
            });
        }
        else
        {
            var validSdks = new[]
            {
                "Microsoft.NET.Sdk",
                "Microsoft.NET.Sdk.Web",
                "Microsoft.NET.Sdk.WindowsDesktop",
                "Microsoft.NET.Sdk.Worker",
                "Microsoft.NET.Sdk.BlazorWebAssembly"
            };

            if (!validSdks.Any(sdk => sdkAttribute.Value.StartsWith(sdk, StringComparison.OrdinalIgnoreCase)))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "SDK",
                    Message = $"Unusual SDK value: {sdkAttribute.Value}",
                    SuggestedFix = "Verify this is the correct SDK for your project type"
                });
            }
        }
    }

    private void ValidateTargetFramework(Project project, PostMigrationValidationResult result)
    {
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");

        if (string.IsNullOrEmpty(targetFramework) && string.IsNullOrEmpty(targetFrameworks))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Critical,
                Category = "TargetFramework",
                Message = "No target framework specified",
                SuggestedFix = "Add either TargetFramework or TargetFrameworks property"
            });
        }
        else if (!string.IsNullOrEmpty(targetFramework) && !string.IsNullOrEmpty(targetFrameworks))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Category = "TargetFramework",
                Message = "Both TargetFramework and TargetFrameworks are specified",
                SuggestedFix = "Use either TargetFramework (single) or TargetFrameworks (multi-targeting)"
            });
        }

        // Validate framework values
        var frameworks = !string.IsNullOrEmpty(targetFrameworks)
            ? targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { targetFramework };

        foreach (var framework in frameworks.Where(f => !string.IsNullOrEmpty(f)))
        {
            if (!IsValidTargetFramework(framework))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Category = "TargetFramework",
                    Message = $"Invalid target framework: {framework}",
                    SuggestedFix = "Use a valid target framework moniker (e.g., net8.0, net472)"
                });
            }
        }
    }

    private async Task ValidatePackageReferencesAsync(
        Project project,
        MigrationResult migrationResult,
        PostMigrationValidationResult result,
        CancellationToken cancellationToken)
    {
        var packageReferences = project.Items
            .Where(i => i.ItemType == "PackageReference")
            .ToList();

        // Check for duplicate package references
        var duplicates = packageReferences
            .GroupBy(p => p.EvaluatedInclude, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Category = "PackageReference",
                Message = $"Duplicate package reference: {duplicate.Key}",
                SuggestedFix = "Remove duplicate PackageReference entries"
            });
        }

        // Validate package versions
        foreach (var packageRef in packageReferences)
        {
            var version = packageRef.GetMetadataValue("Version");
            if (string.IsNullOrEmpty(version) && !_options.EnableCentralPackageManagement)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "PackageReference",
                    Message = $"Package reference '{packageRef.EvaluatedInclude}' has no version",
                    SuggestedFix = "Add Version attribute or enable Central Package Management"
                });
            }
        }

        // Check if old Reference items still exist that should be PackageReferences
        var references = project.Items
            .Where(i => i.ItemType == "Reference")
            .Where(r => !r.EvaluatedInclude.StartsWith("System", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var reference in references)
        {
            // Check if this might be available as a NuGet package
            if (CouldBeNuGetPackage(reference.EvaluatedInclude))
            {
                result.Suggestions.Add($"Consider converting Reference '{reference.EvaluatedInclude}' to PackageReference");
            }
        }
    }

    private void ValidateRemovedElements(
        Project project,
        MigrationResult migrationResult,
        PostMigrationValidationResult result)
    {
        // Check if any critical removed elements need manual attention
        foreach (var removedElement in migrationResult.RemovedMSBuildElements)
        {
            if (removedElement.ElementType == "Target" &&
                !string.IsNullOrEmpty(removedElement.XmlContent) &&
                removedElement.XmlContent.Contains("Exec", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "RemovedElements",
                    Message = $"Removed target '{removedElement.Name}' contained Exec tasks",
                    SuggestedFix = removedElement.SuggestedMigrationPath
                });
            }
        }
    }

    private void ValidateProjectStructure(Project project, PostMigrationValidationResult result)
    {
        // Check for common structural issues
        var compileItems = project.Items.Where(i => i.ItemType == "Compile").ToList();
        if (compileItems.Any(c => c.EvaluatedInclude.Contains("**")))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Info,
                Category = "Structure",
                Message = "Project contains glob patterns in Compile items",
                SuggestedFix = "SDK-style projects include .cs files by default - explicit Compile items may not be needed"
            });
        }

        // Check for AssemblyInfo items that should be removed
        if (compileItems.Any(c => c.EvaluatedInclude.Contains("AssemblyInfo", StringComparison.OrdinalIgnoreCase)))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Category = "Structure",
                Message = "Project still includes AssemblyInfo.cs in Compile items",
                SuggestedFix = "Remove AssemblyInfo.cs as SDK generates assembly attributes automatically"
            });
        }
    }

    private void ValidateOutputPaths(Project project, PostMigrationValidationResult result)
    {
        var outputPath = project.GetPropertyValue("OutputPath");
        var baseOutputPath = project.GetPropertyValue("BaseOutputPath");
        var baseIntermediateOutputPath = project.GetPropertyValue("BaseIntermediateOutputPath");

        if (!string.IsNullOrEmpty(outputPath))
        {
            result.Suggestions.Add("Consider removing OutputPath and using default SDK conventions (bin/Debug, etc.)");
        }

        if (!string.IsNullOrEmpty(baseOutputPath) || !string.IsNullOrEmpty(baseIntermediateOutputPath))
        {
            result.Suggestions.Add("Custom output paths detected - verify they work correctly with SDK-style projects");
        }
    }

    private void ValidateDependencies(Project project, PostMigrationValidationResult result)
    {
        // Check for circular dependencies
        var projectReferences = project.Items
            .Where(i => i.ItemType == "ProjectReference")
            .Select(i => i.EvaluatedInclude)
            .ToList();

        if (projectReferences.Any())
        {
            result.Suggestions.Add($"Verify {projectReferences.Count} project references build correctly");
        }
    }

    private void CheckCommonMigrationIssues(Project project, XDocument doc, PostMigrationValidationResult result)
    {
        // Check for legacy imports
        if (doc.Descendants("Import").Any())
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Category = "Legacy",
                Message = "Project still contains Import elements",
                SuggestedFix = "SDK-style projects typically don't need explicit imports"
            });
        }

        // Check for common properties that might cause issues
        var problematicProperties = new[]
        {
            "PostBuildEvent",
            "PreBuildEvent",
            "RunPostBuildEvent"
        };

        foreach (var prop in problematicProperties)
        {
            var value = project.GetPropertyValue(prop);
            if (!string.IsNullOrEmpty(value))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Info,
                    Category = "BuildEvents",
                    Message = $"Project uses {prop}",
                    SuggestedFix = "Consider migrating to MSBuild targets with BeforeTargets/AfterTargets"
                });
            }
        }

        // Check for Web.config or App.config
        var projectDir = Path.GetDirectoryName(project.FullPath);
        if (projectDir != null)
        {
            var configFiles = new[] { "web.config", "app.config", "Web.config", "App.config" };
            foreach (var configFile in configFiles)
            {
                var configPath = Path.Combine(projectDir, configFile);
                if (File.Exists(configPath))
                {
                    result.Suggestions.Add($"Found {configFile} - ensure it's properly configured for the target framework");
                }
            }
        }
    }

    private bool IsValidTargetFramework(string framework)
    {
        var validFrameworks = new[]
        {
            "net8.0", "net7.0", "net6.0", "net5.0",
            "netcoreapp3.1", "netcoreapp3.0", "netcoreapp2.2", "netcoreapp2.1", "netcoreapp2.0",
            "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.5", "netstandard1.4",
            "netstandard1.3", "netstandard1.2", "netstandard1.1", "netstandard1.0",
            "net48", "net472", "net471", "net47", "net462", "net461", "net46",
            "net452", "net451", "net45", "net40", "net35", "net20"
        };

        return validFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase) ||
               framework.StartsWith("net", StringComparison.OrdinalIgnoreCase);
    }

    private bool CouldBeNuGetPackage(string referenceName)
    {
        // Common patterns for assemblies that are likely available as NuGet packages
        var patterns = new[]
        {
            "Newtonsoft",
            "log4net",
            "NLog",
            "EntityFramework",
            "Dapper",
            "AutoMapper",
            "FluentValidation",
            "Polly",
            "RestSharp",
            "Serilog",
            "xunit",
            "NUnit",
            "Moq",
            "FluentAssertions"
        };

        return patterns.Any(p => referenceName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private void LogValidationReport(PostMigrationValidationReport report)
    {
        _logger.LogInformation("Post-migration validation complete:");
        _logger.LogInformation("  Total projects: {Total}", report.TotalProjects);
        _logger.LogInformation("  Valid projects: {Valid}", report.ValidProjects);
        _logger.LogInformation("  Projects with issues: {WithIssues}", report.ProjectsWithIssues);

        if (report.IssuesBySeverity.Any())
        {
            _logger.LogInformation("Issues by severity:");
            foreach (var (severity, count) in report.IssuesBySeverity.OrderBy(kvp => kvp.Key))
            {
                _logger.LogInformation("  {Severity}: {Count}", severity, count);
            }
        }

        if (report.IssuesByCategory.Any())
        {
            _logger.LogInformation("Issues by category:");
            foreach (var (category, count) in report.IssuesByCategory.OrderByDescending(kvp => kvp.Value))
            {
                _logger.LogInformation("  {Category}: {Count}", category, count);
            }
        }
    }
}