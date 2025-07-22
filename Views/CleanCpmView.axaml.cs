using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SdkMigrator.Views;

public partial class CleanCpmView : UserControl
{
    public CleanCpmView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}