using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class GrpcServiceHandler : IGrpcServiceHandler
{
    private readonly ILogger<GrpcServiceHandler> _logger;
    private bool _generateModernProgramCs = false;

    public GrpcServiceHandler(ILogger<GrpcServiceHandler> logger)
    {
        _logger = logger;
    }

    public async Task<GrpcProjectInfo> DetectGrpcConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new GrpcProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive package analysis for gRPC patterns
        var packageReferences = project.AllEvaluatedItems
            .Where(item => item.ItemType == "PackageReference")
            .ToList();

        await AnalyzeGrpcPackages(info, packageReferences, cancellationToken);

        // Analyze project structure and hosting configuration
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Comprehensive .proto file detection and analysis
        await DetectProtoFiles(info, cancellationToken);

        // Analyze .proto file contents for services and messages
        await AnalyzeProtoFileContents(info, cancellationToken);

        // Check for existing Protobuf items in project
        DetectExistingProtoReferences(project, info);

        // Detect gRPC service implementations
        await DetectGrpcServiceImplementations(info, cancellationToken);

        // Analyze gRPC client usage patterns
        await AnalyzeGrpcClientPatterns(info, cancellationToken);

        // Detect authentication and authorization patterns
        await DetectAuthenticationPatterns(info, cancellationToken);

        // Check for performance optimizations
        await AnalyzePerformanceOptimizations(info, project, cancellationToken);

        // Detect deployment and containerization patterns
        await DetectDeploymentPatterns(info, cancellationToken);

        _logger.LogInformation("Detected gRPC project: Type={ServiceType}, ProtoFiles={Count}, Services={ServiceCount}, HasReflection={HasReflection}, HasGrpcWeb={HasGrpcWeb}, HasAuth={HasAuth}",
            GetGrpcProjectType(info), info.ProtoFiles.Count, GetServiceCount(info), info.HasReflection, info.HasGrpcWeb, HasAuthentication(info));

        return info;
    }

    public async Task MigrateGrpcProjectAsync(
        GrpcProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine optimal migration path based on current state
            if (IsLegacyGrpcProject(info))
            {
                // Migrate legacy gRPC to modern .NET 8+ patterns
                await MigrateLegacyGrpcProject(info, projectElement, packageReferences, result, cancellationToken);
            }
            else if (IsModernGrpcProject(info))
            {
                // Modernize existing .NET 8+ gRPC project
                await ModernizeGrpcProject(info, projectElement, packageReferences, result, cancellationToken);
            }
            else
            {
                // Configure new gRPC project with best practices
                await ConfigureNewGrpcProject(info, projectElement, packageReferences, result, cancellationToken);
            }

            // Apply common gRPC optimizations and best practices
            await ApplyGrpcOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully migrated gRPC project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate gRPC project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "gRPC migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void ConfigureProtoFiles(string projectDirectory, XElement projectElement, GrpcProjectInfo info)
    {
        if (!info.ProtoFiles.Any())
            return;

        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        foreach (var protoFile in info.ProtoFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, protoFile);
            
            // Configure Protobuf compilation
            var protobufElement = new XElement("Protobuf", 
                new XAttribute("Include", relativePath));

            // Determine if this is a server or client proto
            var protoContent = File.ReadAllText(protoFile);
            if (protoContent.Contains("service "))
            {
                // This proto defines services - configure for server generation
                protobufElement.Add(new XElement("GrpcServices", "Server"));
            }
            else
            {
                // This is likely a message-only proto or client proto 
                protobufElement.Add(new XElement("GrpcServices", "Client"));
            }

            // Check if this item already exists
            var existingProtobuf = itemGroup.Elements("Protobuf")
                .FirstOrDefault(e => e.Attribute("Include")?.Value == relativePath);
            
            if (existingProtobuf == null)
            {
                itemGroup.Add(protobufElement);
            }
            else
            {
                // Update existing element
                existingProtobuf.ReplaceWith(protobufElement);
            }
        }

        _logger.LogInformation("Configured {Count} .proto files for compilation", info.ProtoFiles.Count);
    }

    public void ConfigureGrpcProperties(XElement projectElement, GrpcProjectInfo info)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ??
                           new XElement("PropertyGroup");

        // Set target framework
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");

        // Enable nullable reference types
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");

        // gRPC-specific properties
        if (info.HasGrpcWeb)
        {
            SetOrUpdateProperty(propertyGroup, "EnableGrpcWeb", "true");
        }

        if (info.HasReflection)
        {
            SetOrUpdateProperty(propertyGroup, "EnableGrpcReflection", "true");
        }

        // Protobuf compiler options
        SetOrUpdateProperty(propertyGroup, "ProtobufMessageNameFormat", "PascalCase");
        SetOrUpdateProperty(propertyGroup, "ProtobufGenerateExtensions", "true");
    }

    private async Task DetectProtoFiles(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var protoFiles = Directory.GetFiles(info.ProjectDirectory, "*.proto", SearchOption.AllDirectories);
            info.ProtoFiles = protoFiles.ToList();

            // Also check common proto locations
            var commonPaths = new[]
            {
                Path.Combine(info.ProjectDirectory, "Protos"),
                Path.Combine(info.ProjectDirectory, "Proto"),
                Path.Combine(info.ProjectDirectory, "Schemas")
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path))
                {
                    var additionalProtos = Directory.GetFiles(path, "*.proto", SearchOption.AllDirectories);
                    info.ProtoFiles.AddRange(additionalProtos.Where(p => !info.ProtoFiles.Contains(p)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect .proto files: {Error}", ex.Message);
        }
    }

    private void DetectExistingProtoReferences(Project project, GrpcProjectInfo info)
    {
        var protobufItems = project.AllEvaluatedItems
            .Where(item => item.ItemType == "Protobuf");

        foreach (var item in protobufItems)
        {
            var include = item.EvaluatedInclude;
            var grpcServices = item.GetMetadataValue("GrpcServices");
            
            if (!string.IsNullOrEmpty(grpcServices))
            {
                info.ProtoReferences[include] = grpcServices;
            }
        }
    }

    private async Task EnsureGrpcPackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // Core gRPC packages
        await EnsureCoreGrpcPackages(packageReferences, info, cancellationToken);

        // Feature-specific packages
        await EnsureFeatureSpecificPackages(packageReferences, info, cancellationToken);

        // Authentication and security packages
        await EnsureSecurityPackages(packageReferences, info, cancellationToken);

        // Performance and monitoring packages
        await EnsurePerformancePackages(packageReferences, info, cancellationToken);

        // Development and tooling packages
        await EnsureDevelopmentPackages(packageReferences, info, cancellationToken);
    }

    private async Task EnsureCoreGrpcPackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // Grpc.AspNetCore (main package for ASP.NET Core gRPC)
        if (!packageReferences.Any(p => p.PackageId == "Grpc.AspNetCore"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Grpc.AspNetCore",
                Version = "2.66.0" // Latest stable version
            });
        }

        // Grpc.Tools for .proto compilation
        if (!packageReferences.Any(p => p.PackageId == "Grpc.Tools"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Grpc.Tools",
                Version = "2.66.0",
                Metadata = { ["PrivateAssets"] = "All" }
            });
        }

        // Google.Protobuf for message handling
        if (!packageReferences.Any(p => p.PackageId == "Google.Protobuf"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Google.Protobuf",
                Version = "3.25.3" // Latest stable version
            });
        }
    }

    private async Task EnsureFeatureSpecificPackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // gRPC Web support
        if (info.HasGrpcWeb && !packageReferences.Any(p => p.PackageId == "Grpc.AspNetCore.Web"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Grpc.AspNetCore.Web",
                Version = "2.66.0"
            });
        }

        // gRPC Reflection
        if (info.HasReflection && !packageReferences.Any(p => p.PackageId == "Grpc.AspNetCore.Server.Reflection"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Grpc.AspNetCore.Server.Reflection",
                Version = "2.66.0"
            });
        }

        // Health checks for gRPC services
        if (info.ProtoReferences.ContainsKey("grpc.health.v1") && !packageReferences.Any(p => p.PackageId == "Grpc.HealthCheck"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Grpc.HealthCheck",
                Version = "2.66.0"
            });
        }
    }

    private async Task EnsureSecurityPackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // Authentication packages if auth patterns detected
        if (info.ProtoReferences.ContainsKey("Authentication") || 
            info.ProtoReferences.ContainsKey("Authorization"))
        {
            if (!packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Authentication.JwtBearer"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "Microsoft.AspNetCore.Authentication.JwtBearer",
                    Version = "8.0.8"
                });
            }
        }
    }

    private async Task EnsurePerformancePackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // Performance monitoring
        if (info.ProtoReferences.ContainsKey("Monitoring") || 
            info.ProtoReferences.ContainsKey("Metrics"))
        {
            if (!packageReferences.Any(p => p.PackageId == "OpenTelemetry.Extensions.Hosting"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "OpenTelemetry.Extensions.Hosting",
                    Version = "1.9.0"
                });
            }

            if (!packageReferences.Any(p => p.PackageId == "OpenTelemetry.Instrumentation.GrpcNetClient"))
            {
                packageReferences.Add(new PackageReference
                {
                    PackageId = "OpenTelemetry.Instrumentation.GrpcNetClient",
                    Version = "1.9.0-beta.1"
                });
            }
        }
    }

    private async Task EnsureDevelopmentPackages(List<PackageReference> packageReferences, GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // gRPC UI for development (if detected development patterns)
        if (info.ProtoReferences.ContainsKey("Development") && 
            !packageReferences.Any(p => p.PackageId == "Microsoft.AspNetCore.Grpc.Swagger"))
        {
            packageReferences.Add(new PackageReference
            {
                PackageId = "Microsoft.AspNetCore.Grpc.Swagger",
                Version = "0.3.7"
            });
        }
    }

    // New comprehensive analysis methods
    private async Task AnalyzeGrpcPackages(GrpcProjectInfo info, List<Microsoft.Build.Evaluation.ProjectItem> packageReferences, CancellationToken cancellationToken)
    {
        foreach (var package in packageReferences)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");

            switch (packageId)
            {
                case "Grpc.AspNetCore":
                    info.ProtoReferences["CoreGrpc"] = version ?? "";
                    break;

                case "Grpc.AspNetCore.Web":
                    info.HasGrpcWeb = true;
                    break;

                case "Grpc.AspNetCore.Server.Reflection":
                case "Grpc.Reflection":
                    info.HasReflection = true;
                    break;

                case "Grpc.HealthCheck":
                    info.ProtoReferences["grpc.health.v1"] = "HealthCheck";
                    break;

                case "Microsoft.AspNetCore.Authentication.JwtBearer":
                case "Microsoft.AspNetCore.Authorization":
                    info.ProtoReferences["Authentication"] = "JWT";
                    break;

                case "OpenTelemetry.Extensions.Hosting":
                case "OpenTelemetry.Instrumentation.GrpcNetClient":
                    info.ProtoReferences["Monitoring"] = "OpenTelemetry";
                    break;

                case "Microsoft.AspNetCore.Grpc.Swagger":
                    info.ProtoReferences["Development"] = "Swagger";
                    break;

                // Legacy packages that need migration
                case "Grpc.Core":
                case "Grpc.Core.Api":
                    info.ProtoReferences["Legacy"] = packageId;
                    break;
            }
        }
    }

    private async Task AnalyzeProjectStructure(GrpcProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check target framework
        var targetFramework = project.GetPropertyValue("TargetFramework");
        if (!string.IsNullOrEmpty(targetFramework))
        {
            info.TargetFramework = targetFramework;
            info.Properties["IsNet8Plus"] = (targetFramework.StartsWith("net8.0") || targetFramework.StartsWith("net9.0")).ToString();
        }

        // Check for containerization support
        var enableSdkContainerSupport = project.GetPropertyValue("EnableSdkContainerSupport");
        if (string.Equals(enableSdkContainerSupport, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.Properties["SupportsContainers"] = "true";
        }

        // Check for gRPC-specific properties
        var enableGrpcReflection = project.GetPropertyValue("EnableGrpcReflection");
        if (string.Equals(enableGrpcReflection, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.HasReflection = true;
        }

        var enableGrpcWeb = project.GetPropertyValue("EnableGrpcWeb");
        if (string.Equals(enableGrpcWeb, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.HasGrpcWeb = true;
        }
    }

    private async Task AnalyzeProtoFileContents(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        var serviceCount = 0;
        var messageCount = 0;

        foreach (var protoFile in info.ProtoFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(protoFile, cancellationToken);
                
                // Count services
                var serviceMatches = Regex.Matches(content, @"service\s+(\w+)", RegexOptions.IgnoreCase);
                serviceCount += serviceMatches.Count;
                
                // Count messages
                var messageMatches = Regex.Matches(content, @"message\s+(\w+)", RegexOptions.IgnoreCase);
                messageCount += messageMatches.Count;
                
                // Detect specific patterns
                if (content.Contains("google.api.http") || content.Contains("option (google.api.http)"))
                {
                    info.ProtoReferences["RestApi"] = "HTTP/JSON transcoding";
                }
                
                if (content.Contains("stream "))
                {
                    info.ProtoReferences["Streaming"] = "Bidirectional streaming";
                }
                
                if (content.Contains("google.protobuf.Any") || content.Contains("google.protobuf.Struct"))
                {
                    info.ProtoReferences["DynamicTypes"] = "Well-known types";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze proto file {File}: {Error}", protoFile, ex.Message);
            }
        }

        info.Properties["ServiceCount"] = serviceCount.ToString();
        info.Properties["MessageCount"] = messageCount.ToString();
    }

    private async Task DetectGrpcServiceImplementations(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, sourceFile);
                
                // Detect gRPC service implementations
                if (content.Contains(": ServiceBase") || content.Contains(": I") && content.Contains("GrpcService"))
                {
                    var serviceName = ExtractServiceName(content);
                    if (!string.IsNullOrEmpty(serviceName))
                    {
                        info.ProtoReferences[$"Implementation_{serviceName}"] = relativePath;
                    }
                }
                
                // Detect interceptors
                if (content.Contains(": Interceptor") || content.Contains("ServerCallContext"))
                {
                    info.ProtoReferences["Interceptors"] = "Custom interceptors";
                }
                
                // Detect middleware patterns
                if (content.Contains("app.UseGrpcWeb") || content.Contains("app.MapGrpcService"))
                {
                    info.ProtoReferences["Middleware"] = "gRPC middleware";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze source file {File}: {Error}", sourceFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeGrpcClientPatterns(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                
                // Detect client usage patterns
                if (content.Contains("GrpcChannel") || content.Contains(".Client"))
                {
                    info.ProtoReferences["ClientUsage"] = "gRPC client";
                }
                
                // Detect retry policies
                if (content.Contains("RetryPolicy") || content.Contains("Polly"))
                {
                    info.ProtoReferences["RetryPolicy"] = "Retry handling";
                }
                
                // Detect load balancing
                if (content.Contains("LoadBalancing") || content.Contains("RoundRobin"))
                {
                    info.ProtoReferences["LoadBalancing"] = "Client-side load balancing";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze client patterns in {File}: {Error}", sourceFile, ex.Message);
            }
        }
    }

    private async Task DetectAuthenticationPatterns(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                
                // Detect authentication patterns
                if (content.Contains("[Authorize]") || content.Contains("AuthorizeAttribute"))
                {
                    info.ProtoReferences["Authorization"] = "ASP.NET Core Authorization";
                }
                
                if (content.Contains("CallCredentials") || content.Contains("Metadata"))
                {
                    info.ProtoReferences["CallCredentials"] = "gRPC call credentials";
                }
                
                if (content.Contains("ServerCallContext") && content.Contains("User"))
                {
                    info.ProtoReferences["ContextAuth"] = "Context-based auth";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze auth patterns in {File}: {Error}", sourceFile, ex.Message);
            }
        }
    }

    private async Task AnalyzePerformanceOptimizations(GrpcProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Check for HTTP/2 configuration
        var sourceFiles = Directory.GetFiles(info.ProjectDirectory, "*.cs", SearchOption.AllDirectories);
        
        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                
                if (content.Contains("Http2") || content.Contains("HttpVersion.Version20"))
                {
                    info.ProtoReferences["Http2"] = "HTTP/2 optimization";
                }
                
                if (content.Contains("Compression") || content.Contains("Gzip"))
                {
                    info.ProtoReferences["Compression"] = "Response compression";
                }
                
                if (content.Contains("MessagePack") || content.Contains("ProtoBuf"))
                {
                    info.ProtoReferences["Serialization"] = "Custom serialization";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze performance patterns in {File}: {Error}", sourceFile, ex.Message);
            }
        }

        // Check project-level optimizations
        var serverGcLlvm = project.GetPropertyValue("ServerGarbageCollection");
        if (string.Equals(serverGcLlvm, "true", StringComparison.OrdinalIgnoreCase))
        {
            info.ProtoReferences["ServerGC"] = "Server garbage collection";
        }
    }

    private async Task DetectDeploymentPatterns(GrpcProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for Dockerfile
        var dockerfilePath = Path.Combine(info.ProjectDirectory, "Dockerfile");
        if (File.Exists(dockerfilePath))
        {
            info.Properties["HasDockerfile"] = "true";
            
            var content = await File.ReadAllTextAsync(dockerfilePath, cancellationToken);
            if (content.Contains("EXPOSE 80") || content.Contains("EXPOSE 443"))
            {
                info.ProtoReferences["HttpExposure"] = "HTTP endpoints";
            }
        }

        // Check for Kubernetes manifests
        var kubernetesFiles = Directory.GetFiles(info.ProjectDirectory, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "*.yml", SearchOption.AllDirectories))
            .Where(f => File.ReadAllText(f).Contains("apiVersion:"))
            .ToList();
        
        if (kubernetesFiles.Any())
        {
            info.Properties["HasKubernetesManifests"] = "true";
        }
    }

    private string ExtractServiceName(string content)
    {
        try
        {
            // Extract class name that inherits from ServiceBase
            var match = Regex.Match(content, @"class\s+(\w+)\s*:\s*\w+\.ServiceBase", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            
            // Alternative pattern for interface implementations
            match = Regex.Match(content, @"class\s+(\w+)\s*:\s*I\w+", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to extract service name: {Error}", ex.Message);
        }
        
        return string.Empty;
    }

    // Migration methods
    private async Task MigrateLegacyGrpcProject(GrpcProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating legacy gRPC project to modern .NET 8+ patterns");

        // Set modern SDK
        projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Web");

        // Remove legacy packages
        await RemoveLegacyGrpcPackages(packageReferences, result, cancellationToken);

        // Update to modern packages
        await EnsureGrpcPackages(packageReferences, info, cancellationToken);

        // Configure modern gRPC properties
        await ConfigureModernGrpcProperties(info, projectElement, result, cancellationToken);

        // Create modern Program.cs if needed
        await CreateModernProgramCs(info, result, cancellationToken);

        result.Warnings.Add("Legacy gRPC project migrated to .NET 8+ with modern patterns. Review generated configuration.");
        result.Warnings.Add("Verify .proto file compilation and service registration in Program.cs.");
    }

    private async Task ModernizeGrpcProject(GrpcProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing .NET 8+ gRPC project with latest best practices");

        // Update packages to latest versions
        await UpdateGrpcPackagesToLatest(packageReferences, info, result, cancellationToken);

        // Apply modern performance optimizations
        await ApplyModernPerformanceOptimizations(info, projectElement, result, cancellationToken);

        result.Warnings.Add("gRPC project modernized with latest .NET 8+ features and optimizations.");
    }

    private async Task ConfigureNewGrpcProject(GrpcProjectInfo info, XElement projectElement, List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring new gRPC project with modern best practices");

        // Set modern SDK
        projectElement.SetAttributeValue("Sdk", "Microsoft.NET.Sdk.Web");

        // Configure modern properties
        await ConfigureModernGrpcProperties(info, projectElement, result, cancellationToken);

        // Add essential packages
        await EnsureGrpcPackages(packageReferences, info, cancellationToken);

        result.Warnings.Add("New gRPC project configured with modern best practices and optimizations.");
    }

    private async Task ApplyGrpcOptimizations(GrpcProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Performance optimizations
        SetOrUpdateProperty(propertyGroup, "ServerGarbageCollection", "true");
        SetOrUpdateProperty(propertyGroup, "PublishTrimmed", "true");
        SetOrUpdateProperty(propertyGroup, "TrimMode", "partial");
        
        // gRPC-specific optimizations
        SetOrUpdateProperty(propertyGroup, "GrpcResponseCompressionLevel", "Optimal");
        
        // Container optimizations if applicable
        if (info.Properties.ContainsKey("HasDockerfile") && info.Properties["HasDockerfile"] == "true")
        {
            SetOrUpdateProperty(propertyGroup, "EnableSdkContainerSupport", "true");
            SetOrUpdateProperty(propertyGroup, "ContainerImageName", Path.GetFileNameWithoutExtension(info.ProjectPath).ToLowerInvariant());
            SetOrUpdateProperty(propertyGroup, "ContainerPort", "8080");
        }
        
        result.Warnings.Add("Applied gRPC performance optimizations for production deployment.");
    }

    private async Task RemoveLegacyGrpcPackages(List<PackageReference> packageReferences, MigrationResult result, CancellationToken cancellationToken)
    {
        var legacyPackages = new[]
        {
            "Grpc.Core",
            "Grpc.Core.Api",
            "Google.Protobuf.Tools"
        };

        var removedCount = 0;
        foreach (var legacyPackage in legacyPackages)
        {
            var removed = packageReferences.RemoveAll(p => p.PackageId == legacyPackage);
            removedCount += removed;
        }
        
        if (removedCount > 0)
        {
            result.Warnings.Add($"Removed {removedCount} legacy gRPC packages. Replaced with modern equivalents.");
        }
    }

    private async Task ConfigureModernGrpcProperties(GrpcProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Essential modern properties
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        SetOrUpdateProperty(propertyGroup, "ImplicitUsings", "enable");
        SetOrUpdateProperty(propertyGroup, "Nullable", "enable");
        
        // gRPC-specific properties
        if (info.HasGrpcWeb)
        {
            SetOrUpdateProperty(propertyGroup, "EnableGrpcWeb", "true");
        }
        
        if (info.HasReflection)
        {
            SetOrUpdateProperty(propertyGroup, "EnableGrpcReflection", "true");
        }
        
        // Protobuf compiler optimizations
        SetOrUpdateProperty(propertyGroup, "ProtobufMessageNameFormat", "PascalCase");
        SetOrUpdateProperty(propertyGroup, "ProtobufGenerateExtensions", "true");
        SetOrUpdateProperty(propertyGroup, "ProtobufGenerateJsonOptions", "true");
    }

    private async Task CreateModernProgramCs(GrpcProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        if (!_generateModernProgramCs)
        {
            _logger.LogInformation("Skipping Program.cs generation as GenerateModernProgramCs is disabled");
            return;
        }
        
        var programCsPath = Path.Combine(info.ProjectDirectory, "Program.cs");
        
        if (!File.Exists(programCsPath))
        {
            var programContent = GenerateModernProgramCs(info);
            await File.WriteAllTextAsync(programCsPath, programContent, cancellationToken);
            result.Warnings.Add("Created modern Program.cs with gRPC configuration. Review and customize as needed.");
        }
    }

    private string GenerateModernProgramCs(GrpcProjectInfo info)
    {
        var content = @"using Microsoft.AspNetCore.Server.Kestrel.Core;
";
        
        if (info.HasGrpcWeb)
            content += "using Microsoft.AspNetCore.Grpc.Web;\n";
        if (info.HasReflection)
            content += "using Microsoft.Extensions.DependencyInjection;\n";

        content += @"
var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for HTTP/2
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http2);
});

// Add gRPC services
builder.Services.AddGrpc();
";

        if (info.HasGrpcWeb)
            content += "builder.Services.AddGrpcWeb();\n";
        
        if (info.HasReflection)
            content += "builder.Services.AddGrpcReflection();\n";

        content += @"
var app = builder.Build();

// Configure gRPC pipeline
";

        if (info.HasGrpcWeb)
            content += "app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });\n";

        // Add service mappings based on detected services
        var serviceCount = int.Parse(info.Properties.GetValueOrDefault("ServiceCount", "0"));
        if (serviceCount > 0)
        {
            content += "\n// Map gRPC services\n";
            content += "// TODO: Replace with your actual service implementations\n";
            content += "// app.MapGrpcService<YourGrpcService>();\n";
        }

        if (info.HasReflection)
            content += "\napp.MapGrpcReflectionService();\n";

        content += "\napp.Run();\n";

        return content;
    }

    private async Task UpdateGrpcPackagesToLatest(List<PackageReference> packageReferences, GrpcProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        var grpcPackages = packageReferences.Where(p => p.PackageId.StartsWith("Grpc.") || p.PackageId.StartsWith("Google.Protobuf")).ToList();
        
        foreach (var package in grpcPackages)
        {
            if (package.PackageId.StartsWith("Grpc."))
                package.Version = "2.66.0";
            else if (package.PackageId == "Google.Protobuf")
                package.Version = "3.25.3";
        }
    }

    private async Task ApplyModernPerformanceOptimizations(GrpcProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Advanced performance settings
        SetOrUpdateProperty(propertyGroup, "GrpcMaxMessageSize", "4194304"); // 4MB
        SetOrUpdateProperty(propertyGroup, "GrpcKeepAliveTime", "30");
        SetOrUpdateProperty(propertyGroup, "GrpcKeepAliveTimeout", "5");
        
        result.Warnings.Add("Applied advanced gRPC performance optimizations for high-throughput scenarios.");
    }

    // Helper methods
    private bool IsLegacyGrpcProject(GrpcProjectInfo info)
    {
        return info.ProtoReferences.ContainsKey("Legacy") || 
               (!info.Properties.ContainsKey("IsNet8Plus") || info.Properties["IsNet8Plus"] != "true");
    }

    private bool IsModernGrpcProject(GrpcProjectInfo info)
    {
        return info.Properties.ContainsKey("IsNet8Plus") && info.Properties["IsNet8Plus"] == "true" &&
               !info.ProtoReferences.ContainsKey("Legacy");
    }

    private string GetGrpcProjectType(GrpcProjectInfo info)
    {
        if (info.HasGrpcWeb && info.HasReflection) return "Full-Featured";
        if (info.HasGrpcWeb) return "gRPC-Web";
        if (info.HasReflection) return "Reflective";
        if (info.ProtoReferences.ContainsKey("Streaming")) return "Streaming";
        return "Standard";
    }

    private int GetServiceCount(GrpcProjectInfo info)
    {
        return int.Parse(info.Properties.GetValueOrDefault("ServiceCount", "0"));
    }

    private bool HasAuthentication(GrpcProjectInfo info)
    {
        return info.ProtoReferences.ContainsKey("Authentication") ||
               info.ProtoReferences.ContainsKey("Authorization") ||
               info.ProtoReferences.ContainsKey("CallCredentials");
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
    
    public void SetGenerateModernProgramCs(bool enabled)
    {
        _generateModernProgramCs = enabled;
        _logger.LogInformation("GenerateModernProgramCs set to: {Enabled}", enabled);
    }
}