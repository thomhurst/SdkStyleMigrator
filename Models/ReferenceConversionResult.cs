using Microsoft.Build.Evaluation;

namespace SdkMigrator.Models;

/// <summary>
/// Represents the result of converting assembly references, including both successfully
/// converted package references and unconverted assembly references.
/// </summary>
public class ReferenceConversionResult
{
    /// <summary>
    /// Package references that were successfully converted from assembly references.
    /// </summary>
    public List<PackageReference> PackageReferences { get; set; } = new();

    /// <summary>
    /// Assembly references that could not be converted to package references
    /// and should be preserved as-is in the new project format.
    /// </summary>
    public List<UnconvertedReference> UnconvertedReferences { get; set; } = new();

    /// <summary>
    /// Warnings generated during the conversion process.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Represents an assembly reference that could not be converted to a package reference.
/// </summary>
public class UnconvertedReference
{
    /// <summary>
    /// The assembly identity containing name, version, culture, and public key token.
    /// </summary>
    public AssemblyIdentity Identity { get; set; } = new();

    /// <summary>
    /// The hint path to the assembly, if specified in the original reference.
    /// </summary>
    public string? HintPath { get; set; }

    /// <summary>
    /// Whether this reference should be copied to the output directory.
    /// </summary>
    public bool? Private { get; set; }

    /// <summary>
    /// Additional metadata from the original reference item.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// The reason why this reference could not be converted.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Creates an UnconvertedReference from an MSBuild ProjectItem.
    /// </summary>
    public static UnconvertedReference FromProjectItem(ProjectItem item, string reason)
    {
        var reference = new UnconvertedReference
        {
            Identity = AssemblyIdentity.Parse(item.EvaluatedInclude),
            Reason = reason
        };

        // Extract common metadata
        if (item.HasMetadata("HintPath"))
            reference.HintPath = item.GetMetadataValue("HintPath");

        if (item.HasMetadata("Private"))
        {
            var privateValue = item.GetMetadataValue("Private");
            if (bool.TryParse(privateValue, out var isPrivate))
                reference.Private = isPrivate;
        }

        // Store any additional metadata
        foreach (var metadata in item.Metadata)
        {
            if (metadata.Name != "HintPath" && metadata.Name != "Private")
            {
                reference.Metadata[metadata.Name] = metadata.EvaluatedValue;
            }
        }

        return reference;
    }
}