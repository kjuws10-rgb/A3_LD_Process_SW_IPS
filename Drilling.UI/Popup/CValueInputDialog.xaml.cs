using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drilling.Common.Managers;

namespace Drilling.UI.Popup;

public partial class CValueInputDialog : Window
{
    private readonly EN_RECIPE_DATA_TYPE _dataType;
    private readonly double _min;
    private readonly double _max;

    public CValueInputDialog(
        string parameter,
        string value,
        EN_RECIPE_DATA_TYPE dataType,
        double min,
        double max)
    {
        InitializeComponent();
        _dataType = dataType;
        _min = min;
        _max = max;
        PromptText.Text = parameter;
        ValueTextBox.Text = value;
        RangeText.Text = dataType is EN_RECIPE_DATA_TYPE.Int or EN_RECIPE_DATA_TYPE.Double && max > min
            ? $"Range: {min.ToString(CultureInfo.InvariantCulture)} ~ {max.ToString(CultureInfo.InvariantCulture)}"
            : dataType == EN_RECIPE_DATA_TYPE.String ? "String input" : "Numeric input";
        DataObject.AddPastingHandler(ValueTextBox, ValueTextBoxPasting);
        BuildKeys();
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.CaretIndex = ValueTextBox.Text.Length;
        };
    }

    public string ResultValue
    {
        get
        {
            return ValueTextBox.Text;
        }
    }

    private void BuildKeys()
    {
        string[] Evaluate_dataTypeSwitch1()
        {
            var switchValue = _dataType;
            switch (switchValue)
            {
                case EN_RECIPE_DATA_TYPE.Int:
                    return ["7", "8", "9", "4", "5", "6", "1", "2", "3", "0", "-"];
                case EN_RECIPE_DATA_TYPE.Double:
                    return ["7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "-"];
                default:
                    return "1234567890QWERTYUIOPASDFGHJKLZXCVBNM_-./:".Select(value => value.ToString()).ToArray();
            }
        }

        var keys = Evaluate_dataTypeSwitch1();

        KeyGrid.Columns = _dataType == EN_RECIPE_DATA_TYPE.String ? 10 : 3;
        foreach (var key in keys)
        {
            var button = new Button
            {
                Content = key,
                Tag = key,
                Style = (Style)FindResource("KeyButton")
            };
            button.Click += KeyClick;
            KeyGrid.Children.Add(button);
        }
    }

    private void KeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (key == "-")
        {
            ValueTextBox.Text = ValueTextBox.Text.StartsWith('-')
                ? ValueTextBox.Text[1..]
                : $"-{ValueTextBox.Text}";
            ValueTextBox.CaretIndex = ValueTextBox.Text.Length;
            return;
        }
        if (key == "." && ValueTextBox.Text.Contains('.')) return;
        var insertionStart = ValueTextBox.SelectionStart;
        ValueTextBox.SelectedText = key;
        ValueTextBox.CaretIndex = insertionStart + key.Length;
        ValueTextBox.SelectionLength = 0;
    }

    private void SpaceClick(object sender, RoutedEventArgs e)
    {
        if (_dataType != EN_RECIPE_DATA_TYPE.String) return;
        var insertionStart = ValueTextBox.SelectionStart;
        ValueTextBox.SelectedText = " ";
        ValueTextBox.CaretIndex = insertionStart + 1;
        ValueTextBox.SelectionLength = 0;
    }

    private void BackClick(object sender, RoutedEventArgs e)
    {
        if (ValueTextBox.SelectionLength > 0)
        {
            ValueTextBox.SelectedText = "";
        }
        else if (ValueTextBox.CaretIndex > 0)
        {
            var caretIndex = ValueTextBox.CaretIndex;
            ValueTextBox.Text = ValueTextBox.Text.Remove(caretIndex - 1, 1);
            ValueTextBox.CaretIndex = caretIndex - 1;
        }
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        ValueTextBox.Clear();
    }

    private void ValueTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_dataType == EN_RECIPE_DATA_TYPE.String) return;

        e.Handled = !IsValidNumericInput(CreateProspectiveValue(e.Text));
    }

    private void ValueTextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_dataType == EN_RECIPE_DATA_TYPE.String) return;

        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) ||
            e.DataObject.GetData(DataFormats.UnicodeText) is not string pastedText ||
            !IsValidNumericInput(CreateProspectiveValue(pastedText)))
        {
            e.CancelCommand();
        }
    }

    private string CreateProspectiveValue(string input)
    {
        var text = ValueTextBox.Text ?? "";
        var start = Math.Clamp(ValueTextBox.SelectionStart, 0, text.Length);
        var length = Math.Clamp(ValueTextBox.SelectionLength, 0, text.Length - start);
        return text.Remove(start, length).Insert(start, input);
    }

    private bool IsValidNumericInput(string value)
    {
        if (value.Length == 0 || value == "-") return true;

        var unsigned = value[0] == '-' ? value[1..] : value;
        if (_dataType == EN_RECIPE_DATA_TYPE.Int)
        {
            return unsigned.Length > 0 && unsigned.All(char.IsDigit);
        }

        var decimalPointCount = 0;
        foreach (var character in unsigned)
        {
            if (char.IsDigit(character)) continue;
            if (character == '.' && ++decimalPointCount == 1) continue;
            return false;
        }
        return true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OkClick(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        var value = ValueTextBox.Text.Trim();
        if (!Validate(value, out var error))
        {
            MessageBox.Show(this, error, "Invalid Value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ValueTextBox.Text = value;
        DialogResult = true;
    }

    private bool Validate(string value, out string error)
    {
        error = "";
        if (_dataType == EN_RECIPE_DATA_TYPE.String) return true;

        if (_dataType == EN_RECIPE_DATA_TYPE.Int)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                error = "Enter a valid integer.";
                return false;
            }
            return ValidateRange(parsed, out error);
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            error = "Enter a valid number.";
            return false;
        }
        return ValidateRange(number, out error);
    }

    private bool ValidateRange(double value, out string error)
    {
        error = "";
        if (_max <= _min || value >= _min && value <= _max) return true;
        error = $"Value must be between {_min.ToString(CultureInfo.InvariantCulture)} and {_max.ToString(CultureInfo.InvariantCulture)}.";
        return false;
    }
}
