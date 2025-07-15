namespace SdkMigrator.Models;

/// <summary>
/// Represents the full identity of an assembly reference including name, version, culture, and public key token.
/// </summary>
public class AssemblyIdentity
{
    /// <summary>
    /// The simple name of the assembly (e.g., "System.Drawing").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The version of the assembly (e.g., "4.0.0.0").
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The culture of the assembly (e.g., "neutral").
    /// </summary>
    public string? Culture { get; set; }

    /// <summary>
    /// The public key token of the assembly (e.g., "b03f5f7f11d50a3a").
    /// </summary>
    public string? PublicKeyToken { get; set; }

    /// <summary>
    /// The original full reference string as it appeared in the project file.
    /// </summary>
    public string OriginalInclude { get; set; } = string.Empty;

    /// <summary>
    /// Parses an assembly reference string into an AssemblyIdentity object.
    /// </summary>
    /// <param name="referenceInclude">The reference string (e.g., "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")</param>
    /// <returns>An AssemblyIdentity object with parsed values.</returns>
    public static AssemblyIdentity Parse(string referenceInclude)
    {
        var identity = new AssemblyIdentity
        {
            OriginalInclude = referenceInclude
        };

        var parts = referenceInclude.Split(',');
        if (parts.Length == 0)
            return identity;

        // First part is always the assembly name
        identity.Name = parts[0].Trim();

        // Parse the remaining parts
        for (int i = 1; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            var keyValue = part.Split('=');
            if (keyValue.Length != 2)
                continue;

            var key = keyValue[0].Trim();
            var value = keyValue[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "version":
                    identity.Version = value;
                    break;
                case "culture":
                    identity.Culture = value;
                    break;
                case "publickeytoken":
                    identity.PublicKeyToken = value;
                    break;
            }
        }

        return identity;
    }

    /// <summary>
    /// Returns the full assembly reference string with all metadata.
    /// </summary>
    public override string ToString()
    {
        var result = Name;

        if (!string.IsNullOrEmpty(Version))
            result += $", Version={Version}";

        if (!string.IsNullOrEmpty(Culture))
            result += $", Culture={Culture}";

        if (!string.IsNullOrEmpty(PublicKeyToken))
            result += $", PublicKeyToken={PublicKeyToken}";

        return result;
    }
}