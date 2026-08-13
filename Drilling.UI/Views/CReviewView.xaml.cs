using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Drilling.UI.Menu;
using Drilling.UI.Menu.Menus;

namespace Drilling.UI.Views;

public partial class CReviewView : UserControl
{
    private static readonly Brush SampleSelectedBackground = CreateBrush(0x12, 0x3C, 0x4A);
    private static readonly Brush SampleSelectedBorder = CreateBrush(0x4F, 0xAF, 0xC4);
    private static readonly Brush SampleClearedBackground = CreateBrush(0x20, 0x2A, 0x34);
    private static readonly Brush SampleClearedBorder = CreateBrush(0x3B, 0x4A, 0x5B);
    private readonly HashSet<string> _sampleDragVisitedHoleKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sampleDragChangedHoleKeys = [];
    private CButtonCommand? _sampleDragSelectionCommand;
    private bool _isSampleHoleDragActive;
    private bool _sampleDragUse;

    public CReviewView()
    {
        InitializeComponent();
    }

    private void SampleHoleItemsControl_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var button = FindSampleHoleButton(e.OriginalSource as DependencyObject);
        if (button?.DataContext is not ST_REVIEW_POINT_SELECT_ROW row)
        {
            return;
        }

        _isSampleHoleDragActive = true;
        _sampleDragUse = !row.Use;
        _sampleDragSelectionCommand = row.DragSelectionCommand;
        _sampleDragVisitedHoleKeys.Clear();
        _sampleDragChangedHoleKeys.Clear();
        PaintSampleHole(button, row);
        Mouse.Capture(SampleHoleItemsControl, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void SampleHoleItemsControl_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isSampleHoleDragActive || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var hit = SampleHoleItemsControl.InputHitTest(
            e.GetPosition(SampleHoleItemsControl)) as DependencyObject;
        var button = FindSampleHoleButton(hit);
        if (button?.DataContext is ST_REVIEW_POINT_SELECT_ROW row)
        {
            PaintSampleHole(button, row);
        }

        e.Handled = true;
    }

    private void SampleHoleItemsControl_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        CommitSampleHoleDrag();
        e.Handled = true;
    }

    private void SampleHoleItemsControl_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        CommitSampleHoleDrag();
    }

    private void PaintSampleHole(
        Button button,
        ST_REVIEW_POINT_SELECT_ROW row)
    {
        if (!_sampleDragVisitedHoleKeys.Add(row.HoleKey) ||
            row.Use == _sampleDragUse)
        {
            return;
        }

        _sampleDragChangedHoleKeys.Add(row.HoleKey);
        button.Background = _sampleDragUse
            ? SampleSelectedBackground
            : SampleClearedBackground;
        button.BorderBrush = _sampleDragUse
            ? SampleSelectedBorder
            : SampleClearedBorder;
        button.BorderThickness = _sampleDragUse
            ? new Thickness(2)
            : new Thickness(1);
    }

    private void CommitSampleHoleDrag()
    {
        if (!_isSampleHoleDragActive)
        {
            return;
        }

        var command = _sampleDragSelectionCommand;
        var changedHoleKeys = _sampleDragChangedHoleKeys.ToArray();
        var use = _sampleDragUse;

        _isSampleHoleDragActive = false;
        _sampleDragSelectionCommand = null;
        _sampleDragVisitedHoleKeys.Clear();
        _sampleDragChangedHoleKeys.Clear();
        if (Mouse.Captured == SampleHoleItemsControl)
        {
            Mouse.Capture(null);
        }

        if (changedHoleKeys.Length > 0 && command?.CanExecute(null) == true)
        {
            command.Execute(new ST_REVIEW_SAMPLE_DRAG_SELECTION(changedHoleKeys, use));
        }
    }

    private static Button? FindSampleHoleButton(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button button &&
                button.DataContext is ST_REVIEW_POINT_SELECT_ROW)
            {
                return button;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
