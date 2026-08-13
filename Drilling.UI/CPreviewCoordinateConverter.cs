using System.Globalization;
using System.Windows.Data;

namespace Drilling.UI;

public sealed class CPreviewCoordinateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 6 ||
            !TryDouble(values[0], out var position) ||
            !TryDouble(values[1], out var actualWidth) ||
            !TryDouble(values[2], out var actualHeight) ||
            !TryDouble(values[3], out var designWidth) ||
            !TryDouble(values[4], out var designHeight) ||
            !TryDouble(values[5], out var elementSize) ||
            actualWidth <= 0.0 || actualHeight <= 0.0 ||
            designWidth <= 0.0 || designHeight <= 0.0)
        {
            return 0.0;
        }

        var parameterText = (parameter?.ToString() ?? "").ToUpperInvariant();
        if (parameterText.Contains("STRETCH", StringComparison.OrdinalIgnoreCase))
        {
            var stretchScale = parameterText.Contains("Y", StringComparison.OrdinalIgnoreCase)
                ? actualHeight / designHeight
                : actualWidth / designWidth;
            return (position * stretchScale) - (elementSize / 2.0);
        }

        var scale = parameterText.Contains("FILL", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(actualWidth / designWidth, actualHeight / designHeight)
            : Math.Min(actualWidth / designWidth, actualHeight / designHeight);
        var isVertical = parameterText.Contains("Y", StringComparison.OrdinalIgnoreCase);
        var offset = isVertical
            ? (actualHeight - (designHeight * scale)) / 2.0
            : (actualWidth - (designWidth * scale)) / 2.0;
        var convertedPosition = offset + (position * scale);

        return convertedPosition - (elementSize / 2.0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryDouble(object value, out double result)
    {
        if (value is double number)
        {
            result = number;
            return true;
        }

        return double.TryParse(
            value?.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }
}
