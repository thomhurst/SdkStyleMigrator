using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class DirectoryBuildPropsGenerator : IDirectoryBuildPropsGenerator
{
    private readonly ILogger<DirectoryBuildPropsGenerator> _logger;
    private readonly MigrationOptions _options;
    private readonly IAuditService _auditService;

    public DirectoryBuildPropsGenerator(ILogger<DirectoryBuildPropsGenerator> logger, IAuditService auditService, MigrationOptions options)
    {
        _logger = logger;
        _auditService = auditService;
        _options = options;
    }

    public async Task GenerateDirectoryBuildPropsAsync(string rootDirectory, Dictionary<string, AssemblyProperties> projectProperties, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(rootDirectory, "Directory.Build.props");

        var commonProperties = ExtractCommonProperties(projectProperties);

        // Always create Directory.Build.props for binding redirects and other settings
        // even if there are no common assembly properties

        XDocument doc;
        XElement projectElement;

        if (File.Exists(filePath) && !_options.DryRun)
        {
            _logger.LogInformation("Updating existing Directory.Build.props at {Path}", filePath);
            doc = XDocument.Load(filePath);
            projectElement = doc.Root!;
        }
        else if (File.Exists(filePath) && _options.DryRun)
        {
            _logger.LogInformation("[DRY RUN] Would update existing Directory.Build.props at {Path}", filePath);
            doc = XDocument.Load(filePath);
            projectElement = doc.Root!;
        }
        else
        {
            var action = _options.DryRun ? "[DRY RUN] Would create" : "Creating";
            _logger.LogInformation("{Action} new Directory.Build.props at {Path}", action, filePath);
            doc = new XDocument(new XDeclaration("1.0", "utf-8", null));
            projectElement = new XElement("Project");
            doc.Add(projectElement);
        }

        var assemblyPropGroup = projectElement.Elements("PropertyGroup")
            .FirstOrDefault(pg => pg.Elements().Any(e =>
                e.Name.LocalName == "GenerateAssemblyInfo" ||
                e.Name.LocalName.StartsWith("Assembly") ||
                e.Name.LocalName == "Company" ||
                e.Name.LocalName == "Product" ||
                e.Name.LocalName == "Copyright" ||
                e.Name.LocalName == "FileVersion" ||
                e.Name.LocalName == "NeutralResourcesLanguage" ||
                e.Name.LocalName == "ComVisible"));

        if (assemblyPropGroup == null)
        {
            assemblyPropGroup = new XElement("PropertyGroup");
            assemblyPropGroup.Add(new XComment("Assembly Information"));

            var firstPropGroup = projectElement.Elements("PropertyGroup").FirstOrDefault();
            if (firstPropGroup != null)
            {
                firstPropGroup.AddAfterSelf(assemblyPropGroup);
            }
            else
            {
                projectElement.AddFirst(assemblyPropGroup);
            }
        }

        AddOrUpdateProperty(assemblyPropGroup, "GenerateAssemblyInfo", "true");

        // Add assembly properties if they exist
        if (!string.IsNullOrEmpty(commonProperties.Company))
            AddOrUpdateProperty(assemblyPropGroup, "Company", commonProperties.Company);

        if (!string.IsNullOrEmpty(commonProperties.Product))
            AddOrUpdateProperty(assemblyPropGroup, "Product", commonProperties.Product);

        if (!string.IsNullOrEmpty(commonProperties.Copyright))
            AddOrUpdateProperty(assemblyPropGroup, "Copyright", commonProperties.Copyright);

        if (!string.IsNullOrEmpty(commonProperties.Trademark))
            AddOrUpdateProperty(assemblyPropGroup, "Trademark", commonProperties.Trademark);

        if (!string.IsNullOrEmpty(commonProperties.AssemblyVersion))
            AddOrUpdateProperty(assemblyPropGroup, "AssemblyVersion", commonProperties.AssemblyVersion);

        if (!string.IsNullOrEmpty(commonProperties.FileVersion))
            AddOrUpdateProperty(assemblyPropGroup, "FileVersion", commonProperties.FileVersion);

        if (!string.IsNullOrEmpty(commonProperties.NeutralResourcesLanguage))
            AddOrUpdateProperty(assemblyPropGroup, "NeutralResourcesLanguage", commonProperties.NeutralResourcesLanguage);

        if (commonProperties.ComVisible.HasValue)
            AddOrUpdateProperty(assemblyPropGroup, "ComVisible", commonProperties.ComVisible.Value.ToString().ToLower());

        // Add automatic binding redirect generation
        var bindingRedirectPropGroup = projectElement.Elements("PropertyGroup")
            .FirstOrDefault(pg => pg.Elements().Any(e =>
                e.Name.LocalName == "AutoGenerateBindingRedirects"));

        if (bindingRedirectPropGroup == null)
        {
            bindingRedirectPropGroup = new XElement("PropertyGroup");
            bindingRedirectPropGroup.Add(new XComment("Binding Redirect Configuration"));
            assemblyPropGroup.AddAfterSelf(bindingRedirectPropGroup);
        }

        AddOrUpdateProperty(bindingRedirectPropGroup, "AutoGenerateBindingRedirects", "true");
        AddOrUpdateProperty(bindingRedirectPropGroup, "GenerateBindingRedirectsOutputType", "true");

        // Add InternalsVisibleTo for test projects
        var itemGroup = projectElement.Elements("ItemGroup")
            .FirstOrDefault(ig => ig.Elements().Any(e => e.Name.LocalName == "InternalsVisibleTo"));

        if (itemGroup == null)
        {
            itemGroup = new XElement("ItemGroup");
            itemGroup.Add(new XComment("Make internals visible to test projects"));
            bindingRedirectPropGroup.AddAfterSelf(itemGroup);
        }

        // Check if InternalsVisibleTo for Tests already exists
        var internalsVisibleToTests = itemGroup.Elements("InternalsVisibleTo")
            .FirstOrDefault(e => e.Attribute("Include")?.Value == "$(MSBuildProjectName).Tests");

        if (internalsVisibleToTests == null)
        {
            _logger.LogDebug("Adding InternalsVisibleTo for $(MSBuildProjectName).Tests");
            itemGroup.Add(new XElement("InternalsVisibleTo", 
                new XAttribute("Include", "$(MSBuildProjectName).Tests")));
        }
        else
        {
            _logger.LogDebug("InternalsVisibleTo for $(MSBuildProjectName).Tests already exists");
        }

        if (!_options.DryRun)
        {
            var isNewFile = !File.Exists(filePath);
            var beforeHash = isNewFile ? string.Empty : await FileHashCalculator.CalculateHashAsync(filePath, cancellationToken);
            var beforeSize = isNewFile ? 0 : new FileInfo(filePath).Length;

            // Save without XML declaration
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(filePath, settings))
            {
                doc.Save(writer);
            }
            _logger.LogInformation("Successfully created/updated Directory.Build.props at {Path}", filePath);

            if (isNewFile)
            {
                await _auditService.LogFileCreationAsync(new FileCreationAudit
                {
                    FilePath = filePath,
                    FileHash = await FileHashCalculator.CalculateHashAsync(filePath, cancellationToken),
                    FileSize = new FileInfo(filePath).Length,
                    CreationType = "Directory.Build.props generation"
                }, cancellationToken);
            }
            else
            {
                await _auditService.LogFileModificationAsync(new FileModificationAudit
                {
                    FilePath = filePath,
                    BeforeHash = beforeHash,
                    AfterHash = await FileHashCalculator.CalculateHashAsync(filePath, cancellationToken),
                    BeforeSize = beforeSize,
                    AfterSize = new FileInfo(filePath).Length,
                    ModificationType = "Directory.Build.props update"
                }, cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation("[DRY RUN] Would create/update Directory.Build.props at {Path}", filePath);
            _logger.LogDebug("[DRY RUN] Directory.Build.props content:\n{Content}", doc.ToString());
        }
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
            if (existing.Value != value)
            {
                _logger.LogDebug("Updating property {Name} from '{OldValue}' to '{NewValue}'", name, existing.Value, value);
                existing.Value = value;
            }
            else
            {
                _logger.LogDebug("Property {Name} already has value '{Value}'", name, value);
            }
        }
        else
        {
            _logger.LogDebug("Adding new property {Name} with value '{Value}'", name, value);
            propertyGroup.Add(new XElement(name, value));
        }
    }
}