using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;

namespace SdkMigrator.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private int _selectedTabIndex;
    private readonly Lazy<MigrationViewModel> _migrationViewModel;
    private readonly Lazy<RollbackViewModel> _rollbackViewModel;
    private readonly Lazy<AnalysisViewModel> _analysisViewModel;
    private readonly Lazy<CleanDepsViewModel> _cleanDepsViewModel;
    private readonly Lazy<CleanCpmViewModel> _cleanCpmViewModel;

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
    }

    public MainViewModel(
        Lazy<MigrationViewModel> migrationViewModel,
        Lazy<RollbackViewModel> rollbackViewModel,
        Lazy<AnalysisViewModel> analysisViewModel,
        Lazy<CleanDepsViewModel> cleanDepsViewModel,
        Lazy<CleanCpmViewModel> cleanCpmViewModel)
    {
        _migrationViewModel = migrationViewModel;
        _rollbackViewModel = rollbackViewModel;
        _analysisViewModel = analysisViewModel;
        _cleanDepsViewModel = cleanDepsViewModel;
        _cleanCpmViewModel = cleanCpmViewModel;
        
        // Initialize with Migration view
        _currentViewModel = _migrationViewModel.Value;
        
        // Subscribe to tab changes
        this.WhenAnyValue(x => x.SelectedTabIndex)
            .Subscribe(UpdateCurrentViewModel)
            .DisposeWith(Disposables);
    }

    private void UpdateCurrentViewModel(int index)
    {
        // Dispose previous ViewModel if it's disposable
        if (CurrentViewModel is IDisposable disposable && CurrentViewModel != GetViewModelForIndex(index))
        {
            disposable.Dispose();
        }
        
        CurrentViewModel = GetViewModelForIndex(index);
    }
    
    private ViewModelBase GetViewModelForIndex(int index) => index switch
    {
        0 => _migrationViewModel.Value,
        1 => _rollbackViewModel.Value,
        2 => _analysisViewModel.Value,
        3 => _cleanDepsViewModel.Value,
        4 => _cleanCpmViewModel.Value,
        _ => _migrationViewModel.Value
    };
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose all ViewModels if they were created
            if (_migrationViewModel.IsValueCreated) (_migrationViewModel.Value as IDisposable)?.Dispose();
            if (_rollbackViewModel.IsValueCreated) (_rollbackViewModel.Value as IDisposable)?.Dispose();
            if (_analysisViewModel.IsValueCreated) (_analysisViewModel.Value as IDisposable)?.Dispose();
            if (_cleanDepsViewModel.IsValueCreated) (_cleanDepsViewModel.Value as IDisposable)?.Dispose();
            if (_cleanCpmViewModel.IsValueCreated) (_cleanCpmViewModel.Value as IDisposable)?.Dispose();
        }
        
        base.Dispose(disposing);
    }
}