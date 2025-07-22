using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SdkMigrator.Views;

public partial class TargetSelectionDialog : Window
{
    public TargetSelectionDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}