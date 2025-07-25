using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SdkMigrator.Services;

/// <summary>
/// Advanced deployment pattern analyzer for CI/CD configurations,
/// containerization strategies, and deployment architectures
/// </summary>
public class DeploymentAnalyzer
{
    private readonly ILogger<DeploymentAnalyzer> _logger;

    public DeploymentAnalyzer(ILogger<DeploymentAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes deployment patterns including CI/CD pipelines, containerization,
    /// orchestration, and deployment strategies
    /// </summary>
    public async Task<DeploymentAnalysisResult> AnalyzeDeploymentPatterns(
        Project project,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new DeploymentAnalysisResult
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = projectDirectory
        };

        try
        {
            // Analyze CI/CD configurations
            await AnalyzeCiCdPipelines(projectDirectory, result, cancellationToken);

            // Analyze containerization
            await AnalyzeContainerization(project, projectDirectory, result, cancellationToken);

            // Analyze deployment configurations
            await AnalyzeDeploymentConfigurations(projectDirectory, result, cancellationToken);

            // Analyze infrastructure as code
            await AnalyzeInfrastructureAsCode(projectDirectory, result, cancellationToken);

            // Analyze deployment strategies
            await AnalyzeDeploymentStrategies(project, result, cancellationToken);

            // Analyze environment configurations
            await AnalyzeEnvironmentConfigurations(projectDirectory, result, cancellationToken);

            // Analyze security and secrets management
            await AnalyzeSecurityConfiguration(projectDirectory, result, cancellationToken);

            // Determine deployment maturity
            DetermineDeploymentMaturity(result);

            _logger.LogInformation("Deployment analysis complete: Maturity={Maturity}, CI/CD={HasCICD}, Container={HasContainer}, IaC={HasIaC}",
                result.DeploymentMaturity, result.HasCiCdPipeline, result.HasContainerization, result.HasInfrastructureAsCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze deployment patterns for {ProjectPath}", project.FullPath);
            result.AnalysisErrors.Add($"Deployment analysis error: {ex.Message}");
        }

        return result;
    }

    private async Task AnalyzeCiCdPipelines(string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // GitHub Actions
        var githubWorkflowsPath = Path.Combine(projectDirectory, ".github", "workflows");
        if (Directory.Exists(githubWorkflowsPath))
        {
            result.HasCiCdPipeline = true;
            result.CiCdPlatforms.Add("GitHub Actions");
            
            var workflowFiles = Directory.GetFiles(githubWorkflowsPath, "*.yml")
                .Concat(Directory.GetFiles(githubWorkflowsPath, "*.yaml"));
            
            foreach (var workflowFile in workflowFiles)
            {
                await AnalyzeGitHubActionsWorkflow(workflowFile, result, cancellationToken);
            }
        }

        // Azure DevOps
        var azureDevOpsFiles = Directory.GetFiles(projectDirectory, "azure-pipelines*.yml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDirectory, "azure-pipelines*.yaml", SearchOption.AllDirectories));
        
        if (azureDevOpsFiles.Any())
        {
            result.HasCiCdPipeline = true;
            result.CiCdPlatforms.Add("Azure DevOps");
            
            foreach (var pipelineFile in azureDevOpsFiles)
            {
                await AnalyzeAzureDevOpsPipeline(pipelineFile, result, cancellationToken);
            }
        }

        // GitLab CI
        var gitlabCiPath = Path.Combine(projectDirectory, ".gitlab-ci.yml");
        if (File.Exists(gitlabCiPath))
        {
            result.HasCiCdPipeline = true;
            result.CiCdPlatforms.Add("GitLab CI");
            await AnalyzeGitLabCiPipeline(gitlabCiPath, result, cancellationToken);
        }

        // Jenkins
        var jenkinsFile = Path.Combine(projectDirectory, "Jenkinsfile");
        if (File.Exists(jenkinsFile))
        {
            result.HasCiCdPipeline = true;
            result.CiCdPlatforms.Add("Jenkins");
            await AnalyzeJenkinsPipeline(jenkinsFile, result, cancellationToken);
        }
    }

    private async Task AnalyzeGitHubActionsWorkflow(string workflowPath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(workflowPath, cancellationToken);
            
            var pipeline = new CiCdPipeline
            {
                Name = Path.GetFileName(workflowPath),
                Platform = "GitHub Actions",
                FilePath = workflowPath
            };

            // Analyze triggers using regex
            var triggerMatch = Regex.Match(content, @"on:\s*\[(.*?)\]", RegexOptions.Singleline);
            if (triggerMatch.Success)
            {
                var triggers = triggerMatch.Groups[1].Value.Split(',')
                    .Select(t => t.Trim().Trim('"', '\''))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                pipeline.Triggers = triggers;
            }
            else
            {
                // Check for detailed trigger format
                var pushMatch = Regex.Match(content, @"on:\s*push:", RegexOptions.Multiline);
                var prMatch = Regex.Match(content, @"on:\s*pull_request:", RegexOptions.Multiline);
                var scheduleMatch = Regex.Match(content, @"on:\s*schedule:", RegexOptions.Multiline);
                
                var triggers = new List<string>();
                if (pushMatch.Success) triggers.Add("push");
                if (prMatch.Success) triggers.Add("pull_request");
                if (scheduleMatch.Success) triggers.Add("schedule");
                pipeline.Triggers = triggers;
            }

            // Analyze jobs
            var jobMatches = Regex.Matches(content, @"^\s{2}(\w+):\s*$", RegexOptions.Multiline);
            foreach (Match jobMatch in jobMatches)
            {
                var jobName = jobMatch.Groups[1].Value;
                pipeline.Jobs.Add(jobName);
                
                // Check for deployment job
                var jobSection = GetJobSection(content, jobName);
                if (jobSection.Contains("environment:") || jobSection.Contains("deployment"))
                {
                    pipeline.HasDeploymentStages = true;
                }
            }

            // Detect deployment strategies
            if (content.Contains("deployment") || content.Contains("environment:"))
            {
                pipeline.HasDeploymentStages = true;
            }

            // Detect advanced patterns
            if (content.Contains("matrix:"))
            {
                pipeline.Features.Add("Matrix builds");
            }
            
            if (content.Contains("if:") && content.Contains("github.ref"))
            {
                pipeline.Features.Add("Conditional deployment");
            }

            result.CiCdPipelines.Add(pipeline);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze GitHub Actions workflow {File}: {Error}", workflowPath, ex.Message);
        }
    }

    private async Task AnalyzeAzureDevOpsPipeline(string pipelinePath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(pipelinePath, cancellationToken);
            var pipeline = new CiCdPipeline
            {
                Name = Path.GetFileName(pipelinePath),
                Platform = "Azure DevOps",
                FilePath = pipelinePath
            };

            // Detect multi-stage pipeline
            if (content.Contains("stages:"))
            {
                pipeline.HasDeploymentStages = true;
                pipeline.Features.Add("Multi-stage pipeline");
            }

            // Detect deployment jobs
            if (content.Contains("deployment:") || content.Contains("- deployment"))
            {
                pipeline.Features.Add("Deployment jobs");
            }

            // Detect environments
            if (content.Contains("environment:"))
            {
                var envMatches = Regex.Matches(content, @"environment:\s*['""]*(\w+)");
                foreach (Match match in envMatches)
                {
                    pipeline.Environments.Add(match.Groups[1].Value);
                }
            }

            // Detect advanced features
            if (content.Contains("strategy:") && content.Contains("canary"))
            {
                result.DeploymentStrategies.Add("Canary");
            }

            if (content.Contains("strategy:") && content.Contains("rolling"))
            {
                result.DeploymentStrategies.Add("Rolling");
            }

            result.CiCdPipelines.Add(pipeline);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze Azure DevOps pipeline {File}: {Error}", pipelinePath, ex.Message);
        }
    }

    private async Task AnalyzeContainerization(Project project, string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for Dockerfiles
        var dockerfiles = Directory.GetFiles(projectDirectory, "Dockerfile*", SearchOption.AllDirectories);
        if (dockerfiles.Any())
        {
            result.HasContainerization = true;
            
            foreach (var dockerfile in dockerfiles)
            {
                await AnalyzeDockerfile(dockerfile, result, cancellationToken);
            }
        }

        // Check for Docker Compose
        var composeFiles = Directory.GetFiles(projectDirectory, "docker-compose*.yml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDirectory, "docker-compose*.yaml", SearchOption.AllDirectories));
        
        if (composeFiles.Any())
        {
            result.HasOrchestration = true;
            result.OrchestrationPlatforms.Add("Docker Compose");
            
            foreach (var composeFile in composeFiles)
            {
                await AnalyzeDockerCompose(composeFile, result, cancellationToken);
            }
        }

        // Check for .NET SDK container support
        var enableSdkContainer = project.GetPropertyValue("EnableSdkContainerSupport");
        if (string.Equals(enableSdkContainer, "true", StringComparison.OrdinalIgnoreCase))
        {
            result.HasContainerization = true;
            result.ContainerizationFeatures.Add(".NET SDK Container Support");
        }

        // Check for Kubernetes manifests
        var k8sPatterns = new[] { "*.yaml", "*.yml" };
        var k8sKeywords = new[] { "apiVersion:", "kind:", "Deployment", "Service", "ConfigMap" };
        
        foreach (var pattern in k8sPatterns)
        {
            var yamlFiles = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            foreach (var yamlFile in yamlFiles)
            {
                var content = await File.ReadAllTextAsync(yamlFile, cancellationToken);
                if (k8sKeywords.Any(keyword => content.Contains(keyword)))
                {
                    result.HasOrchestration = true;
                    if (!result.OrchestrationPlatforms.Contains("Kubernetes"))
                    {
                        result.OrchestrationPlatforms.Add("Kubernetes");
                    }
                    
                    await AnalyzeKubernetesManifest(yamlFile, result, cancellationToken);
                }
            }
        }
    }

    private async Task AnalyzeDockerfile(string dockerfilePath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(dockerfilePath, cancellationToken);
            var containerInfo = new ContainerInfo
            {
                Type = "Docker",
                FilePath = dockerfilePath
            };

            // Analyze base image
            var fromMatch = Regex.Match(content, @"FROM\s+([^\s]+)");
            if (fromMatch.Success)
            {
                containerInfo.BaseImage = fromMatch.Groups[1].Value;
            }

            // Detect multi-stage builds
            var fromCount = Regex.Matches(content, @"^FROM\s+", RegexOptions.Multiline).Count;
            if (fromCount > 1)
            {
                containerInfo.Features.Add("Multi-stage build");
                result.ContainerizationFeatures.Add("Multi-stage Docker builds");
            }

            // Detect security best practices
            if (content.Contains("USER ") && !content.Contains("USER root"))
            {
                containerInfo.SecurityFeatures.Add("Non-root user");
            }

            if (content.Contains("--no-cache"))
            {
                containerInfo.SecurityFeatures.Add("No-cache layers");
            }

            // Detect .NET specific optimizations
            if (content.Contains("dotnet restore") && content.Contains("--runtime"))
            {
                containerInfo.Features.Add("Runtime-specific restore");
            }

            if (content.Contains("dotnet publish") && content.Contains("-c Release"))
            {
                containerInfo.Features.Add("Release build");
            }

            result.Containers.Add(containerInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze Dockerfile {File}: {Error}", dockerfilePath, ex.Message);
        }
    }

    private async Task AnalyzeDeploymentConfigurations(string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for deployment scripts
        var deploymentScripts = Directory.GetFiles(projectDirectory, "deploy.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".ps1", ".sh", ".cmd", ".bat" }.Contains(Path.GetExtension(f)));

        foreach (var script in deploymentScripts)
        {
            result.DeploymentScripts.Add(new DeploymentScript
            {
                Path = script,
                Type = Path.GetExtension(script) switch
                {
                    ".ps1" => "PowerShell",
                    ".sh" => "Shell",
                    ".cmd" or ".bat" => "Batch",
                    _ => "Unknown"
                }
            });
        }

        // Check for environment-specific configurations
        var configFiles = new[]
        {
            "appsettings.*.json",
            "web.*.config",
            "app.*.config"
        };

        foreach (var pattern in configFiles)
        {
            var files = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var envName = ExtractEnvironmentName(file);
                if (!string.IsNullOrEmpty(envName) && !result.Environments.Contains(envName))
                {
                    result.Environments.Add(envName);
                }
            }
        }
    }

    private async Task AnalyzeInfrastructureAsCode(string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Terraform
        var terraformFiles = Directory.GetFiles(projectDirectory, "*.tf", SearchOption.AllDirectories);
        if (terraformFiles.Any())
        {
            result.HasInfrastructureAsCode = true;
            result.IaCPlatforms.Add("Terraform");
        }

        // ARM Templates
        var armTemplates = Directory.GetFiles(projectDirectory, "*.json", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("$schema") && 
                       File.ReadAllText(f).Contains("deploymentTemplate"));
        
        if (armTemplates.Any())
        {
            result.HasInfrastructureAsCode = true;
            result.IaCPlatforms.Add("ARM Templates");
        }

        // Bicep
        var bicepFiles = Directory.GetFiles(projectDirectory, "*.bicep", SearchOption.AllDirectories);
        if (bicepFiles.Any())
        {
            result.HasInfrastructureAsCode = true;
            result.IaCPlatforms.Add("Bicep");
        }

        // AWS CloudFormation
        var cfnTemplates = Directory.GetFiles(projectDirectory, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDirectory, "*.yml", SearchOption.AllDirectories))
            .Where(f => File.ReadAllText(f).Contains("AWSTemplateFormatVersion"));
        
        if (cfnTemplates.Any())
        {
            result.HasInfrastructureAsCode = true;
            result.IaCPlatforms.Add("CloudFormation");
        }

        // Pulumi
        if (File.Exists(Path.Combine(projectDirectory, "Pulumi.yaml")))
        {
            result.HasInfrastructureAsCode = true;
            result.IaCPlatforms.Add("Pulumi");
        }
    }

    private async Task AnalyzeDeploymentStrategies(Project project, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check project properties for deployment hints
        var msDeploy = project.GetPropertyValue("WebPublishMethod");
        if (!string.IsNullOrEmpty(msDeploy))
        {
            result.DeploymentMethods.Add($"MSDeploy ({msDeploy})");
        }

        // Check for feature flags
        var packages = project.AllEvaluatedItems.Where(i => i.ItemType == "PackageReference");
        foreach (var package in packages)
        {
            var packageId = package.EvaluatedInclude;
            if (packageId.Contains("FeatureManagement") || 
                packageId.Contains("LaunchDarkly") || 
                packageId.Contains("ConfigCat"))
            {
                result.DeploymentStrategies.Add("Feature Flags");
                break;
            }
        }

        // Check for blue-green deployment patterns
        if (result.Environments.Any(e => e.Contains("blue", StringComparison.OrdinalIgnoreCase)) ||
            result.Environments.Any(e => e.Contains("green", StringComparison.OrdinalIgnoreCase)))
        {
            result.DeploymentStrategies.Add("Blue-Green");
        }
    }

    private async Task AnalyzeEnvironmentConfigurations(string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Analyze configuration transforms
        var transformFiles = Directory.GetFiles(projectDirectory, "*.transform", SearchOption.AllDirectories);
        if (transformFiles.Any())
        {
            result.ConfigurationManagement.Add("Config Transforms");
        }

        // Check for environment variables usage
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        var envVarUsage = false;
        
        foreach (var file in sourceFiles.Take(20)) // Sample first 20 files
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (content.Contains("Environment.GetEnvironmentVariable") || 
                content.Contains("IConfiguration") && content.Contains("GetValue"))
            {
                envVarUsage = true;
                break;
            }
        }

        if (envVarUsage)
        {
            result.ConfigurationManagement.Add("Environment Variables");
        }
    }

    private async Task AnalyzeSecurityConfiguration(string projectDirectory, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for secrets management
        var secretsFiles = new[]
        {
            ".env",
            "secrets.json",
            "*.pfx",
            "*.key",
            "*.pem"
        };

        foreach (var pattern in secretsFiles)
        {
            var files = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            if (files.Any())
            {
                result.SecurityConcerns.Add($"Found potential secrets files: {pattern}");
            }
        }

        // Check for Azure Key Vault references
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDirectory, "*.json", SearchOption.AllDirectories));
        
        foreach (var file in sourceFiles.Take(20))
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (content.Contains("KeyVault") || content.Contains("SecretClient"))
            {
                result.SecretsManagement.Add("Azure Key Vault");
                break;
            }
        }

        // Check for other secrets management solutions
        if (result.CiCdPipelines.Any(p => p.FilePath.Contains("vault") || 
                                         p.Features.Any(f => f.Contains("secrets"))))
        {
            result.SecretsManagement.Add("CI/CD Secrets");
        }
    }

    private void DetermineDeploymentMaturity(DeploymentAnalysisResult result)
    {
        var maturityScore = 0;

        // CI/CD maturity
        if (result.HasCiCdPipeline) maturityScore += 20;
        if (result.CiCdPipelines.Any(p => p.HasDeploymentStages)) maturityScore += 10;
        if (result.CiCdPipelines.Any(p => p.Environments.Count > 2)) maturityScore += 10;

        // Containerization maturity
        if (result.HasContainerization) maturityScore += 15;
        if (result.HasOrchestration) maturityScore += 10;
        if (result.ContainerizationFeatures.Contains("Multi-stage Docker builds")) maturityScore += 5;

        // Infrastructure as Code
        if (result.HasInfrastructureAsCode) maturityScore += 15;

        // Deployment strategies
        if (result.DeploymentStrategies.Contains("Blue-Green")) maturityScore += 10;
        if (result.DeploymentStrategies.Contains("Canary")) maturityScore += 10;
        if (result.DeploymentStrategies.Contains("Feature Flags")) maturityScore += 5;

        // Security
        if (result.SecretsManagement.Any()) maturityScore += 10;
        if (!result.SecurityConcerns.Any(c => c.Contains("secrets files"))) maturityScore += 5;

        // Determine maturity level
        if (maturityScore >= 80)
            result.DeploymentMaturity = "Advanced";
        else if (maturityScore >= 50)
            result.DeploymentMaturity = "Intermediate";
        else if (maturityScore >= 20)
            result.DeploymentMaturity = "Basic";
        else
            result.DeploymentMaturity = "Minimal";

        result.MaturityScore = maturityScore;
    }

    private string GetJobSection(string content, string jobName)
    {
        try
        {
            // Find the job section
            var jobPattern = $@"^\s{{2}}{jobName}:\s*$";
            var jobMatch = Regex.Match(content, jobPattern, RegexOptions.Multiline);
            
            if (!jobMatch.Success)
                return string.Empty;
            
            var startIndex = jobMatch.Index;
            
            // Find the next job or end of file
            var nextJobPattern = @"^\s{2}\w+:\s*$";
            var remainingContent = content.Substring(startIndex + jobMatch.Length);
            var nextJobMatch = Regex.Match(remainingContent, nextJobPattern, RegexOptions.Multiline);
            
            var endIndex = nextJobMatch.Success 
                ? startIndex + jobMatch.Length + nextJobMatch.Index 
                : content.Length;
            
            return content.Substring(startIndex, endIndex - startIndex);
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task AnalyzeGitLabCiPipeline(string pipelinePath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Similar analysis for GitLab CI
        var pipeline = new CiCdPipeline
        {
            Name = ".gitlab-ci.yml",
            Platform = "GitLab CI",
            FilePath = pipelinePath
        };
        
        result.CiCdPipelines.Add(pipeline);
    }

    private async Task AnalyzeJenkinsPipeline(string pipelinePath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Similar analysis for Jenkins
        var pipeline = new CiCdPipeline
        {
            Name = "Jenkinsfile",
            Platform = "Jenkins",
            FilePath = pipelinePath
        };
        
        result.CiCdPipelines.Add(pipeline);
    }

    private async Task AnalyzeDockerCompose(string composePath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Analyze Docker Compose configuration
        try
        {
            var content = await File.ReadAllTextAsync(composePath, cancellationToken);
            if (content.Contains("deploy:") && content.Contains("replicas:"))
            {
                result.OrchestrationFeatures.Add("Service replicas");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze Docker Compose {File}: {Error}", composePath, ex.Message);
        }
    }

    private async Task AnalyzeKubernetesManifest(string manifestPath, DeploymentAnalysisResult result, CancellationToken cancellationToken)
    {
        // Analyze Kubernetes manifests
        try
        {
            var content = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            if (content.Contains("kind: Deployment"))
            {
                result.OrchestrationFeatures.Add("Kubernetes Deployments");
            }
            if (content.Contains("kind: Service"))
            {
                result.OrchestrationFeatures.Add("Kubernetes Services");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze Kubernetes manifest {File}: {Error}", manifestPath, ex.Message);
        }
    }

    private string ExtractEnvironmentName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var parts = fileName.Split('.');
        
        if (parts.Length > 1)
        {
            var envName = parts[^1];
            var commonEnvs = new[] { "Development", "Staging", "Production", "Test", "UAT", "QA" };
            
            if (commonEnvs.Any(e => e.Equals(envName, StringComparison.OrdinalIgnoreCase)))
            {
                return envName;
            }
        }

        return string.Empty;
    }
}

// Result models
public class DeploymentAnalysisResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    
    // CI/CD
    public bool HasCiCdPipeline { get; set; }
    public List<string> CiCdPlatforms { get; set; } = new();
    public List<CiCdPipeline> CiCdPipelines { get; set; } = new();
    
    // Containerization
    public bool HasContainerization { get; set; }
    public bool HasOrchestration { get; set; }
    public List<string> OrchestrationPlatforms { get; set; } = new();
    public List<ContainerInfo> Containers { get; set; } = new();
    public List<string> ContainerizationFeatures { get; set; } = new();
    public List<string> OrchestrationFeatures { get; set; } = new();
    
    // Infrastructure as Code
    public bool HasInfrastructureAsCode { get; set; }
    public List<string> IaCPlatforms { get; set; } = new();
    
    // Deployment configuration
    public List<string> Environments { get; set; } = new();
    public List<string> DeploymentStrategies { get; set; } = new();
    public List<string> DeploymentMethods { get; set; } = new();
    public List<DeploymentScript> DeploymentScripts { get; set; } = new();
    public List<string> ConfigurationManagement { get; set; } = new();
    
    // Security
    public List<string> SecretsManagement { get; set; } = new();
    public List<string> SecurityConcerns { get; set; } = new();
    
    // Analysis results
    public string DeploymentMaturity { get; set; } = "Unknown";
    public int MaturityScore { get; set; }
    public List<string> AnalysisErrors { get; set; } = new();
}

public class CiCdPipeline
{
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> Triggers { get; set; } = new();
    public List<string> Stages { get; set; } = new();
    public List<string> Jobs { get; set; } = new();
    public List<string> Environments { get; set; } = new();
    public bool HasDeploymentStages { get; set; }
    public List<string> Features { get; set; } = new();
}

public class ContainerInfo
{
    public string Type { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string BaseImage { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public List<string> SecurityFeatures { get; set; } = new();
}

public class DeploymentScript
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}