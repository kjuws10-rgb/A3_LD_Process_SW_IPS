using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drilling.Common.Managers;
using Drilling.UI.Menu.Menus;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CManualView : UserControl
{
    public CManualView()
    {
        InitializeComponent();
    }

    private void OpenTextInput(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = true;
        var dataType = textBox.Tag as string == "Int"
            ? EN_RECIPE_DATA_TYPE.Int
            : EN_RECIPE_DATA_TYPE.Double;
        var prompt = textBox.ToolTip?.ToString() ?? "Value";
        var dialog = new CValueInputDialog(prompt, textBox.Text, dataType, 0, 0)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;
        textBox.Text = dialog.ResultValue;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void OpenManualParameterInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ST_MANUAL_PARAMETER row }) return;
        e.Handled = true;
        var dialog = new CValueInputDialog(row.Parameter, row.Value, row.DataType, row.Min, row.Max)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true) row.Value = dialog.ResultValue;
    }

    private void OpenCommandStateInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ST_MANUAL_COMMAND_STATE row } || !row.IsEditable) return;
        e.Handled = true;
        var dataType = row.Name.Contains("Count", StringComparison.OrdinalIgnoreCase)
            ? EN_RECIPE_DATA_TYPE.Int
            : EN_RECIPE_DATA_TYPE.Double;
        var dialog = new CValueInputDialog(row.Name, row.Value, dataType, 0, 0)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true) row.Value = dialog.ResultValue;
    }

    private void OpenStageTargetInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ST_MANUAL_STAGE_AXIS row }) return;
        e.Handled = true;
        var dialog = new CValueInputDialog($"{row.DisplayAxis} Target Position", row.TargetPosition, EN_RECIPE_DATA_TYPE.Double, 0, 0)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true) row.TargetPosition = dialog.ResultValue;
    }
}
