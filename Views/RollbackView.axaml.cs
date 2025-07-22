using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SdkMigrator.Views;

public partial class RollbackView : UserControl
{
    public RollbackView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}