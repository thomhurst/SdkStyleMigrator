using SdkMigrator.Models;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Service for handling user interactions during migration
/// </summary>
public interface IUserInteractionService
{
    /// <summary>
    /// Prompts the user to select which imports to keep during migration
    /// </summary>
    Task<ImportScanResult> SelectImportsAsync(
        ImportScanResult scanResult, 
        ImportSelectionOptions options,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Prompts the user to select which targets to keep during migration
    /// </summary>
    Task<TargetScanResult> SelectTargetsAsync(
        TargetScanResult scanResult,
        TargetSelectionOptions options,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Asks user a yes/no question
    /// </summary>
    Task<bool> AskYesNoAsync(string question, bool defaultValue = true);
    
    /// <summary>
    /// Shows a list of options and gets user selection
    /// </summary>
    Task<int> SelectFromListAsync(string prompt, List<string> options);
    
    /// <summary>
    /// Shows information to the user
    /// </summary>
    void ShowInformation(string message);
    
    /// <summary>
    /// Shows a warning to the user
    /// </summary>
    void ShowWarning(string message);
}