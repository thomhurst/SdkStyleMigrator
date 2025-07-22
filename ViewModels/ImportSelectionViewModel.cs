using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using ReactiveUI;
using SdkMigrator.Models;

namespace SdkMigrator.ViewModels;

public class ImportSelectionViewModel : ViewModelBase
{
    private ImportScanResult _scanResult;
    private ObservableCollection<ImportGroupViewModel> _importGroups = new();
    private int _selectedCount;

    public ImportScanResult ScanResult => _scanResult;
    public ObservableCollection<ImportGroupViewModel> ImportGroups => _importGroups;

    public int SelectedCount
    {
        get => _selectedCount;
        set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public ImportSelectionViewModel(ImportScanResult scanResult)
    {
        _scanResult = scanResult;
        
        // Create view models for each import group
        foreach (var group in scanResult.ImportGroups)
        {
            ImportGroups.Add(new ImportGroupViewModel(group, UpdateSelectedCount));
        }
        
        UpdateSelectedCount();
        
        OkCommand = ReactiveCommand.Create(OnOk);
        CancelCommand = ReactiveCommand.Create(OnCancel);
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = _scanResult.ImportGroups.Sum(g => g.Imports.Count(i => i.UserDecision));
    }

    private void OnOk()
    {
        // The dialog will be closed by the view
    }

    private void OnCancel()
    {
        // Reset all decisions to default
        foreach (var group in _scanResult.ImportGroups)
        {
            foreach (var import in group.Imports)
            {
                import.UserDecision = !import.IsSystemImport;
            }
        }
        // The dialog will be closed by the view
    }
}

public class ImportGroupViewModel : ViewModelBase
{
    private readonly Action _updateCallback;
    private bool _allSelected;

    public string GroupName { get; }
    public ObservableCollection<ProjectImportInfo> Imports { get; }

    public bool AllSelected
    {
        get => _allSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _allSelected, value);
            if (_allSelected != Imports.All(i => i.UserDecision))
            {
                foreach (var import in Imports)
                {
                    import.UserDecision = value;
                }
                _updateCallback?.Invoke();
            }
        }
    }

    public ImportGroupViewModel(ImportGroup group, Action updateCallback)
    {
        GroupName = group.ImportFile;
        Imports = new ObservableCollection<ProjectImportInfo>(group.Imports);
        _updateCallback = updateCallback;
        
        // Subscribe to changes in individual imports
        foreach (var import in Imports)
        {
            import.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ProjectImportInfo.UserDecision))
                {
                    _updateCallback?.Invoke();
                    UpdateAllSelectedState();
                }
            };
        }
        
        UpdateAllSelectedState();
    }

    private void UpdateAllSelectedState()
    {
        var newState = Imports.All(i => i.UserDecision);
        if (_allSelected != newState)
        {
            _allSelected = newState;
            this.RaisePropertyChanged(nameof(AllSelected));
        }
    }
}