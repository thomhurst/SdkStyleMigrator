using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SdkMigrator.ViewModels;
using System;

namespace SdkMigrator;

/// <summary>
/// ViewLocator implementation following Avalonia best practices.
/// Automatically resolves Views based on ViewModel type names.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            try
            {
                return (Control)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                // Log error and return fallback
                System.Diagnostics.Debug.WriteLine($"Failed to create view for {name}: {ex.Message}");
                return new TextBlock { Text = $"Error creating view: {name}" };
            }
        }

        return new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}