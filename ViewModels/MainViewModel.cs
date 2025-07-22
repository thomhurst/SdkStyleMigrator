using System.Reactive.Linq;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace SdkMigrator.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private int _selectedTabIndex;
    private readonly IServiceProvider _serviceProvider;

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
            UpdateCurrentViewModel(value);
        }
    }

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        
        // Initialize with Migration view
        _currentViewModel = _serviceProvider.GetRequiredService<MigrationViewModel>();
    }

    private void UpdateCurrentViewModel(int index)
    {
        CurrentViewModel = index switch
        {
            0 => _serviceProvider.GetRequiredService<MigrationViewModel>(),
            1 => _serviceProvider.GetRequiredService<RollbackViewModel>(),
            2 => _serviceProvider.GetRequiredService<AnalysisViewModel>(),
            3 => _serviceProvider.GetRequiredService<CleanDepsViewModel>(),
            4 => _serviceProvider.GetRequiredService<CleanCpmViewModel>(),
            _ => _serviceProvider.GetRequiredService<MigrationViewModel>()
        };
    }
}