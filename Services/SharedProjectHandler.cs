using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class SharedProjectHandler : ISharedProjectHandler
{
    private readonly ILogger<SharedProjectHandler> _logger;

    public SharedProjectHandler(ILogger<SharedProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<SharedProjectInfo> DetectSharedProjectConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new SharedProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive project structure analysis
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Comprehensive source file detection and analysis
        await DetectAndAnalyzeSourceFiles(info, cancellationToken);

        // Comprehensive dependency analysis
        await AnalyzeDependencies(info, cancellationToken);

        // Find and analyze projects that reference this shared project
        await AnalyzeReferencingProjects(info, cancellationToken);

        // Analyze platform-specific patterns and code sharing strategies
        await AnalyzePlatformSpecificPatterns(info, cancellationToken);

        // Check for modern .NET features and patterns
        await AnalyzeModernDotNetPatterns(info, cancellationToken);

        // Determine optimal migration strategy
        await AnalyzeMigrationStrategies(info, cancellationToken);

        // Analyze code quality and maintainability
        await AnalyzeCodeQuality(info, cancellationToken);

        _logger.LogInformation("Detected Shared project: Type={Type}, SourceFiles={Count}, ReferencingProjects={RefCount}, Strategy={Strategy}, CanConvert={CanConvert}, HasModernPatterns={Modern}",
            GetSharedProjectType(info), info.SourceFiles.Count, info.ReferencingProjects.Count, 
            GetRecommendedStrategy(info), info.CanConvertToClassLibrary, HasModernPatterns(info));

        return info;
    }

    public async Task<SharedProjectMigrationGuidance> GetMigrationGuidanceAsync(
        SharedProjectInfo info,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        var guidance = new SharedProjectMigrationGuidance
        {
            ShouldConvertToClassLibrary = info.CanConvertToClassLibrary,
            RecommendedTargetFramework = GetRecommendedTargetFramework(info)
        };

        // Determine optimal migration strategy based on comprehensive analysis
        var strategy = GetRecommendedStrategy(info);
        
        switch (strategy)
        {
            case "ClassLibrary":
                await ProvideClassLibraryMigrationGuidance(info, guidance, cancellationToken);
                break;
            case "MultiTargeting":
                await ProvideMultiTargetingGuidance(info, guidance, cancellationToken);
                break;
            case "Refactor":
                await ProvideRefactoringGuidance(info, guidance, cancellationToken);
                break;
            case "ModernShared":
                await ProvideModernSharedProjectGuidance(info, guidance, cancellationToken);
                break;
            default:
                await ProvideGenericMigrationGuidance(info, guidance, cancellationToken);
                break;
        }

        // Add specific recommendations based on analysis
        await AddSpecificRecommendations(info, guidance, cancellationToken);

        return guidance;
    }

    public async Task<XElement?> ConvertToClassLibraryAsync(
        SharedProjectInfo info,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!info.CanConvertToClassLibrary)
        {
            _logger.LogWarning("Shared project cannot be converted to class library: {ProjectPath}", info.ProjectPath);
            return null;
        }

        try
        {
            var projectElement = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"));

            // Add property group
            var propertyGroup = new XElement("PropertyGroup");
            propertyGroup.Add(new XElement("TargetFramework", "net8.0"));
            propertyGroup.Add(new XElement("ImplicitUsings", "enable"));
            propertyGroup.Add(new XElement("Nullable", "enable"));
            projectElement.Add(propertyGroup);

            // Source files are automatically included by SDK-style projects
            // Only need to explicitly include non-standard files
            var itemGroup = new XElement("ItemGroup");
            var hasNonStandardFiles = false;

            foreach (var sourceFile in info.SourceFiles)
            {
                var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
                if (extension != ".cs" && extension != ".vb" && extension != ".fs")
                {
                    var relativePath = Path.GetRelativePath(info.ProjectDirectory, sourceFile);
                    itemGroup.Add(new XElement("None", new XAttribute("Include", relativePath)));
                    hasNonStandardFiles = true;
                }
            }

            if (hasNonStandardFiles)
            {
                projectElement.Add(itemGroup);
            }

            _logger.LogInformation("Successfully converted shared project to class library format: {ProjectPath}", info.ProjectPath);
            return projectElement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert shared project to class library: {ProjectPath}", info.ProjectPath);
            return null;
        }
    }

    public async Task<List<string>> FindReferencingProjectsAsync(string sharedProjectPath, CancellationToken cancellationToken = default)
    {
        var referencingProjects = new List<string>();

        try
        {
            var searchDirectory = Path.GetDirectoryName(Path.GetDirectoryName(sharedProjectPath)) ?? string.Empty;
            var projectFiles = Directory.GetFiles(searchDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(searchDirectory, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(searchDirectory, "*.fsproj", SearchOption.AllDirectories));

            foreach (var projectFile in projectFiles)
            {
                var content = await File.ReadAllTextAsync(projectFile, cancellationToken);
                var sharedProjectName = Path.GetFileNameWithoutExtension(sharedProjectPath);
                
                if (content.Contains($"{sharedProjectName}.projitems") || content.Contains("SharedProject"))
                {
                    referencingProjects.Add(projectFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to find referencing projects: {Error}", ex.Message);
        }

        return referencingProjects;
    }

    public bool CanConvertToClassLibrary(SharedProjectInfo info)
    {
        // Check for platform-specific code patterns that would prevent conversion
        foreach (var sourceFile in info.SourceFiles)
        {
            try
            {
                var content = File.ReadAllText(sourceFile);
                
                // Look for platform-specific compiler directives
                if (content.Contains("#if ANDROID") || 
                    content.Contains("#if IOS") ||
                    content.Contains("#if WINDOWS") ||
                    content.Contains("#if __MOBILE__") ||
                    content.Contains("partial class") && content.Contains("Platform"))
                {
                    return false;
                }
            }
            catch
            {
                // If we can't read a file, be conservative
                return false;
            }
        }

        return true;
    }

    // Comprehensive analysis methods
    private async Task AnalyzeProjectStructure(SharedProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check shared project manifest (.shproj)
        var shprojPath = info.ProjectPath;
        if (File.Exists(shprojPath))
        {
            info.Properties["HasShprojFile"] = "true";
            
            // Check for project GUID and other metadata
            var content = await File.ReadAllTextAsync(shprojPath, cancellationToken);
            if (content.Contains("ProjectGuid"))
            {
                var guidMatch = Regex.Match(content, @"<ProjectGuid>\{([^}]+)\}</ProjectGuid>");
                if (guidMatch.Success)
                {
                    info.Properties["ProjectGuid"] = guidMatch.Groups[1].Value;
                }
            }

            // Check for project items file reference
            var projitemsMatch = Regex.Match(content, @"<Import Project=""([^""]+\.projitems)""");
            if (projitemsMatch.Success)
            {
                info.Properties["ProjitemsFile"] = projitemsMatch.Groups[1].Value;
            }
        }

        // Check for .projitems file
        var projitemsFile = Path.ChangeExtension(shprojPath, ".projitems");
        if (File.Exists(projitemsFile))
        {
            info.Properties["HasProjitemsFile"] = "true";
            await AnalyzeProjitemsFile(info, projitemsFile, cancellationToken);
        }
    }

    private async Task AnalyzeProjitemsFile(SharedProjectInfo info, string projitemsPath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(projitemsPath, cancellationToken);
            
            // Analyze conditional compilation symbols
            var symbolMatches = Regex.Matches(content, @"DefineConstants[^>]*>([^<]+)<");
            foreach (Match match in symbolMatches)
            {
                var symbols = match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var symbol in symbols)
                {
                    if (!info.Properties.ContainsKey("ConditionalSymbols"))
                        info.Properties["ConditionalSymbols"] = symbol;
                    else
                        info.Properties["ConditionalSymbols"] += $";{symbol}";
                }
            }

            // Check for platform-specific includes
            if (content.Contains("Condition") && (content.Contains("ANDROID") || content.Contains("IOS") || content.Contains("WINDOWS")))
            {
                info.Properties["HasPlatformSpecificIncludes"] = "true";
            }

            // Check for resource files
            if (content.Contains("EmbeddedResource") || content.Contains("AndroidResource"))
            {
                info.Properties["HasEmbeddedResources"] = "true";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze projitems file {File}: {Error}", projitemsPath, ex.Message);
        }
    }

    private async Task DetectAndAnalyzeSourceFiles(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => 
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".cs" || ext == ".vb" || ext == ".fs" || ext == ".xaml" || ext == ".resx" || 
                           ext == ".json" || ext == ".xml" || ext == ".txt";
                })
                .ToList();

            info.SourceFiles = sourceFiles;

            // Categorize source files
            await CategorizeSourceFiles(info, cancellationToken);

            // Analyze code patterns and complexity
            await AnalyzeCodeComplexity(info, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect source files: {Error}", ex.Message);
        }
    }

    private async Task CategorizeSourceFiles(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var codeFiles = 0;
        var xamlFiles = 0;
        var resourceFiles = 0;
        var configFiles = 0;

        foreach (var file in info.SourceFiles)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            switch (extension)
            {
                case ".cs":
                case ".vb":
                case ".fs":
                    codeFiles++;
                    break;
                case ".xaml":
                    xamlFiles++;
                    break;
                case ".resx":
                case ".strings":
                    resourceFiles++;
                    break;
                case ".json":
                case ".xml":
                case ".config":
                    configFiles++;
                    break;
            }
        }

        info.Properties["CodeFileCount"] = codeFiles.ToString();
        info.Properties["XamlFileCount"] = xamlFiles.ToString();
        info.Properties["ResourceFileCount"] = resourceFiles.ToString();
        info.Properties["ConfigFileCount"] = configFiles.ToString();
    }

    private async Task AnalyzeCodeComplexity(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var totalLines = 0;
        var classCount = 0;
        var interfaceCount = 0;
        var namespaceCount = 0;

        var codeFiles = info.SourceFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".cs").Take(20);
        
        foreach (var codeFile in codeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(codeFile, cancellationToken);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                totalLines += lines.Length;

                classCount += Regex.Matches(content, @"\bclass\s+\w+").Count;
                interfaceCount += Regex.Matches(content, @"\binterface\s+\w+").Count;
                namespaceCount += Regex.Matches(content, @"\bnamespace\s+[\w\.]+").Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze code file {File}: {Error}", codeFile, ex.Message);
            }
        }

        info.Properties["TotalLinesOfCode"] = totalLines.ToString();
        info.Properties["ClassCount"] = classCount.ToString();
        info.Properties["InterfaceCount"] = interfaceCount.ToString();
        info.Properties["NamespaceCount"] = namespaceCount.ToString();

        // Determine complexity level
        if (totalLines > 5000 || classCount > 50)
            info.Properties["ComplexityLevel"] = "High";
        else if (totalLines > 1000 || classCount > 10)
            info.Properties["ComplexityLevel"] = "Medium";
        else
            info.Properties["ComplexityLevel"] = "Low";
    }

    private async Task AnalyzeDependencies(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var dependencies = new HashSet<string>();
        var nugetPackages = new HashSet<string>();

        var codeFiles = info.SourceFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".cs").Take(20);
        
        foreach (var codeFile in codeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(codeFile, cancellationToken);
                
                // Extract using statements
                var usingMatches = Regex.Matches(content, @"using\s+([\w\.]+);");
                foreach (Match match in usingMatches)
                {
                    var usingStatement = match.Groups[1].Value;
                    if (!usingStatement.StartsWith("System") && usingStatement.Contains('.'))
                    {
                        dependencies.Add(usingStatement);
                        
                        // Common NuGet package patterns
                        if (usingStatement.StartsWith("Newtonsoft.Json"))
                            nugetPackages.Add("Newtonsoft.Json");
                        else if (usingStatement.StartsWith("Microsoft.Extensions"))
                            nugetPackages.Add("Microsoft.Extensions.*");
                        else if (usingStatement.StartsWith("Xamarin"))
                            nugetPackages.Add("Xamarin.*");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze dependencies in {File}: {Error}", codeFile, ex.Message);
            }
        }

        if (dependencies.Any())
        {
            info.Properties["ExternalDependencies"] = string.Join(";", dependencies.Take(10));
        }

        if (nugetPackages.Any())
        {
            info.Properties["DetectedNuGetPackages"] = string.Join(";", nugetPackages);
        }
    }

    private async Task AnalyzeReferencingProjects(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        info.ReferencingProjects = await FindReferencingProjectsAsync(info.ProjectPath, cancellationToken);

        if (info.ReferencingProjects.Any())
        {
            var projectTypes = new HashSet<string>();
            var targetFrameworks = new HashSet<string>();

            foreach (var referencingProject in info.ReferencingProjects.Take(10))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(referencingProject, cancellationToken);
                    
                    // Detect project type
                    if (content.Contains("Microsoft.NET.Sdk.Maui"))
                        projectTypes.Add("MAUI");
                    else if (content.Contains("Xamarin.iOS") || content.Contains("MonoTouch"))
                        projectTypes.Add("Xamarin.iOS");
                    else if (content.Contains("MonoAndroid") || content.Contains("Xamarin.Android"))
                        projectTypes.Add("Xamarin.Android");
                    else if (content.Contains("Microsoft.NET.Sdk.Web"))
                        projectTypes.Add("Web");
                    else
                        projectTypes.Add("Library");

                    // Extract target frameworks
                    var frameworkMatch = Regex.Match(content, @"<TargetFramework[s]?>([^<]+)</TargetFramework[s]?>");
                    if (frameworkMatch.Success)
                    {
                        var frameworks = frameworkMatch.Groups[1].Value.Split(';');
                        foreach (var framework in frameworks)
                        {
                            targetFrameworks.Add(framework.Trim());
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze referencing project {Project}: {Error}", referencingProject, ex.Message);
                }
            }

            if (projectTypes.Any())
                info.Properties["ReferencingProjectTypes"] = string.Join(";", projectTypes);
            
            if (targetFrameworks.Any())
                info.Properties["ReferencingTargetFrameworks"] = string.Join(";", targetFrameworks);
        }
    }

    private async Task AnalyzePlatformSpecificPatterns(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var platformPatterns = new List<string>();
        var conditionalCompilationSymbols = new HashSet<string>();

        var codeFiles = info.SourceFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".cs").Take(20);
        
        foreach (var codeFile in codeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(codeFile, cancellationToken);
                
                // Check for platform-specific compiler directives
                var platformDirectives = new[]
                {
                    "#if ANDROID", "#if IOS", "#if WINDOWS", "#if MACCATALYST",
                    "#if __ANDROID__", "#if __IOS__", "#if __MOBILE__",
                    "#ifdef ANDROID", "#ifdef IOS"
                };

                foreach (var directive in platformDirectives)
                {
                    if (content.Contains(directive))
                    {
                        var platform = directive.Replace("#if ", "").Replace("#ifdef ", "").Replace("__", "");
                        platformPatterns.Add($"Platform-specific code for {platform}");
                        conditionalCompilationSymbols.Add(platform);
                    }
                }

                // Check for platform-specific APIs
                if (content.Contains("UIKit") || content.Contains("Foundation") && content.Contains("iOS"))
                {
                    platformPatterns.Add("iOS-specific APIs detected");
                    conditionalCompilationSymbols.Add("IOS");
                }

                if (content.Contains("Android.") || content.Contains("Java."))
                {
                    platformPatterns.Add("Android-specific APIs detected");
                    conditionalCompilationSymbols.Add("ANDROID");
                }

                if (content.Contains("Windows.") && !content.Contains("Windows.Forms"))
                {
                    platformPatterns.Add("Windows-specific APIs detected");
                    conditionalCompilationSymbols.Add("WINDOWS");
                }

                // Check for partial classes (common in shared projects)
                if (content.Contains("partial class"))
                {
                    info.Properties["HasPartialClasses"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze platform patterns in {File}: {Error}", codeFile, ex.Message);
            }
        }

        if (platformPatterns.Any())
        {
            info.Properties["PlatformPatterns"] = string.Join("; ", platformPatterns.Distinct());
            info.CanConvertToClassLibrary = false; // Platform-specific code prevents simple conversion
        }

        if (conditionalCompilationSymbols.Any())
        {
            info.Properties["DetectedPlatformSymbols"] = string.Join(";", conditionalCompilationSymbols);
        }
    }

    private async Task AnalyzeModernDotNetPatterns(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var modernPatternCount = 0;

        var codeFiles = info.SourceFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".cs").Take(20);
        
        foreach (var codeFile in codeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(codeFile, cancellationToken);

                // Check for modern C# features
                if (content.Contains("record ") || content.Contains("record class"))
                {
                    info.Properties["UsesRecords"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("nullable enable") || content.Contains("?"))
                {
                    info.Properties["UsesNullableReferences"] = "true";
                    modernPatternCount++;
                }

                if (content.Contains("using ") && content.Contains(";") && !content.Contains("using System"))
                {
                    var globalUsingMatch = Regex.Match(content, @"global using\s+");
                    if (globalUsingMatch.Success)
                    {
                        info.Properties["UsesGlobalUsings"] = "true";
                        modernPatternCount++;
                    }
                }

                if (content.Contains("await ") || content.Contains("async "))
                {
                    info.Properties["UsesAsyncAwait"] = "true";
                    modernPatternCount++;
                }

                // Check for dependency injection patterns
                if (content.Contains("IServiceCollection") || content.Contains("services.Add"))
                {
                    info.Properties["UsesDependencyInjection"] = "true";
                    modernPatternCount++;
                }

                // Check for configuration patterns
                if (content.Contains("IConfiguration") || content.Contains("IOptions"))
                {
                    info.Properties["UsesModernConfiguration"] = "true";
                    modernPatternCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze modern patterns in {File}: {Error}", codeFile, ex.Message);
            }
        }

        info.Properties["ModernPatternCount"] = modernPatternCount.ToString();
        info.Properties["HasModernPatterns"] = (modernPatternCount >= 2).ToString();
    }

    private async Task AnalyzeMigrationStrategies(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        // Determine if conversion to class library is feasible
        info.CanConvertToClassLibrary = await CanConvertToClassLibraryAdvanced(info, cancellationToken);

        // Set recommended conversion path
        if (info.Properties.ContainsKey("PlatformPatterns"))
        {
            if (info.Properties.ContainsKey("ReferencingProjectTypes") && 
                info.Properties["ReferencingProjectTypes"].Contains("MAUI"))
            {
                info.RecommendedConversionPath = "MultiTargeting";
            }
            else
            {
                info.RecommendedConversionPath = "Refactor";
            }
        }
        else if (info.Properties.ContainsKey("HasModernPatterns") && 
                 info.Properties["HasModernPatterns"] == "true")
        {
            info.RecommendedConversionPath = "ClassLibrary";
        }
        else
        {
            info.RecommendedConversionPath = "ModernShared";
        }
    }

    private async Task AnalyzeCodeQuality(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        var qualityIssues = new List<string>();
        var qualityScore = 100;

        // Check complexity
        if (info.Properties.ContainsKey("ComplexityLevel"))
        {
            var complexity = info.Properties["ComplexityLevel"];
            if (complexity == "High")
            {
                qualityIssues.Add("High code complexity may require refactoring before migration");
                qualityScore -= 20;
            }
            else if (complexity == "Medium")
            {
                qualityScore -= 10;
            }
        }

        // Check for platform-specific code
        if (info.Properties.ContainsKey("PlatformPatterns"))
        {
            qualityIssues.Add("Platform-specific code requires careful migration strategy");
            qualityScore -= 15;
        }

        // Check for modern patterns
        if (!info.Properties.ContainsKey("HasModernPatterns") || 
            info.Properties["HasModernPatterns"] != "true")
        {
            qualityIssues.Add("Limited modern .NET patterns - consider modernization");
            qualityScore -= 10;
        }

        // Check file organization
        var codeFileCount = int.Parse(info.Properties.GetValueOrDefault("CodeFileCount", "0"));
        if (codeFileCount > 50)
        {
            qualityIssues.Add("Large number of files may benefit from reorganization");
            qualityScore -= 5;
        }

        info.Properties["QualityScore"] = qualityScore.ToString();
        if (qualityIssues.Any())
        {
            info.Properties["QualityIssues"] = string.Join("; ", qualityIssues);
        }
    }

    // Migration guidance methods
    private async Task ProvideClassLibraryMigrationGuidance(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        guidance.RequiredChanges.Add("Convert .shproj to .csproj with Microsoft.NET.Sdk");
        guidance.RequiredChanges.Add("Update source file includes to use SDK-style globbing");
        guidance.RequiredChanges.Add("Add PackageReference elements for dependencies");
        guidance.RequiredChanges.Add("Update target framework to .NET 8+ for modern features");

        if (info.Properties.ContainsKey("DetectedNuGetPackages"))
        {
            guidance.RequiredChanges.Add($"Add NuGet packages: {info.Properties["DetectedNuGetPackages"]}");
        }

        foreach (var referencingProject in info.ReferencingProjects)
        {
            guidance.ReferencingProjectUpdates.Add($"Update {Path.GetFileName(referencingProject)} to use ProjectReference instead of SharedProject import");
        }
    }

    private async Task ProvideMultiTargetingGuidance(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        guidance.RequiredChanges.Add("Convert to multi-targeting class library with platform-specific code");
        guidance.RequiredChanges.Add("Use conditional compilation or partial classes for platform differences");
        guidance.RequiredChanges.Add("Consider using #if directives or separate assemblies for platform-specific APIs");

        if (info.Properties.ContainsKey("DetectedPlatformSymbols"))
        {
            var platforms = info.Properties["DetectedPlatformSymbols"].Split(';');
            guidance.RequiredChanges.Add($"Configure target frameworks for platforms: {string.Join(", ", platforms)}");
        }

        guidance.RequiredChanges.Add("Recommended: net8.0-android;net8.0-ios;net8.0-windows for MAUI compatibility");
    }

    private async Task ProvideRefactoringGuidance(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        guidance.RequiredChanges.Add("Refactor platform-specific code into separate projects or abstractions");
        guidance.RequiredChanges.Add("Extract shared interfaces and common logic into class library");
        guidance.RequiredChanges.Add("Use dependency injection for platform-specific implementations");
        guidance.RequiredChanges.Add("Consider using platform abstraction patterns (e.g., interface-based design)");

        if (info.Properties.ContainsKey("PlatformPatterns"))
        {
            guidance.RequiredChanges.Add($"Address platform-specific patterns: {info.Properties["PlatformPatterns"]}");
        }
    }

    private async Task ProvideModernSharedProjectGuidance(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        guidance.RequiredChanges.Add("Modernize shared project with .NET 8+ patterns while keeping shared project format");
        guidance.RequiredChanges.Add("Add nullable reference types support");
        guidance.RequiredChanges.Add("Implement modern async/await patterns");
        guidance.RequiredChanges.Add("Consider adding dependency injection support");

        if (!info.Properties.ContainsKey("UsesNullableReferences"))
        {
            guidance.RequiredChanges.Add("Enable nullable reference types for better code quality");
        }

        if (!info.Properties.ContainsKey("UsesAsyncAwait"))
        {
            guidance.RequiredChanges.Add("Consider implementing async/await patterns where appropriate");
        }
    }

    private async Task ProvideGenericMigrationGuidance(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        guidance.RequiredChanges.Add("Review shared project structure and determine optimal migration path");
        guidance.RequiredChanges.Add("Consider converting to class library if no platform-specific code exists");
        guidance.RequiredChanges.Add("Evaluate code quality and complexity before migration");

        if (info.Properties.ContainsKey("QualityIssues"))
        {
            guidance.RequiredChanges.Add($"Address quality issues: {info.Properties["QualityIssues"]}");
        }
    }

    private async Task AddSpecificRecommendations(SharedProjectInfo info, SharedProjectMigrationGuidance guidance, CancellationToken cancellationToken)
    {
        // Add recommendations based on complexity
        if (info.Properties.ContainsKey("ComplexityLevel") && info.Properties["ComplexityLevel"] == "High")
        {
            guidance.RequiredChanges.Add("Consider breaking down large shared project into smaller, focused libraries");
        }

        // Add recommendations based on referencing projects
        if (info.Properties.ContainsKey("ReferencingProjectTypes"))
        {
            var projectTypes = info.Properties["ReferencingProjectTypes"];
            if (projectTypes.Contains("MAUI"))
            {
                guidance.RequiredChanges.Add("Ensure compatibility with .NET MAUI multi-targeting requirements");
            }
            if (projectTypes.Contains("Xamarin"))
            {
                guidance.RequiredChanges.Add("Plan migration from Xamarin to .NET MAUI for long-term support");
            }
        }

        // Add recommendations for modern .NET features
        if (info.Properties.ContainsKey("HasModernPatterns") && info.Properties["HasModernPatterns"] == "true")
        {
            guidance.RequiredChanges.Add("Leverage existing modern patterns during migration");
        }
    }

    // Enhanced CanConvertToClassLibrary method
    private async Task<bool> CanConvertToClassLibraryAdvanced(SharedProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for platform-specific patterns that prevent conversion
        if (info.Properties.ContainsKey("PlatformPatterns"))
        {
            return false;
        }

        // Check for platform-specific includes in projitems
        if (info.Properties.ContainsKey("HasPlatformSpecificIncludes"))
        {
            return false;
        }

        // Check individual source files for platform-specific code
        return CanConvertToClassLibrary(info);
    }

    // Helper methods
    private string GetRecommendedTargetFramework(SharedProjectInfo info)
    {
        if (info.Properties.ContainsKey("ReferencingTargetFrameworks"))
        {
            var frameworks = info.Properties["ReferencingTargetFrameworks"];
            if (frameworks.Contains("net8.0") || frameworks.Contains("net9.0"))
                return "net8.0";
            if (frameworks.Contains("net6.0"))
                return "net6.0";
        }

        // Default to .NET 8 for new projects
        return "net8.0";
    }

    private string GetRecommendedStrategy(SharedProjectInfo info)
    {
        return info.RecommendedConversionPath ?? "ClassLibrary";
    }

    private string GetSharedProjectType(SharedProjectInfo info)
    {
        if (info.Properties.ContainsKey("PlatformPatterns"))
            return "Platform-Specific";
        if (info.Properties.ContainsKey("HasModernPatterns") && info.Properties["HasModernPatterns"] == "true")
            return "Modern-Shared";
        if (info.Properties.ContainsKey("ReferencingProjectTypes") && info.Properties["ReferencingProjectTypes"].Contains("MAUI"))
            return "MAUI-Shared";
        if (info.Properties.ContainsKey("ReferencingProjectTypes") && info.Properties["ReferencingProjectTypes"].Contains("Xamarin"))
            return "Xamarin-Shared";
        return "Legacy-Shared";
    }

    private bool HasModernPatterns(SharedProjectInfo info)
    {
        return info.Properties.ContainsKey("HasModernPatterns") && 
               info.Properties["HasModernPatterns"] == "true";
    }
}