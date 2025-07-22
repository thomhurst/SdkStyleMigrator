using System.Reactive.Disposables;
using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;

namespace SdkMigrator.ViewModels;

public abstract class ViewModelBase : ReactiveObject, IDisposable, IValidatableViewModel
{
    protected CompositeDisposable Disposables { get; } = new();
    
    private bool _disposed;
    
    public ValidationContext ValidationContext { get; } = new ValidationContext();
    
    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            Disposables?.Dispose();
        }
        
        _disposed = true;
    }
}