using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class DockerProjectHandler : IDockerProjectHandler
{
    private readonly ILogger<DockerProjectHandler> _logger;

    public DockerProjectHandler(ILogger<DockerProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<DockerProjectInfo> DetectDockerConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new DockerProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty,
            IsOrchestrationProject = true // .dcproj files are orchestration projects
        };

        // Comprehensive project structure analysis
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Comprehensive Docker files detection and analysis
        await DetectAndAnalyzeDockerFiles(info, cancellationToken);

        // Analyze Docker Compose configurations
        await AnalyzeDockerComposeConfiguration(info, cancellationToken);

        // Detect container orchestration patterns
        await DetectOrchestrationPatterns(info, cancellationToken);

        // Analyze security and best practices
        await AnalyzeDockerSecurity(info, cancellationToken);

        // Detect CI/CD and deployment patterns
        await DetectCiCdPatterns(info, cancellationToken);

        // Analyze networking and volume configurations
        await AnalyzeNetworkingAndVolumes(info, cancellationToken);

        // Check for modern Docker features and optimizations
        await AnalyzeModernDockerFeatures(info, cancellationToken);

        // Detect legacy patterns and migration opportunities
        await DetectLegacyPatterns(info, cancellationToken);

        _logger.LogInformation("Detected Docker project: Type={Type}, ComposeFiles={Count}, Dockerfiles={DockerfileCount}, Services={ServiceCount}, UsesModernFeatures={Modern}, NeedsMigration={NeedsMigration}",
            GetDockerProjectType(info), info.ComposeFiles.Count, info.Dockerfiles.Count, info.Services.Count, 
            HasModernFeatures(info), info.Properties.ContainsKey("NeedsMigration"));

        return info;
    }

    public async Task MigrateDockerProjectAsync(
        DockerProjectInfo info, 
        XElement projectElement,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine optimal migration strategy based on project analysis
            if (IsModernDockerProject(info))
            {
                // Modernize existing .NET 8/9 Docker project with latest best practices
                await ModernizeDockerProject(info, projectElement, result, cancellationToken);
            }
            else if (IsLegacyDockerProject(info))
            {
                // Migrate legacy Docker project to modern .NET 8/9 patterns
                await MigrateLegacyDockerProject(info, projectElement, result, cancellationToken);
            }
            else if (RequiresSpecialHandling(info))
            {
                // Provide comprehensive migration guidance for complex scenarios
                await ProvideComprehensiveMigrationGuidance(info, result, cancellationToken);
            }
            else
            {
                // Configure new Docker project with modern best practices
                await ConfigureNewDockerProject(info, projectElement, result, cancellationToken);
            }

            // Apply common Docker optimizations and best practices
            await ApplyDockerOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully processed Docker project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate Docker project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "Docker migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void EnsureDockerFilesIncluded(string projectDirectory, XElement projectElement, DockerProjectInfo info)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Include docker-compose files
        foreach (var composeFile in info.ComposeFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, composeFile);
            EnsureItemIncluded(itemGroup, "None", relativePath);
        }

        // Include Dockerfiles
        foreach (var dockerfile in info.Dockerfiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, dockerfile);
            EnsureItemIncluded(itemGroup, "None", relativePath);
        }
    }

    public bool RequiresSpecialHandling(DockerProjectInfo info)
    {
        // Docker orchestration projects (.dcproj) typically require manual handling
        return info.IsOrchestrationProject || info.ComposeFiles.Count > 1;
    }

    public DockerMigrationGuidance GetMigrationGuidance(DockerProjectInfo info)
    {
        var guidance = new DockerMigrationGuidance
        {
            RequiresManualMigration = true
        };

        guidance.RecommendedActions.Add("Review docker-compose.yml files for service definitions");
        guidance.RecommendedActions.Add("Verify container dependencies and network configurations");
        guidance.RecommendedActions.Add("Consider using Docker Compose directly instead of .dcproj");

        guidance.AlternativeApproaches.Add("Use Visual Studio Container Tools with individual projects");
        guidance.AlternativeApproaches.Add("Migrate to Kubernetes manifests for production deployments");
        guidance.AlternativeApproaches.Add("Use Azure Container Apps or AWS ECS for cloud deployments");

        return guidance;
    }

    // Comprehensive analysis methods
    private async Task AnalyzeProjectStructure(DockerProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check if this is a .dcproj file or individual project with Docker support
        var projectExtension = Path.GetExtension(info.ProjectPath).ToLowerInvariant();
        info.IsOrchestrationProject = projectExtension == ".dcproj";

        // Check for SDK container support (.NET 8+)
        var enableSdkContainerSupport = project.GetPropertyValue("EnableSdkContainerSupport");
        if (string.Equals(enableSdkContainerSupport, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.Properties["UsesSdkContainerSupport"] = "true";
        }

        // Check for container image properties
        var containerImageName = project.GetPropertyValue("ContainerImageName");
        if (!string.IsNullOrEmpty(containerImageName))
        {
            info.Properties["ContainerImageName"] = containerImageName;
        }

        var containerImageTag = project.GetPropertyValue("ContainerImageTag");
        if (!string.IsNullOrEmpty(containerImageTag))
        {
            info.Properties["ContainerImageTag"] = containerImageTag;
        }

        // Check target framework for .NET version
        var targetFramework = project.GetPropertyValue("TargetFramework");
        if (!string.IsNullOrEmpty(targetFramework))
        {
            info.Properties["TargetFramework"] = targetFramework;
            info.Properties["IsNet8Plus"] = (targetFramework.StartsWith("net8.0") || targetFramework.StartsWith("net9.0")).ToString();
        }
    }

    private async Task DetectAndAnalyzeDockerFiles(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            // Find docker-compose files with comprehensive patterns
            var composeFiles = Directory.GetFiles(info.ProjectDirectory, "docker-compose*.yml", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(info.ProjectDirectory, "docker-compose*.yaml", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(info.ProjectDirectory, "compose*.yml", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(info.ProjectDirectory, "compose*.yaml", SearchOption.AllDirectories))
                .ToList();

            info.ComposeFiles = composeFiles;

            // Find Dockerfiles with comprehensive patterns
            var dockerfiles = Directory.GetFiles(info.ProjectDirectory, "Dockerfile*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(info.ProjectDirectory, "*.dockerfile", SearchOption.AllDirectories))
                .ToList();

            info.Dockerfiles = dockerfiles;

            // Analyze each Dockerfile for patterns and base images
            await AnalyzeDockerfiles(info, cancellationToken);

            // Find .dockerignore files
            var dockerignoreFiles = Directory.GetFiles(info.ProjectDirectory, ".dockerignore", SearchOption.AllDirectories);
            if (dockerignoreFiles.Any())
            {
                info.Properties["HasDockerignore"] = "true";
            }

            // Check for Docker-related scripts
            var dockerScripts = Directory.GetFiles(info.ProjectDirectory, "docker*.sh", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(info.ProjectDirectory, "docker*.ps1", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(info.ProjectDirectory, "build*.docker*", SearchOption.AllDirectories))
                .ToList();

            if (dockerScripts.Any())
            {
                info.Properties["HasDockerScripts"] = "true";
                info.Properties["DockerScriptCount"] = dockerScripts.Count.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect and analyze Docker files: {Error}", ex.Message);
        }
    }

    private async Task AnalyzeDockerfiles(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        foreach (var dockerfile in info.Dockerfiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(dockerfile, cancellationToken);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                // Analyze base image
                var fromLine = lines.FirstOrDefault(l => l.Trim().StartsWith("FROM", StringComparison.OrdinalIgnoreCase));
                if (fromLine != null)
                {
                    var baseImage = ExtractBaseImage(fromLine);
                    if (baseImage.Contains("mcr.microsoft.com/dotnet"))
                    {
                        info.Properties["UsesMicrosoftDotnetImages"] = "true";
                        
                        if (baseImage.Contains("aspnet:8.0") || baseImage.Contains("runtime:8.0"))
                            info.Properties["UsesNet8Runtime"] = "true";
                        else if (baseImage.Contains("aspnet:9.0") || baseImage.Contains("runtime:9.0"))
                            info.Properties["UsesNet9Runtime"] = "true";
                        else if (baseImage.Contains("aspnet:6.0") || baseImage.Contains("runtime:6.0"))
                            info.Properties["UsesNet6Runtime"] = "true";
                        
                        if (baseImage.Contains("-alpine"))
                            info.Properties["UsesAlpineImages"] = "true";
                        if (baseImage.Contains("-chiseled"))
                            info.Properties["UsesChiseledImages"] = "true";
                    }
                }

                // Check for multi-stage builds
                var fromCount = lines.Count(l => l.Trim().StartsWith("FROM", StringComparison.OrdinalIgnoreCase));
                if (fromCount > 1)
                {
                    info.Properties["UsesMultiStageBuilds"] = "true";
                    info.Properties["BuildStageCount"] = fromCount.ToString();
                }

                // Check for security best practices
                if (content.Contains("USER ", StringComparison.OrdinalIgnoreCase))
                    info.Properties["UsesNonRootUser"] = "true";

                if (content.Contains("HEALTHCHECK", StringComparison.OrdinalIgnoreCase))
                    info.Properties["HasHealthCheck"] = "true";

                // Check for .NET-specific optimizations
                if (content.Contains("dotnet publish", StringComparison.OrdinalIgnoreCase))
                {
                    info.Properties["UsesDotnetPublish"] = "true";
                    
                    if (content.Contains("--self-contained"))
                        info.Properties["UsesSelfContainedDeployment"] = "true";
                    if (content.Contains("--no-restore"))
                        info.Properties["UsesOptimizedBuild"] = "true";
                }

                // Check for volume definitions
                if (content.Contains("VOLUME", StringComparison.OrdinalIgnoreCase))
                    info.Properties["DefinesVolumes"] = "true";

                // Check for environment variables
                if (content.Contains("ENV ", StringComparison.OrdinalIgnoreCase))
                    info.Properties["DefinesEnvironmentVariables"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze Dockerfile {File}: {Error}", dockerfile, ex.Message);
            }
        }
    }

    private string ExtractBaseImage(string fromLine)
    {
        // Extract base image from FROM instruction
        var parts = fromLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[1].Split(' ')[0]; // Handle AS alias
        }
        return string.Empty;
    }

    private async Task AnalyzeDockerComposeConfiguration(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        foreach (var composeFile in info.ComposeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(composeFile, cancellationToken);
                var fileName = Path.GetFileName(composeFile);

                // Detect compose file type
                if (fileName.Contains("override"))
                    info.Properties["HasComposeOverride"] = "true";
                if (fileName.Contains("production") || fileName.Contains("prod"))
                    info.Properties["HasProductionCompose"] = "true";
                if (fileName.Contains("development") || fileName.Contains("dev"))
                    info.Properties["HasDevelopmentCompose"] = "true";

                // Parse services (improved parsing)
                await ParseComposeServices(info, content, composeFile, cancellationToken);

                // Check for version
                if (content.Contains("version:"))
                {
                    var versionMatch = Regex.Match(content, @"version:\s*['""]?([^'""]+)['""]?");
                    if (versionMatch.Success)
                    {
                        info.Properties["ComposeVersion"] = versionMatch.Groups[1].Value;
                    }
                }

                // Check for networking configuration
                if (content.Contains("networks:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["DefinesCustomNetworks"] = "true";

                // Check for volume configuration
                if (content.Contains("volumes:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["DefinesVolumes"] = "true";

                // Check for secrets management
                if (content.Contains("secrets:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["UsesSecrets"] = "true";

                // Check for environment file usage
                if (content.Contains("env_file:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["UsesEnvFiles"] = "true";

                // Check for build context
                if (content.Contains("build:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["HasBuildContext"] = "true";

                // Check for health checks
                if (content.Contains("healthcheck:", StringComparison.OrdinalIgnoreCase))
                    info.Properties["HasServiceHealthChecks"] = "true";

                // Check for resource limits
                if (content.Contains("mem_limit:") || content.Contains("cpus:"))
                    info.Properties["DefinesResourceLimits"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze Docker Compose file {File}: {Error}", composeFile, ex.Message);
            }
        }
    }

    private async Task ParseComposeServices(DockerProjectInfo info, string content, string composeFile, CancellationToken cancellationToken)
    {
        var lines = content.Split('\n');
        var inServicesSection = false;
        var currentIndent = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            if (trimmedLine == "services:")
            {
                inServicesSection = true;
                currentIndent = line.Length - line.TrimStart().Length;
                continue;
            }

            if (inServicesSection && !string.IsNullOrWhiteSpace(line))
            {
                var lineIndent = line.Length - line.TrimStart().Length;
                
                // If we've moved back to the same or lesser indent, we might be out of services
                if (lineIndent <= currentIndent && !trimmedLine.EndsWith(':'))
                {
                    inServicesSection = false;
                    continue;
                }

                // Service definition (immediate child of services with colon)
                if (lineIndent == currentIndent + 2 && trimmedLine.EndsWith(':') && !trimmedLine.Contains(' '))
                {
                    var serviceName = trimmedLine.TrimEnd(':');
                    if (!string.IsNullOrWhiteSpace(serviceName))
                    {
                        info.Services[serviceName] = composeFile;
                    }
                }
            }
        }
    }

    private async Task DetectOrchestrationPatterns(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for Kubernetes manifests
        var potentialKubernetesFiles = Directory.GetFiles(info.ProjectDirectory, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.yml", SearchOption.AllDirectories));
        
        var kubernetesFiles = new List<string>();
        foreach (var f in potentialKubernetesFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(f, cancellationToken);
                if (content.Contains("apiVersion:") && content.Contains("kind:"))
                {
                    kubernetesFiles.Add(f);
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        if (kubernetesFiles.Any())
        {
            info.Properties["HasKubernetesManifests"] = "true";
            info.Properties["KubernetesManifestCount"] = kubernetesFiles.Count.ToString();
        }

        // Check for Helm charts
        var helmFiles = Directory.GetFiles(info.ProjectDirectory, "Chart.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "Chart.yml", SearchOption.AllDirectories))
            .ToList();

        if (helmFiles.Any())
        {
            info.Properties["HasHelmCharts"] = "true";
        }

        // Check for Docker Swarm configurations
        var stackFiles = Directory.GetFiles(info.ProjectDirectory, "*stack*.yml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*stack*.yaml", SearchOption.AllDirectories))
            .ToList();

        if (stackFiles.Any())
        {
            info.Properties["HasDockerSwarmStacks"] = "true";
        }

        // Check for container orchestration tools
        var skaffoldFiles = Directory.GetFiles(info.ProjectDirectory, "skaffold.yaml", SearchOption.AllDirectories);
        if (skaffoldFiles.Any())
        {
            info.Properties["UsesSkaffold"] = "true";
        }

        var tiltFiles = Directory.GetFiles(info.ProjectDirectory, "Tiltfile", SearchOption.AllDirectories);
        if (tiltFiles.Any())
        {
            info.Properties["UsesTilt"] = "true";
        }
    }

    private async Task AnalyzeDockerSecurity(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        var securityIssues = new List<string>();
        var securityFeatures = new List<string>();

        foreach (var dockerfile in info.Dockerfiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(dockerfile, cancellationToken);

                // Check for security best practices
                if (!content.Contains("USER ", StringComparison.OrdinalIgnoreCase))
                {
                    securityIssues.Add("Dockerfile runs as root user (security risk)");
                }
                else
                {
                    securityFeatures.Add("Uses non-root user");
                }

                // Check for secrets handling
                if (content.Contains("ARG") && (content.Contains("PASSWORD") || content.Contains("SECRET") || content.Contains("KEY")))
                {
                    securityIssues.Add("Potential secrets in build args");
                }

                // Check for package updates
                if (content.Contains("apt-get update") && !content.Contains("apt-get upgrade"))
                {
                    securityIssues.Add("Package updates without upgrades");
                }

                // Check for minimal base images
                if (content.Contains("mcr.microsoft.com/dotnet"))
                {
                    if (content.Contains("-alpine"))
                        securityFeatures.Add("Uses minimal Alpine base images");
                    if (content.Contains("-chiseled"))
                        securityFeatures.Add("Uses ultra-minimal chiseled images");
                }

                // Check for COPY vs ADD usage
                if (content.Contains("ADD ") && !content.Contains("--from="))
                {
                    securityIssues.Add("Uses ADD instead of COPY (potential security risk)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze security for Dockerfile {File}: {Error}", dockerfile, ex.Message);
            }
        }

        if (securityIssues.Any())
        {
            info.Properties["SecurityIssues"] = string.Join("; ", securityIssues);
        }

        if (securityFeatures.Any())
        {
            info.Properties["SecurityFeatures"] = string.Join("; ", securityFeatures);
        }
    }

    private async Task DetectCiCdPatterns(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for GitHub Actions with Docker
        var githubWorkflows = Directory.GetFiles(Path.Combine(info.ProjectDirectory, ".github", "workflows"), "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(Path.Combine(info.ProjectDirectory, ".github", "workflows"), "*.yaml", SearchOption.TopDirectoryOnly))
            .ToList();

        foreach (var workflow in githubWorkflows)
        {
            try
            {
                var content = await File.ReadAllTextAsync(workflow, cancellationToken);
                if (content.Contains("docker") || content.Contains("container"))
                {
                    info.Properties["HasGitHubActionsDocker"] = "true";
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze GitHub workflow {File}: {Error}", workflow, ex.Message);
            }
        }

        // Check for Azure DevOps pipelines
        var azurePipelines = Directory.GetFiles(info.ProjectDirectory, "azure-pipelines*.yml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "azure-pipelines*.yaml", SearchOption.AllDirectories))
            .ToList();

        if (azurePipelines.Any())
        {
            info.Properties["HasAzurePipelines"] = "true";
        }

        // Check for GitLab CI
        var gitlabCi = Directory.GetFiles(info.ProjectDirectory, ".gitlab-ci.yml", SearchOption.AllDirectories);
        if (gitlabCi.Any())
        {
            info.Properties["HasGitLabCI"] = "true";
        }

        // Check for Jenkins files
        var jenkinsFiles = Directory.GetFiles(info.ProjectDirectory, "Jenkinsfile*", SearchOption.AllDirectories);
        if (jenkinsFiles.Any())
        {
            info.Properties["HasJenkinsfile"] = "true";
        }
    }

    private async Task AnalyzeNetworkingAndVolumes(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        foreach (var composeFile in info.ComposeFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(composeFile, cancellationToken);

                // Analyze networking patterns
                if (content.Contains("bridge"))
                    info.Properties["UsesBridgeNetworking"] = "true";
                if (content.Contains("host"))
                    info.Properties["UsesHostNetworking"] = "true";
                if (content.Contains("overlay"))
                    info.Properties["UsesOverlayNetworking"] = "true";

                // Analyze port configurations
                var portMatches = Regex.Matches(content, @"ports:\s*\n\s*-\s*['""]?(\d+):(\d+)");
                if (portMatches.Count > 0)
                {
                    info.Properties["ExposedPortCount"] = portMatches.Count.ToString();
                }

                // Analyze volume patterns
                if (content.Contains("bind"))
                    info.Properties["UsesBindMounts"] = "true";
                if (content.Contains("volume"))
                    info.Properties["UsesNamedVolumes"] = "true";
                if (content.Contains("tmpfs"))
                    info.Properties["UsesTmpfsVolumes"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze networking and volumes for {File}: {Error}", composeFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeModernDockerFeatures(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        var modernFeatureCount = 0;

        // Check for .NET 8+ SDK container support
        if (info.Properties.ContainsKey("UsesSdkContainerSupport"))
        {
            modernFeatureCount++;
        }

        // Check for modern base images
        if (info.Properties.ContainsKey("UsesNet8Runtime") || info.Properties.ContainsKey("UsesNet9Runtime"))
        {
            modernFeatureCount++;
        }

        // Check for chiseled/minimal images
        if (info.Properties.ContainsKey("UsesChiseledImages") || info.Properties.ContainsKey("UsesAlpineImages"))
        {
            modernFeatureCount++;
        }

        // Check for multi-stage builds
        if (info.Properties.ContainsKey("UsesMultiStageBuilds"))
        {
            modernFeatureCount++;
        }

        // Check for health checks
        if (info.Properties.ContainsKey("HasHealthCheck") || info.Properties.ContainsKey("HasServiceHealthChecks"))
        {
            modernFeatureCount++;
        }

        // Check for secrets management
        if (info.Properties.ContainsKey("UsesSecrets"))
        {
            modernFeatureCount++;
        }

        // Check for BuildKit features
        foreach (var dockerfile in info.Dockerfiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(dockerfile, cancellationToken);
                if (content.Contains("# syntax=") || content.Contains("--mount="))
                {
                    info.Properties["UsesBuildKit"] = "true";
                    modernFeatureCount++;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to check BuildKit features in {File}: {Error}", dockerfile, ex.Message);
            }
        }

        info.Properties["ModernFeatureCount"] = modernFeatureCount.ToString();
        info.Properties["HasModernFeatures"] = (modernFeatureCount >= 3).ToString();
    }

    private async Task DetectLegacyPatterns(DockerProjectInfo info, CancellationToken cancellationToken)
    {
        var legacyPatterns = new List<string>();

        // Check for legacy .NET runtimes
        if (info.Properties.ContainsKey("UsesNet6Runtime"))
        {
            legacyPatterns.Add("Uses .NET 6 runtime (consider upgrading to .NET 8+)");
        }

        foreach (var dockerfile in info.Dockerfiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(dockerfile, cancellationToken);

                // Check for deprecated base images
                if (content.Contains("microsoft/dotnet"))
                {
                    legacyPatterns.Add("Uses deprecated microsoft/dotnet base images");
                }

                // Check for legacy patterns
                if (content.Contains("MAINTAINER"))
                {
                    legacyPatterns.Add("Uses deprecated MAINTAINER instruction");
                }

                // Check for inefficient patterns
                if (content.Contains("RUN apt-get update && apt-get install") && !content.Contains("--no-install-recommends"))
                {
                    legacyPatterns.Add("Inefficient package installation without --no-install-recommends");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to detect legacy patterns in {File}: {Error}", dockerfile, ex.Message);
            }
        }

        // Check for legacy compose versions
        if (info.Properties.ContainsKey("ComposeVersion"))
        {
            var version = info.Properties["ComposeVersion"];
            if (version.StartsWith("2.") || version.StartsWith("1."))
            {
                legacyPatterns.Add($"Uses legacy Docker Compose version {version}");
            }
        }

        if (legacyPatterns.Any())
        {
            info.Properties["LegacyPatterns"] = string.Join("; ", legacyPatterns);
            info.Properties["NeedsMigration"] = "true";
        }
    }

    // Migration methods
    private async Task ModernizeDockerProject(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing .NET 8/9 Docker project with latest best practices");

        // Apply latest Docker optimizations
        await ApplyLatestDockerOptimizations(info, projectElement, result, cancellationToken);

        // Update to latest base images and practices
        await RecommendLatestBaseImages(info, result, cancellationToken);

        result.Warnings.Add("Docker project modernized with latest .NET 8/9 practices and optimizations.");
    }

    private async Task MigrateLegacyDockerProject(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating legacy Docker project to modern .NET 8/9 patterns");

        // Enable SDK container support if applicable
        await ConfigureModernContainerSupport(info, projectElement, result, cancellationToken);

        // Provide migration guidance for legacy patterns
        await ProvideLegacyMigrationGuidance(info, result, cancellationToken);

        result.Warnings.Add("Legacy Docker project migration guidance provided. Review Dockerfiles and compose configurations.");
        result.Warnings.Add("Consider using .NET 8+ SDK container support for simplified containerization.");
    }

    private async Task ProvideComprehensiveMigrationGuidance(DockerProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Providing comprehensive migration guidance for complex Docker project");

        var guidance = GetMigrationGuidance(info);
        
        result.Warnings.Add("Complex Docker orchestration project requires manual migration:");
        foreach (var action in guidance.RecommendedActions)
        {
            result.Warnings.Add($"• {action}");
        }

        result.Warnings.Add("Alternative approaches:");
        foreach (var approach in guidance.AlternativeApproaches)
        {
            result.Warnings.Add($"• {approach}");
        }

        // Provide specific guidance based on analysis
        await ProvideSpecificMigrationGuidance(info, result, cancellationToken);
    }

    private async Task ConfigureNewDockerProject(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring new Docker project with modern best practices");

        // Configure modern container properties
        await ConfigureModernContainerSupport(info, projectElement, result, cancellationToken);

        // Ensure Docker files are included
        EnsureDockerFilesIncluded(info.ProjectDirectory, projectElement, info);

        result.Warnings.Add("New Docker project configured with modern .NET 8/9 container support.");
    }

    private async Task ApplyDockerOptimizations(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Apply .NET 8+ SDK container optimizations
        if (info.Properties.ContainsKey("IsNet8Plus") && info.Properties["IsNet8Plus"] == "true")
        {
            SetOrUpdateProperty(propertyGroup, "EnableSdkContainerSupport", "true");
            
            if (!string.IsNullOrEmpty(info.Properties.GetValueOrDefault("ContainerImageName")))
            {
                SetOrUpdateProperty(propertyGroup, "ContainerImageName", info.Properties["ContainerImageName"]);
            }
            else
            {
                var defaultImageName = Path.GetFileNameWithoutExtension(info.ProjectPath).ToLowerInvariant();
                SetOrUpdateProperty(propertyGroup, "ContainerImageName", defaultImageName);
            }

            // Container optimization properties
            SetOrUpdateProperty(propertyGroup, "ContainerImageTag", "latest");
            SetOrUpdateProperty(propertyGroup, "ContainerWorkingDirectory", "/app");
        }

        result.Warnings.Add("Applied Docker project optimizations for modern .NET development.");
    }

    private async Task ConfigureModernContainerSupport(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Enable SDK container support for .NET 8+
        SetOrUpdateProperty(propertyGroup, "EnableSdkContainerSupport", "true");
        
        // Configure container image properties
        var imageName = Path.GetFileNameWithoutExtension(info.ProjectPath).ToLowerInvariant();
        SetOrUpdateProperty(propertyGroup, "ContainerImageName", imageName);
        SetOrUpdateProperty(propertyGroup, "ContainerImageTag", "latest");
        SetOrUpdateProperty(propertyGroup, "ContainerWorkingDirectory", "/app");
        
        // Security optimizations
        SetOrUpdateProperty(propertyGroup, "ContainerUser", "app");
        
        result.Warnings.Add("Configured modern .NET 8+ SDK container support with security optimizations.");
    }

    private async Task ApplyLatestDockerOptimizations(DockerProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var recommendations = new List<string>();

        // Recommend latest .NET runtime images
        if (!info.Properties.ContainsKey("UsesNet8Runtime") && !info.Properties.ContainsKey("UsesNet9Runtime"))
        {
            recommendations.Add("Update Dockerfiles to use latest .NET 8+ runtime images (mcr.microsoft.com/dotnet/aspnet:8.0)");
        }

        // Recommend chiseled images for minimal attack surface
        if (!info.Properties.ContainsKey("UsesChiseledImages"))
        {
            recommendations.Add("Consider using chiseled .NET images for minimal attack surface (mcr.microsoft.com/dotnet/aspnet:8.0-chiseled)");
        }

        // Recommend multi-stage builds
        if (!info.Properties.ContainsKey("UsesMultiStageBuilds"))
        {
            recommendations.Add("Implement multi-stage Dockerfiles for optimized image size");
        }

        // Recommend health checks
        if (!info.Properties.ContainsKey("HasHealthCheck"))
        {
            recommendations.Add("Add HEALTHCHECK instructions to Dockerfiles for container monitoring");
        }

        // Recommend non-root user
        if (info.Properties.ContainsKey("SecurityIssues") && info.Properties["SecurityIssues"].Contains("root user"))
        {
            recommendations.Add("Configure containers to run as non-root user for security");
        }

        // Recommend .dockerignore
        if (!info.Properties.ContainsKey("HasDockerignore"))
        {
            recommendations.Add("Create .dockerignore file to optimize build context");
        }

        foreach (var recommendation in recommendations)
        {
            result.Warnings.Add(recommendation);
        }
    }

    private async Task RecommendLatestBaseImages(DockerProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        result.Warnings.Add("Latest .NET Docker Image Recommendations:");
        result.Warnings.Add("• Runtime: mcr.microsoft.com/dotnet/aspnet:8.0-chiseled (minimal attack surface)");
        result.Warnings.Add("• SDK: mcr.microsoft.com/dotnet/sdk:8.0 (for multi-stage builds)");
        result.Warnings.Add("• Alpine: mcr.microsoft.com/dotnet/aspnet:8.0-alpine (smaller size)");
        result.Warnings.Add("• Use specific tags instead of 'latest' for production deployments");
    }

    private async Task ProvideLegacyMigrationGuidance(DockerProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        if (info.Properties.ContainsKey("LegacyPatterns"))
        {
            result.Warnings.Add($"Legacy patterns detected: {info.Properties["LegacyPatterns"]}");
        }

        result.Warnings.Add("Legacy Docker Migration Steps:");
        result.Warnings.Add("1. Update base images to latest .NET 8+ versions");
        result.Warnings.Add("2. Replace deprecated instructions (MAINTAINER → LABEL)");
        result.Warnings.Add("3. Implement multi-stage builds for optimization");
        result.Warnings.Add("4. Add security best practices (non-root user, health checks)");
        result.Warnings.Add("5. Update Docker Compose to version 3.8+ or consider Compose Spec");
    }

    private async Task ProvideSpecificMigrationGuidance(DockerProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        // Kubernetes-specific guidance
        if (info.Properties.ContainsKey("HasKubernetesManifests"))
        {
            result.Warnings.Add("Kubernetes integration detected - consider using Helm for deployment management");
        }

        // CI/CD specific guidance
        if (info.Properties.ContainsKey("HasGitHubActionsDocker"))
        {
            result.Warnings.Add("GitHub Actions Docker workflows detected - ensure proper security practices");
        }

        // Security-specific guidance
        if (info.Properties.ContainsKey("SecurityIssues"))
        {
            result.Warnings.Add($"Security improvements needed: {info.Properties["SecurityIssues"]}");
        }

        // Orchestration-specific guidance
        if (info.Services.Count > 5)
        {
            result.Warnings.Add("Complex multi-service setup detected - consider microservices orchestration patterns");
        }
    }

    // Helper methods
    private bool IsModernDockerProject(DockerProjectInfo info)
    {
        return info.Properties.ContainsKey("IsNet8Plus") && info.Properties["IsNet8Plus"] == "true" &&
               !info.Properties.ContainsKey("NeedsMigration");
    }

    private bool IsLegacyDockerProject(DockerProjectInfo info)
    {
        return info.Properties.ContainsKey("NeedsMigration") && info.Properties["NeedsMigration"] == "true";
    }

    private string GetDockerProjectType(DockerProjectInfo info)
    {
        if (info.Properties.ContainsKey("UsesSdkContainerSupport"))
            return "SDK-Container";
        if (info.Properties.ContainsKey("HasKubernetesManifests"))
            return "Kubernetes";
        if (info.IsOrchestrationProject)
            return "Orchestration";
        if (info.ComposeFiles.Any())
            return "Compose";
        return "Dockerfile";
    }

    private bool HasModernFeatures(DockerProjectInfo info)
    {
        return info.Properties.ContainsKey("HasModernFeatures") && 
               info.Properties["HasModernFeatures"] == "true";
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

    private static void EnsureItemIncluded(XElement itemGroup, string itemType, string include)
    {
        var existingItem = itemGroup.Elements(itemType)
            .FirstOrDefault(e => e.Attribute("Include")?.Value == include);

        if (existingItem == null)
        {
            itemGroup.Add(new XElement(itemType, new XAttribute("Include", include)));
        }
    }
}