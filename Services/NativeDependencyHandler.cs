using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;
using SdkMigrator.Abstractions;
using System.Xml.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;

namespace SdkMigrator.Services;

public class NativeDependencyHandler : INativeDependencyHandler
{
    private readonly ILogger<NativeDependencyHandler> _logger;

    private static readonly string[] NativeExtensions = new[]
    {
        ".dll", ".so", ".dylib", ".a", ".lib", ".ocx", ".node", ".pyd"
    };

    private static readonly string[] ManagedAssemblyPaths = new[]
    {
        "packages", ".nuget", "bin\\debug", "bin\\release", "obj",
        @"\.nuget\cache\", @"/.nuget/cache/", @"\Users\", @"/Users/"
    };

    private static readonly Dictionary<string, string> RuntimeIdentifierMappings = new()
    {
        { "x86", "win-x86" },
        { "x64", "win-x64" },
        { "Win32", "win-x86" },
        { "amd64", "win-x64" },
        { "arm", "win-arm" },
        { "arm64", "win-arm64" },
        { "linux-x64", "linux-x64" },
        { "linux-x86", "linux-x86" },
        { "linux-arm", "linux-arm" },
        { "linux-arm64", "linux-arm64" },
        { "osx-x64", "osx-x64" },
        { "osx-arm64", "osx-arm64" }
    };

    private static readonly string[] CommonSystemDlls = new[]
    {
        "kernel32", "user32", "advapi32", "ole32", "oleaut32", "shell32",
        "gdi32", "ws2_32", "msvcrt", "ntdll", "comctl32", "comdlg32",
        "crypt32", "dbghelp", "imagehlp", "iphlpapi", "mpr", "netapi32",
        "powrprof", "psapi", "rpcrt4", "secur32", "setupapi", "shlwapi",
        "urlmon", "userenv", "version", "winhttp", "wininet", "winmm",
        "winspool", "wintrust", "wtsapi32", "libc", "libm", "libdl",
        "libpthread", "librt", "libstdc++", "libgcc_s"
    };

    public NativeDependencyHandler(ILogger<NativeDependencyHandler> logger)
    {
        _logger = logger;
    }

    public List<NativeDependency> DetectNativeDependencies(Project project)
    {
        var dependencies = new List<NativeDependency>();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";

        // Check for runtime-specific folders (runtimes/*/native pattern)
        DetectRuntimeSpecificDependencies(project, projectDir, dependencies);

        // Check References with HintPath
        var references = project.Items.Where(i => i.ItemType == "Reference");
        foreach (var reference in references)
        {
            var hintPath = reference.GetMetadataValue("HintPath");
            if (string.IsNullOrEmpty(hintPath)) continue;

            // Skip if it's in a managed location
            if (ManagedAssemblyPaths.Any(p => hintPath.Contains(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(projectDir, hintPath));
            if (File.Exists(fullPath))
            {
                var (isNative, architecture) = AnalyzeBinary(fullPath);
                if (isNative)
                {
                    dependencies.Add(new NativeDependency
                    {
                        SourcePath = hintPath,
                        FileName = Path.GetFileName(hintPath),
                        DetectedFrom = "Reference with HintPath",
                        IsNative = true,
                        Architecture = architecture
                    });
                }
            }
        }

        // Check Content/None items that might be native DLLs
        var contentItems = project.Items.Where(i =>
            (i.ItemType == "Content" || i.ItemType == "None") &&
            NativeExtensions.Any(ext => i.EvaluatedInclude.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        foreach (var item in contentItems)
        {
            var copyToOutput = item.GetMetadataValue("CopyToOutputDirectory");
            if (!string.IsNullOrEmpty(copyToOutput) && copyToOutput != "Never")
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, item.EvaluatedInclude));
                if (File.Exists(fullPath))
                {
                    var (isNative, architecture) = AnalyzeBinary(fullPath);
                    dependencies.Add(new NativeDependency
                    {
                        SourcePath = item.EvaluatedInclude,
                        FileName = Path.GetFileName(item.EvaluatedInclude),
                        DetectedFrom = $"{item.ItemType} item",
                        CopyToOutputDirectory = copyToOutput,
                        IsNative = isNative,
                        Architecture = architecture
                    });
                }
            }
        }

        // Check for P/Invoke DllImport attributes in code (heuristic)
        var dllImportPattern = @"\[DllImport\s*\(\s*""([^""]+)""";
        var csFiles = project.Items.Where(i => i.ItemType == "Compile" &&
            i.EvaluatedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        foreach (var csFile in csFiles.Take(100)) // Limit to avoid performance issues
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, csFile.EvaluatedInclude));
                if (File.Exists(fullPath))
                {
                    var content = File.ReadAllText(fullPath);
                    var matches = System.Text.RegularExpressions.Regex.Matches(content, dllImportPattern);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            var dllName = match.Groups[1].Value;
                            var dllNameWithoutExt = Path.GetFileNameWithoutExtension(dllName);
                            
                            // Skip common system DLLs
                            if (!CommonSystemDlls.Any(sys => dllNameWithoutExt.Equals(sys, StringComparison.OrdinalIgnoreCase)))
                            {
                                dependencies.Add(new NativeDependency
                                {
                                    SourcePath = dllName,
                                    FileName = dllName.EndsWith(".dll") ? dllName : dllName + ".dll",
                                    DetectedFrom = $"DllImport in {Path.GetFileName(csFile.EvaluatedInclude)}",
                                    IsPInvoke = true,
                                    IsNative = true
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error scanning file {File} for DllImport", csFile.EvaluatedInclude);
            }
        }

        return dependencies.DistinctBy(d => d.FileName.ToLowerInvariant()).ToList();
    }

    public void MigrateNativeDependencies(List<NativeDependency> dependencies, XElement projectRoot, MigrationResult result)
    {
        if (!dependencies.Any()) return;

        var itemGroup = new XElement("ItemGroup");
        var hasItems = false;
        var hasRuntimeSpecific = dependencies.Any(d => !string.IsNullOrEmpty(d.RuntimeIdentifier));

        // Group dependencies by runtime identifier
        var runtimeGroups = dependencies
            .Where(d => !d.IsPInvoke && !string.IsNullOrEmpty(d.SourcePath))
            .GroupBy(d => d.RuntimeIdentifier ?? "any");

        foreach (var group in runtimeGroups)
        {
            foreach (var dep in group)
            {
                // Skip items from NuGet cache
                if (dep.SourcePath.Contains(@"\.nuget\cache\", StringComparison.OrdinalIgnoreCase) ||
                    dep.SourcePath.Contains(@"/.nuget/cache/", StringComparison.OrdinalIgnoreCase) ||
                    (dep.SourcePath.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase) && dep.SourcePath.Contains(@"\.nuget\", StringComparison.OrdinalIgnoreCase)) ||
                    (dep.SourcePath.Contains(@"/Users/", StringComparison.OrdinalIgnoreCase) && dep.SourcePath.Contains(@"/.nuget/", StringComparison.OrdinalIgnoreCase)) ||
                    (dep.SourcePath.Contains(@"C:\Users\", StringComparison.OrdinalIgnoreCase) && dep.SourcePath.Contains(@"\.nuget\", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("Skipping NuGet cache item: {Path}", dep.SourcePath);
                    continue;
                }

                // For runtime-specific dependencies, use the correct pattern
                if (!string.IsNullOrEmpty(dep.RuntimeIdentifier))
                {
                    // These are typically handled automatically by the SDK
                    // but we'll add a comment about them
                    _logger.LogInformation("Runtime-specific dependency detected: {File} for {RID}", 
                        dep.FileName, dep.RuntimeIdentifier);
                    continue;
                }

                // For native dependencies, ensure they're copied to output
                var element = new XElement("None",
                    new XAttribute("Include", dep.SourcePath));

                var copyToOutput = dep.CopyToOutputDirectory;
                if (string.IsNullOrEmpty(copyToOutput))
                {
                    copyToOutput = "PreserveNewest";
                }

                element.Add(new XElement("CopyToOutputDirectory", copyToOutput));
                element.Add(new XElement("Visible", "false"));

                // Add platform-specific conditions based on file extension and architecture
                string? condition = null;
                if (dep.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    condition = dep.Architecture switch
                    {
                        "x86" => "'$(Platform)' == 'x86' Or '$(Platform)' == 'Win32'",
                        "x64" => "'$(Platform)' == 'x64' Or '$(Platform)' == 'AnyCPU'",
                        "ARM" => "'$(Platform)' == 'ARM'",
                        "ARM64" => "'$(Platform)' == 'ARM64'",
                        _ => "'$(OS)' == 'Windows_NT'"
                    };
                }
                else if (dep.FileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                {
                    condition = "'$(OS)' == 'Unix' And '$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::Linux)))' == 'true'";
                }
                else if (dep.FileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                {
                    condition = "'$(OS)' == 'Unix' And '$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::OSX)))' == 'true'";
                }

                if (!string.IsNullOrEmpty(condition))
                {
                    element.Add(new XAttribute("Condition", condition));
                }

                // Add Link metadata if the file is in a subdirectory
                if (dep.SourcePath.Contains('/') || dep.SourcePath.Contains('\\'))
                {
                    element.Add(new XElement("Link", dep.FileName));
                }

                itemGroup.Add(element);
                hasItems = true;

                _logger.LogInformation("Migrated native dependency: {File} (Architecture: {Arch})", 
                    dep.FileName, dep.Architecture);
            }
        }

        if (hasItems)
        {
            projectRoot.Add(itemGroup);
        }

        // Add runtime identifier support if needed
        if (hasRuntimeSpecific)
        {
            var runtimeIdentifiers = dependencies
                .Where(d => !string.IsNullOrEmpty(d.RuntimeIdentifier))
                .Select(d => d.RuntimeIdentifier!)
                .Distinct()
                .ToList();

            if (runtimeIdentifiers.Any())
            {
                result.Warnings.Add($"Runtime-specific native dependencies detected. Consider adding <RuntimeIdentifiers>{string.Join(";", runtimeIdentifiers)}</RuntimeIdentifiers> to your project for proper multi-platform support.");
            }
        }

        // Add warnings
        var warning = new StringBuilder();
        warning.AppendLine("Native dependencies detected. Please verify:");
        warning.AppendLine();

        foreach (var dep in dependencies)
        {
            var archInfo = !string.IsNullOrEmpty(dep.Architecture) && dep.Architecture != "Unknown" 
                ? $", Architecture: {dep.Architecture}" 
                : "";
            warning.AppendLine($"- {dep.FileName} (detected from: {dep.DetectedFrom}{archInfo})");
        }

        warning.AppendLine();
        warning.AppendLine("Ensure these files:");
        warning.AppendLine("1. Are available at build time");
        warning.AppendLine("2. Are deployed with your application");
        warning.AppendLine("3. Match the target platform architecture");

        // Add architecture-specific warnings
        var architectures = dependencies
            .Where(d => !string.IsNullOrEmpty(d.Architecture) && d.Architecture != "Unknown")
            .Select(d => d.Architecture)
            .Distinct()
            .ToList();

        if (architectures.Count > 1)
        {
            warning.AppendLine();
            warning.AppendLine($"Multiple architectures detected: {string.Join(", ", architectures)}");
            warning.AppendLine("Consider using runtime-specific folders (runtimes/<RID>/native/) for proper deployment.");
        }

        if (dependencies.Any(d => d.IsPInvoke))
        {
            warning.AppendLine();
            warning.AppendLine("P/Invoke declarations detected. Consider:");
            warning.AppendLine("- Using NuGet packages that include native dependencies");
            warning.AppendLine("- Setting up runtimes folder for platform-specific assets");
            warning.AppendLine("- Using SetDllDirectory or explicit paths for runtime resolution");
            warning.AppendLine("- Adding <RuntimeIdentifiers> to your project for multi-platform support");
        }

        result.Warnings.Add(warning.ToString());
    }

    private void DetectRuntimeSpecificDependencies(Project project, string projectDir, List<NativeDependency> dependencies)
    {
        // Look for runtime-specific folders
        var runtimesPath = Path.Combine(projectDir, "runtimes");
        if (Directory.Exists(runtimesPath))
        {
            foreach (var runtimeDir in Directory.GetDirectories(runtimesPath))
            {
                var nativePath = Path.Combine(runtimeDir, "native");
                if (Directory.Exists(nativePath))
                {
                    var runtimeId = Path.GetFileName(runtimeDir);
                    foreach (var file in Directory.GetFiles(nativePath).Where(f => 
                        NativeExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
                    {
                        var (isNative, architecture) = AnalyzeBinary(file);
                        if (isNative)
                        {
                            dependencies.Add(new NativeDependency
                            {
                                SourcePath = Path.GetRelativePath(projectDir, file),
                                FileName = Path.GetFileName(file),
                                DetectedFrom = $"Runtime-specific folder ({runtimeId})",
                                IsNative = true,
                                Architecture = architecture,
                                RuntimeIdentifier = runtimeId
                            });
                        }
                    }
                }
            }
        }
    }

    private (bool IsNative, string Architecture) AnalyzeBinary(string filePath)
    {
        try
        {
            // First try to determine if it's a PE file and get architecture
            using (var stream = File.OpenRead(filePath))
            using (var peReader = new PEReader(stream))
            {
                if (!peReader.HasMetadata)
                {
                    // No metadata = likely native
                    var machine = peReader.PEHeaders.CoffHeader.Machine;
                    var architecture = machine switch
                    {
                        Machine.I386 => "x86",
                        Machine.Amd64 => "x64",
                        Machine.Arm => "ARM",
                        Machine.Arm64 => "ARM64",
                        Machine.IA64 => "Itanium",
                        _ => "Unknown"
                    };
                    return (true, architecture);
                }
                else
                {
                    // Has metadata, check if it's managed
                    var metadataReader = peReader.GetMetadataReader();
                    // If we can read metadata, it's likely managed
                    return (false, string.Empty);
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Not a valid PE file, but might be a native library for other platforms
            if (filePath.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "Unix/macOS");
            }
            return (true, "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error analyzing binary {FilePath}", filePath);
            // Conservative approach: if we can't analyze it, assume it might be native
            return (true, "Unknown");
        }
    }
}

public class NativeDependency
{
    public string SourcePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DetectedFrom { get; set; } = string.Empty;
    public string? CopyToOutputDirectory { get; set; }
    public bool IsNative { get; set; }
    public bool IsPInvoke { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string? RuntimeIdentifier { get; set; }
}