using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class NativeDependencyHandler
{
    private readonly ILogger<NativeDependencyHandler> _logger;
    
    private static readonly string[] NativeExtensions = new[]
    {
        ".dll", ".so", ".dylib", ".a", ".lib", ".ocx"
    };
    
    private static readonly string[] ManagedAssemblyPaths = new[]
    {
        "packages", ".nuget", "bin\\debug", "bin\\release", "obj"
    };

    public NativeDependencyHandler(ILogger<NativeDependencyHandler> logger)
    {
        _logger = logger;
    }

    public List<NativeDependency> DetectNativeDependencies(Project project)
    {
        var dependencies = new List<NativeDependency>();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";
        
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
            if (File.Exists(fullPath) && IsLikelyNativeDependency(fullPath))
            {
                dependencies.Add(new NativeDependency
                {
                    SourcePath = hintPath,
                    FileName = Path.GetFileName(hintPath),
                    DetectedFrom = "Reference with HintPath",
                    IsNative = true
                });
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
                    dependencies.Add(new NativeDependency
                    {
                        SourcePath = item.EvaluatedInclude,
                        FileName = Path.GetFileName(item.EvaluatedInclude),
                        DetectedFrom = $"{item.ItemType} item",
                        CopyToOutputDirectory = copyToOutput,
                        IsNative = IsLikelyNativeDependency(fullPath)
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
                            if (!dllName.StartsWith("kernel32") && !dllName.StartsWith("user32") && 
                                !dllName.StartsWith("advapi32") && !dllName.StartsWith("ole32"))
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
        
        foreach (var dep in dependencies.Where(d => !d.IsPInvoke && !string.IsNullOrEmpty(d.SourcePath)))
        {
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
            
            // Add platform-specific conditions if needed
            if (dep.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                element.Add(new XAttribute("Condition", "'$(OS)' == 'Windows_NT'"));
            }
            else if (dep.FileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
            {
                element.Add(new XAttribute("Condition", "'$(OS)' == 'Unix' And '$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::Linux)))' == 'true'"));
            }
            else if (dep.FileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                element.Add(new XAttribute("Condition", "'$(OS)' == 'Unix' And '$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::OSX)))' == 'true'"));
            }
            
            itemGroup.Add(element);
            hasItems = true;
            
            _logger.LogInformation("Migrated native dependency: {File}", dep.FileName);
        }
        
        if (hasItems)
        {
            projectRoot.Add(itemGroup);
        }
        
        // Add warnings
        var warning = new StringBuilder();
        warning.AppendLine("Native dependencies detected. Please verify:");
        warning.AppendLine();
        
        foreach (var dep in dependencies)
        {
            warning.AppendLine($"- {dep.FileName} (detected from: {dep.DetectedFrom})");
        }
        
        warning.AppendLine();
        warning.AppendLine("Ensure these files:");
        warning.AppendLine("1. Are available at build time");
        warning.AppendLine("2. Are deployed with your application");
        warning.AppendLine("3. Match the target platform architecture (x86/x64/ARM)");
        
        if (dependencies.Any(d => d.IsPInvoke))
        {
            warning.AppendLine();
            warning.AppendLine("P/Invoke declarations detected. Consider:");
            warning.AppendLine("- Using NuGet packages that include native dependencies");
            warning.AppendLine("- Setting up runtimes folder for platform-specific assets");
            warning.AppendLine("- Using SetDllDirectory or explicit paths for runtime resolution");
        }
        
        result.Warnings.Add(warning.ToString());
    }
    
    private bool IsLikelyNativeDependency(string filePath)
    {
        try
        {
            // Simple heuristic: try to load as managed assembly
            using (var stream = File.OpenRead(filePath))
            {
                var buffer = new byte[2];
                if (stream.Read(buffer, 0, 2) == 2)
                {
                    // Check for MZ header
                    if (buffer[0] == 0x4D && buffer[1] == 0x5A)
                    {
                        // It's a PE file, but we can't easily determine if it's managed
                        // without more complex parsing
                        return !filePath.Contains("System.", StringComparison.OrdinalIgnoreCase) &&
                               !filePath.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch
        {
            // If we can't read it, assume it might be native
        }
        
        return true;
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
}