using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class T4TemplateHandler
{
    private readonly ILogger<T4TemplateHandler> _logger;

    public T4TemplateHandler(ILogger<T4TemplateHandler> logger)
    {
        _logger = logger;
    }

    public T4TemplateInfo DetectT4Templates(Project project)
    {
        var info = new T4TemplateInfo();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";

        // Find all T4 templates in the project
        var t4Items = project.Items
            .Where(i => (i.ItemType == "None" || i.ItemType == "Content" || i.ItemType == "Compile") &&
                       i.EvaluatedInclude.EndsWith(".tt", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var t4Item in t4Items)
        {
            var template = new T4Template
            {
                FilePath = t4Item.EvaluatedInclude,
                ItemType = t4Item.ItemType,
                Generator = t4Item.GetMetadataValue("Generator"),
                LastGenOutput = t4Item.GetMetadataValue("LastGenOutput"),
                CustomToolNamespace = t4Item.GetMetadataValue("CustomToolNamespace")
            };

            // Check if it has the T4 generator
            if (string.IsNullOrEmpty(template.Generator))
            {
                template.Generator = "TextTemplatingFileGenerator";
            }

            // Check if output file exists
            if (!string.IsNullOrEmpty(template.LastGenOutput))
            {
                var outputPath = Path.Combine(projectDir, Path.GetDirectoryName(template.FilePath) ?? "", template.LastGenOutput);
                template.OutputExists = File.Exists(outputPath);
            }
            else
            {
                // Default output is .cs file with same name
                var outputFile = Path.ChangeExtension(template.FilePath, ".cs");
                var outputPath = Path.Combine(projectDir, outputFile);
                template.OutputExists = File.Exists(outputPath);
                template.LastGenOutput = Path.GetFileName(outputFile);
            }

            info.Templates.Add(template);
        }

        info.HasT4Templates = info.Templates.Any();

        // Check if project already has T4 SDK reference
        var hasT4Sdk = project.Items.Any(i =>
            i.ItemType == "PackageReference" &&
            (i.EvaluatedInclude.Equals("Microsoft.TextTemplating.Targets", StringComparison.OrdinalIgnoreCase) ||
             i.EvaluatedInclude.Equals("T4SDK", StringComparison.OrdinalIgnoreCase)));

        info.HasT4SdkReference = hasT4Sdk;

        return info;
    }

    public void MigrateT4Templates(T4TemplateInfo t4Info, XElement projectElement, MigrationResult result)
    {
        if (!t4Info.HasT4Templates)
            return;

        _logger.LogInformation("Migrating {Count} T4 templates", t4Info.Templates.Count);

        // Add T4 SDK package if not already present
        if (!t4Info.HasT4SdkReference)
        {
            var itemGroup = projectElement.Elements("ItemGroup")
                .FirstOrDefault(ig => ig.Elements("PackageReference").Any());

            if (itemGroup == null)
            {
                itemGroup = new XElement("ItemGroup");
                projectElement.Add(itemGroup);
            }

            // Add T4 SDK package
            var t4Package = new XElement("PackageReference",
                new XAttribute("Include", "Microsoft.TextTemplating.Targets"),
                new XAttribute("Version", "17.0.0"));
            itemGroup.Add(t4Package);

            _logger.LogInformation("Added Microsoft.TextTemplating.Targets package reference");
        }

        // Create ItemGroup for T4 templates
        var t4ItemGroup = new XElement("ItemGroup");
        var hasItems = false;

        foreach (var template in t4Info.Templates)
        {
            // T4 templates should be None items with proper metadata
            var t4Element = new XElement("None",
                new XAttribute("Update", template.FilePath));

            // Add generator metadata
            t4Element.Add(new XElement("Generator", template.Generator));

            // Add output file metadata
            if (!string.IsNullOrEmpty(template.LastGenOutput))
            {
                t4Element.Add(new XElement("LastGenOutput", template.LastGenOutput));
            }

            // Add custom tool namespace if specified
            if (!string.IsNullOrEmpty(template.CustomToolNamespace))
            {
                t4Element.Add(new XElement("CustomToolNamespace", template.CustomToolNamespace));
            }

            t4ItemGroup.Add(t4Element);
            hasItems = true;

            // If the generated file is included as Compile, update it to be DependentUpon
            if (!string.IsNullOrEmpty(template.LastGenOutput))
            {
                var outputElement = new XElement("Compile",
                    new XAttribute("Update", Path.Combine(Path.GetDirectoryName(template.FilePath) ?? "", template.LastGenOutput)));
                outputElement.Add(new XElement("DependentUpon", Path.GetFileName(template.FilePath)));
                outputElement.Add(new XElement("AutoGen", "True"));
                outputElement.Add(new XElement("DesignTime", "True"));
                t4ItemGroup.Add(outputElement);
            }

            _logger.LogDebug("Migrated T4 template: {FilePath}", template.FilePath);
        }

        if (hasItems)
        {
            projectElement.Add(t4ItemGroup);
        }

        // Add warnings and guidance
        result.Warnings.Add($"T4 Templates: Migrated {t4Info.Templates.Count} templates");
        result.Warnings.Add("T4 Template migration notes:");
        result.Warnings.Add("- Microsoft.TextTemplating.Targets package has been added");
        result.Warnings.Add("- Templates will be processed at build time");
        result.Warnings.Add("- To transform manually: dotnet msbuild -t:TransformAll");
        result.Warnings.Add("- Visual Studio should continue to support design-time transformation");

        if (t4Info.Templates.Any(t => !t.OutputExists))
        {
            result.Warnings.Add("- Some T4 outputs are missing - run transformation after migration");
        }

        // Add MSBuild target for T4 transformation if needed
        var transformTarget = new XElement("Target",
            new XAttribute("Name", "TransformOnBuild"),
            new XAttribute("BeforeTargets", "BeforeBuild"));
        transformTarget.Add(new XElement("Exec",
            new XAttribute("Command", "dotnet msbuild -t:TransformAll"),
            new XAttribute("Condition", "'$(DesignTimeBuild)' != 'true'")));

        // Only add if user wants build-time transformation (optional)
        // projectElement.Add(transformTarget);
    }
}

public class T4TemplateInfo
{
    public bool HasT4Templates { get; set; }
    public bool HasT4SdkReference { get; set; }
    public List<T4Template> Templates { get; set; } = new();
}

public class T4Template
{
    public string FilePath { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Generator { get; set; } = string.Empty;
    public string? LastGenOutput { get; set; }
    public string? CustomToolNamespace { get; set; }
    public bool OutputExists { get; set; }
}