using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

internal static class CCellPreviewDrawing
{
    public static void DrawAlignKeys(
        DrawingContext context,
        ST_GLASS_PREVIEW_FRAME frame,
        double glassWidth,
        double glassHeight,
        double marginX,
        double marginY)
    {
        var brush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        brush.Freeze();
        var typeface = new Typeface(
            new FontFamily("Segoe UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        var crossPen = new Pen(brush, 1.6)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square
        };
        crossPen.Freeze();
        const double crossHalfLength = 4.0;
        const double labelGap = 6.0;
        var safeGlassWidth = Math.Max(1.0, glassWidth);
        var safeGlassHeight = Math.Max(1.0, glassHeight);
        var safeMarginX = Math.Clamp(marginX, 0.0, safeGlassWidth / 2.0);
        var safeMarginY = Math.Clamp(marginY, 0.0, safeGlassHeight / 2.0);
        var leftX = frame.CanvasLeft + (safeMarginX / safeGlassWidth * frame.Width);
        var rightX = frame.CanvasLeft + frame.Width - (safeMarginX / safeGlassWidth * frame.Width);
        var topY = frame.CanvasTop + (safeMarginY / safeGlassHeight * frame.Height);
        var bottomY = frame.CanvasTop + frame.Height - (safeMarginY / safeGlassHeight * frame.Height);

        DrawKey("AK1", leftX, topY, false, false);
        DrawKey("AK2", leftX, bottomY, false, true);
        DrawKey("AK3", rightX, topY, true, false);
        DrawKey("AK4", rightX, bottomY, true, true);
        return;

        void DrawKey(string name, double centerX, double centerY, bool alignRight, bool alignBelow)
        {
            DrawCross(centerX, centerY);
            DrawLabel(name, centerX, alignBelow ? centerY + 2.0 : centerY - 15.0, alignRight);
        }

        void DrawCross(double centerX, double centerY)
        {
            context.DrawLine(
                crossPen,
                new Point(centerX - crossHalfLength, centerY),
                new Point(centerX + crossHalfLength, centerY));
            context.DrawLine(
                crossPen,
                new Point(centerX, centerY - crossHalfLength),
                new Point(centerX, centerY + crossHalfLength));
        }

        void DrawLabel(string name, double dotX, double top, bool alignRight)
        {
            var text = new FormattedText(
                name,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                10.0,
                brush,
                1.0);
            var left = alignRight
                ? dotX - labelGap - text.Width
                : dotX + labelGap;
            context.DrawText(text, new Point(left, top));
        }
    }

    public static IReadOnlyList<ST_DISTORTION_KEY_PREVIEW> CreateDistortionKeyPreviews(
        double glassWidth,
        double glassHeight,
        double akMarginX,
        double akMarginY,
        Func<string, double?> readValue)
    {
        var defaultSpanX = Math.Max(0.0, glassWidth - (akMarginX * 2.0));
        var defaultSpanY = Math.Max(0.0, glassHeight - (akMarginY * 2.0));

        return Enumerable.Range(1, 6)
            .Select(keyNo =>
            {
                var defaultX = keyNo switch
                {
                    1 or 4 => 0.0,
                    2 or 5 => defaultSpanX / 2.0,
                    _ => defaultSpanX
                };
                var defaultY = keyNo <= 3
                    ? 0.0
                    : defaultSpanY;
                var x = readValue($"DISTORTION_KEY{keyNo}_X") ?? defaultX;
                var y = readValue($"DISTORTION_KEY{keyNo}_Y") ?? defaultY;
                return new ST_DISTORTION_KEY_PREVIEW(
                    keyNo,
                    akMarginX + x,
                    akMarginY + y);
            })
            .ToArray();
    }

    public static void DrawDistortionKeys(
        DrawingContext context,
        ST_GLASS_PREVIEW_FRAME frame,
        double glassWidth,
        double glassHeight,
        IReadOnlyList<ST_DISTORTION_KEY_PREVIEW> distortionKeys)
    {
        if (distortionKeys.Count == 0)
        {
            return;
        }

        var brush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        brush.Freeze();
        var typeface = new Typeface(
            new FontFamily("Segoe UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        var crossPen = new Pen(brush, 2.0)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square
        };
        crossPen.Freeze();
        var safeGlassWidth = Math.Max(1.0, glassWidth);
        var safeGlassHeight = Math.Max(1.0, glassHeight);
        var scaleX = frame.Width / safeGlassWidth;
        var scaleY = frame.Height / safeGlassHeight;
        const double crossHalfLength = 6.0;
        const double labelGap = 7.0;

        foreach (var key in distortionKeys)
        {
            if (!double.IsFinite(key.GlassX) ||
                !double.IsFinite(key.GlassY) ||
                key.GlassX < 0.0 ||
                key.GlassX > safeGlassWidth ||
                key.GlassY < 0.0 ||
                key.GlassY > safeGlassHeight)
            {
                continue;
            }

            var centerX = frame.CanvasLeft + (key.GlassX * scaleX);
            var centerY = frame.CanvasTop + (key.GlassY * scaleY);
            context.DrawLine(
                crossPen,
                new Point(centerX - crossHalfLength, centerY),
                new Point(centerX + crossHalfLength, centerY));
            context.DrawLine(
                crossPen,
                new Point(centerX, centerY - crossHalfLength),
                new Point(centerX, centerY + crossHalfLength));

            var text = new FormattedText(
                key.DisplayText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                10.0,
                brush,
                1.0);
            var left = ClampToRange(
                centerX + labelGap,
                frame.CanvasLeft + 2.0,
                frame.CanvasLeft + frame.Width - text.Width - 2.0);
            var top = ClampToRange(
                centerY - text.Height - 2.0,
                frame.CanvasTop + 2.0,
                frame.CanvasTop + frame.Height - text.Height - 2.0);
            context.DrawText(text, new Point(left, top));
        }
    }

    private static double ClampToRange(double value, double min, double max)
    {
        return max < min
            ? min
            : Math.Clamp(value, min, max);
    }

    public static ST_CELL_PREVIEW_LABEL? CreateCellLabel(
        int cellNo,
        Rect cellBounds,
        double designWidth,
        double designHeight,
        bool isSelected = false)
    {
        if (cellBounds.IsEmpty)
        {
            return null;
        }

        var label = new FormattedText(
            $"Cell{cellNo}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            10.0,
            Brushes.White,
            1.0);
        var badgeWidth = Math.Max(34.0, label.Width + 10.0);
        var centerX = cellBounds.Left + (cellBounds.Width / 2.0);
        var centerY = cellBounds.Top + (cellBounds.Height / 2.0);

        return new ST_CELL_PREVIEW_LABEL(
            cellNo,
            centerX,
            centerY,
            badgeWidth,
            designWidth,
            designHeight,
            isSelected);
    }
}

public sealed record ST_CELL_PREVIEW_LABEL(
    int CellNo,
    double CanvasCenterX,
    double CanvasCenterY,
    double Width,
    double DesignWidth,
    double DesignHeight,
    bool IsSelected)
{
    public string DisplayText
    {
        get
        {
            return $"Cell{CellNo}";
        }
    }

    public double Height
    {
        get
        {
            return 16.0;
        }
    }
}

public sealed record ST_DISTORTION_KEY_PREVIEW(
    int KeyNo,
    double GlassX,
    double GlassY)
{
    public string DisplayText
    {
        get
        {
            return $"DK{KeyNo}";
        }
    }
}
