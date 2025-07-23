using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class ConsoleUserInteractionService : IUserInteractionService
{
    private readonly ILogger<ConsoleUserInteractionService> _logger;

    public ConsoleUserInteractionService(ILogger<ConsoleUserInteractionService> logger)
    {
        _logger = logger;
        _logger.LogInformation("ConsoleUserInteractionService created - THIS SHOULD NOT BE USED IN UI MODE");
    }

    public async Task<ImportScanResult> SelectImportsAsync(
        ImportScanResult scanResult, 
        ImportSelectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.InteractiveMode || !scanResult.HasCustomImports)
        {
            // In non-interactive mode or if no custom imports, return as-is
            return scanResult;
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("PROJECT IMPORT SELECTION");
        Console.WriteLine("========================================");
        Console.WriteLine($"Found {scanResult.TotalImports} imports across {scanResult.ImportGroups.Count} files/packages");
        Console.WriteLine();

        // First, ask if user wants to review imports
        if (!await AskYesNoAsync("Would you like to review and select which imports to keep?", false))
        {
            ShowInformation("Keeping all imports by default.");
            return scanResult;
        }

        // Group imports by category for easier review
        var categorizedGroups = scanResult.ImportGroups
            .GroupBy(g => g.Category)
            .OrderBy(g => g.Key);

        foreach (var categoryGroup in categorizedGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            Console.WriteLine();
            Console.WriteLine($"Category: {categoryGroup.Key}");
            Console.WriteLine(new string('-', 40));

            foreach (var importGroup in categoryGroup)
            {
                // Skip system imports
                if (importGroup.Imports.All(i => i.IsSystemImport))
                    continue;

                Console.WriteLine($"\nImport Group: {importGroup.ImportFile}");
                Console.WriteLine($"Contains {importGroup.TotalCount} import(s)");

                // Show sample import details
                var firstImport = importGroup.Imports.First();
                if (!string.IsNullOrEmpty(firstImport.Condition))
                {
                    Console.WriteLine($"  Condition: {firstImport.Condition}");
                }

                // Ask user decision for the entire group
                var actionOptions = new List<string>
                {
                    "Keep all imports in this group",
                    "Remove all imports in this group",
                    "Review each import individually"
                };

                var choice = await SelectFromListAsync("Select action:", actionOptions);

                switch (choice)
                {
                    case 0: // Keep all
                        foreach (var import in importGroup.Imports)
                        {
                            import.UserDecision = true;
                        }
                        break;
                        
                    case 1: // Remove all
                        foreach (var import in importGroup.Imports)
                        {
                            import.UserDecision = false;
                        }
                        ShowWarning($"Will remove all imports from {importGroup.ImportFile}");
                        break;
                        
                    case 2: // Review individually
                        foreach (var import in importGroup.Imports)
                        {
                            Console.WriteLine($"\n  Import: {import.ImportPath}");
                            if (!string.IsNullOrEmpty(import.Condition))
                            {
                                Console.WriteLine($"  Condition: {import.Condition}");
                            }
                            if (!string.IsNullOrEmpty(import.Label))
                            {
                                Console.WriteLine($"  Label: {import.Label}");
                            }
                            
                            import.UserDecision = await AskYesNoAsync("  Keep this import?", true);
                        }
                        break;
                }
            }
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("IMPORT SELECTION SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Total imports: {scanResult.TotalImports}");
        Console.WriteLine($"Imports to keep: {scanResult.SelectedImports}");
        Console.WriteLine($"Imports to remove: {scanResult.TotalImports - scanResult.SelectedImports}");
        Console.WriteLine();

        return scanResult;
    }

    public async Task<bool> AskYesNoAsync(string question, bool defaultValue = true)
    {
        var defaultText = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{question} [{defaultText}]: ");
        
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        if (string.IsNullOrEmpty(response))
        {
            return defaultValue;
        }
        
        return response == "y" || response == "yes";
    }

    public async Task<int> SelectFromListAsync(string prompt, List<string> options)
    {
        Console.WriteLine(prompt);
        for (int i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {options[i]}");
        }
        
        while (true)
        {
            Console.Write("Enter your choice (1-{0}): ", options.Count);
            var input = Console.ReadLine();
            
            if (int.TryParse(input, out var choice) && choice >= 1 && choice <= options.Count)
            {
                return choice - 1;
            }
            
            Console.WriteLine("Invalid choice. Please try again.");
        }
    }

    public void ShowInformation(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"ℹ {message}");
        Console.ForegroundColor = oldColor;
    }

    public void ShowWarning(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {message}");
        Console.ForegroundColor = oldColor;
    }

    public async Task<TargetScanResult> SelectTargetsAsync(
        TargetScanResult scanResult,
        TargetSelectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.InteractiveMode || !scanResult.HasCustomTargets)
        {
            // In non-interactive mode or if no custom targets, return as-is
            return scanResult;
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("PROJECT TARGET SELECTION");
        Console.WriteLine("========================================");
        Console.WriteLine($"Found {scanResult.TotalTargets} targets across {scanResult.TargetGroups.Count} categories");
        Console.WriteLine();

        // First, ask if user wants to review targets
        if (!await AskYesNoAsync("Would you like to review and select which targets to keep?", false))
        {
            ShowInformation("Keeping all targets by default.");
            return scanResult;
        }

        // Group targets by category for easier review
        var categorizedGroups = scanResult.TargetGroups
            .OrderBy(g => g.Category);

        foreach (var targetGroup in categorizedGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Skip system targets
            if (targetGroup.Targets.All(t => t.IsSystemTarget))
                continue;

            Console.WriteLine();
            Console.WriteLine($"Category: {targetGroup.Category}");
            Console.WriteLine(new string('-', 40));
            Console.WriteLine($"Contains {targetGroup.TotalCount} target(s)");

            // Show target names in this group
            var targetNames = targetGroup.Targets.Select(t => t.TargetName).Distinct().ToList();
            if (targetNames.Count <= 5)
            {
                Console.WriteLine($"Targets: {string.Join(", ", targetNames)}");
            }
            else
            {
                Console.WriteLine($"Targets: {string.Join(", ", targetNames.Take(3))} and {targetNames.Count - 3} more...");
            }

            // Ask user decision for the entire group
            var actionOptions = new List<string>
            {
                "Keep all targets in this category",
                "Remove all targets in this category",
                "Review each target individually"
            };

            var choice = await SelectFromListAsync("Select action:", actionOptions);

            switch (choice)
            {
                case 0: // Keep all
                    foreach (var target in targetGroup.Targets)
                    {
                        target.UserDecision = true;
                    }
                    break;
                    
                case 1: // Remove all
                    foreach (var target in targetGroup.Targets)
                    {
                        target.UserDecision = false;
                    }
                    ShowWarning($"Will remove all {targetGroup.Category} targets");
                    break;
                    
                case 2: // Review individually
                    foreach (var target in targetGroup.Targets)
                    {
                        Console.WriteLine($"\n  Target: {target.TargetName}");
                        Console.WriteLine($"  {target.Description}");
                        
                        if (!string.IsNullOrEmpty(target.Condition))
                        {
                            Console.WriteLine($"  Condition: {target.Condition}");
                        }
                        if (!string.IsNullOrEmpty(target.DependsOnTargets))
                        {
                            Console.WriteLine($"  DependsOn: {target.DependsOnTargets}");
                        }
                        if (!string.IsNullOrEmpty(target.BeforeTargets))
                        {
                            Console.WriteLine($"  Before: {target.BeforeTargets}");
                        }
                        if (!string.IsNullOrEmpty(target.AfterTargets))
                        {
                            Console.WriteLine($"  After: {target.AfterTargets}");
                        }
                        
                        target.UserDecision = await AskYesNoAsync("  Keep this target?", true);
                    }
                    break;
            }
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("TARGET SELECTION SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Total targets: {scanResult.TotalTargets}");
        Console.WriteLine($"Targets to keep: {scanResult.SelectedTargets}");
        Console.WriteLine($"Targets to remove: {scanResult.TotalTargets - scanResult.SelectedTargets}");
        Console.WriteLine();

        return scanResult;
    }
}