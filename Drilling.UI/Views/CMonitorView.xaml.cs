using System.Windows;
using System.Windows.Controls;
using Drilling.Common.Managers;
using Drilling.UI.Menu.Menus;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CMonitorView : UserControl
{
    public CMonitorView()
    {
        InitializeComponent();
    }

    private void OpenPowerMeterSettingInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ST_PWM_SETTING_ROW row } || row.UsesSelectionEditor)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(row.Parameter, row.Value, row.DataType, 0, 0)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
        }
    }

    private void OpenLaserSettingInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ST_MONITOR_LASER_CONTROL_ROW row } button || !row.CanCommand)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(
            row.CurrentLabel,
            row.Setting.Value,
            EN_RECIPE_DATA_TYPE.Int,
            0,
            int.MaxValue)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Setting.Value = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }

    private void OpenChillerTemperatureInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ST_MONITOR_PARAMETER_ROW row } button)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(
            row.Parameter,
            row.Value,
            EN_RECIPE_DATA_TYPE.Double,
            0,
            0)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }

    private void OpenAttenuatorPositionInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ST_MONITOR_PARAMETER_ROW row } button)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(
            row.Parameter,
            row.Value,
            EN_RECIPE_DATA_TYPE.Double,
            0,
            0)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }

    private void OpenBetTableInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                DataContext: ST_MONITOR_BET_TABLE_ROW row,
                Tag: string field
            } button)
        {
            return;
        }

        e.Handled = true;
        string EvaluateFieldSwitch1()
        {
            var switchValue = field;
            switch (switchValue)
            {
                case "DESCRIPTION":
                    return row.Description;
                case "DIV":
                    return row.Div;
                case "MAG":
                    return row.Mag;
                default:
                    return "";
            }
        }

        var currentValue = EvaluateFieldSwitch1();
        var dataType = field == "DESCRIPTION"
            ? EN_RECIPE_DATA_TYPE.String
            : EN_RECIPE_DATA_TYPE.Int;
        var max = field is "DIV" or "MAG" ? 4500 : 0;
        var dialog = new CValueInputDialog(field, currentValue, dataType, 0, max)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        switch (field)
        {
            case "DESCRIPTION": row.Description = dialog.ResultValue; break;
            case "DIV": row.Div = dialog.ResultValue; break;
            case "MAG": row.Mag = dialog.ResultValue; break;
        }
        button.Content = dialog.ResultValue;
    }

    private void OpenBetPositionInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ST_MONITOR_PARAMETER_ROW row } button)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(
            row.Parameter,
            row.Value,
            EN_RECIPE_DATA_TYPE.Int,
            0,
            4500)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.Value = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }

    private void OpenMelsecWriteInput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ST_MONITOR_MELSEC_ROW row } button || !row.CanWrite || row.UsesWriteSelection)
        {
            return;
        }

        e.Handled = true;
        var dialog = new CValueInputDialog(
            $"{row.Id} Write Value",
            row.WriteValue,
            row.WriteInputType,
            0,
            0)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            row.WriteValue = dialog.ResultValue;
            button.Content = dialog.ResultValue;
        }
    }
}
