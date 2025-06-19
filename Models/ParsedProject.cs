using Microsoft.Build.Evaluation;

namespace SdkMigrator.Models;

public class ParsedProject
{
    public Project Project { get; set; } = null!;
    public bool LoadedWithDefensiveParsing { get; set; }
    public List<string> RemovedImports { get; set; } = new();
}