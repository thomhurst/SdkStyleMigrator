using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class DirectoryBuildPropsGenerator : IDirectoryBuildPropsGenerator
{
    private readonly ILogger<DirectoryBuildPropsGenerator> _logger;

    public DirectoryBuildPropsGenerator(ILogger<DirectoryBuildPropsGenerator> logger)
    {
        _logger = logger;
    }

    public Task GenerateDirectoryBuildPropsAsync(string rootDirectory, Dictionary<string, AssemblyProperties> projectProperties, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(rootDirectory, "Directory.Build.props");
        
        var commonProperties = ExtractCommonProperties(projectProperties);
        
        if (!commonProperties.HasProperties())
        {
            _logger.LogInformation("No common assembly properties found, skipping Directory.Build.props generation");
            return Task.CompletedTask;
        }

        XDocument doc;
        XElement projectElement;

        if (File.Exists(filePath))
        {
            _logger.LogInformation("Updating existing Directory.Build.props at {Path}", filePath);
            doc = XDocument.Load(filePath);
            projectElement = doc.Root!;
        }
        else
        {
            _logger.LogInformation("Creating new Directory.Build.props at {Path}", filePath);
            doc = new XDocument();
            projectElement = new XElement("Project");
            doc.Add(projectElement);
        }

        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault();
        if (propertyGroup == null)
        {
            propertyGroup = new XElement("PropertyGroup");
            projectElement.AddFirst(propertyGroup);
        }

        AddOrUpdateProperty(propertyGroup, "GenerateAssemblyInfo", "true");
        
        if (!string.IsNullOrEmpty(commonProperties.Company))
            AddOrUpdateProperty(propertyGroup, "Company", commonProperties.Company);
        
        if (!string.IsNullOrEmpty(commonProperties.Product))
            AddOrUpdateProperty(propertyGroup, "Product", commonProperties.Product);
        
        if (!string.IsNullOrEmpty(commonProperties.Copyright))
            AddOrUpdateProperty(propertyGroup, "Copyright", commonProperties.Copyright);
        
        if (!string.IsNullOrEmpty(commonProperties.Trademark))
            AddOrUpdateProperty(propertyGroup, "Trademark", commonProperties.Trademark);
        
        if (!string.IsNullOrEmpty(commonProperties.AssemblyVersion))
            AddOrUpdateProperty(propertyGroup, "AssemblyVersion", commonProperties.AssemblyVersion);
        
        if (!string.IsNullOrEmpty(commonProperties.FileVersion))
            AddOrUpdateProperty(propertyGroup, "FileVersion", commonProperties.FileVersion);
        
        if (!string.IsNullOrEmpty(commonProperties.NeutralResourcesLanguage))
            AddOrUpdateProperty(propertyGroup, "NeutralResourcesLanguage", commonProperties.NeutralResourcesLanguage);
        
        if (commonProperties.ComVisible.HasValue)
            AddOrUpdateProperty(propertyGroup, "ComVisible", commonProperties.ComVisible.Value.ToString().ToLower());

        doc.Save(filePath);
        
        _logger.LogInformation("Successfully created/updated Directory.Build.props with common assembly properties");
        return Task.CompletedTask;
    }

    private AssemblyProperties ExtractCommonProperties(Dictionary<string, AssemblyProperties> projectProperties)
    {
        if (!projectProperties.Any())
            return new AssemblyProperties();

        var common = new AssemblyProperties();
        var allProps = projectProperties.Values.ToList();

        var firstProps = allProps.First();
        
        if (allProps.All(p => p.Company == firstProps.Company))
            common.Company = firstProps.Company;
        
        if (allProps.All(p => p.Product == firstProps.Product))
            common.Product = firstProps.Product;
        
        if (allProps.All(p => p.Copyright == firstProps.Copyright))
            common.Copyright = firstProps.Copyright;
        
        if (allProps.All(p => p.Trademark == firstProps.Trademark))
            common.Trademark = firstProps.Trademark;
        
        if (allProps.All(p => p.AssemblyVersion == firstProps.AssemblyVersion))
            common.AssemblyVersion = firstProps.AssemblyVersion;
        
        if (allProps.All(p => p.FileVersion == firstProps.FileVersion))
            common.FileVersion = firstProps.FileVersion;
        
        if (allProps.All(p => p.NeutralResourcesLanguage == firstProps.NeutralResourcesLanguage))
            common.NeutralResourcesLanguage = firstProps.NeutralResourcesLanguage;
        
        if (allProps.All(p => p.ComVisible == firstProps.ComVisible))
            common.ComVisible = firstProps.ComVisible;

        return common;
    }

    private void AddOrUpdateProperty(XElement propertyGroup, string name, string value)
    {
        var existing = propertyGroup.Elements(name).FirstOrDefault();
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            propertyGroup.Add(new XElement(name, value));
        }
    }
}