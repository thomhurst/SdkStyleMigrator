using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace SdkMigrator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Console.WriteLine("MainWindow constructor called");
        InitializeComponent();
        Console.WriteLine("MainWindow InitializeComponent completed");
        
        // Force opaque background for WSL
        this.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        this.Background = Avalonia.Media.Brushes.White;
        this.SystemDecorations = SystemDecorations.Full;
        
        // Force render (commented out as Renderer is not directly accessible)
        // this.Renderer?.Paint(new Avalonia.Rect(0, 0, 1200, 800));
        
        // Ensure window is fully initialized
        Opened += (_, _) => 
        {
            Console.WriteLine("MainWindow Opened event fired");
            // Force repaint
            this.InvalidateVisual();
            
            // Try to force the background again
            var canvas = this.Content as Canvas;
            if (canvas != null && canvas.Children.Count > 0)
            {
                var rect = canvas.Children[0] as Rectangle;
                if (rect != null)
                {
                    rect.Fill = Avalonia.Media.Brushes.White;
                    rect.InvalidateVisual();
                }
            }
        };
    }
}