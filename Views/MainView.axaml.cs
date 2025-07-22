using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SdkMigrator.ViewModels;

namespace SdkMigrator.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        
        if (Design.IsDesignMode)
            return;
            
        DataContext = App.Services?.GetRequiredService<MainViewModel>();
    }
}