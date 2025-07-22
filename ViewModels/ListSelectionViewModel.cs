using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Controls;
using ReactiveUI;

namespace SdkMigrator.ViewModels;

public class ListSelectionViewModel : ViewModelBase
{
    private string _prompt;
    private int _selectedIndex;

    public string Prompt
    {
        get => _prompt;
        set => this.RaiseAndSetIfChanged(ref _prompt, value);
    }

    public ObservableCollection<string> Options { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedIndex, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public ListSelectionViewModel(string prompt, List<string> options)
    {
        _prompt = prompt;
        Options = new ObservableCollection<string>(options);
        _selectedIndex = 0;
        
        OkCommand = ReactiveCommand.Create(OnOk);
        CancelCommand = ReactiveCommand.Create(OnCancel);
    }

    private void OnOk()
    {
        // The dialog will be closed by the view
    }

    private void OnCancel()
    {
        SelectedIndex = -1;
        // The dialog will be closed by the view
    }
}