using System.Globalization;
using Drilling.Common.Review;

namespace Drilling.Common.Recipe;

public sealed record ST_RECIPE_HOLE_POINT(
    int SequenceNo,
    string HoleKey,
    int HeadNo,
    int CellNo,
    int HoleNo,
    int Column,
    int Row,
    int PixelCountX,
    int PixelCountY,
    double DesignX,
    double DesignY,
    double ScannerGx,
    double ScannerGy,
    double StageX,
    double StageY,
    double RecipeOffsetX,
    double RecipeOffsetY,
    double HeadDefaultOffsetX,
    double HeadDefaultOffsetY,
    double ReviewOffsetX,
    double ReviewOffsetY,
    double ScannerOffsetX,
    double ScannerOffsetY,
    double StageWaitPosition)
{
    public double OffsetX
    {
        get
        {
            return RecipeOffsetX;
        }
    }

    public double OffsetY
    {
        get
        {
            return RecipeOffsetY;
        }
    }
}

public sealed record ST_RECIPE_HOLE_PLAN(
    int HeadCount,
    int CellCount,
    double GlassSizeX,
    double GlassSizeY,
    double EncoderScale,
    IReadOnlyList<ST_RECIPE_HOLE_POINT> Points);

internal sealed record ST_RECIPE_RAW_HOLE_POINT(
    string HoleKey,
    int CellNo,
    int HoleNo,
    int Column,
    int Row,
    int PixelCountX,
    int PixelCountY,
    double DesignX,
    double DesignY,
    double RecipeOffsetX,
    double RecipeOffsetY,
    double ReviewOffsetX,
    double ReviewOffsetY);

internal readonly record struct ST_HEAD_ASSIGNMENT_CANDIDATE(
    int HeadNo,
    double Distance);

public static class CRecipeHolePlan
{
    private sealed class CPreparedHolePoint
    {
        public CPreparedHolePoint(
            int index,
            ST_RECIPE_RAW_HOLE_POINT point,
            int headNo,
            double headCenterX,
            double scannerGxDirection,
            double scannerGyDirection,
            double headDefaultOffsetX,
            double headDefaultOffsetY,
            double stageHoleX,
            double shotStageY)
        {
            Index = index;
            Point = point;
            HeadNo = headNo;
            HeadCenterX = headCenterX;
            ScannerGxDirection = scannerGxDirection;
            ScannerGyDirection = scannerGyDirection;
            HeadDefaultOffsetX = headDefaultOffsetX;
            HeadDefaultOffsetY = headDefaultOffsetY;
            StageHoleX = stageHoleX;
            ShotStageY = shotStageY;
        }

        public int Index { get; }
        public ST_RECIPE_RAW_HOLE_POINT Point { get; }
        public int HeadNo { get; }
        public double HeadCenterX { get; }
        public double ScannerGxDirection { get; }
        public double ScannerGyDirection { get; }
        public double HeadDefaultOffsetX { get; }
        public double HeadDefaultOffsetY { get; }
        public double StageHoleX { get; }
        public double ShotStageY { get; }
    }

    public static ST_RECIPE_HOLE_PLAN Build(
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var headCount = Math.Clamp(ReadInt(parameters, 8, "HEAD_COUNT"), 1, 8);
        var cellCount = Math.Max(1, ReadInt(parameters, 1, "CELL_COUNT"));
        var glassSizeX = ReadDouble(parameters, 0.0, "GLASS_SIZE_X");
        var glassSizeY = ReadDouble(parameters, 0.0, "GLASS_SIZE_Y");
        var encoderScale = ReadDouble(parameters, 16000.0, "SCAN_ENCODER_SCALE");
        var stageScanDirectionY = ReadDirection(parameters, -1.0, "STAGE_SCAN_DIRECTION_Y");
        var scanStartDelayLengthY = Math.Abs(ReadDouble(parameters, 0.0, "SCAN_START_DELAY_LENGTH_Y"));
        var akMarginY = ReadDouble(parameters, 0.0, "AK_MARGIN_Y");
        int HandleAssignedHeadCounts1(int headNo)
        {
            return headNo;
        }

        int HandleAssignedHeadCounts2(int _)
        {
            return 0;
        }

        var assignedHeadCounts = Enumerable.Range(1, headCount)
            .ToDictionary(HandleAssignedHeadCounts1, HandleAssignedHeadCounts2);
        var rawPoints = BuildRawPoints(parameters, cellCount);
        List<CPreparedHolePoint> preparedPointList = new List<CPreparedHolePoint>();
        for (int index = 0; index < rawPoints.Count; index++)
        {
            ST_RECIPE_RAW_HOLE_POINT point = rawPoints[index];
            int headNo = AssignHeadNo(
                point.DesignX,
                headCount,
                parameters,
                assignedHeadCounts);
            if (headNo > 0)
            {
                assignedHeadCounts[headNo]++;
            }

            double headCenterX = headNo > 0
                ? ReadHeadCenterX(parameters, headNo)
                : point.DesignX;
            double headCenterY = headNo > 0
                ? ReadHeadCenterY(parameters, headNo)
                : 0.0;
            double scannerGxDirection = headNo > 0
                ? ReadHeadScannerDirection(
                    parameters,
                    headNo,
                    headNo % 2 == 0 ? 1.0 : -1.0,
                    "STAGE_X_TO_GX")
                : 1.0;
            double scannerGyDirection = headNo > 0
                ? ReadHeadScannerDirection(
                    parameters,
                    headNo,
                    headNo % 2 == 0 ? -1.0 : 1.0,
                    "STAGE_Y_TO_GY")
                : 1.0;
            double headDefaultOffsetX = headNo > 0
                ? ReadHeadDefaultOffset(parameters, headNo, "X")
                : 0.0;
            double headDefaultOffsetY = headNo > 0
                ? ReadHeadDefaultOffset(parameters, headNo, "Y")
                : 0.0;
            double stageHoleX = point.DesignX;
            double stageHoleY = ConvertRecipeYToStageY(point.DesignY - akMarginY);
            double shotStageY = headCenterY + stageHoleY;

            preparedPointList.Add(new CPreparedHolePoint(
                index,
                point,
                headNo,
                headCenterX,
                scannerGxDirection,
                scannerGyDirection,
                headDefaultOffsetX,
                headDefaultOffsetY,
                stageHoleX,
                shotStageY));
        }

        CPreparedHolePoint[] preparedPoints = preparedPointList.ToArray();
        double GetShotStageY(CPreparedHolePoint point)
        {
            return point.ShotStageY;
        }

        var firstShotStageY = preparedPoints.Length > 0
            ? stageScanDirectionY < 0.0
                ? preparedPoints.Max(GetShotStageY)
                : preparedPoints.Min(GetShotStageY)
            : 0.0;
        var scanStartStageY = firstShotStageY - (stageScanDirectionY * scanStartDelayLengthY);
        List<ST_RECIPE_HOLE_POINT> calculatedPointList = new List<ST_RECIPE_HOLE_POINT>();
        foreach (CPreparedHolePoint point in preparedPoints)
        {
            double stageWaitDistanceY = (point.ShotStageY - scanStartStageY) * stageScanDirectionY;
            if (stageWaitDistanceY < 0.0 && Math.Abs(stageWaitDistanceY) <= 0.000001)
            {
                stageWaitDistanceY = 0.0;
            }

            double scannerBaseGx = ApplyScannerAxis(
                point.StageHoleX,
                point.HeadCenterX,
                0.0,
                point.ScannerGxDirection);
            double scannerBaseGy = stageWaitDistanceY * stageScanDirectionY * point.ScannerGyDirection;
            double headDefaultScannerOffsetX = point.HeadDefaultOffsetX * point.ScannerGxDirection;
            double headDefaultScannerOffsetY =
                ConvertRecipeYToStageY(point.HeadDefaultOffsetY) * point.ScannerGyDirection;
            double scannerOffsetX =
                point.Point.RecipeOffsetX + headDefaultScannerOffsetX + point.Point.ReviewOffsetX;
            double scannerOffsetY =
                point.Point.RecipeOffsetY + headDefaultScannerOffsetY + point.Point.ReviewOffsetY;
            double scannerGx = scannerBaseGx + scannerOffsetX;
            double scannerGy = scannerBaseGy + scannerOffsetY;
            double stageWaitPosition = stageWaitDistanceY * Math.Abs(encoderScale) * stageScanDirectionY;

            calculatedPointList.Add(new ST_RECIPE_HOLE_POINT(
                point.Index + 1,
                point.Point.HoleKey,
                point.HeadNo,
                point.Point.CellNo,
                point.Point.HoleNo,
                point.Point.Column,
                point.Point.Row,
                point.Point.PixelCountX,
                point.Point.PixelCountY,
                point.Point.DesignX,
                point.Point.DesignY,
                scannerGx,
                scannerGy,
                point.StageHoleX,
                point.ShotStageY,
                point.Point.RecipeOffsetX,
                point.Point.RecipeOffsetY,
                point.HeadDefaultOffsetX,
                point.HeadDefaultOffsetY,
                point.Point.ReviewOffsetX,
                point.Point.ReviewOffsetY,
                scannerOffsetX,
                scannerOffsetY,
                stageWaitPosition));
        }

        ST_RECIPE_HOLE_POINT[] calculatedPoints = calculatedPointList.ToArray();
        ST_RECIPE_HOLE_POINT SelectPoint3(ST_RECIPE_HOLE_POINT point, int index)
        {
            return point with { SequenceNo = index + 1 };
        }

        var points = OrderProcessPoints(calculatedPoints, stageScanDirectionY)
            .Select(SelectPoint3)
            .ToArray();

        return new ST_RECIPE_HOLE_PLAN(
            headCount,
            cellCount,
            glassSizeX,
            glassSizeY,
            encoderScale,
            points);
    }

    private static IReadOnlyList<ST_RECIPE_RAW_HOLE_POINT> BuildRawPoints(
        IReadOnlyDictionary<string, string> parameters,
        int cellCount)
    {
        var akMarginX = ReadDouble(parameters, 0.0, "AK_MARGIN_X");
        var akMarginY = ReadDouble(parameters, 0.0, "AK_MARGIN_Y");
        var globalPixelCountX = Math.Max(1, ReadInt(parameters, 1, "NUM_OF_PIXEL_X"));
        var globalPixelCountY = Math.Max(1, ReadInt(parameters, 1, "NUM_OF_PIXEL_Y"));
        var globalPitchX = ReadDouble(parameters, 1.0, "PITCH_X");
        var globalPitchY = ReadDouble(parameters, globalPitchX, "PITCH_Y");
        var reviewOffsetX = ReadDouble(parameters, 0.0, "REVIEW_OFFSET_X");
        var reviewOffsetY = ReadDouble(parameters, 0.0, "REVIEW_OFFSET_Y");
        var rawPoints = new List<ST_RECIPE_RAW_HOLE_POINT>();

        for (var cellNo = 1; cellNo <= cellCount; cellNo++)
        {
            var explicitHoleCount = ReadInt(
                parameters,
                -1,
                $"CELL{cellNo}_HOLE_COUNT");

            if (explicitHoleCount > 0)
            {
                rawPoints.AddRange(BuildExplicitCellHoles(
                    parameters,
                    cellNo,
                    explicitHoleCount,
                    akMarginX,
                    akMarginY,
                    reviewOffsetX,
                    reviewOffsetY,
                    globalPixelCountX,
                    globalPixelCountY,
                    globalPitchX));
                continue;
            }

            rawPoints.AddRange(BuildFallbackCellGrid(
                parameters,
                cellNo,
                akMarginX,
                akMarginY,
                reviewOffsetX,
                reviewOffsetY,
                globalPixelCountX,
                globalPixelCountY,
                globalPitchX,
                globalPitchY));
        }

        return rawPoints;
    }

    private static IReadOnlyList<ST_RECIPE_RAW_HOLE_POINT> BuildExplicitCellHoles(
        IReadOnlyDictionary<string, string> parameters,
        int cellNo,
        int holeCount,
        double akMarginX,
        double akMarginY,
        double defaultReviewOffsetX,
        double defaultReviewOffsetY,
        int globalPixelCountX,
        int globalPixelCountY,
        double globalPitchX)
    {
        var cellBaseX = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X");
        var cellBaseY = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y");
        var rotation = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ROTATION");
        var radians = rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var points = new List<ST_RECIPE_RAW_HOLE_POINT>(holeCount);

        for (var holeNo = 1; holeNo <= holeCount; holeNo++)
        {
            var holePrefix = $"CELL{cellNo}_HOLE{holeNo}";
            var localX = ReadDouble(parameters, (holeNo - 1) * globalPitchX, $"{holePrefix}_X");
            var localY = ReadDouble(parameters, 0.0, $"{holePrefix}_Y");
            var rotatedX = (localX * cos) - (localY * sin);
            var rotatedY = (localX * sin) + (localY * cos);
            var pixelCountX = Math.Max(1, ReadInt(parameters, globalPixelCountX, $"{holePrefix}_NUM_OF_PIXEL_X"));
            var pixelCountY = Math.Max(1, ReadInt(parameters, globalPixelCountY, $"{holePrefix}_NUM_OF_PIXEL_Y"));
            var holeName = CReviewHoleNameFormatter.ToMatrixName(holeNo, pixelCountX);
            var recipeOffsetPrefix = $"CELL{cellNo}_{holeName}_RECIPE_OFFSET_";
            var reviewOffsetPrefix = $"CELL{cellNo}_{holeName}_REVIEW_OFFSET_";
            var recipeOffsetX = ReadDouble(parameters, 0.0, $"{recipeOffsetPrefix}X");
            var recipeOffsetY = ReadDouble(parameters, 0.0, $"{recipeOffsetPrefix}Y");
            var reviewOffsetX = ReadDouble(parameters, defaultReviewOffsetX, $"{reviewOffsetPrefix}X");
            var reviewOffsetY = ReadDouble(parameters, defaultReviewOffsetY, $"{reviewOffsetPrefix}Y");

            points.Add(new ST_RECIPE_RAW_HOLE_POINT(
                ToHoleKey(cellNo, holeNo),
                cellNo,
                holeNo,
                holeNo - 1,
                0,
                pixelCountX,
                pixelCountY,
                akMarginX + cellBaseX + rotatedX,
                akMarginY + cellBaseY + rotatedY,
                recipeOffsetX,
                recipeOffsetY,
                reviewOffsetX,
                reviewOffsetY));
        }

        return points;
    }

    private static IReadOnlyList<ST_RECIPE_RAW_HOLE_POINT> BuildFallbackCellGrid(
        IReadOnlyDictionary<string, string> parameters,
        int cellNo,
        double akMarginX,
        double akMarginY,
        double defaultReviewOffsetX,
        double defaultReviewOffsetY,
        int globalPixelCountX,
        int globalPixelCountY,
        double globalPitchX,
        double globalPitchY)
    {
        var cellBaseX = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X");
        var cellBaseY = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y");
        var rotation = ReadDouble(parameters, 0.0, $"CELL{cellNo}_ROTATION");
        var pixelCountX = Math.Max(1, ReadInt(parameters, globalPixelCountX, $"CELL{cellNo}_NUM_OF_PIXEL_X"));
        var pixelCountY = Math.Max(1, ReadInt(parameters, globalPixelCountY, $"CELL{cellNo}_NUM_OF_PIXEL_Y"));
        var pitchX = ReadDouble(parameters, globalPitchX, $"CELL{cellNo}_PITCH_X");
        var pitchY = ReadDouble(parameters, globalPitchY, $"CELL{cellNo}_PITCH_Y");
        var calculated = CCellPointCalculator.Calculate(new ST_CELL_POINT_INPUT(
            cellNo,
            cellBaseX,
            cellBaseY,
            rotation,
            pixelCountX,
            pixelCountY,
            pitchX > 0.0 ? pitchX : 1.0,
            pitchY > 0.0 ? pitchY : Math.Max(1.0, pitchX),
            akMarginX,
            akMarginY));

        if (!calculated.IsValid)
        {
            return [];
        }
        ST_RECIPE_RAW_HOLE_POINT SelectPoint4(ST_CELL_DRILL_POINT point)
        {
            var holeName = CReviewHoleNameFormatter.ToMatrixName(point.PointNo, pixelCountX);
            var recipeOffsetPrefix = $"CELL{cellNo}_{holeName}_RECIPE_OFFSET_";
            var reviewOffsetPrefix = $"CELL{cellNo}_{holeName}_REVIEW_OFFSET_";
            return new ST_RECIPE_RAW_HOLE_POINT(
                ToHoleKey(cellNo, point.PointNo),
                cellNo,
                point.PointNo,
                point.Column,
                point.Row,
                pixelCountX,
                pixelCountY,
                point.X,
                point.Y,
                ReadDouble(parameters, 0.0, $"{recipeOffsetPrefix}X"),
                ReadDouble(parameters, 0.0, $"{recipeOffsetPrefix}Y"),
                ReadDouble(parameters, defaultReviewOffsetX, $"{reviewOffsetPrefix}X"),
                ReadDouble(parameters, defaultReviewOffsetY, $"{reviewOffsetPrefix}Y"));
        }
        return calculated.Points
            .Select(SelectPoint4)
            .ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_HOLE_POINT> OrderProcessPoints(
        IReadOnlyList<ST_RECIPE_HOLE_POINT> points,
        double stageScanDirectionY)
    {
        double HandleRows5(ST_RECIPE_HOLE_POINT point)
        {
            return Math.Round(point.StageWaitPosition, 6);
        }

        var rows = points
            .GroupBy(HandleRows5)
            .ToArray();
        double GetRowSortKey6(IGrouping<double, ST_RECIPE_HOLE_POINT> row)
        {
            return row.Key;
        }

        double GetRowSortKey7(IGrouping<double, ST_RECIPE_HOLE_POINT> row)
        {
            return row.Key;
        }

        var orderedRows = (stageScanDirectionY < 0.0
                ? rows.OrderByDescending(GetRowSortKey6)
                : rows.OrderBy(GetRowSortKey7))
            .ToArray();
        var orderedPoints = new List<ST_RECIPE_HOLE_POINT>(points.Count);

        for (var rowIndex = 0; rowIndex < orderedRows.Length; rowIndex++)
        {
            double GetPointSortKey8(ST_RECIPE_HOLE_POINT point)
            {
                return point.DesignX;
            }

            int GetPointSortKey9(ST_RECIPE_HOLE_POINT point)
            {
                return point.CellNo;
            }

            int GetPointSortKey10(ST_RECIPE_HOLE_POINT point)
            {
                return point.HoleNo;
            }

            double GetPointSortKey11(ST_RECIPE_HOLE_POINT point)
            {
                return point.DesignX;
            }

            int GetPointSortKey12(ST_RECIPE_HOLE_POINT point)
            {
                return point.CellNo;
            }

            int GetPointSortKey13(ST_RECIPE_HOLE_POINT point)
            {
                return point.HoleNo;
            }

            var rowPoints = rowIndex % 2 == 0
                ? orderedRows[rowIndex].OrderBy(GetPointSortKey8).ThenBy(GetPointSortKey9).ThenBy(GetPointSortKey10)
                : orderedRows[rowIndex].OrderByDescending(GetPointSortKey11).ThenBy(GetPointSortKey12).ThenBy(GetPointSortKey13);

            orderedPoints.AddRange(rowPoints);
        }

        return orderedPoints;
    }

    private static int AssignHeadNo(
        double designX,
        int headCount,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<int, int> assignedHeadCounts)
    {
        if (headCount <= 1)
        {
            return 1;
        }
        bool CheckHeadNo14(int headNo)
        {
            return ReadNullableDouble(
                            parameters,
                            $"H{headNo:00}_SCAN_FIELD_WIDTH_X",
                            $"H{headNo:00}_HEAD_FIELD_WIDTH_X").HasValue;
        }

        var hasIndividualAreas = Enumerable.Range(1, headCount).Any(CheckHeadNo14);

        if (hasIndividualAreas)
        {
            ST_HEAD_ASSIGNMENT_CANDIDATE? SelectHeadNo15(int headNo)
            {
                var centerX = ReadHeadCenterX(parameters, headNo);
                var widthX = ReadNullableDouble(
                        parameters,
                        $"H{headNo:00}_SCAN_FIELD_WIDTH_X",
                        $"H{headNo:00}_HEAD_FIELD_WIDTH_X")
                    ?? 110.0;
                widthX = widthX > 0.0 ? widthX : 110.0;
                var halfWidth = widthX / 2.0;

                return designX >= centerX - halfWidth && designX <= centerX + halfWidth
                    ? new ST_HEAD_ASSIGNMENT_CANDIDATE(headNo, Math.Abs(designX - centerX))
                    : (ST_HEAD_ASSIGNMENT_CANDIDATE?)null;
            }
            bool FilterCandidate16(ST_HEAD_ASSIGNMENT_CANDIDATE? candidate)
            {
                return candidate.HasValue;
            }

            ST_HEAD_ASSIGNMENT_CANDIDATE SelectCandidate17(ST_HEAD_ASSIGNMENT_CANDIDATE? candidate)
            {
                return candidate!.Value;
            }

            var candidates = Enumerable.Range(1, headCount)
                .Select(SelectHeadNo15)
                .Where(FilterCandidate16)
                .Select(SelectCandidate17)
                .ToArray();

            return candidates.Length == 0
                ? 0
                : SelectHeadCandidate(candidates, assignedHeadCounts);
        }
        ST_HEAD_ASSIGNMENT_CANDIDATE SelectHeadNo18(int headNo)
        {
            return new ST_HEAD_ASSIGNMENT_CANDIDATE(
                            headNo,
                            Math.Abs(designX - ReadHeadCenterX(parameters, headNo)));
        }

        var fallbackCandidates = Enumerable.Range(1, headCount)
            .Select(SelectHeadNo18)
            .ToArray();

        return SelectHeadCandidate(fallbackCandidates, assignedHeadCounts);
    }

    private static int SelectHeadCandidate(
        IReadOnlyList<ST_HEAD_ASSIGNMENT_CANDIDATE> candidates,
        IReadOnlyDictionary<int, int> assignedHeadCounts)
    {
        double GetCandidateSortKey19(ST_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return candidate.Distance;
        }

        int GetCandidateSortKey20(ST_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return assignedHeadCounts.TryGetValue(candidate.HeadNo, out var count)
                            ? count
                            : 0;
        }

        int GetCandidateSortKey21(ST_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return candidate.HeadNo;
        }

        return candidates
            .OrderBy(GetCandidateSortKey19)
            .ThenBy(GetCandidateSortKey20)
            .ThenBy(GetCandidateSortKey21)
            .First()
            .HeadNo;
    }

    private static double ReadHeadCenterX(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        const double fallbackHead1PositionX = -5.0;
        const double fallbackHeadGapX = 200.0;
        var head1PositionX = ReadDouble(
            parameters,
            fallbackHead1PositionX,
            "H01_AK_POSITION_X");
        var headGapX = ReadDouble(
            parameters,
            fallbackHeadGapX,
            "HeadGapX");
        var akMarginX = ReadDouble(parameters, 0.0, "AK_MARGIN_X");
        return akMarginX + head1PositionX + ((headNo - 1) * headGapX);
    }

    private static double ReadHeadCenterY(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        var explicitHeadY = ReadNullableDouble(
            parameters,
            $"H{headNo:00}_AK_POSITION_Y");
        if (explicitHeadY.HasValue)
        {
            return explicitHeadY.Value;
        }

        var head1CenterY = ReadDouble(parameters, 0.0, "REVIEW_TO_HEAD1_GAP_Y");
        var headGapY = ReadDouble(parameters, 0.0, "HeadGapY");
        return headNo % 2 == 0
            ? head1CenterY + headGapY
            : head1CenterY;
    }

    private static double ReadHeadDefaultOffset(
        IReadOnlyDictionary<string, string> parameters,
        int headNo,
        string axis)
    {
        return ReadDouble(
            parameters,
            0.0,
            $"H{headNo:00}_DEFAULT_OFFSET_{axis}");
    }

    private static double ConvertRecipeYToStageY(
        double recipeY)
    {
        return -recipeY;
    }

    private static double ApplyScannerAxis(
        double designPosition,
        double stageStartPosition,
        double offset,
        double direction)
    {
        return ((designPosition - stageStartPosition) * direction) + offset;
    }

    private static double ReadHeadScannerDirection(
        IReadOnlyDictionary<string, string> parameters,
        int headNo,
        double defaultValue,
        string directionName)
    {
        return ReadDirection(
            parameters,
            defaultValue,
            $"H{headNo:00}_{directionName}_SIGN",
            $"H{headNo:00}_{directionName}_DIRECTION",
            $"{directionName}_SIGN",
            $"{directionName}_DIRECTION");
    }

    private static double ReadDirection(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        bool FilterKey22(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey22))
        {
            if (!parameters.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim().ToUpperInvariant();
            if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                return NormalizeDirection(number, key);
            }

            if (normalized is "-" or "-1" or "REVERSE" or "REV" or "NEGATIVE" or "MINUS")
            {
                return -1.0;
            }

            if (normalized is "+" or "+1" or "FORWARD" or "FWD" or "POSITIVE" or "PLUS")
            {
                return 1.0;
            }

            throw new InvalidOperationException($"Invalid direction value. {key}={value}");
        }

        return NormalizeDirection(defaultValue, "DEFAULT_DIRECTION");
    }

    private static double NormalizeDirection(
        double value,
        string key)
    {
        if (value < 0.0)
        {
            return -1.0;
        }

        if (value > 0.0)
        {
            return 1.0;
        }

        throw new InvalidOperationException($"Direction value cannot be 0. {key}=0");
    }

    private static string ToHoleKey(
        int cellNo,
        int holeNo)
    {
        return $"CELL{Math.Max(1, cellNo):000}_HOLE{Math.Max(1, holeNo):0000}";
    }

    private static double ReadDouble(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        bool FilterKey23(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey23))
        {
            if (parameters.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return defaultValue;
    }

    private static double? ReadNullableDouble(
        IReadOnlyDictionary<string, string> parameters,
        params string[] keys)
    {
        bool FilterKey24(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey24))
        {
            if (parameters.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return null;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> parameters,
        int defaultValue,
        params string[] keys)
    {
        bool FilterKey25(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey25))
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                continue;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
            {
                return intResult;
            }

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleResult))
            {
                return (int)Math.Round(doubleResult);
            }
        }

        return defaultValue;
    }
}

