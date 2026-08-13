using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Drilling.Common.Managers;
using Drilling.Common.Recipe;
using Drilling.Common.Review;

namespace Drilling.UI.Menu.Menus;

internal static class CReviewGlassPreviewBuilder
{
    private const double CanvasWidth = 860.0;
    private const double CanvasHeight = 430.0;
    private const double FrameLeft = 44.0;
    private const double FrameTop = 50.0;
    private const double FrameMaxWidth = 772.0;
    private const double FrameMaxHeight = 340.0;

    public static ST_REVIEW_GLASS_PREVIEW Build(
        ST_RECIPE_DATA recipe,
        int defaultCellCount,
        int currentCellNo = 0,
        int currentHoleNo = 0,
        IReadOnlyList<ST_REVIEW_PLAN_POINT>? reviewPoints = null,
        bool useSampleSelectionColors = false,
        IReadOnlySet<int>? visibleCellNos = null,
        ST_REVIEW_GLASS_AXIS_INDICATOR? axisIndicator = null,
        IReadOnlyList<ST_REVIEW_GLASS_AXIS_INDICATOR>? axisIndicators = null)
    {
        var glassWidth = ReadDouble(recipe, 500.0, "GLASS_SIZE_X");
        var glassHeight = ReadDouble(recipe, 300.0, "GLASS_SIZE_Y");
        var akMarginX = ReadDouble(recipe, 55.0, "AK_MARGIN_X");
        var akMarginY = ReadDouble(recipe, 45.0, "AK_MARGIN_Y");
        double? HandleDistortionKeys1(string key)
        {
            return ReadNullableDouble(recipe, key);
        }

        var distortionKeys = CCellPreviewDrawing.CreateDistortionKeyPreviews(
            glassWidth,
            glassHeight,
            akMarginX,
            akMarginY,
HandleDistortionKeys1);
        var cellCount = Math.Clamp(
            ReadInt(recipe, Math.Max(1, defaultCellCount), "CELL_COUNT"),
            1,
            1000);
        var reviewPointList = reviewPoints ?? [];
        (int CellNo, int HoleNo) HandleHoleStates2(ST_REVIEW_PLAN_POINT point)
        {
            return (point.CellNo, point.HoleNo);
        }

        (int CellNo, int HoleNo) HandleHoleStates3(IGrouping<(int CellNo, int HoleNo), ST_REVIEW_PLAN_POINT> group)
        {
            return group.Key;
        }

        EN_REVIEW_POINT_STATE HandleHoleStates4(IGrouping<(int CellNo, int HoleNo), ST_REVIEW_PLAN_POINT> group)
        {
            return group.Last().State;
        }

        var holeStates = reviewPointList
            .GroupBy(HandleHoleStates2)
            .ToDictionary(HandleHoleStates3, HandleHoleStates4);
        (int CellNo, int HoleNo) HandleHoleLocations5(ST_REVIEW_PLAN_POINT point)
        {
            return (point.CellNo, point.HoleNo);
        }

        (int CellNo, int HoleNo) HandleHoleLocations6(IGrouping<(int CellNo, int HoleNo), ST_REVIEW_PLAN_POINT> group)
        {
            return group.Key;
        }

        ST_REVIEW_PLAN_POINT HandleHoleLocations7(IGrouping<(int CellNo, int HoleNo), ST_REVIEW_PLAN_POINT> group)
        {
            return group.Last();
        }

        var holeLocations = reviewPointList
            .GroupBy(HandleHoleLocations5)
            .ToDictionary(HandleHoleLocations6, HandleHoleLocations7);
        bool FilterCellNo8(int cellNo)
        {
            return cellNo >= 1 && cellNo <= cellCount;
        }

        int GetCellNoSortKey9(int cellNo)
        {
            return cellNo;
        }

        var displayedCellNos = visibleCellNos is null
            ? Enumerable.Range(1, cellCount).ToArray()
            : visibleCellNos
                .Where(FilterCellNo8)
                .Distinct()
                .OrderBy(GetCellNoSortKey9)
                .ToArray();

        if (glassWidth <= 0.0 || glassHeight <= 0.0)
        {
            return new ST_REVIEW_GLASS_PREVIEW(
                null,
                [],
                null,
                $"{displayedCellNos.Length} Cells / Glass size is invalid");
        }

        var scale = Math.Min(FrameMaxWidth / glassWidth, FrameMaxHeight / glassHeight);
        var frameWidth = glassWidth * scale;
        var frameHeight = glassHeight * scale;
        var frameLeft = FrameLeft + ((FrameMaxWidth - frameWidth) / 2.0);
        var frameTop = FrameTop + ((FrameMaxHeight - frameHeight) / 2.0);
        var frame = new ST_GLASS_PREVIEW_FRAME(frameLeft, frameTop, frameWidth, frameHeight);
        var drawing = new DrawingGroup();
        var labels = new List<ST_CELL_PREVIEW_LABEL>();
        var previewAxisIndicators = axisIndicators ?? (axisIndicator is null ? [] : [axisIndicator]);
        ST_REVIEW_CURRENT_HOLE_MARKER? currentHoleMarker = null;
        long totalHoleCount = 0;

        using (var context = drawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                new Rect(0.0, 0.0, CanvasWidth, CanvasHeight));

            foreach (var cellNo in displayedCellNos)
            {
                var prefix = $"CELL{cellNo}";
                var countX = ReadInt(
                    recipe,
                    ReadInt(recipe, 1, "NUM_OF_PIXEL_X"),
                    $"{prefix}_NUM_OF_PIXEL_X");
                var countY = ReadInt(
                    recipe,
                    ReadInt(recipe, 1, "NUM_OF_PIXEL_Y"),
                    $"{prefix}_NUM_OF_PIXEL_Y");
                var globalPitchX = ReadDouble(recipe, 0.0, "PITCH_X");
                var globalPitchY = ReadDouble(recipe, globalPitchX, "PITCH_Y");
                var pitchX = ReadDouble(
                    recipe,
                    globalPitchX,
                    $"{prefix}_PITCH_X");
                var pitchY = ReadDouble(
                    recipe,
                    globalPitchY,
                    $"{prefix}_PITCH_Y");
                var firstX = ReadDouble(
                    recipe,
                    0.0,
                    $"{prefix}_ALIGN_TO_1ST_PIXEL_X");
                var firstY = ReadDouble(
                    recipe,
                    0.0,
                    $"{prefix}_ALIGN_TO_1ST_PIXEL_Y");
                var rotation = ReadDouble(
                    recipe,
                    0.0,
                    $"{prefix}_ROTATION");
                var holeSize = Math.Max(
                    0.0,
                    ReadDouble(
                        recipe,
                        ReadDouble(recipe, 0.0, "PIXEL_SIZE"),
                        $"{prefix}_PIXEL_SIZE"));
                var result = CCellPointCalculator.Calculate(new ST_CELL_POINT_INPUT(
                    cellNo,
                    firstX,
                    firstY,
                    rotation,
                    countX,
                    countY,
                    pitchX,
                    pitchY,
                    akMarginX,
                    akMarginY));
                if (!result.IsValid)
                {
                    continue;
                }

                totalHoleCount += result.Points.Count;
                var holeRadius = holeSize / 2.0;
                var previewHoleSize = Math.Clamp(holeSize * scale, 1.5, 12.0);
                var cellPixels = new HashSet<long>();
                var holeVisuals = new List<(double X, double Y, double Size, EN_REVIEW_POINT_STATE State)>();
                var minCanvasX = double.MaxValue;
                var minCanvasY = double.MaxValue;
                var maxCanvasX = double.MinValue;
                var maxCanvasY = double.MinValue;

                foreach (var point in result.Points)
                {
                    var coordinatePoint = holeLocations.GetValueOrDefault((cellNo, point.PointNo));
                    var pointX = coordinatePoint?.DesignX ?? point.X;
                    var pointY = coordinatePoint?.DesignY ?? point.Y;
                    var canvasX = frameLeft + (pointX * scale);
                    var canvasY = frameTop + (pointY * scale);
                    if (cellNo == currentCellNo && point.PointNo == currentHoleNo)
                    {
                        currentHoleMarker = new ST_REVIEW_CURRENT_HOLE_MARKER(
                            canvasX,
                            canvasY,
                            Math.Max(8.0, previewHoleSize + 5.0),
                            CanvasWidth,
                            CanvasHeight);
                    }

                    minCanvasX = Math.Min(minCanvasX, canvasX);
                    minCanvasY = Math.Min(minCanvasY, canvasY);
                    maxCanvasX = Math.Max(maxCanvasX, canvasX);
                    maxCanvasY = Math.Max(maxCanvasY, canvasY);

                    var pixelX = (int)Math.Round(canvasX);
                    var pixelY = (int)Math.Round(canvasY);
                    var pixelKey = ((long)pixelX << 32) | (uint)pixelY;
                    var isInside = pointX - holeRadius >= 0.0 &&
                        pointX + holeRadius <= glassWidth &&
                        pointY - holeRadius >= 0.0 &&
                        pointY + holeRadius <= glassHeight;
                    if (cellPixels.Add(pixelKey))
                    {
                        var state = holeStates.GetValueOrDefault(
                            (cellNo, point.PointNo),
                            EN_REVIEW_POINT_STATE.Ready);
                        holeVisuals.Add((
                            pixelX,
                            pixelY,
                            isInside ? previewHoleSize : 4.0,
                            state));
                    }
                }
                EN_REVIEW_POINT_STATE GroupByHoleCallback10((double X, double Y, double Size, EN_REVIEW_POINT_STATE State) hole)
                {
                    return hole.State;
                }

                foreach (var stateGroup in holeVisuals.GroupBy(GroupByHoleCallback10))
                {
                    var geometry = new StreamGeometry();
                    using (var geometryContext = geometry.Open())
                    {
                        foreach (var hole in stateGroup)
                        {
                            AddCircle(geometryContext, hole.X, hole.Y, hole.Size);
                        }
                    }

                    geometry.Freeze();
                    context.DrawGeometry(
                        useSampleSelectionColors
                            ? CReviewStatusBrush.ForSampleSelection(stateGroup.Key)
                            : CReviewStatusBrush.ForPreviewBaseState(stateGroup.Key),
                        null,
                        geometry);
                }

                if (minCanvasX <= maxCanvasX && minCanvasY <= maxCanvasY)
                {
                    var labelPadding = Math.Max(4.0, previewHoleSize / 2.0);
                    var labelBounds = new Rect(
                        minCanvasX - labelPadding,
                        minCanvasY - labelPadding,
                        Math.Max(1.0, maxCanvasX - minCanvasX + (labelPadding * 2.0)),
                        Math.Max(1.0, maxCanvasY - minCanvasY + (labelPadding * 2.0)));
                    var label = CCellPreviewDrawing.CreateCellLabel(
                        cellNo,
                        labelBounds,
                        CanvasWidth,
                        CanvasHeight,
                        cellNo == currentCellNo);
                    if (label is not null)
                    {
                        labels.Add(label);
                    }
                }
            }

            CCellPreviewDrawing.DrawAlignKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                akMarginX,
                akMarginY);
            CCellPreviewDrawing.DrawDistortionKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                distortionKeys);
        }

        drawing.Freeze();
        var hasAxisIndicators = previewAxisIndicators.Count > 0;
        var paddingX = hasAxisIndicators
            ? Math.Max(96.0, frameWidth * 0.03)
            : frameWidth * 0.03;
        var paddingY = hasAxisIndicators
            ? Math.Max(68.0, frameHeight * 0.03)
            : Math.Max(22.0, frameHeight * 0.03);
        var previewRect = new Rect(
            0.0,
            0.0,
            frameWidth + (paddingX * 2.0),
            frameHeight + (paddingY * 2.0));
        var glassRect = new Rect(paddingX, paddingY, frameWidth, frameHeight);
        ST_CELL_PREVIEW_LABEL SelectLabel11(ST_CELL_PREVIEW_LABEL label)
        {
            return label with
            {
                CanvasCenterX = label.CanvasCenterX - frameLeft + paddingX,
                CanvasCenterY = label.CanvasCenterY - frameTop + paddingY,
                DesignWidth = previewRect.Width,
                DesignHeight = previewRect.Height
            };
        }

        var translatedLabels = labels
            .Select(SelectLabel11)
            .ToArray();
        var translatedCurrentHoleMarker = currentHoleMarker is null
            ? null
            : currentHoleMarker with
            {
                CanvasCenterX = currentHoleMarker.CanvasCenterX - frameLeft + paddingX,
                CanvasCenterY = currentHoleMarker.CanvasCenterY - frameTop + paddingY,
                DesignWidth = previewRect.Width,
                DesignHeight = previewRect.Height
            };
        var previewDrawing = new DrawingGroup();

        using (var context = previewDrawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                previewRect);
            var glassPen = new Pen(new SolidColorBrush(Color.FromRgb(102, 136, 164)), 1.8);
            glassPen.Freeze();
            context.DrawRectangle(null, glassPen, glassRect);
            context.PushClip(new RectangleGeometry(previewRect));
            context.PushTransform(new TranslateTransform(
                -(frameLeft - paddingX),
                -(frameTop - paddingY)));
            context.DrawDrawing(drawing);
            context.Pop();
            context.Pop();
            foreach (var previewAxisIndicator in previewAxisIndicators)
            {
                DrawPreviewAxisIndicator(context, previewRect, glassRect, previewAxisIndicator);
            }
        }

        previewDrawing.Freeze();
        var previewImage = new DrawingImage(previewDrawing);
        previewImage.Freeze();

        return new ST_REVIEW_GLASS_PREVIEW(
            previewImage,
            translatedLabels,
            translatedCurrentHoleMarker,
            $"{displayedCellNos.Length} Cells / {totalHoleCount:N0} Holes / Glass {glassWidth:0.#} x {glassHeight:0.#} mm");
    }

    private static void AddCircle(
        StreamGeometryContext context,
        double x,
        double y,
        double size)
    {
        var radius = size / 2.0;
        var control = radius * 0.5522847498;
        context.BeginFigure(new Point(x + radius, y), true, true);
        context.BezierTo(
            new Point(x + radius, y + control),
            new Point(x + control, y + radius),
            new Point(x, y + radius),
            true,
            false);
        context.BezierTo(
            new Point(x - control, y + radius),
            new Point(x - radius, y + control),
            new Point(x - radius, y),
            true,
            false);
        context.BezierTo(
            new Point(x - radius, y - control),
            new Point(x - control, y - radius),
            new Point(x, y - radius),
            true,
            false);
        context.BezierTo(
            new Point(x + control, y - radius),
            new Point(x + radius, y - control),
            new Point(x + radius, y),
            true,
            false);
    }

    private static void DrawPreviewAxisIndicator(
        DrawingContext context,
        Rect previewRect,
        Rect glassRect,
        ST_REVIEW_GLASS_AXIS_INDICATOR axisIndicator)
    {
        var axisBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        axisBrush.Freeze();
        var axisPen = new Pen(axisBrush, 2.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        axisPen.Freeze();

        var origin = GetPreviewAxisOrigin(previewRect, glassRect, axisIndicator);
        var xEnd = axisIndicator.XPositiveRight
            ? new Point(origin.X + 46.0, origin.Y)
            : new Point(origin.X - 46.0, origin.Y);
        var yEnd = axisIndicator.YPositiveDown
            ? new Point(origin.X, origin.Y + 42.0)
            : new Point(origin.X, origin.Y - 42.0);

        if (!string.IsNullOrWhiteSpace(axisIndicator.XLabel))
        {
            context.DrawLine(axisPen, origin, xEnd);
            DrawHorizontalArrowHead(context, axisPen, xEnd, axisIndicator.XPositiveRight);
        }

        if (!string.IsNullOrWhiteSpace(axisIndicator.YLabel))
        {
            context.DrawLine(axisPen, origin, yEnd);
            DrawVerticalArrowHead(context, axisPen, yEnd, axisIndicator.YPositiveDown);
        }

        if (!string.IsNullOrWhiteSpace(axisIndicator.XLabel))
        {
            DrawPreviewAxisText(
                context,
                axisIndicator.XLabel,
                new Point(
                    axisIndicator.XPositiveRight ? xEnd.X + 4.0 : Math.Max(2.0, xEnd.X - 24.0),
                    Math.Max(2.0, xEnd.Y - 10.0)),
                axisBrush);
        }

        if (!string.IsNullOrWhiteSpace(axisIndicator.YLabel))
        {
            DrawPreviewAxisText(
                context,
                axisIndicator.YLabel,
                new Point(
                    yEnd.X + 4.0,
                    axisIndicator.YPositiveDown ? yEnd.Y - 4.0 : Math.Max(2.0, yEnd.Y - 10.0)),
                axisBrush);
        }

        if (!string.IsNullOrWhiteSpace(axisIndicator.Title))
        {
            DrawPreviewAxisText(
                context,
                axisIndicator.Title,
                GetPreviewAxisTitlePoint(axisIndicator, origin, previewRect),
                axisBrush,
                11.0);
        }
    }

    private static Point GetPreviewAxisTitlePoint(
        ST_REVIEW_GLASS_AXIS_INDICATOR axisIndicator,
        Point origin,
        Rect previewRect)
    {
        var anchor = axisIndicator.Anchor.ToUpperInvariant();
        var x = origin.X - 18.0;
        var y = origin.Y - 24.0;

        if (anchor == "BOTTOM_LEFT")
        {
            x = origin.X + 4.0;
            y = origin.Y + 12.0;
        }
        else if (anchor == "BOTTOM_RIGHT")
        {
            x = origin.X - 92.0;
            y = origin.Y + 12.0;
        }

        return new Point(
            ClampPreviewAxisCoordinate(x, previewRect.Left + 4.0, previewRect.Right - 112.0),
            ClampPreviewAxisCoordinate(y, previewRect.Top + 4.0, previewRect.Bottom - 16.0));
    }

    private static Point GetPreviewAxisOrigin(
        Rect previewRect,
        Rect glassRect,
        ST_REVIEW_GLASS_AXIS_INDICATOR axisIndicator)
    {
        var anchor = axisIndicator.Anchor.ToUpperInvariant();
        if (anchor == "TOP_RIGHT")
        {
            var x = axisIndicator.XPositiveRight
                ? glassRect.Right - 54.0
                : glassRect.Right + 14.0;
            var y = axisIndicator.YPositiveDown
                ? glassRect.Top - 42.0
                : glassRect.Top - 8.0;
            return new Point(
                ClampPreviewAxisCoordinate(x, previewRect.Left + 8.0, previewRect.Right - 8.0),
                ClampPreviewAxisCoordinate(y, previewRect.Top + 8.0, previewRect.Bottom - 8.0));
        }

        if (anchor == "BOTTOM_LEFT")
        {
            var x = axisIndicator.XPositiveRight
                ? glassRect.Left - 30.0
                : glassRect.Left + 54.0;
            var y = axisIndicator.YPositiveDown
                ? glassRect.Bottom + 14.0
                : glassRect.Bottom + 42.0;
            return new Point(
                ClampPreviewAxisCoordinate(x, previewRect.Left + 8.0, previewRect.Right - 8.0),
                ClampPreviewAxisCoordinate(y, previewRect.Top + 8.0, previewRect.Bottom - 8.0));
        }

        if (anchor == "BOTTOM_RIGHT")
        {
            var x = axisIndicator.XPositiveRight
                ? glassRect.Right - 54.0
                : glassRect.Right + 14.0;
            var y = axisIndicator.YPositiveDown
                ? glassRect.Bottom + 14.0
                : glassRect.Bottom + 42.0;
            return new Point(
                ClampPreviewAxisCoordinate(x, previewRect.Left + 8.0, previewRect.Right - 8.0),
                ClampPreviewAxisCoordinate(y, previewRect.Top + 8.0, previewRect.Bottom - 8.0));
        }

        var topLeftX = axisIndicator.XPositiveRight
            ? glassRect.Left - 30.0
            : glassRect.Left + 54.0;
        var topLeftY = axisIndicator.YPositiveDown
            ? glassRect.Top - 42.0
            : glassRect.Top - 8.0;
        return new Point(
            ClampPreviewAxisCoordinate(topLeftX, previewRect.Left + 8.0, previewRect.Right - 8.0),
            ClampPreviewAxisCoordinate(topLeftY, previewRect.Top + 8.0, previewRect.Bottom - 8.0));
    }

    private static double ClampPreviewAxisCoordinate(
        double value,
        double min,
        double max)
    {
        var safeMax = Math.Max(min, max);
        return Math.Clamp(value, min, safeMax);
    }

    private static void DrawHorizontalArrowHead(
        DrawingContext context,
        Pen axisPen,
        Point end,
        bool positiveRight)
    {
        var direction = positiveRight ? 1.0 : -1.0;
        context.DrawLine(axisPen, new Point(end.X - (direction * 8.0), end.Y - 4.0), end);
        context.DrawLine(axisPen, new Point(end.X - (direction * 8.0), end.Y + 4.0), end);
    }

    private static void DrawVerticalArrowHead(
        DrawingContext context,
        Pen axisPen,
        Point end,
        bool positiveDown)
    {
        var direction = positiveDown ? 1.0 : -1.0;
        context.DrawLine(axisPen, new Point(end.X - 4.0, end.Y - (direction * 8.0)), end);
        context.DrawLine(axisPen, new Point(end.X + 4.0, end.Y - (direction * 8.0)), end);
    }

    private static void DrawPreviewAxisText(
        DrawingContext context,
        string text,
        Point point,
        Brush brush,
        double fontSize = 13.0)
    {
        var label = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            fontSize,
            brush,
            1.0);

        context.DrawText(label, point);
    }

    private static int ReadInt(
        ST_RECIPE_DATA recipe,
        int defaultValue,
        params string[] keys)
    {
        var text = ReadText(recipe, keys);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            ? (int)Math.Round(doubleValue)
            : defaultValue;
    }

    private static double ReadDouble(
        ST_RECIPE_DATA recipe,
        double defaultValue,
        params string[] keys)
    {
        var text = ReadText(recipe, keys);

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static double? ReadNullableDouble(
        ST_RECIPE_DATA recipe,
        string key)
    {
        var text = ReadText(recipe, key);

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string ReadText(
        ST_RECIPE_DATA recipe,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            bool MatchItem12(ST_RECIPE_PARAM item)
            {
                return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                item.Name.Equals(key, StringComparison.OrdinalIgnoreCase);
            }

            var parameter = recipe.Parameters.FirstOrDefault(MatchItem12);
            if (parameter is not null && !string.IsNullOrWhiteSpace(parameter.Value))
            {
                return parameter.Value.Trim();
            }
        }

        return "";
    }
}

public sealed record ST_REVIEW_GLASS_PREVIEW(
    ImageSource? Image,
    IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellLabels,
    ST_REVIEW_CURRENT_HOLE_MARKER? CurrentHoleMarker,
    string Summary);

internal sealed record ST_REVIEW_GLASS_AXIS_INDICATOR(
    string XLabel,
    string YLabel,
    bool XPositiveRight,
    bool YPositiveDown,
    string Anchor = "TOP_LEFT",
    string Title = "");

public sealed record ST_REVIEW_CURRENT_HOLE_MARKER(
    double CanvasCenterX,
    double CanvasCenterY,
    double Width,
    double DesignWidth,
    double DesignHeight)
{
    public double Height
    {
        get
        {
            return Width;
        }
    }
}
