using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Drilling.Common.Managers;
using Drilling.UI.Menu.Menus;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CPicoMotorMonitorView : UserControl
{
    public CPicoMotorMonitorView()
    {
        InitializeComponent();
    }

    private void PicoMonitorPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var editor = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
        if (editor is null)
        {
            EndAllValueEdits();
            return;
        }

        if (editor.IsReadOnly)
        {
            if (e.ClickCount >= 2)
            {
                EndAllValueEdits(editor);
                editor.IsReadOnly = false;
                editor.Cursor = Cursors.IBeam;
                editor.Focus();
                editor.SelectAll();
            }
            else
            {
                editor.SelectionLength = 0;
                Keyboard.ClearFocus();
            }

            e.Handled = true;
        }
    }

    private void PicoValueEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox editor)
        {
            editor.IsReadOnly = true;
            editor.Cursor = Cursors.Arrow;
            editor.SelectionLength = 0;
        }
    }

    private void EndAllValueEdits(TextBox? except = null)
    {
        foreach (var editor in FindVisualChildren<TextBox>(this))
        {
            if (ReferenceEquals(editor, except))
            {
                continue;
            }

            editor.IsReadOnly = true;
            editor.Cursor = Cursors.Arrow;
            editor.SelectionLength = 0;
        }

        if (except is null)
        {
            Keyboard.ClearFocus();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void PicoJogButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: { } parameter } button
            && DataContext is CRootView rootView
            && rootView.CurrentScreen.Monitor is { } monitor)
        {
            Mouse.Capture(button);
            monitor.PicoJogStartCommand.Execute(parameter);
            e.Handled = true;
        }
    }

    private void PicoJogButtonPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button)
        {
            button.ReleaseMouseCapture();
        }

        StopPicoJog();
        e.Handled = true;
    }

    private void PicoJogButtonMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button && button.IsMouseCaptured)
        {
            button.ReleaseMouseCapture();
            StopPicoJog();
        }
    }

    private void StopPicoJog()
    {
        if (DataContext is CRootView rootView
            && rootView.CurrentScreen.Monitor is { } monitor)
        {
            monitor.PicoJogStopCommand.Execute(null);
        }
    }

    private void OpenPicoValueInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ST_MONITOR_PARAMETER_ROW row } button)
        {
            return;
        }

        e.Handled = true;
        var isCount = row.Parameter.Equals("Set Count", StringComparison.OrdinalIgnoreCase);
        var nonNegative = row.Parameter is "Set Velocity" or "Set Acceleration" or "Relative Move" || isCount;
        var dialog = new CValueInputDialog(
            row.Parameter,
            row.Value,
            isCount ? EN_RECIPE_DATA_TYPE.Int : EN_RECIPE_DATA_TYPE.Double,
            nonNegative ? (isCount ? 1 : 0) : 0,
            nonNegative ? int.MaxValue : 0)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }
}
