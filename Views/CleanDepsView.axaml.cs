using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SdkMigrator.Views;

public partial class CleanDepsView : UserControl
{
    public CleanDepsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}