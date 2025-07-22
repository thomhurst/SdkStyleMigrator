using System;
using Avalonia.Controls;

namespace SdkMigrator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Console.WriteLine("MainWindow constructor called");
        InitializeComponent();
        Console.WriteLine("MainWindow InitializeComponent completed");
        
        // Force opaque background for WSL
        this.TransparencyLevelHint = WindowTransparencyLevel.None;
        this.Background = Avalonia.Media.Brushes.White;
        
        // Ensure window is fully initialized
        Opened += (_, _) => 
        {
            Console.WriteLine("MainWindow Opened event fired");
            // Force repaint
            this.InvalidateVisual();
        };
    }
}