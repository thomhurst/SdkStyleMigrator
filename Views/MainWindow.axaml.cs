using Avalonia.Controls;

namespace SdkMigrator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Ensure window is fully initialized
        Opened += (_, _) => { };
    }
}