using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class TestProjectHandler : ITestProjectHandler
{
    private readonly ILogger<TestProjectHandler> _logger;
    private readonly INuGetPackageResolver _nugetResolver;

    // Test framework detection patterns
    private static readonly Dictionary<string, TestFrameworkInfo> TestFrameworkMappings = new()
    {
        ["MSTest"] = new()
        {
            DetectionPackages = new[] { "MSTest.TestFramework", "Microsoft.VisualStudio.TestPlatform.TestFramework" },
            RequiredPackages = new Dictionary<string, string>
            {
                ["MSTest.TestFramework"] = "3.1.1",
                ["MSTest.TestAdapter"] = "3.1.1",
                ["Microsoft.NET.Test.Sdk"] = "17.8.0"
            },
            TestAdapterPackage = "MSTest.TestAdapter"
        },
        ["xUnit"] = new()
        {
            DetectionPackages = new[] { "xunit", "xunit.core" },
            RequiredPackages = new Dictionary<string, string>
            {
                ["xunit"] = "2.6.2",
                ["xunit.runner.visualstudio"] = "2.5.4",
                ["Microsoft.NET.Test.Sdk"] = "17.8.0"
            },
            TestAdapterPackage = "xunit.runner.visualstudio"
        },
        ["NUnit"] = new()
        {
            DetectionPackages = new[] { "NUnit" },
            RequiredPackages = new Dictionary<string, string>
            {
                ["NUnit"] = "3.14.0",
                ["NUnit3TestAdapter"] = "4.5.0",
                ["Microsoft.NET.Test.Sdk"] = "17.8.0"
            },
            TestAdapterPackage = "NUnit3TestAdapter"
        },
        ["SpecFlow"] = new()
        {
            DetectionPackages = new[] { "SpecFlow", "TechTalk.SpecFlow" },
            RequiredPackages = new Dictionary<string, string>
            {
                ["SpecFlow"] = "3.9.74",
                ["SpecFlow.Tools.MsBuild.Generation"] = "3.9.74",
                ["Microsoft.NET.Test.Sdk"] = "17.8.0"
            },
            IsSpecialFramework = true,
            RequiresAdditionalConfiguration = true
        }
    };

    public TestProjectHandler(
        ILogger<TestProjectHandler> logger,
        INuGetPackageResolver nugetResolver)
    {
        _logger = logger;
        _nugetResolver = nugetResolver;
    }

    public async Task<TestProjectInfo> DetectTestFrameworkAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new TestProjectInfo
        {
            ProjectPath = project.FullPath
        };

        // Check for test settings files
        var projectDir = Path.GetDirectoryName(project.FullPath);
        if (!string.IsNullOrEmpty(projectDir))
        {
            info.RunSettingsFiles = Directory.GetFiles(projectDir, "*.runsettings", SearchOption.AllDirectories).ToList();
            info.TestSettingsFiles = Directory.GetFiles(projectDir, "*.testsettings", SearchOption.AllDirectories).ToList();
            
            // Check for test playlists
            var playlistFiles = Directory.GetFiles(projectDir, "*.playlist", SearchOption.AllDirectories);
            if (playlistFiles.Any())
            {
                info.TestPlaylistFiles = playlistFiles.ToList();
                _logger.LogInformation("Found {Count} test playlist files", playlistFiles.Length);
            }
        }

        // Detect test framework from package references
        var packageRefs = project.Items
            .Where(i => i.ItemType == "PackageReference")
            .Select(i => i.EvaluatedInclude)
            .ToList();

        // Also check references for legacy projects
        var references = project.Items
            .Where(i => i.ItemType == "Reference")
            .Select(i => i.EvaluatedInclude)
            .ToList();

        foreach (var (frameworkName, frameworkInfo) in TestFrameworkMappings)
        {
            if (frameworkInfo.DetectionPackages.Any(p => 
                packageRefs.Any(r => r.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                references.Any(r => r.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
            {
                info.DetectedFrameworks.Add(frameworkName);
                _logger.LogInformation("Detected {Framework} test framework", frameworkName);
            }
        }

        // Detect code coverage tools
        if (packageRefs.Any(p => p.StartsWith("coverlet", StringComparison.OrdinalIgnoreCase)))
        {
            info.CodeCoverageTools.Add("Coverlet");
        }
        if (packageRefs.Any(p => p.StartsWith("OpenCover", StringComparison.OrdinalIgnoreCase)))
        {
            info.CodeCoverageTools.Add("OpenCover");
        }

        // Check for SpecFlow feature files
        if (info.DetectedFrameworks.Contains("SpecFlow"))
        {
            info.FeatureFiles = project.Items
                .Where(i => i.EvaluatedInclude.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.EvaluatedInclude)
                .ToList();
        }

        return info;
    }

    public async Task MigrateTestConfigurationAsync(
        TestProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        // Migrate test settings references
        if (info.TestSettingsFiles.Any())
        {
            result.Warnings.Add($"Found {info.TestSettingsFiles.Count} .testsettings files. " +
                "Consider converting to .runsettings format for SDK-style projects.");
            
            // Add guidance for conversion
            foreach (var testSettings in info.TestSettingsFiles)
            {
                var fileName = Path.GetFileName(testSettings);
                result.RemovedMSBuildElements.Add(new RemovedMSBuildElement
                {
                    ElementType = "TestSettings",
                    Name = fileName,
                    Reason = ".testsettings format is legacy",
                    SuggestedMigrationPath = $"Convert to .runsettings format. See: https://docs.microsoft.com/en-us/visualstudio/test/migrate-testsettings-to-runsettings"
                });
            }
        }

        // Preserve .runsettings references
        if (info.RunSettingsFiles.Any())
        {
            var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault();
            if (propertyGroup != null)
            {
                var runSettingsFile = info.RunSettingsFiles.First();
                var relativePath = Path.GetRelativePath(Path.GetDirectoryName(info.ProjectPath)!, runSettingsFile);
                propertyGroup.Add(new XElement("RunSettingsFilePath", relativePath));
                _logger.LogInformation("Preserved RunSettings reference: {Path}", relativePath);
            }
        }

        // Add appropriate test adapter packages
        foreach (var framework in info.DetectedFrameworks)
        {
            if (TestFrameworkMappings.TryGetValue(framework, out var frameworkInfo))
            {
                foreach (var (packageId, defaultVersion) in frameworkInfo.RequiredPackages)
                {
                    // Check if package already exists
                    if (!packageReferences.Any(p => p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        var version = await _nugetResolver.GetLatestStableVersionAsync(packageId, cancellationToken) 
                                      ?? defaultVersion;
                        
                        packageReferences.Add(new PackageReference
                        {
                            PackageId = packageId,
                            Version = version
                        });
                        
                        _logger.LogInformation("Added test package: {Package} {Version}", packageId, version);
                    }
                }

                // Handle SpecFlow special configuration
                if (frameworkInfo.IsSpecialFramework && framework == "SpecFlow")
                {
                    await ConfigureSpecFlowAsync(info, projectElement, packageReferences, cancellationToken);
                }
            }
        }

        // Add code coverage packages if needed
        if (info.CodeCoverageTools.Contains("Coverlet") && 
            !packageReferences.Any(p => p.PackageId.StartsWith("coverlet", StringComparison.OrdinalIgnoreCase)))
        {
            var coverletVersion = await _nugetResolver.GetLatestStableVersionAsync("coverlet.collector", cancellationToken) 
                                  ?? "6.0.0";
            packageReferences.Add(new PackageReference
            {
                PackageId = "coverlet.collector",
                Version = coverletVersion
            });
            _logger.LogInformation("Added Coverlet code coverage package");
        }

        // Add test playlist support warning
        if (info.TestPlaylistFiles.Any())
        {
            result.Warnings.Add($"Found {info.TestPlaylistFiles.Count} test playlist files. " +
                "Ensure these are compatible with your test runner.");
        }
    }

    private async Task ConfigureSpecFlowAsync(
        TestProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        CancellationToken cancellationToken)
    {
        // Ensure SpecFlow MSBuild targets are included
        if (!packageReferences.Any(p => p.PackageId == "SpecFlow.Tools.MsBuild.Generation"))
        {
            var version = await _nugetResolver.GetLatestStableVersionAsync("SpecFlow.Tools.MsBuild.Generation", cancellationToken) 
                          ?? "3.9.74";
            var package = new PackageReference
            {
                PackageId = "SpecFlow.Tools.MsBuild.Generation",
                Version = version
            };
            package.Metadata["IncludeAssets"] = "build";
            packageReferences.Add(package);
        }

        // Add SpecFlow configuration if feature files exist
        if (info.FeatureFiles.Any())
        {
            _logger.LogInformation("Configuring SpecFlow for {Count} feature files", info.FeatureFiles.Count);
            
            // Ensure feature files are included properly
            var hasFeatureGlob = projectElement.Descendants("None")
                .Any(e => e.Attribute("Update")?.Value == "**\\*.feature");
                
            if (!hasFeatureGlob)
            {
                var itemGroup = new XElement("ItemGroup");
                itemGroup.Add(new XElement("None",
                    new XAttribute("Update", "**\\*.feature"),
                    new XElement("Generator", "SpecFlowSingleFileGenerator"),
                    new XElement("LastGenOutput", "%(Filename).feature.cs")));
                projectElement.Add(itemGroup);
            }
        }

        // Detect which test framework SpecFlow is using
        var specFlowTestFramework = "SpecFlow.xUnit"; // Default
        if (info.DetectedFrameworks.Contains("NUnit"))
        {
            specFlowTestFramework = "SpecFlow.NUnit";
        }
        else if (info.DetectedFrameworks.Contains("MSTest"))
        {
            specFlowTestFramework = "SpecFlow.MSTest";
        }

        if (!packageReferences.Any(p => p.PackageId == specFlowTestFramework))
        {
            var version = await _nugetResolver.GetLatestStableVersionAsync(specFlowTestFramework, cancellationToken) 
                          ?? "3.9.74";
            packageReferences.Add(new PackageReference
            {
                PackageId = specFlowTestFramework,
                Version = version
            });
            _logger.LogInformation("Added {Package} for SpecFlow integration", specFlowTestFramework);
        }
    }

    public void ConvertTestSettingsToRunSettings(string testSettingsPath, string outputPath)
    {
        try
        {
            var testSettingsXml = XDocument.Load(testSettingsPath);
            var runSettingsXml = new XDocument(
                new XElement("RunSettings",
                    new XElement("RunConfiguration",
                        new XElement("TargetPlatform", "x64"),
                        new XElement("ResultsDirectory", ".\\TestResults")
                    ),
                    new XElement("DataCollectionRunSettings",
                        new XElement("DataCollectors",
                            new XElement("DataCollector",
                                new XAttribute("friendlyName", "Code Coverage"),
                                new XAttribute("uri", "datacollector://Microsoft/CodeCoverage/2.0")
                            )
                        )
                    )
                )
            );

            // TODO: Add more sophisticated conversion logic based on testsettings content
            
            runSettingsXml.Save(outputPath);
            _logger.LogInformation("Converted {TestSettings} to {RunSettings}", testSettingsPath, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert test settings file: {Path}", testSettingsPath);
        }
    }
}

public class TestFrameworkInfo
{
    public string[] DetectionPackages { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> RequiredPackages { get; set; } = new();
    public string? TestAdapterPackage { get; set; }
    public bool IsSpecialFramework { get; set; }
    public bool RequiresAdditionalConfiguration { get; set; }
}

