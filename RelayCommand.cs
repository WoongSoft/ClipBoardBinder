using System.Windows.Input;

namespace ID648;

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute,Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this,EventArgs.Empty);
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?,bool>? _canExecute;

    public RelayCommand(Action<T?> execute,Func<T?,bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if(!TryGetParameter(parameter,out var value))
        {
            return false;
        }

        return _canExecute?.Invoke(value) ?? true;
    }

    public void Execute(object? parameter)
    {
        if(!TryGetParameter(parameter,out var value))
        {
            return;
        }

        _execute(value);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this,EventArgs.Empty);
    }

    private static bool TryGetParameter(object? parameter,out T? value)
    {
        if(parameter is null)
        {
            value = default;
            return true;
        }

        if(parameter is T typedValue)
        {
            value = typedValue;
            return true;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if(targetType.IsEnum && parameter is string enumName && Enum.TryParse(targetType,enumName,true,out var enumValue))
        {
            value = (T)enumValue;
            return true;
        }

        value = default;
        return false;
    }
}
