using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Drilling.UI.Menu;

public static class CButtonCommandBehavior
{
    private static readonly Dictionary<CButtonCommand, HashSet<UIElement>> RegisteredTargets =
        new Dictionary<CButtonCommand, HashSet<UIElement>>();

    private static readonly DependencyPropertyDescriptor CommandParameterDescriptor =
        DependencyPropertyDescriptor.FromProperty(
            ButtonBase.CommandParameterProperty,
            typeof(ButtonBase));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(CButtonCommand),
        typeof(CButtonCommandBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static CButtonCommand? GetCommand(DependencyObject target)
    {
        return (CButtonCommand?)target.GetValue(CommandProperty);
    }

    public static void SetCommand(DependencyObject target, CButtonCommand? value)
    {
        target.SetValue(CommandProperty, value);
    }

    private static void OnCommandChanged(
        DependencyObject target,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (target is not UIElement element)
        {
            return;
        }

        if (element is ButtonBase button)
        {
            button.Click -= Button_Click;
            CommandParameterDescriptor.RemoveValueChanged(button, ButtonCommandParameterChanged);
        }
        else
        {
            element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
        }

        if (eventArgs.OldValue is CButtonCommand oldCommand)
        {
            UnregisterTarget(oldCommand, element);
        }

        if (eventArgs.NewValue is CButtonCommand newCommand)
        {
            RegisterTarget(newCommand, element);
            if (element is ButtonBase commandButton)
            {
                commandButton.Click += Button_Click;
                CommandParameterDescriptor.AddValueChanged(commandButton, ButtonCommandParameterChanged);
            }
            else
            {
                element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            }

            UpdateTargetState(element, newCommand);
        }
        else
        {
            element.SetCurrentValue(UIElement.IsEnabledProperty, true);
        }
    }

    private static void RegisterTarget(CButtonCommand command, UIElement element)
    {
        if (!RegisteredTargets.TryGetValue(command, out HashSet<UIElement>? targets))
        {
            targets = new HashSet<UIElement>();
            RegisteredTargets.Add(command, targets);
            command.CanExecuteChanged += Command_CanExecuteChanged;
        }

        targets.Add(element);
    }

    private static void UnregisterTarget(CButtonCommand command, UIElement element)
    {
        if (!RegisteredTargets.TryGetValue(command, out HashSet<UIElement>? targets))
        {
            return;
        }

        targets.Remove(element);
        if (targets.Count == 0)
        {
            command.CanExecuteChanged -= Command_CanExecuteChanged;
            RegisteredTargets.Remove(command);
        }
    }

    private static void Element_MouseLeftButtonDown(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        CButtonCommand? command = GetCommand(element);
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void Button_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ButtonBase button)
        {
            return;
        }

        CButtonCommand? command = GetCommand(button);
        object? parameter = button.CommandParameter;
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static void Command_CanExecuteChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not CButtonCommand command ||
            !RegisteredTargets.TryGetValue(command, out HashSet<UIElement>? targets))
        {
            return;
        }

        foreach (UIElement element in targets)
        {
            UpdateTargetState(element, command);
        }
    }

    private static void ButtonCommandParameterChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not ButtonBase button)
        {
            return;
        }

        CButtonCommand? command = GetCommand(button);
        if (command is not null)
        {
            UpdateTargetState(button, command);
        }
    }

    private static void UpdateTargetState(UIElement element, CButtonCommand command)
    {
        object? parameter = element is ButtonBase button
            ? button.CommandParameter
            : null;
        bool canExecute = command.CanExecute(parameter);
        element.SetCurrentValue(UIElement.IsEnabledProperty, canExecute);
    }
}
