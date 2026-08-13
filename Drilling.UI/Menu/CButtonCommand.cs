using System.Windows.Input;

namespace Drilling.UI.Menu;

public sealed class CButtonCommand(
    Action<object?> execute,
    Predicate<object?>? canExecute = null) : ICommand
{
    public static CButtonCommand NoOp { get; } = new(DoNothing);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        execute(parameter);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DoNothing(object? parameter)
    {
    }
}

