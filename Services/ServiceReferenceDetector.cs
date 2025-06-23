using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class ServiceReferenceDetector
{
    private readonly ILogger<ServiceReferenceDetector> _logger;

    public ServiceReferenceDetector(ILogger<ServiceReferenceDetector> logger)
    {
        _logger = logger;
    }

    public ServiceReferenceInfo DetectServiceReferences(Project project)
    {
        var result = new ServiceReferenceInfo();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";

        // Check for Service References folder
        var serviceReferencesPath = Path.Combine(projectDir, "Service References");
        var connectedServicesPath = Path.Combine(projectDir, "Connected Services");

        if (Directory.Exists(serviceReferencesPath))
        {
            result.HasServiceReferences = true;
            result.ServiceReferencePath = serviceReferencesPath;

            // Find .svcmap files
            var svcmapFiles = Directory.GetFiles(serviceReferencesPath, "*.svcmap", SearchOption.AllDirectories);
            foreach (var svcmap in svcmapFiles)
            {
                var serviceName = Path.GetFileNameWithoutExtension(svcmap);
                result.ServiceReferenceNames.Add(serviceName);

                // Try to find the WSDL URL
                try
                {
                    var content = File.ReadAllText(svcmap);
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(content,
                        @"<MetadataSource.*?Address=""([^""]+)""",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (urlMatch.Success)
                    {
                        result.ServiceEndpoints[serviceName] = urlMatch.Groups[1].Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading svcmap file: {File}", svcmap);
                }
            }
        }

        if (Directory.Exists(connectedServicesPath))
        {
            result.HasConnectedServices = true;
            result.ConnectedServicesPath = connectedServicesPath;
        }

        // Check for WCF client configuration in app.config
        var appConfigPath = Path.Combine(projectDir, "app.config");
        if (!File.Exists(appConfigPath))
        {
            appConfigPath = Path.Combine(projectDir, "App.config");
        }

        if (File.Exists(appConfigPath))
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(appConfigPath);
                var clientEndpoints = doc.Descendants("endpoint")
                    .Where(e => e.Parent?.Name.LocalName == "client")
                    .Select(e => new
                    {
                        Name = e.Attribute("name")?.Value,
                        Address = e.Attribute("address")?.Value,
                        Contract = e.Attribute("contract")?.Value
                    })
                    .Where(e => e.Name != null)
                    .ToList();

                foreach (var endpoint in clientEndpoints)
                {
                    result.ConfiguredEndpoints.Add(new WcfEndpoint
                    {
                        Name = endpoint.Name!,
                        Address = endpoint.Address ?? "",
                        Contract = endpoint.Contract ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error parsing app.config for WCF endpoints");
            }
        }

        // Check for generated Reference.cs files
        var referenceFiles = Directory.GetFiles(projectDir, "Reference.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains("Service References", StringComparison.OrdinalIgnoreCase))
            .ToList();
        result.GeneratedFiles.AddRange(referenceFiles);

        return result;
    }

    public void AddServiceReferenceWarnings(ServiceReferenceInfo info, MigrationResult result)
    {
        if (!info.HasServiceReferences && !info.HasConnectedServices) return;

        var warning = new StringBuilder();
        warning.AppendLine("⚠️ Service References Detected");
        warning.AppendLine();
        warning.AppendLine("Legacy WCF Service References are not compatible with SDK-style projects.");
        warning.AppendLine();

        if (info.ServiceReferenceNames.Any())
        {
            warning.AppendLine("Found service references:");
            foreach (var name in info.ServiceReferenceNames)
            {
                warning.AppendLine($"  - {name}");
                if (info.ServiceEndpoints.TryGetValue(name, out var endpoint))
                {
                    warning.AppendLine($"    Endpoint: {endpoint}");
                }
            }
            warning.AppendLine();
        }

        warning.AppendLine("REQUIRED ACTIONS:");
        warning.AppendLine("1. Install dotnet-svcutil tool:");
        warning.AppendLine("   dotnet tool install --global dotnet-svcutil");
        warning.AppendLine();
        warning.AppendLine("2. Regenerate service proxies for each service:");

        foreach (var name in info.ServiceReferenceNames)
        {
            if (info.ServiceEndpoints.TryGetValue(name, out var endpoint))
            {
                warning.AppendLine($"   dotnet-svcutil {endpoint} --outputDir Services/{name}");
            }
            else
            {
                warning.AppendLine($"   dotnet-svcutil [WSDL_URL] --outputDir Services/{name}");
            }
        }

        warning.AppendLine();
        warning.AppendLine("3. Update your code to use the new generated proxies");
        warning.AppendLine();
        warning.AppendLine("4. Add required NuGet packages:");
        warning.AppendLine("   - System.ServiceModel.Duplex");
        warning.AppendLine("   - System.ServiceModel.Http");
        warning.AppendLine("   - System.ServiceModel.NetTcp");
        warning.AppendLine("   - System.ServiceModel.Security");
        warning.AppendLine();
        warning.AppendLine("Alternative: Consider migrating to REST/HTTP clients if the service supports it.");

        result.Warnings.Add(warning.ToString());

        // Mark files for removal
        foreach (var file in info.GeneratedFiles)
        {
            result.RemovedElements.Add($"Service Reference file: {Path.GetFileName(file)}");
        }

        if (info.HasServiceReferences)
        {
            result.RemovedElements.Add($"Service References folder: {info.ServiceReferencePath}");
        }
    }
}

public class ServiceReferenceInfo
{
    public bool HasServiceReferences { get; set; }
    public bool HasConnectedServices { get; set; }
    public string? ServiceReferencePath { get; set; }
    public string? ConnectedServicesPath { get; set; }
    public List<string> ServiceReferenceNames { get; set; } = new();
    public Dictionary<string, string> ServiceEndpoints { get; set; } = new();
    public List<WcfEndpoint> ConfiguredEndpoints { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public class WcfEndpoint
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Contract { get; set; } = string.Empty;
}