using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SdkMigrator.ViewModels;

namespace SdkMigrator.Views;

public partial class MigrationView : UserControl
{
    public MigrationView()
    {
        InitializeComponent();
    }

    private void OnTestClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Console.WriteLine("Test button clicked successfully!");
            Console.WriteLine($"Current time: {DateTime.Now}");
            Console.WriteLine($"Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test button exception: {ex.Message}");
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Console.WriteLine("OnBrowseClick called");
            Console.WriteLine($"DataContext type: {DataContext?.GetType().Name ?? "null"}");
            
            if (DataContext is MigrationViewModel viewModel)
            {
                Console.WriteLine("Calling BrowseDirectoryCommand.Execute");
                viewModel.BrowseDirectoryCommand.Execute(null);
                Console.WriteLine("BrowseDirectoryCommand.Execute completed");
            }
            else
            {
                Console.WriteLine("DataContext is not MigrationViewModel");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OnBrowseClick exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}