using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Controls;
using ReactiveUI;
using SdkMigrator.Models;

namespace SdkMigrator.ViewModels;

public class TargetSelectionViewModel : ViewModelBase
{
    private TargetScanResult _scanResult;
    private ObservableCollection<TargetGroupViewModel> _targetGroups = new();
    private int _selectedCount;

    public TargetScanResult ScanResult => _scanResult;
    public ObservableCollection<TargetGroupViewModel> TargetGroups => _targetGroups;

    public int SelectedCount
    {
        get => _selectedCount;
        set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public TargetSelectionViewModel(TargetScanResult scanResult)
    {
        _scanResult = scanResult;
        
        // Create view models for each target group
        foreach (var group in scanResult.TargetGroups)
        {
            TargetGroups.Add(new TargetGroupViewModel(group, UpdateSelectedCount));
        }
        
        UpdateSelectedCount();
        
        OkCommand = ReactiveCommand.Create(OnOk);
        CancelCommand = ReactiveCommand.Create(OnCancel);
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = _scanResult.TargetGroups.Sum(g => g.Targets.Count(t => t.UserDecision));
    }

    private void OnOk()
    {
        // The dialog will be closed by the view
    }

    private void OnCancel()
    {
        // Reset all decisions to default
        foreach (var group in _scanResult.TargetGroups)
        {
            foreach (var target in group.Targets)
            {
                target.UserDecision = !target.IsSystemTarget;
            }
        }
        // The dialog will be closed by the view
    }
}

public class TargetGroupViewModel : ViewModelBase
{
    private readonly TargetGroup _group;
    private readonly Action _updateCallback;
    private bool _allSelected;

    public string Category => _group.Category;
    public ObservableCollection<ProjectTargetInfo> Targets { get; }

    public bool AllSelected
    {
        get => _allSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _allSelected, value);
            if (_allSelected != Targets.All(t => t.UserDecision))
            {
                foreach (var target in Targets)
                {
                    target.UserDecision = value;
                }
                _updateCallback?.Invoke();
            }
        }
    }

    public TargetGroupViewModel(TargetGroup group, Action updateCallback)
    {
        _group = group;
        Targets = new ObservableCollection<ProjectTargetInfo>(group.Targets);
        _updateCallback = updateCallback;
        
        // Subscribe to changes in individual targets
        foreach (var target in Targets)
        {
            target.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ProjectTargetInfo.UserDecision))
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
        var newState = Targets.All(t => t.UserDecision);
        if (_allSelected != newState)
        {
            _allSelected = newState;
            this.RaisePropertyChanged(nameof(AllSelected));
        }
    }
}