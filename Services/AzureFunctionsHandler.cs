using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Versioning;

namespace SdkMigrator.Services;

public class AzureFunctionsHandler : IAzureFunctionsHandler
{
    private readonly ILogger<AzureFunctionsHandler> _logger;
    private bool _generateModernProgramCs = false;

    public AzureFunctionsHandler(ILogger<AzureFunctionsHandler> logger)
    {
        _logger = logger;
    }

    public async Task<FunctionsProjectInfo> DetectFunctionsConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new FunctionsProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Detect Functions version and model from package references
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        await DetectFunctionsVersionAndModel(info, packageReferences, cancellationToken);

        // Check for required files
        info.HasHostJson = File.Exists(Path.Combine(info.ProjectDirectory, "host.json"));
        info.HasLocalSettingsJson = File.Exists(Path.Combine(info.ProjectDirectory, "local.settings.json"));
        info.HasProgramCs = File.Exists(Path.Combine(info.ProjectDirectory, "Program.cs"));
        info.HasStartupCs = File.Exists(Path.Combine(info.ProjectDirectory, "Startup.cs"));

        // Find function.json files
        if (Directory.Exists(info.ProjectDirectory))
        {
            info.FunctionJsonFiles = Directory.GetFiles(info.ProjectDirectory, "function.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(info.ProjectDirectory, f))
                .ToList();
        }

        // Analyze project structure and detect legacy patterns
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Check for extension bundles and runtime configuration
        await DetectRuntimeConfiguration(info, cancellationToken);

        // Detect Functions trigger types and bindings
        await DetectFunctionTriggers(info, cancellationToken);

        _logger.LogInformation("Detected Azure Functions project: Version={Version}, Model={Model}, Functions={FunctionCount}, NeedsIsolatedMigration={NeedsMigration}",
            info.FunctionsVersion, info.UsesIsolatedModel ? "Isolated" : "In-Process", info.FunctionJsonFiles.Count, info.NeedsIsolatedModelMigration);

        return info;
    }

    public async Task MigrateFunctionsProjectAsync(
        FunctionsProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Migrate to isolated worker model if needed
            if (info.NeedsIsolatedModelMigration)
            {
                await MigrateToIsolatedWorkerModel(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (info.UsesIsolatedModel)
            {
                // Already isolated - ensure correct configuration
                await ConfigureIsolatedWorkerProjectEnhanced(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Legacy in-process model - configure for .NET 8 with warnings
                await ConfigureLegacyInProcessProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Common configuration for all Functions projects
            await ConfigureCommonFunctionsSettings(info, projectElement, result, cancellationToken);

            _logger.LogInformation("Successfully migrated Azure Functions project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate Azure Functions project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "Azure Functions migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void EnsureFunctionsFilesIncluded(string projectDirectory, XElement projectElement)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Ensure host.json is included
        var hostJsonPath = Path.Combine(projectDirectory, "host.json");
        if (File.Exists(hostJsonPath))
        {
            EnsureItemIncluded(itemGroup, "None", "host.json", new Dictionary<string, string>
            {
                ["CopyToOutputDirectory"] = "PreserveNewest"
            });
        }

        // Ensure local.settings.json is included (but not published)
        var localSettingsPath = Path.Combine(projectDirectory, "local.settings.json");
        if (File.Exists(localSettingsPath))
        {
            EnsureItemIncluded(itemGroup, "None", "local.settings.json", new Dictionary<string, string>
            {
                ["CopyToOutputDirectory"] = "PreserveNewest",
                ["CopyToPublishDirectory"] = "Never"
            });
        }

        // Include function.json files
        var functionJsonFiles = Directory.GetFiles(projectDirectory, "function.json", SearchOption.AllDirectories);
        foreach (var functionJsonFile in functionJsonFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, functionJsonFile);
            EnsureItemIncluded(itemGroup, "None", relativePath, new Dictionary<string, string>
            {
                ["CopyToOutputDirectory"] = "PreserveNewest"
            });
        }
    }

    public void MigrateFunctionsRuntime(XElement projectElement, FunctionsProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");
        
        if (propertyGroup.Parent == null)
            projectElement.Add(propertyGroup);

        // Set runtime configuration based on Functions version
        if (info.FunctionsVersion == "v4")
        {
            SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        }
        else if (info.FunctionsVersion == "v3")
        {
            SetOrUpdateProperty(propertyGroup, "TargetFramework", "net6.0");
        }

        // Configure for isolated worker model
        if (info.UsesIsolatedModel)
        {
            SetOrUpdateProperty(propertyGroup, "OutputType", "Exe");
        }
    }

    private async Task DetectFunctionsVersionAndModel(FunctionsProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Microsoft.NET.Sdk.Functions":
                    if (!string.IsNullOrEmpty(version))
                    {
                        if (version.StartsWith("4.")) info.FunctionsVersion = "v4";
                        else if (version.StartsWith("3.")) info.FunctionsVersion = "v3";
                        else if (version.StartsWith("1.")) info.FunctionsVersion = "v1";
                    }
                    info.HasInProcessPackages = true;
                    break;

                case "Microsoft.Azure.Functions.Worker":
                    info.UsesIsolatedModel = true;
                    info.HasIsolatedWorkerPackages = true;
                    if (!string.IsNullOrEmpty(version))
                    {
                        info.Properties["WorkerVersion"] = version;
                        // Check for .NET 8+ compatible versions
                        if (IsVersion(version, ">=", "1.19.0"))
                        {
                            info.Properties["SupportsNet8"] = "true";
                        }
                    }
                    break;

                case "Microsoft.Azure.Functions.Worker.Sdk":
                    info.UsesIsolatedModel = true;
                    info.HasIsolatedWorkerPackages = true;
                    if (!string.IsNullOrEmpty(version))
                    {
                        info.Properties["WorkerSdkVersion"] = version;
                    }
                    break;

                case "Microsoft.Azure.WebJobs":
                case "Microsoft.Azure.WebJobs.Extensions":
                    info.HasInProcessPackages = true;
                    break;

                // Track specific trigger/binding packages
                case var pkg when pkg.StartsWith("Microsoft.Azure.Functions.Worker.Extensions."):
                    info.IsolatedWorkerExtensions.Add(pkg.Replace("Microsoft.Azure.Functions.Worker.Extensions.", ""));
                    break;

                case var pkg when pkg.StartsWith("Microsoft.Azure.WebJobs.Extensions."):
                    info.InProcessExtensions.Add(pkg.Replace("Microsoft.Azure.WebJobs.Extensions.", ""));
                    break;
            }
        }

        // Advanced worker model detection based on multiple factors
        await PerformAdvancedWorkerModelDetection(info, cancellationToken);

        // Determine if migration to isolated model is needed
        info.NeedsIsolatedModelMigration = info.HasInProcessPackages && !info.UsesIsolatedModel;
        
        // Default to v4 for new projects
        if (string.IsNullOrEmpty(info.FunctionsVersion))
            info.FunctionsVersion = "v4";
    }

    private async Task AnalyzeProjectStructure(FunctionsProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check project output type
        var outputType = project.GetPropertyValue("OutputType");
        info.IsExecutableProject = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase);

        // Analyze source files for Functions patterns
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                
                // Check for in-process function attributes
                if (content.Contains("[FunctionName"))
                {
                    info.InProcessFunctionMethods.Add(Path.GetRelativePath(info.ProjectDirectory, sourceFile));
                }

                // Check for isolated worker function attributes
                if (content.Contains("[Function("))
                {
                    info.IsolatedFunctionMethods.Add(Path.GetRelativePath(info.ProjectDirectory, sourceFile));
                }

                // Check for legacy WebJobs patterns
                if (content.Contains("using Microsoft.Azure.WebJobs"))
                {
                    info.UsesLegacyWebJobsPatterns = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze source file {File}: {Error}", sourceFile, ex.Message);
            }
        }
    }

    private async Task DetectRuntimeConfiguration(FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        // Analyze host.json
        var hostJsonPath = Path.Combine(info.ProjectDirectory, "host.json");
        if (File.Exists(hostJsonPath))
        {
            await AnalyzeHostJson(info, hostJsonPath, cancellationToken);
        }

        // Analyze local.settings.json
        var localSettingsPath = Path.Combine(info.ProjectDirectory, "local.settings.json");
        if (File.Exists(localSettingsPath))
        {
            await AnalyzeLocalSettings(info, localSettingsPath, cancellationToken);
        }
    }

    private async Task AnalyzeHostJson(FunctionsProjectInfo info, string hostJsonPath, CancellationToken cancellationToken)
    {
        try
        {
            var hostJsonContent = await File.ReadAllTextAsync(hostJsonPath, cancellationToken);
            var hostJson = JsonDocument.Parse(hostJsonContent);
            
            // Check Functions version in host.json
            if (hostJson.RootElement.TryGetProperty("version", out var versionElement))
            {
                var hostVersion = versionElement.GetString();
                if (!string.IsNullOrEmpty(hostVersion) && string.IsNullOrEmpty(info.FunctionsVersion))
                {
                    info.FunctionsVersion = hostVersion;
                }
            }

            // Check extension bundles
            if (hostJson.RootElement.TryGetProperty("extensionBundle", out var extensionBundle))
            {
                if (extensionBundle.TryGetProperty("id", out var id))
                    info.ExtensionBundles["id"] = id.GetString() ?? string.Empty;
                
                if (extensionBundle.TryGetProperty("version", out var version))
                    info.ExtensionBundles["version"] = version.GetString() ?? string.Empty;
            }

            // Check for logging configuration
            if (hostJson.RootElement.TryGetProperty("logging", out var logging))
            {
                info.HasCustomLoggingConfig = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to parse host.json: {Error}", ex.Message);
        }
    }

    private async Task AnalyzeLocalSettings(FunctionsProjectInfo info, string localSettingsPath, CancellationToken cancellationToken)
    {
        try
        {
            var localSettingsContent = await File.ReadAllTextAsync(localSettingsPath, cancellationToken);
            var localSettings = JsonDocument.Parse(localSettingsContent);
            
            if (localSettings.RootElement.TryGetProperty("Values", out var values))
            {
                // Check FUNCTIONS_WORKER_RUNTIME
                if (values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out var runtime))
                {
                    var runtimeValue = runtime.GetString();
                    info.ConfiguredWorkerRuntime = runtimeValue;
                    
                    // Check if configured for isolated model
                    if (string.Equals(runtimeValue, "dotnet-isolated", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ConfiguredForIsolatedRuntime = true;
                    }
                    else if (string.Equals(runtimeValue, "dotnet", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ConfiguredForInProcessRuntime = true;
                    }
                }

                // Check for other important settings
                foreach (var property in values.EnumerateObject())
                {
                    info.LocalSettingsValues[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to parse local.settings.json: {Error}", ex.Message);
        }
    }

    private async Task DetectFunctionTriggers(FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        // Analyze function.json files to understand triggers and bindings
        foreach (var functionJsonFile in info.FunctionJsonFiles)
        {
            var fullPath = Path.Combine(info.ProjectDirectory, functionJsonFile);
            try
            {
                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var functionJson = JsonDocument.Parse(content);
                
                if (functionJson.RootElement.TryGetProperty("bindings", out var bindings))
                {
                    foreach (var binding in bindings.EnumerateArray())
                    {
                        if (binding.TryGetProperty("type", out var type))
                        {
                            var triggerType = type.GetString();
                            if (!string.IsNullOrEmpty(triggerType) && !info.DetectedTriggerTypes.Contains(triggerType))
                            {
                                info.DetectedTriggerTypes.Add(triggerType);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse function.json {File}: {Error}", functionJsonFile, ex.Message);
            }
        }
    }

    private async Task MigrateToIsolatedWorkerModel(FunctionsProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating Azure Functions project from in-process to isolated worker model");

        // Remove legacy in-process packages
        RemoveLegacyInProcessPackages(packageReferences);

        // Add isolated worker packages
        await AddIsolatedWorkerPackages(packageReferences, cancellationToken);

        // Configure project for isolated worker
        await ConfigureIsolatedWorkerProjectEnhanced(info, projectElement, packageReferences, result, cancellationToken);

        // Create or update Program.cs
        await CreateProgramCsForIsolatedWorker(info, result, cancellationToken);

        // Update configuration files
        await UpdateConfigurationForIsolatedWorker(info, result, cancellationToken);

        // Remove Startup.cs if it exists
        await RemoveStartupCs(info, result, cancellationToken);

        result.Warnings.Add("Azure Functions project migrated from in-process to isolated worker model. Review and update function code to use isolated worker patterns.");
        result.Warnings.Add("Verify FUNCTIONS_WORKER_RUNTIME is set to 'dotnet-isolated' in Azure portal application settings.");
    }

    private async Task ConfigureIsolatedWorkerProject(FunctionsProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Set essential properties for isolated worker
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        SetOrUpdateProperty(propertyGroup, "OutputType", "Exe");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        SetOrUpdateProperty(propertyGroup, "AzureFunctionsVersion", "v4");
        
        // Include configuration files
        EnsureFunctionsFilesIncluded(info.ProjectDirectory, projectElement);
        
        // Add isolated worker packages
        await AddIsolatedWorkerPackages(packageReferences, cancellationToken);
    }

    private async Task ConfigureLegacyInProcessProject(FunctionsProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Configuring legacy in-process Azure Functions project. Consider migrating to isolated worker model for full .NET 8 support.");

        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Configure for .NET 8 in-process (limited support)
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        SetOrUpdateProperty(propertyGroup, "AzureFunctionsVersion", "v4");
        
        // Add legacy package if not present
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.NET.Sdk.Functions"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.NET.Sdk.Functions",
                Version = "4.4.0"
            });
        }

        result.Warnings.Add("Azure Functions in-process model has limited .NET 8 support. Migration to isolated worker model is strongly recommended.");
        result.Warnings.Add("For .NET 8 in-process support, ensure these application settings: FUNCTIONS_WORKER_RUNTIME='dotnet', FUNCTIONS_EXTENSION_VERSION='~4', FUNCTIONS_INPROC_NET8_ENABLED='1'");
    }

    private async Task ConfigureCommonFunctionsSettings(FunctionsProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        // Ensure host.json exists with proper configuration
        await EnsureHostJsonExists(info, result, cancellationToken);
        
        // Ensure local.settings.json exists with proper configuration
        await EnsureLocalSettingsJsonExists(info, result, cancellationToken);
        
        // Include configuration files in project
        EnsureFunctionsFilesIncluded(info.ProjectDirectory, projectElement);
    }

    private void RemoveLegacyInProcessPackages(List<PackageReference> packageReferences)
    {
        var legacyPackages = new[]
        {
            "Microsoft.NET.Sdk.Functions",
            "Microsoft.Azure.WebJobs",
            "Microsoft.Azure.WebJobs.Core",
            "Microsoft.Azure.WebJobs.Extensions",
            "Microsoft.Azure.WebJobs.Extensions.Http",
            "Microsoft.Azure.WebJobs.Extensions.Storage",
            "Microsoft.Azure.WebJobs.Host"
        };

        packageReferences.RemoveAll(p => legacyPackages.Contains(p.PackageId));
    }

    private async Task AddIsolatedWorkerPackages(List<PackageReference> packageReferences, CancellationToken cancellationToken)
    {
        // Core isolated worker packages
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker",
                Version = "1.21.0"
            });
        }

        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker.Sdk"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker.Sdk",
                Version = "1.17.2",
                Metadata = { ["PrivateAssets"] = "All" }
            });
        }

        // Application Insights for isolated worker
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker.ApplicationInsights"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker.ApplicationInsights",
                Version = "1.2.0"
            });
        }

        // Configuration support
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Extensions.Hosting"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Extensions.Hosting",
                Version = "8.0.0"
            });
        }
    }

    private async Task CreateProgramCsForIsolatedWorker(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        if (!_generateModernProgramCs)
        {
            _logger.LogInformation("Skipping Program.cs generation as GenerateModernProgramCs is disabled");
            return;
        }
        
        var programCsPath = Path.Combine(info.ProjectDirectory, "Program.cs");
        
        if (!File.Exists(programCsPath))
        {
            var programCsContent = GenerateIsolatedWorkerProgramCs();
            await File.WriteAllTextAsync(programCsPath, programCsContent, cancellationToken);
            result.Warnings.Add("Created Program.cs for isolated worker model. Review and customize as needed.");
            _logger.LogInformation("Created Program.cs for isolated worker model");
        }
        else
        {
            // Check if existing Program.cs needs updates for isolated worker
            var existingContent = await File.ReadAllTextAsync(programCsPath, cancellationToken);
            if (!existingContent.Contains("ConfigureFunctionsWorkerDefaults"))
            {
                result.Warnings.Add("Existing Program.cs may need updates for isolated worker model. Ensure it calls ConfigureFunctionsWorkerDefaults().");
            }
        }
    }

    private string GenerateIsolatedWorkerProgramCs()
    {
        return @"using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();
";
    }

    private async Task UpdateConfigurationForIsolatedWorker(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        // Update local.settings.json
        var localSettingsPath = Path.Combine(info.ProjectDirectory, "local.settings.json");
        if (File.Exists(localSettingsPath))
        {
            await UpdateLocalSettingsForIsolatedWorker(localSettingsPath, result, cancellationToken);
        }

        // Update host.json if needed
        var hostJsonPath = Path.Combine(info.ProjectDirectory, "host.json");
        if (File.Exists(hostJsonPath))
        {
            await UpdateHostJsonForIsolatedWorker(hostJsonPath, result, cancellationToken);
        }
    }

    private async Task UpdateLocalSettingsForIsolatedWorker(string localSettingsPath, MigrationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(localSettingsPath, cancellationToken);
            var settings = JsonDocument.Parse(content);
            var rootElement = settings.RootElement.Clone();
            
            var updatedSettings = new Dictionary<string, object>();
            
            // Copy existing structure
            foreach (var property in rootElement.EnumerateObject())
            {
                if (property.Name == "Values")
                {
                    var values = new Dictionary<string, string>();
                    foreach (var value in property.Value.EnumerateObject())
                    {
                        values[value.Name] = value.Value.GetString() ?? "";
                    }
                    
                    // Update FUNCTIONS_WORKER_RUNTIME
                    values["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated";
                    updatedSettings["Values"] = values;
                }
                else
                {
                    updatedSettings[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                }
            }
            
            // Write updated settings
            var updatedContent = JsonSerializer.Serialize(updatedSettings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(localSettingsPath, updatedContent, cancellationToken);
            
            result.Warnings.Add("Updated local.settings.json with FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to update local.settings.json: {Error}", ex.Message);
            result.Warnings.Add("Failed to automatically update local.settings.json. Manually set FUNCTIONS_WORKER_RUNTIME='dotnet-isolated'");
        }
    }

    private async Task UpdateHostJsonForIsolatedWorker(string hostJsonPath, MigrationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(hostJsonPath, cancellationToken);
            var hostJson = JsonDocument.Parse(content);
            
            // Check if host.json needs updates for isolated worker
            bool needsUpdate = false;
            var rootDict = new Dictionary<string, object>();
            
            foreach (var property in hostJson.RootElement.EnumerateObject())
            {
                rootDict[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
            }
            
            // Ensure version is 2.0
            if (!rootDict.ContainsKey("version") || rootDict["version"].ToString() != "2.0")
            {
                rootDict["version"] = "2.0";
                needsUpdate = true;
            }
            
            if (needsUpdate)
            {
                var updatedContent = JsonSerializer.Serialize(rootDict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(hostJsonPath, updatedContent, cancellationToken);
                result.Warnings.Add("Updated host.json for isolated worker model");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to update host.json: {Error}", ex.Message);
        }
    }

    private async Task RemoveStartupCs(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var startupCsPath = Path.Combine(info.ProjectDirectory, "Startup.cs");
        if (File.Exists(startupCsPath))
        {
            try
            {
                // Read Startup.cs content to preserve any custom configuration
                var startupContent = await File.ReadAllTextAsync(startupCsPath, cancellationToken);
                
                // Back it up before removing
                var backupPath = Path.Combine(info.ProjectDirectory, "Startup.cs.backup");
                await File.WriteAllTextAsync(backupPath, startupContent, cancellationToken);
                
                File.Delete(startupCsPath);
                result.Warnings.Add("Removed Startup.cs (backed up as Startup.cs.backup). Migrate any custom configuration to Program.cs.");
                _logger.LogInformation("Removed Startup.cs and created backup");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to remove Startup.cs: {Error}", ex.Message);
                result.Warnings.Add("Failed to automatically remove Startup.cs. Manual removal recommended for isolated worker model.");
            }
        }
    }

    private async Task EnsureHostJsonExists(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var hostJsonPath = Path.Combine(info.ProjectDirectory, "host.json");
        if (!File.Exists(hostJsonPath))
        {
            var defaultHostJson = new
            {
                version = "2.0",
                logging = new
                {
                    applicationInsights = new
                    {
                        samplingSettings = new
                        {
                            isEnabled = true,
                            excludedTypes = "Request"
                        }
                    }
                }
            };
            
            var content = JsonSerializer.Serialize(defaultHostJson, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(hostJsonPath, content, cancellationToken);
            result.Warnings.Add("Created default host.json file");
        }
    }

    private async Task EnsureLocalSettingsJsonExists(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var localSettingsPath = Path.Combine(info.ProjectDirectory, "local.settings.json");
        if (!File.Exists(localSettingsPath))
        {
            var workerRuntime = info.UsesIsolatedModel ? "dotnet-isolated" : "dotnet";
            
            var defaultLocalSettings = new
            {
                IsEncrypted = false,
                Values = new Dictionary<string, string>
                {
                    ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                    ["FUNCTIONS_WORKER_RUNTIME"] = workerRuntime
                }
            };
            
            var content = JsonSerializer.Serialize(defaultLocalSettings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(localSettingsPath, content, cancellationToken);
            result.Warnings.Add("Created default local.settings.json file");
        }
    }

    private static void SetOrUpdateProperty(XElement propertyGroup, string name, string value)
    {
        var existingProperty = propertyGroup.Element(name);
        if (existingProperty != null)
        {
            existingProperty.Value = value;
        }
        else
        {
            propertyGroup.Add(new XElement(name, value));
        }
    }

    private static void EnsureItemIncluded(XElement itemGroup, string itemType, string include, Dictionary<string, string>? metadata = null)
    {
        var existingItem = itemGroup.Elements(itemType)
            .FirstOrDefault(e => e.Attribute("Include")?.Value == include);

        if (existingItem == null)
        {
            var item = new XElement(itemType, new XAttribute("Include", include));
            
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    item.Add(new XElement(kvp.Key, kvp.Value));
                }
            }
            
            itemGroup.Add(item);
        }
    }

    // Advanced worker model detection methods
    private async Task PerformAdvancedWorkerModelDetection(FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for mixed model usage (critical issue)
        if (info.HasInProcessPackages && info.HasIsolatedWorkerPackages)
        {
            info.Properties["HasMixedWorkerModels"] = "true";
            info.Properties["WorkerModelConflict"] = "Critical";
            _logger.LogWarning("Detected mixed worker models (both in-process and isolated packages) - this will cause runtime errors");
        }

        // Analyze runtime configuration conflicts
        if (info.ConfiguredForIsolatedRuntime && info.HasInProcessPackages && !info.HasIsolatedWorkerPackages)
        {
            info.Properties["RuntimeConfigurationMismatch"] = "true";
            _logger.LogWarning("local.settings.json configured for isolated but project uses in-process packages");
        }

        // Check for .NET 8 compatibility issues
        await CheckDotNet8Compatibility(info, cancellationToken);

        // Analyze binding compatibility
        AnalyzeBindingCompatibility(info);

        // Detect advanced patterns
        await DetectAdvancedFunctionsPatterns(info, cancellationToken);
    }

    private async Task CheckDotNet8Compatibility(FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        // Check if targeting .NET 8 or higher
        var targetFramework = info.Properties.GetValueOrDefault("TargetFramework", "");
        if (targetFramework.Contains("net8") || targetFramework.Contains("net9"))
        {
            info.Properties["TargetsDotNet8Plus"] = "true";

            // In-process model has limited .NET 8 support
            if (!info.UsesIsolatedModel)
            {
                info.Properties["Net8InProcessLimitations"] = "true";
                info.Properties["RequiresIsolatedForNet8"] = "true";
                _logger.LogWarning(".NET 8+ requires isolated worker model for full support");
            }

            // Check for compatible package versions
            if (info.Properties.ContainsKey("WorkerVersion"))
            {
                var workerVersion = info.Properties["WorkerVersion"];
                if (!IsVersion(workerVersion, ">=", "1.19.0"))
                {
                    info.Properties["WorkerVersionTooOldForNet8"] = "true";
                    _logger.LogWarning("Microsoft.Azure.Functions.Worker version {Version} is too old for .NET 8. Minimum required: 1.19.0", workerVersion);
                }
            }
        }
    }

    private void AnalyzeBindingCompatibility(FunctionsProjectInfo info)
    {
        // Check for bindings that require special handling in isolated model
        var problematicBindings = new List<string>();

        // These extensions have different APIs in isolated model
        var bindingsWithDifferentApis = new[] { "Durable", "SignalR", "EventGrid" };
        
        foreach (var binding in bindingsWithDifferentApis)
        {
            if (info.InProcessExtensions.Contains(binding) && info.UsesIsolatedModel)
            {
                problematicBindings.Add(binding);
            }
        }

        if (problematicBindings.Any())
        {
            info.Properties["ProblematicBindings"] = string.Join(",", problematicBindings);
            info.Properties["RequiresBindingMigration"] = "true";
        }

        // Check for output binding patterns that differ
        if (info.DetectedTriggerTypes.Any(t => t.Contains("Blob") || t.Contains("Queue")) && info.NeedsIsolatedModelMigration)
        {
            info.Properties["HasOutputBindings"] = "true";
            info.Properties["OutputBindingMigrationNeeded"] = "true";
        }
    }

    private async Task DetectAdvancedFunctionsPatterns(FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories).Take(50);
        
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);

                // Detect Durable Functions patterns
                if (content.Contains("DurableOrchestrationContext") || content.Contains("IDurableOrchestrationContext"))
                {
                    info.Properties["UsesDurableFunctions"] = "true";
                    info.Properties["DurableFunctionsModel"] = "InProcess";
                }
                else if (content.Contains("TaskOrchestrationContext") || content.Contains("DurableTaskClient"))
                {
                    info.Properties["UsesDurableFunctions"] = "true";
                    info.Properties["DurableFunctionsModel"] = "Isolated";
                }

                // Detect dependency injection patterns
                if (content.Contains("[Inject]") || content.Contains("IServiceProvider") || 
                    content.Contains("FunctionsStartup") || content.Contains("IFunctionsHostBuilder"))
                {
                    info.Properties["UsesDependencyInjection"] = "true";
                    
                    if (content.Contains("FunctionsStartup"))
                    {
                        info.Properties["DIPattern"] = "InProcess";
                    }
                    else if (content.Contains("ConfigureFunctionsWorkerDefaults"))
                    {
                        info.Properties["DIPattern"] = "Isolated";
                    }
                }

                // Detect middleware usage (isolated only)
                if (content.Contains("IFunctionsWorkerMiddleware") || content.Contains("UseWhen"))
                {
                    info.Properties["UsesMiddleware"] = "true";
                }

                // Detect custom binding usage
                if (content.Contains("IAsyncCollector") || content.Contains("IBinder"))
                {
                    info.Properties["UsesCustomBindings"] = "true";
                }

                // Detect HTTP trigger response patterns
                if (content.Contains("HttpResponseData") || content.Contains("HttpRequestData"))
                {
                    info.Properties["UsesIsolatedHttpPatterns"] = "true";
                }
                else if (content.Contains("HttpRequest") && content.Contains("IActionResult"))
                {
                    info.Properties["UsesInProcessHttpPatterns"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze patterns in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private bool IsVersion(string version, string comparison, string targetVersion)
    {
        try
        {
            if (NuGetVersion.TryParse(version, out var current) && 
                NuGetVersion.TryParse(targetVersion, out var target))
            {
                return comparison switch
                {
                    ">=" => current >= target,
                    ">" => current > target,
                    "<=" => current <= target,
                    "<" => current < target,
                    "==" => current == target,
                    _ => false
                };
            }
        }
        catch
        {
            // Fall back to string comparison
            return version.CompareTo(targetVersion) >= 0;
        }
        
        return false;
    }

    // Enhanced migration methods with .NET 8+ support
    private async Task AddIsolatedWorkerPackagesEnhanced(List<PackageReference> packageReferences, FunctionsProjectInfo info, CancellationToken cancellationToken)
    {
        // Determine appropriate package versions based on target framework
        var targetsDotNet8 = info.Properties.GetValueOrDefault("TargetsDotNet8Plus", "false") == "true";
        
        // Core isolated worker packages with .NET 8+ compatible versions
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker",
                Version = targetsDotNet8 ? "1.21.0" : "1.19.0"
            });
        }

        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker.Sdk"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker.Sdk",
                Version = targetsDotNet8 ? "1.17.2" : "1.16.4",
                Metadata = { ["PrivateAssets"] = "All" }
            });
        }

        // Add appropriate extension packages based on detected bindings
        await AddIsolatedExtensionPackages(packageReferences, info, targetsDotNet8, cancellationToken);

        // Application Insights for isolated worker
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Azure.Functions.Worker.ApplicationInsights"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Azure.Functions.Worker.ApplicationInsights",
                Version = "1.2.0"
            });
        }

        // Configuration support
        if (!packageReferences.Any(p => p.PackageId == "Microsoft.Extensions.Hosting"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.Extensions.Hosting",
                Version = targetsDotNet8 ? "8.0.0" : "7.0.1"
            });
        }
    }

    private async Task AddIsolatedExtensionPackages(List<PackageReference> packageReferences, FunctionsProjectInfo info, bool targetsDotNet8, CancellationToken cancellationToken)
    {
        // Map in-process extensions to isolated equivalents
        var extensionMappings = new Dictionary<string, (string Package, string Version)>
        {
            ["Storage"] = ("Microsoft.Azure.Functions.Worker.Extensions.Storage", "6.3.0"),
            ["Storage.Blobs"] = ("Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs", "6.3.0"),
            ["Storage.Queues"] = ("Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues", "5.2.0"),
            ["ServiceBus"] = ("Microsoft.Azure.Functions.Worker.Extensions.ServiceBus", "5.16.4"),
            ["EventHubs"] = ("Microsoft.Azure.Functions.Worker.Extensions.EventHubs", "6.0.2"),
            ["EventGrid"] = ("Microsoft.Azure.Functions.Worker.Extensions.EventGrid", "3.5.1"),
            ["CosmosDB"] = ("Microsoft.Azure.Functions.Worker.Extensions.CosmosDB", "4.6.0"),
            ["Timer"] = ("Microsoft.Azure.Functions.Worker.Extensions.Timer", "4.3.0"),
            ["Http"] = ("Microsoft.Azure.Functions.Worker.Extensions.Http", "3.2.0"),
            ["Sql"] = ("Microsoft.Azure.Functions.Worker.Extensions.Sql", "3.0.534"),
            ["Tables"] = ("Microsoft.Azure.Functions.Worker.Extensions.Tables", "1.3.0"),
            ["DurableTask"] = ("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", "1.1.1"),
            ["SignalRService"] = ("Microsoft.Azure.Functions.Worker.Extensions.SignalRService", "1.13.0")
        };

        // Add packages based on detected extensions
        foreach (var extension in info.InProcessExtensions)
        {
            if (extensionMappings.TryGetValue(extension, out var mapping))
            {
                if (!packageReferences.Any(p => p.PackageId == mapping.Package))
                {
                    packageReferences.Add(new PackageReference
                    {
                        PackageId = mapping.Package,
                        Version = mapping.Version
                    });
                }
            }
        }

        // Add packages based on detected trigger types
        foreach (var triggerType in info.DetectedTriggerTypes)
        {
            var triggerLower = triggerType.ToLowerInvariant();
            
            if (triggerLower.Contains("blob") && !packageReferences.Any(p => p.PackageId.Contains("Storage.Blobs")))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs",
                    Version = "6.3.0"
                });
            }
            
            if (triggerLower.Contains("queue") && !packageReferences.Any(p => p.PackageId.Contains("Storage.Queues")))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues",
                    Version = "5.2.0"
                });
            }
        }
    }

    // Enhanced configuration methods
    private async Task ConfigureIsolatedWorkerProjectEnhanced(FunctionsProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Determine target framework based on requirements
        var targetFramework = DetermineOptimalTargetFramework(info);
        
        // Set essential properties for isolated worker
        SetOrUpdateProperty(propertyGroup, "TargetFramework", targetFramework);
        SetOrUpdateProperty(propertyGroup, "OutputType", "Exe");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        SetOrUpdateProperty(propertyGroup, "AzureFunctionsVersion", "v4");
        
        // Add .NET 8 specific configurations
        if (targetFramework.Contains("net8") || targetFramework.Contains("net9"))
        {
            SetOrUpdateProperty(propertyGroup, "PublishReadyToRun", "true");
            SetOrUpdateProperty(propertyGroup, "EnableRequestDelegation", "true");
            SetOrUpdateProperty(propertyGroup, "InvariantGlobalization", "false");
        }
        
        // Include configuration files
        EnsureFunctionsFilesIncluded(info.ProjectDirectory, projectElement);
        
        // Add isolated worker packages with version detection
        await AddIsolatedWorkerPackagesEnhanced(packageReferences, info, cancellationToken);

        // Add migration guidance based on detected patterns
        await AddMigrationGuidance(info, result, cancellationToken);
    }

    private string DetermineOptimalTargetFramework(FunctionsProjectInfo info)
    {
        // Check current target framework
        var currentFramework = info.Properties.GetValueOrDefault("TargetFramework", "");
        
        // If already targeting .NET 8+, keep it
        if (currentFramework.Contains("net8") || currentFramework.Contains("net9"))
        {
            return currentFramework;
        }
        
        // For isolated worker model, recommend .NET 8
        if (info.UsesIsolatedModel || info.NeedsIsolatedModelMigration)
        {
            return "net8.0";
        }
        
        // For in-process model, check version compatibility
        if (info.FunctionsVersion == "v4")
        {
            // v4 supports up to .NET 6 for in-process
            return "net6.0";
        }
        
        // Default to .NET 8 for new projects
        return "net8.0";
    }

    private async Task AddMigrationGuidance(FunctionsProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        // Add specific guidance based on detected patterns
        if (info.Properties.GetValueOrDefault("HasMixedWorkerModels", "false") == "true")
        {
            result.Errors.Add("CRITICAL: Project has both in-process and isolated worker packages. Remove all Microsoft.Azure.WebJobs.* packages.");
        }

        if (info.Properties.GetValueOrDefault("UsesDurableFunctions", "false") == "true")
        {
            result.Warnings.Add("Durable Functions detected. Update orchestration code to use new isolated model APIs (TaskOrchestrationContext instead of IDurableOrchestrationContext).");
        }

        if (info.Properties.GetValueOrDefault("UsesCustomBindings", "false") == "true")
        {
            result.Warnings.Add("Custom bindings detected. Review and update binding implementations for isolated worker model.");
        }

        if (info.Properties.GetValueOrDefault("OutputBindingMigrationNeeded", "false") == "true")
        {
            result.Warnings.Add("Output bindings detected. In isolated model, use return values or MultiResponse for multiple outputs.");
        }

        if (info.Properties.GetValueOrDefault("UsesInProcessHttpPatterns", "false") == "true")
        {
            result.Warnings.Add("HTTP triggers use in-process patterns (HttpRequest/IActionResult). Update to use HttpRequestData/HttpResponseData.");
        }

        if (info.Properties.GetValueOrDefault("RequiresBindingMigration", "false") == "true")
        {
            var bindings = info.Properties.GetValueOrDefault("ProblematicBindings", "");
            result.Warnings.Add($"These bindings require migration: {bindings}. Check isolated model documentation for API changes.");
        }

        if (info.Properties.GetValueOrDefault("Net8InProcessLimitations", "false") == "true")
        {
            result.Warnings.Add("IMPORTANT: .NET 8 in-process support is limited. Set FUNCTIONS_INPROC_NET8_ENABLED='1' in Azure. Isolated model strongly recommended.");
        }
    }
    
    public void SetGenerateModernProgramCs(bool enabled)
    {
        _generateModernProgramCs = enabled;
        _logger.LogInformation("GenerateModernProgramCs set to: {Enabled}", enabled);
    }
}