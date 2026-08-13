using System.Windows;
using System.Windows.Controls;
using Drilling.Common.Managers;
using Drilling.UI.Menu.Menus;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CRecipeView : UserControl
{
    public CRecipeView()
    {
        InitializeComponent();
    }

    private void OpenManagedItemInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ST_RECIPE_MANAGED_ITEM item }) return;
        e.Handled = true;
        var dialog = new CValueInputDialog(item.Item, item.Value, item.DataType, item.Min, item.Max)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true) item.Value = dialog.ResultValue;
    }

    private void OpenCellOverviewInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ST_RECIPE_CELL_OVERVIEW_ROW row,
                Tag: string field
            }) return;
        e.Handled = true;
        var value = field switch
        {
            "FirstX" => row.FirstX, "FirstY" => row.FirstY, "Rotation" => row.Rotation,
            "CountX" => row.CountX, "CountY" => row.CountY, "PitchX" => row.PitchX,
            "PitchY" => row.PitchY, "PixelSize" => row.PixelSize, _ => ""
        };
        var type = field is "CountX" or "CountY" ? EN_RECIPE_DATA_TYPE.Int : EN_RECIPE_DATA_TYPE.Double;
        var dialog = new CValueInputDialog(field, value, type, 0, 0) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        switch (field)
        {
            case "FirstX": row.FirstX = dialog.ResultValue; break;
            case "FirstY": row.FirstY = dialog.ResultValue; break;
            case "Rotation": row.Rotation = dialog.ResultValue; break;
            case "CountX": row.CountX = dialog.ResultValue; break;
            case "CountY": row.CountY = dialog.ResultValue; break;
            case "PitchX": row.PitchX = dialog.ResultValue; break;
            case "PitchY": row.PitchY = dialog.ResultValue; break;
            case "PixelSize": row.PixelSize = dialog.ResultValue; break;
        }
    }

    private void OpenHoleOffsetInput(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ST_RECIPE_HOLE_ROW row } element) return;
        var field = element.Uid;
        e.Handled = true;
        var value = field == "OffsetX" ? row.OffsetX : row.OffsetY;
        var dialog = new CValueInputDialog(field, value, EN_RECIPE_DATA_TYPE.Double, 0, 0)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;
        if (field == "OffsetX") row.OffsetX = dialog.ResultValue;
        else row.OffsetY = dialog.ResultValue;
    }
}
