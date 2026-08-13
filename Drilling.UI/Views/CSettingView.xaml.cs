using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drilling.Common.Managers;
using Drilling.UI.Menu.Menus;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CSettingView : UserControl
{
    public CSettingView()
    {
        InitializeComponent();
    }

    private void CommitSettingGridEdits(
        object sender,
        RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        Keyboard.ClearFocus();
        CommitGridEdit(ParameterGrid);
        CommitGridEdit(InterfaceGrid);
    }

    private static void CommitGridEdit(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void OpenParameterValueInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ST_SYSTEM_PARAMETER_ROW row }) return;

        e.Handled = true;

        var dialog = new CValueInputDialog(
            row.Parameter,
            row.Value,
            row.DataType,
            row.Min,
            row.Max)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
            CommitGridEdit(ParameterGrid);
        }
    }

    private void OpenInterfaceValueInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ST_SETTING_INTERFACE_ROW row,
                Tag: string field
            }) return;

        e.Handled = true;
        string EvaluateFieldSwitch1()
        {
            var switchValue = field;
            switch (switchValue)
            {
                case "DEVICE":
                    return row.Device;
                case "NUMBER":
                    return row.Number;
                case "NICKNAME":
                    return row.NickName;
                case "SYSTEM_SECTION":
                    return row.SystemSection;
                case "ARG1":
                    return row.Arg1;
                case "ARG2":
                    return row.Arg2;
                case "ARG3":
                    return row.Arg3;
                case "ARG4":
                    return row.Arg4;
                case "ARG5":
                    return row.Arg5;
                default:
                    return "";
            }
        }

        var currentValue = EvaluateFieldSwitch1();
        var dataType = field == "NUMBER" ? EN_RECIPE_DATA_TYPE.Int : EN_RECIPE_DATA_TYPE.String;
        var dialog = new CValueInputDialog(field, currentValue, dataType, 0, int.MaxValue)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;
        switch (field)
        {
            case "DEVICE": row.Device = dialog.ResultValue; break;
            case "NUMBER": row.Number = dialog.ResultValue; break;
            case "NICKNAME": row.NickName = dialog.ResultValue; break;
            case "SYSTEM_SECTION": row.SystemSection = dialog.ResultValue; break;
            case "ARG1": row.Arg1 = dialog.ResultValue; break;
            case "ARG2": row.Arg2 = dialog.ResultValue; break;
            case "ARG3": row.Arg3 = dialog.ResultValue; break;
            case "ARG4": row.Arg4 = dialog.ResultValue; break;
            case "ARG5": row.Arg5 = dialog.ResultValue; break;
        }
        CommitGridEdit(InterfaceGrid);
    }
}
