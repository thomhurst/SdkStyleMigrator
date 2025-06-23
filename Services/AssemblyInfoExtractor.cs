using System.Text.RegularExpressions;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using SdkMigrator.Utilities;

namespace SdkMigrator.Services;

public class AssemblyInfoExtractor : IAssemblyInfoExtractor
{
    private readonly ILogger<AssemblyInfoExtractor> _logger;

    private static readonly Regex AssemblyAttributeRegex = new(
        @"^\s*\[assembly\s*:\s*(?:System\.Reflection\.|AssemblyMetadata\s*\(\s*"")?(?<name>\w+)(?:""\s*,\s*)?(?:\()?""?(?<value>[^""\)]+)""?\)?\s*\]",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AssemblyInfoExtractor(ILogger<AssemblyInfoExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<AssemblyProperties> ExtractAssemblyPropertiesAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        var properties = new AssemblyProperties();

        foreach (var pattern in LegacyProjectElements.AssemblyInfoFilePatterns)
        {
            var assemblyInfoFiles = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);

            foreach (var file in assemblyInfoFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogDebug("Extracting assembly properties from {File}", file);
                await ExtractFromFileAsync(file, properties, cancellationToken);
            }
        }

        return properties;
    }

    public Task<AssemblyProperties> ExtractFromProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        var properties = new AssemblyProperties();

        foreach (var prop in project.Properties)
        {
            switch (prop.Name)
            {
                case "AssemblyTitle":
                    properties.AssemblyTitle = prop.EvaluatedValue;
                    break;
                case "AssemblyDescription":
                    properties.AssemblyDescription = prop.EvaluatedValue;
                    break;
                case "AssemblyConfiguration":
                    properties.AssemblyConfiguration = prop.EvaluatedValue;
                    break;
                case "AssemblyCompany":
                case "Company":
                    properties.Company = prop.EvaluatedValue;
                    break;
                case "AssemblyProduct":
                case "Product":
                    properties.Product = prop.EvaluatedValue;
                    break;
                case "AssemblyCopyright":
                case "Copyright":
                    properties.Copyright = prop.EvaluatedValue;
                    break;
                case "AssemblyTrademark":
                case "Trademark":
                    properties.Trademark = prop.EvaluatedValue;
                    break;
                case "AssemblyVersion":
                    properties.AssemblyVersion = prop.EvaluatedValue;
                    break;
                case "FileVersion":
                case "AssemblyFileVersion":
                    properties.FileVersion = prop.EvaluatedValue;
                    break;
                case "NeutralResourcesLanguage":
                    properties.NeutralResourcesLanguage = prop.EvaluatedValue;
                    break;
                case "ComVisible":
                    if (bool.TryParse(prop.EvaluatedValue, out var comVisible))
                        properties.ComVisible = comVisible;
                    break;
                case "Guid":
                case "AssemblyGuid":
                    properties.Guid = prop.EvaluatedValue;
                    break;
            }
        }

        return Task.FromResult(properties);
    }

    private async Task ExtractFromFileAsync(string filePath, AssemblyProperties properties, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var matches = AssemblyAttributeRegex.Matches(content);

            foreach (Match match in matches)
            {
                var name = match.Groups["name"].Value;
                var value = match.Groups["value"].Value;

                switch (name)
                {
                    case "AssemblyTitle":
                        properties.AssemblyTitle ??= value;
                        break;
                    case "AssemblyDescription":
                        properties.AssemblyDescription ??= value;
                        break;
                    case "AssemblyConfiguration":
                        properties.AssemblyConfiguration ??= value;
                        break;
                    case "AssemblyCompany":
                        properties.Company ??= value;
                        break;
                    case "AssemblyProduct":
                        properties.Product ??= value;
                        break;
                    case "AssemblyCopyright":
                        properties.Copyright ??= value;
                        break;
                    case "AssemblyTrademark":
                        properties.Trademark ??= value;
                        break;
                    case "AssemblyVersion":
                        properties.AssemblyVersion ??= value;
                        break;
                    case "AssemblyFileVersion":
                        properties.FileVersion ??= value;
                        break;
                    case "ComVisible":
                        if (bool.TryParse(value, out var comVisible))
                            properties.ComVisible ??= comVisible;
                        break;
                    case "Guid":
                        properties.Guid ??= value;
                        break;
                    case "NeutralResourcesLanguage":
                        properties.NeutralResourcesLanguage ??= value;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract assembly properties from {File}", filePath);
        }
    }
}