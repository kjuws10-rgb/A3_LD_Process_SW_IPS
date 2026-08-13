using System.Globalization;
using Drilling.Common.Interface;
using Drilling.Common.Managers;
using Drilling.Common.Recipe;

namespace Drilling.Common.Review;

public enum EN_REVIEW_POINT_STATE
{
    Ready,
    Current,
    Ok,
    Ng,
    Skip
}

public enum EN_REVIEW_SEQUENCE_STATE
{
    Idle,
    Running,
    Stopping,
    Stopped,
    Completed,
    Failed
}

public enum EN_REVIEW_RULE_TYPE
{
    AllPoint,
    SamplePoint,
    Edge,
    Center,
    HeadPoint,
    CellPoint,
    ZeroLine
}

public sealed record ST_REVIEW_SEQUENCE_STATUS(
    EN_REVIEW_SEQUENCE_STATE State,
    int TotalCount,
    int CompletedCount,
    int NgCount,
    string Message);

public sealed record ST_REVIEW_RULE_DATA(
    string FileName,
    string RuleName,
    EN_REVIEW_RULE_TYPE RuleType,
    int HeadNo,
    int CellNo,
    int ZeroPointCount,
    IReadOnlyList<string> HoleKeys);

public sealed record ST_REVIEW_PLAN_POINT(
    int PointNo,
    string HoleKey,
    int HeadNo,
    int CellNo,
    int HoleNo,
    int PixelCountX,
    int PixelCountY,
    bool Use,
    double DesignX,
    double DesignY,
    double ReviewTargetX,
    double ReviewTargetY,
    double ErrorX,
    double ErrorY,
    EN_REVIEW_POINT_STATE State,
    string Judge)
{
    public double ReviewOffsetX { get; init; }

    public double ReviewOffsetY { get; init; }

    public string HeadName
    {
        get
        {
            return $"H{HeadNo:00}";
        }
    }

    public string CellName
    {
        get
        {
            return $"CELL{CellNo:00}";
        }
    }

    public string HoleName
    {
        get
        {
            return CReviewHoleNameFormatter.ToMatrixName(HoleNo, PixelCountX);
        }
    }

    public string PointName
    {
        get
        {
            return HoleName;
        }
    }
}

public static class CReviewHoleNameFormatter
{
    public static string ToMatrixName(
        int holeNo,
        int columnCount)
    {
        var safeColumnCount = Math.Max(1, columnCount);
        var zeroBasedHoleNo = Math.Max(0, holeNo - 1);
        var column = (zeroBasedHoleNo % safeColumnCount) + 1;
        var row = (zeroBasedHoleNo / safeColumnCount) + 1;

        return $"{ToColumnLetters(column)}{row}";
    }

    private static string ToColumnLetters(int oneBasedColumn)
    {
        var value = Math.Max(1, oneBasedColumn);
        var text = "";

        while (value > 0)
        {
            value--;
            text = (char)('A' + (value % 26)) + text;
            value /= 26;
        }

        return text;
    }
}

public sealed record ST_REVIEW_PLAN(
    string RecipeId,
    string RecipeName,
    int HeadCount,
    int CellCount,
    double ToleranceX,
    double ToleranceY,
    EN_VISION_AXIS_MODE VisionAxisMode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ST_REVIEW_PLAN_POINT> Points)
{
    public IReadOnlyList<ST_REVIEW_PLAN_POINT> ReviewPoints
    {
        get
        {
            return Points
        .Where(point => point.Use)
        .ToArray();
        }
    }

    public int TotalPointCount
    {
        get
        {
            return Points.Count;
        }
    }

    public int ReviewPointCount
    {
        get
        {
            return Points.Count(point => point.Use);
        }
    }
}

public sealed record ST_REVIEW_RESULT_DATA(
    ST_REVIEW_PLAN Plan,
    IReadOnlyList<ST_REVIEW_PLAN_POINT> Results,
    DateTimeOffset SavedAt);

public sealed record ST_REVIEW_RESULT_FILE_ROW(
    DateTimeOffset SavedAt,
    string RecipeId,
    string HoleKey,
    int HeadNo,
    int CellNo,
    double ErrorX,
    double ErrorY,
    string Judge);

public sealed record ST_REVIEW_RESULT_FILE_DATA(
    string FilePath,
    string FileName,
    string RecipeId,
    DateTimeOffset SavedAt,
    IReadOnlyList<ST_REVIEW_RESULT_FILE_ROW> Rows);

public interface IReviewResultFile
{
    string RootPath { get; }

    Task<ST_REVIEW_RESULT_FILE_DATA> Load(
        string path,
        CancellationToken cancellationToken = default);

    Task Save(
        ST_REVIEW_RESULT_DATA result,
        CancellationToken cancellationToken = default);
}

public interface IReviewRuleFile
{
    Task<IReadOnlyList<string>> List(CancellationToken cancellationToken = default);

    Task<ST_REVIEW_RULE_DATA> Load(
        string ruleFileName,
        CancellationToken cancellationToken = default);

    Task Save(
        ST_REVIEW_RULE_DATA rule,
        CancellationToken cancellationToken = default);
}

public interface IReviewManager
{
    ST_REVIEW_PLAN? CurrentPlan { get; }

    EN_REVIEW_SEQUENCE_STATE SequenceState { get; }

    ST_REVIEW_PLAN CreatePlan(
        ST_RECIPE_DATA recipe,
        IReadOnlyCollection<string> selectedHoleKeys);

    ST_REVIEW_PLAN CreatePlan(
        ST_RECIPE_DATA recipe,
        ST_REVIEW_RULE_DATA rule);

    Task<ST_REVIEW_SEQUENCE_STATUS> Start(
        ST_REVIEW_PLAN plan,
        Action<ST_REVIEW_PLAN>? progress = null,
        CancellationToken cancellationToken = default);

    void Stop();

    Task<ST_REVIEW_SEQUENCE_STATUS> RetryRemaining(
        Action<ST_REVIEW_PLAN>? progress = null,
        CancellationToken cancellationToken = default);

    ST_REVIEW_PLAN_POINT? ApplyReviewOffset(string holeKey);

    Task SaveResult(
        ST_REVIEW_PLAN plan,
        IReadOnlyList<ST_REVIEW_PLAN_POINT> results,
        CancellationToken cancellationToken = default);
}

public sealed class CReviewManager(
    IReviewResultFile reviewResultFile,
    IInterfaceManager interfaceManager,
    ISettingManager settingManager) : IReviewManager
{
    private const int MaxHeadCount = 8;
    private const int DefaultHeadCount = 8;
    private const int DefaultCellCount = 20;
    private const double ReviewSequenceRowTolerance = 0.001;
    private readonly SemaphoreSlim _sequenceLock = new(1, 1);
    private readonly object _stateLock = new();
    private bool _stopRequested;
    private ST_REVIEW_PLAN? _currentPlan;

    public ST_REVIEW_PLAN? CurrentPlan
    {
        get
        {
            lock (_stateLock)
            {
                return _currentPlan;
            }
        }
    }

    public EN_REVIEW_SEQUENCE_STATE SequenceState { get; private set; } = EN_REVIEW_SEQUENCE_STATE.Idle;

    public ST_REVIEW_PLAN CreatePlan(
        ST_RECIPE_DATA recipe,
        IReadOnlyCollection<string> selectedHoleKeys)
    {
        var selectedSet = selectedHoleKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(NormalizeHoleKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return CreatePlanCore(
            recipe,
            point => selectedSet.Contains(point.HoleKey));
    }

    public ST_REVIEW_PLAN CreatePlan(
        ST_RECIPE_DATA recipe,
        ST_REVIEW_RULE_DATA rule)
    {
        var allPlan = CreatePlan(recipe, Array.Empty<string>());
        var selectedKeys = BuildRuleHoleKeys(rule, allPlan);

        return CreatePlan(recipe, selectedKeys);
    }

    public async Task<ST_REVIEW_SEQUENCE_STATUS> Start(
        ST_REVIEW_PLAN plan,
        Action<ST_REVIEW_PLAN>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _sequenceLock.WaitAsync(0, cancellationToken))
        {
            return CreateStatus(
                CurrentPlan ?? plan,
                EN_REVIEW_SEQUENCE_STATE.Running,
                "Review sequence is already running.");
        }

        try
        {
            _stopRequested = false;
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Running;
            var workingPlan = ResetPlanForRun(plan);
            SetCurrentPlan(workingPlan, progress);

            foreach (var point in OrderByReviewSequence(workingPlan.ReviewPoints))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    workingPlan = SetWaitingPointsReady(workingPlan);
                    SetCurrentPlan(workingPlan, progress);
                    return CreateStatus(workingPlan, SequenceState, "Review sequence stopped.");
                }

                var currentPoint = point with
                {
                    State = EN_REVIEW_POINT_STATE.Current,
                    Judge = "WAIT"
                };
                workingPlan = UpdatePoint(workingPlan, currentPoint);
                SetCurrentPlan(workingPlan, progress);

                await MoveStageY(currentPoint, cancellationToken);

                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    workingPlan = UpdatePoint(workingPlan, currentPoint with { State = EN_REVIEW_POINT_STATE.Ready });
                    SetCurrentPlan(workingPlan, progress);
                    return CreateStatus(workingPlan, SequenceState, "Review sequence stopped.");
                }

                await MoveVisionX(currentPoint, cancellationToken);

                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    workingPlan = UpdatePoint(workingPlan, currentPoint with { State = EN_REVIEW_POINT_STATE.Ready });
                    SetCurrentPlan(workingPlan, progress);
                    return CreateStatus(workingPlan, SequenceState, "Review sequence stopped.");
                }

                var measurement = await MeasureVision(currentPoint, cancellationToken);

                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    workingPlan = UpdatePoint(workingPlan, currentPoint with { State = EN_REVIEW_POINT_STATE.Ready });
                    SetCurrentPlan(workingPlan, progress);
                    return CreateStatus(workingPlan, SequenceState, "Review sequence stopped.");
                }

                var measuredPoint = ApplyMeasurement(workingPlan, currentPoint, measurement);
                workingPlan = UpdatePoint(workingPlan, measuredPoint);
                SetCurrentPlan(workingPlan, progress);
            }

            SequenceState = EN_REVIEW_SEQUENCE_STATE.Completed;
            SetCurrentPlan(workingPlan, progress);
            await SaveResult(workingPlan, workingPlan.ReviewPoints, cancellationToken);
            return CreateStatus(workingPlan, SequenceState, "Review sequence completed.");
        }
        catch (OperationCanceledException)
        {
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
            var stoppedPlan = SetWaitingPointsReady(CurrentPlan ?? plan);
            SetCurrentPlan(stoppedPlan, progress);
            return CreateStatus(stoppedPlan, SequenceState, "Review sequence canceled.");
        }
        catch (Exception ex)
        {
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Failed;
            var failedPlan = SetWaitingPointsReady(CurrentPlan ?? plan);
            SetCurrentPlan(failedPlan, progress);
            return CreateStatus(failedPlan, SequenceState, ex.Message);
        }
        finally
        {
            _sequenceLock.Release();
        }
    }

    public void Stop()
    {
        _stopRequested = true;
        if (SequenceState == EN_REVIEW_SEQUENCE_STATE.Running)
        {
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopping;
        }
    }

    public ST_REVIEW_PLAN_POINT? ApplyReviewOffset(string holeKey)
    {
        if (SequenceState is EN_REVIEW_SEQUENCE_STATE.Running or EN_REVIEW_SEQUENCE_STATE.Stopping)
        {
            return null;
        }

        var normalizedHoleKey = NormalizeHoleKey(holeKey);
        if (string.IsNullOrWhiteSpace(normalizedHoleKey))
        {
            return null;
        }

        lock (_stateLock)
        {
            var point = _currentPlan?.ReviewPoints.FirstOrDefault(item =>
                item.HoleKey.Equals(normalizedHoleKey, StringComparison.OrdinalIgnoreCase));
            if (point is null || point.State is not (EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng))
            {
                return null;
            }

            var reviewOffsetDelta = CReviewCoordinateTransformer.VisionErrorToScannerOffset(
                point.ErrorX,
                point.ErrorY,
                point.HeadNo,
                _currentPlan!.VisionAxisMode);
            var updatedPoint = point with
            {
                ReviewOffsetX = point.ReviewOffsetX + reviewOffsetDelta.X,
                ReviewOffsetY = point.ReviewOffsetY + reviewOffsetDelta.Y
            };
            _currentPlan = UpdatePoint(_currentPlan!, updatedPoint);
            return updatedPoint;
        }
    }

    public async Task<ST_REVIEW_SEQUENCE_STATUS> RetryRemaining(
        Action<ST_REVIEW_PLAN>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = CurrentPlan;
        if (plan is null)
        {
            return new ST_REVIEW_SEQUENCE_STATUS(
                EN_REVIEW_SEQUENCE_STATE.Idle,
                0,
                0,
                0,
                "Review plan is empty.");
        }

        plan = SetWaitingPointsReady(plan);
        SetCurrentPlan(plan, progress);

        var points = plan.ReviewPoints
            .Where(point => point.State == EN_REVIEW_POINT_STATE.Ready);
        var orderedPoints = OrderByReviewSequence(points)
            .ToArray();

        if (orderedPoints.Length == 0)
        {
            return CreateStatus(plan, SequenceState, "Ready review point is empty.");
        }

        return await RunRetryPoints(orderedPoints, "Review ready point retry completed.", progress, cancellationToken);
    }

    public Task SaveResult(
        ST_REVIEW_PLAN plan,
        IReadOnlyList<ST_REVIEW_PLAN_POINT> results,
        CancellationToken cancellationToken = default)
    {
        return reviewResultFile.Save(
            new ST_REVIEW_RESULT_DATA(plan, results, DateTimeOffset.Now),
            cancellationToken);
    }

    private ST_REVIEW_PLAN CreatePlanCore(
        ST_RECIPE_DATA recipe,
        Func<ST_REVIEW_PLAN_POINT, bool> useSelector)
    {
        const int headCount = DefaultHeadCount;
        var cellCount = ReadRequiredPositiveInt(recipe, "CELL_COUNT");
        var toleranceX = ReadDouble(recipe, 0.030, "REVIEW_TOLERANCE_X");
        var toleranceY = ReadDouble(recipe, 0.030, "REVIEW_TOLERANCE_Y");
        var coordinateSettings = LoadReviewCoordinateSettings();
        var points = BuildHolePoints(recipe, headCount, cellCount, coordinateSettings.HeadLayout)
            .Select(point =>
            {
                var use = useSelector(point);

                return point with
                {
                    Use = use,
                    State = use ? EN_REVIEW_POINT_STATE.Ready : EN_REVIEW_POINT_STATE.Skip,
                    Judge = use ? "WAIT" : "-"
                };
            })
            .ToArray();

        return new ST_REVIEW_PLAN(
            recipe.Id,
            string.IsNullOrWhiteSpace(recipe.Name) ? recipe.Id : recipe.Name,
            headCount,
            cellCount,
            toleranceX,
            toleranceY,
            coordinateSettings.VisionAxisMode,
            DateTimeOffset.Now,
            points);
    }

    private ST_REVIEW_COORDINATE_SETTINGS LoadReviewCoordinateSettings()
    {
        var optionSettings = settingManager
            .LoadSection(EN_SETTING_TAB.Option)
            .GetAwaiter()
            .GetResult();
        var visionAxisMode = CReviewCoordinateTransformer.ParseVisionAxisMode(
            ReadSettingText(optionSettings, "", "VisionXFlip"),
            ReadSettingText(optionSettings, "", "VisionYFlip"),
            ReadSettingText(optionSettings, "", "VisionXyFlip"));

        var headFields = Enumerable.Range(1, 8)
            .Select(headNo =>
            {
                var position = ReadHeadPositionX(optionSettings, headNo);
                const double fallbackWidthX = 110.0;
                var widthX = ReadSettingDouble(
                    optionSettings,
                    fallbackWidthX,
                    $"H{headNo:00}_SCAN_FIELD_WIDTH_X",
                    $"H{headNo:00}_HEAD_FIELD_WIDTH_X");
                return new ST_REVIEW_HEAD_FIELD(headNo, position, widthX > 0.0 ? widthX : fallbackWidthX);
            })
            .ToArray();

        return new ST_REVIEW_COORDINATE_SETTINGS(
            new ST_REVIEW_HEAD_LAYOUT(headFields),
            visionAxisMode);
    }

    private static double ReadHeadPositionX(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        int headNo)
    {
        const double fallbackHead1PositionX = -5.0;
        const double fallbackHeadGapX = 200.0;
        var head1PositionX = ReadSettingDouble(
            settings,
            fallbackHead1PositionX,
            "H01_AK_POSITION_X");
        var headGapX = ReadSettingDouble(
            settings,
            fallbackHeadGapX,
            "HeadGapX");
        return head1PositionX + ((headNo - 1) * headGapX);
    }

    private static double ReadSettingDouble(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        double defaultValue,
        params string[] keys)
    {
        foreach (var setting in settings)
        {
            if (!keys.Any(key =>
                    key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(setting.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static string ReadSettingText(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        string defaultValue,
        params string[] keys)
    {
        foreach (var setting in settings)
        {
            if (!keys.Any(key =>
                    key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(setting.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return string.IsNullOrWhiteSpace(setting.Value)
                ? defaultValue
                : setting.Value.Trim();
        }

        return defaultValue;
    }

    private static IReadOnlyList<ST_REVIEW_PLAN_POINT> BuildHolePoints(
        ST_RECIPE_DATA recipe,
        int headCount,
        int cellCount,
        ST_REVIEW_HEAD_LAYOUT headLayout)
    {
        var akMarginX = ReadDouble(recipe, 55.0, "AK_MARGIN_X");
        var akMarginY = ReadDouble(recipe, 45.0, "AK_MARGIN_Y");
        var reviewOffsetX = ReadDouble(recipe, 0.0, "REVIEW_OFFSET_X");
        var reviewOffsetY = ReadDouble(recipe, 0.0, "REVIEW_OFFSET_Y");
        var globalPixelCountX = Math.Max(1, ReadInt(recipe, 1, "NUM_OF_PIXEL_X"));
        var globalPixelCountY = Math.Max(1, ReadInt(recipe, 1, "NUM_OF_PIXEL_Y"));
        var globalPitchX = ReadDouble(recipe, 0.0, "PITCH_X");
        var globalPitchY = ReadDouble(recipe, globalPitchX, "PITCH_Y");
        var points = new List<ST_REVIEW_PLAN_POINT>();
        var pointNo = 1;

        for (var cellNo = 1; cellNo <= cellCount; cellNo++)
        {
            var holeCount = ReadInt(
                recipe,
                -1,
                $"CELL{cellNo}_HOLE_COUNT");

            if (holeCount <= 0)
            {
                foreach (var point in CreateFallbackHoleGrid(
                    recipe,
                    pointNo,
                    cellNo,
                    headCount,
                    headLayout,
                    akMarginX,
                    akMarginY,
                    reviewOffsetX,
                    reviewOffsetY,
                    globalPixelCountX,
                    globalPixelCountY,
                    globalPitchX,
                    globalPitchY))
                {
                    points.Add(point);
                    pointNo++;
                }

                continue;
            }

            var cellBaseX = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X");
            var cellBaseY = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y");
            var rotation = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ROTATION");
            var radians = rotation * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            for (var holeNo = 1; holeNo <= holeCount; holeNo++)
            {
                var holePrefix = $"CELL{cellNo}_HOLE{holeNo}";
                var localX = ReadDouble(recipe, (holeNo - 1) * globalPitchX, $"{holePrefix}_X");
                var localY = ReadDouble(recipe, 0.0, $"{holePrefix}_Y");
                var rotatedX = (localX * cos) - (localY * sin);
                var rotatedY = (localX * sin) + (localY * cos);
                var designX = akMarginX + cellBaseX + rotatedX;
                var designY = akMarginY + cellBaseY + rotatedY;
                var pixelCountX = Math.Max(1, ReadInt(recipe, globalPixelCountX, $"{holePrefix}_NUM_OF_PIXEL_X"));
                var pixelCountY = Math.Max(1, ReadInt(recipe, globalPixelCountY, $"{holePrefix}_NUM_OF_PIXEL_Y"));
                var holeName = CReviewHoleNameFormatter.ToMatrixName(holeNo, pixelCountX);
                var reviewOffsetPrefix = $"CELL{cellNo}_{holeName}_REVIEW_OFFSET_";
                var holeReviewOffsetX = ReadDouble(
                    recipe,
                    reviewOffsetX,
                    $"{reviewOffsetPrefix}X");
                var holeReviewOffsetY = ReadDouble(
                    recipe,
                    reviewOffsetY,
                    $"{reviewOffsetPrefix}Y");
                var headNo = AssignHeadNo(designX, headCount, headLayout, akMarginX);

                points.Add(new ST_REVIEW_PLAN_POINT(
                    pointNo++,
                    ToHoleKey(cellNo, holeNo),
                    headNo,
                    cellNo,
                    holeNo,
                    pixelCountX,
                    pixelCountY,
                    false,
                    designX,
                    designY,
                    designX,
                    designY,
                    0.0,
                    0.0,
                    EN_REVIEW_POINT_STATE.Skip,
                    "-")
                {
                    ReviewOffsetX = holeReviewOffsetX,
                    ReviewOffsetY = holeReviewOffsetY
                });
            }
        }

        return points;
    }

    private static IReadOnlyList<ST_REVIEW_PLAN_POINT> CreateFallbackHoleGrid(
        ST_RECIPE_DATA recipe,
        int startPointNo,
        int cellNo,
        int headCount,
        ST_REVIEW_HEAD_LAYOUT headLayout,
        double akMarginX,
        double akMarginY,
        double reviewOffsetX,
        double reviewOffsetY,
        int globalPixelCountX,
        int globalPixelCountY,
        double globalPitchX,
        double globalPitchY)
    {
        var cellBaseX = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X");
        var cellBaseY = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y");
        var rotation = ReadDouble(recipe, 0.0, $"CELL{cellNo}_ROTATION");
        var pixelCountX = Math.Max(1, ReadInt(
            recipe,
            globalPixelCountX,
            $"CELL{cellNo}_NUM_OF_PIXEL_X"));
        var pixelCountY = Math.Max(1, ReadInt(
            recipe,
            globalPixelCountY,
            $"CELL{cellNo}_NUM_OF_PIXEL_Y"));
        var pitchX = ReadDouble(
            recipe,
            globalPitchX,
            $"CELL{cellNo}_PITCH_X");
        var pitchY = ReadDouble(
            recipe,
            globalPitchY,
            $"CELL{cellNo}_PITCH_Y");
        pitchX = pitchX > 0.0 ? pitchX : 1.0;
        pitchY = pitchY > 0.0 ? pitchY : pitchX;
        var calculatedPoints = CCellPointCalculator.Calculate(new ST_CELL_POINT_INPUT(
            cellNo,
            cellBaseX,
            cellBaseY,
            rotation,
            pixelCountX,
            pixelCountY,
            pitchX,
            pitchY,
            akMarginX,
            akMarginY));
        if (!calculatedPoints.IsValid)
        {
            return [];
        }

        var points = new List<ST_REVIEW_PLAN_POINT>(calculatedPoints.Points.Count);
        var pointNo = startPointNo;

        foreach (var point in calculatedPoints.Points)
        {
            var holeName = CReviewHoleNameFormatter.ToMatrixName(point.PointNo, pixelCountX);
            var reviewOffsetPrefix = $"CELL{cellNo}_{holeName}_REVIEW_OFFSET_";
            var holeReviewOffsetX = ReadDouble(
                recipe,
                reviewOffsetX,
                $"{reviewOffsetPrefix}X");
            var holeReviewOffsetY = ReadDouble(
                recipe,
                reviewOffsetY,
                $"{reviewOffsetPrefix}Y");
            var headNo = AssignHeadNo(point.X, headCount, headLayout, akMarginX);
            points.Add(new ST_REVIEW_PLAN_POINT(
                pointNo++,
                ToHoleKey(cellNo, point.PointNo),
                headNo,
                cellNo,
                point.PointNo,
                pixelCountX,
                pixelCountY,
                false,
                point.X,
                point.Y,
                point.X,
                point.Y,
                0.0,
                0.0,
                EN_REVIEW_POINT_STATE.Skip,
                "-")
            {
                ReviewOffsetX = holeReviewOffsetX,
                ReviewOffsetY = holeReviewOffsetY
            });
        }

        return points;
    }

    private static IReadOnlyCollection<string> BuildRuleHoleKeys(
        ST_REVIEW_RULE_DATA rule,
        ST_REVIEW_PLAN allPlan)
    {
        if (rule.RuleType is EN_REVIEW_RULE_TYPE.SamplePoint && rule.HoleKeys.Count > 0)
        {
            return rule.HoleKeys
                .Select(NormalizeHoleKey)
                .Where(key => allPlan.Points.Any(point => point.HoleKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (rule.HoleKeys.Count > 0 && rule.RuleType is not EN_REVIEW_RULE_TYPE.AllPoint)
        {
            return rule.HoleKeys
                .Select(NormalizeHoleKey)
                .Where(key => allPlan.Points.Any(point => point.HoleKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return rule.RuleType switch
        {
            EN_REVIEW_RULE_TYPE.AllPoint => allPlan.Points.Select(point => point.HoleKey).ToArray(),
            EN_REVIEW_RULE_TYPE.Edge => SelectEdgeKeys(allPlan),
            EN_REVIEW_RULE_TYPE.Center => SelectCenterKeys(allPlan),
            EN_REVIEW_RULE_TYPE.HeadPoint => allPlan.Points
                .Where(point => point.HeadNo == Math.Clamp(rule.HeadNo, 1, Math.Max(1, allPlan.HeadCount)))
                .Select(point => point.HoleKey)
                .ToArray(),
            EN_REVIEW_RULE_TYPE.CellPoint => allPlan.Points
                .Where(point => point.CellNo == Math.Clamp(rule.CellNo, 1, Math.Max(1, allPlan.CellCount)))
                .Select(point => point.HoleKey)
                .ToArray(),
            EN_REVIEW_RULE_TYPE.ZeroLine => SelectZeroLineKeys(allPlan, rule.ZeroPointCount),
            _ => []
        };
    }

    private static IReadOnlyCollection<string> SelectEdgeKeys(ST_REVIEW_PLAN plan)
    {
        return CReviewSampleRuleSelector.SelectEdgeHoleKeys(plan);
    }

    private static IReadOnlyCollection<string> SelectCenterKeys(ST_REVIEW_PLAN plan)
    {
        return CReviewSampleRuleSelector.SelectCenterHoleKeys(plan);
    }

    private static IReadOnlyCollection<string> SelectZeroLineKeys(
        ST_REVIEW_PLAN plan,
        int zeroPointCount)
    {
        if (plan.Points.Count == 0)
        {
            return [];
        }

        var targetY = (plan.Points.Min(point => point.DesignY) + plan.Points.Max(point => point.DesignY)) / 2.0;
        var count = zeroPointCount <= 0 ? Math.Min(5, plan.Points.Count) : Math.Min(zeroPointCount, plan.Points.Count);

        return plan.Points
            .OrderBy(point => Math.Abs(point.DesignY - targetY))
            .ThenBy(point => point.DesignX)
            .Take(count)
            .Select(point => point.HoleKey)
            .ToArray();
    }

    private async Task MoveStageY(
        ST_REVIEW_PLAN_POINT point,
        CancellationToken cancellationToken)
    {
        var command = FormatCommand(
            "REVIEW_STAGE_Y_MOVE",
            ("HOLE_KEY", point.HoleKey),
            ("POINT", point.PointNo.ToString(CultureInfo.InvariantCulture)),
            ("HEAD", point.HeadNo.ToString(CultureInfo.InvariantCulture)),
            ("CELL", point.CellNo.ToString(CultureInfo.InvariantCulture)),
            ("HOLE", point.HoleNo.ToString(CultureInfo.InvariantCulture)),
            ("Y", FormatDouble(point.ReviewTargetY)));

        await interfaceManager.ExecuteFunction(
            EN_EQP_MODULE.WonikCtrl,
            0,
            command,
            cancellationToken);
    }

    private async Task MoveVisionX(
        ST_REVIEW_PLAN_POINT point,
        CancellationToken cancellationToken)
    {
        var command = FormatCommand(
            "REVIEW_VISION_X_MOVE",
            ("HOLE_KEY", point.HoleKey),
            ("POINT", point.PointNo.ToString(CultureInfo.InvariantCulture)),
            ("HEAD", point.HeadNo.ToString(CultureInfo.InvariantCulture)),
            ("CELL", point.CellNo.ToString(CultureInfo.InvariantCulture)),
            ("HOLE", point.HoleNo.ToString(CultureInfo.InvariantCulture)),
            ("X", FormatDouble(point.ReviewTargetX)));

        await interfaceManager.ExecuteFunction(
            EN_EQP_MODULE.WonikCtrl,
            0,
            command,
            cancellationToken);
    }

    private async Task<ST_REVIEW_MEASURE_RESULT> MeasureVision(
        ST_REVIEW_PLAN_POINT point,
        CancellationToken cancellationToken)
    {
        var command = FormatCommand(
            "REVIEW_MEASURE",
            ("HOLE_KEY", point.HoleKey),
            ("POINT", point.PointNo.ToString(CultureInfo.InvariantCulture)),
            ("HEAD", point.HeadNo.ToString(CultureInfo.InvariantCulture)),
            ("CELL", point.CellNo.ToString(CultureInfo.InvariantCulture)),
            ("HOLE", point.HoleNo.ToString(CultureInfo.InvariantCulture)),
            ("X", FormatDouble(point.ReviewTargetX)),
            ("Y", FormatDouble(point.ReviewTargetY)));
        var response = await interfaceManager.ExecuteFunction(
            EN_EQP_MODULE.Vision,
            0,
            command,
            cancellationToken);
        await DelayForSimulation(cancellationToken);

        return ParseVisionResponse(response, point);
    }

    private async Task DelayForSimulation(CancellationToken cancellationToken)
    {
        if (!IsReviewSimulation())
        {
            return;
        }

        for (var step = 0; step < 30; step++)
        {
            if (_stopRequested)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private bool IsReviewSimulation()
    {
        return interfaceManager.IsSimulation ||
            interfaceManager.IsSimul(EN_EQP_MODULE.WonikCtrl, 0) ||
            interfaceManager.IsSimul(EN_EQP_MODULE.Vision, 0);
    }

    private static ST_REVIEW_PLAN_POINT ApplyMeasurement(
        ST_REVIEW_PLAN plan,
        ST_REVIEW_PLAN_POINT point,
        ST_REVIEW_MEASURE_RESULT measurement)
    {
        var visionErrorX = measurement.X - point.DesignX;
        var visionErrorY = measurement.Y - point.DesignY;
        var errorX = visionErrorX;
        var errorY = visionErrorY;
        var judge = !string.IsNullOrWhiteSpace(measurement.Judge) &&
            !measurement.Judge.Equals("WAIT", StringComparison.OrdinalIgnoreCase)
            ? measurement.Judge.ToUpperInvariant()
            : Math.Abs(errorX) <= plan.ToleranceX && Math.Abs(errorY) <= plan.ToleranceY
                ? "OK"
                : "NG";
        var state = judge.Equals("OK", StringComparison.OrdinalIgnoreCase)
            ? EN_REVIEW_POINT_STATE.Ok
            : EN_REVIEW_POINT_STATE.Ng;

        return point with
        {
            ReviewTargetX = point.DesignX + errorX,
            ReviewTargetY = point.DesignY + errorY,
            ErrorX = errorX,
            ErrorY = errorY,
            State = state,
            Judge = judge
        };
    }

    private static ST_REVIEW_MEASURE_RESULT ParseVisionResponse(
        string response,
        ST_REVIEW_PLAN_POINT point)
    {
        var values = SplitResponse(response);
        var hasX = TryReadDouble(values, "X", out var x) ||
            TryReadDouble(values, "REVIEW_X", out x) ||
            TryReadDouble(values, "MEASURE_X", out x);
        var hasY = TryReadDouble(values, "Y", out var y) ||
            TryReadDouble(values, "REVIEW_Y", out y) ||
            TryReadDouble(values, "MEASURE_Y", out y);
        var judge = values.TryGetValue("JUDGE", out var judgeValue)
            ? judgeValue
            : values.TryGetValue("RESULT", out var resultValue)
                ? resultValue
                : "";

        if (hasX && hasY)
        {
            return new ST_REVIEW_MEASURE_RESULT(x, y, judge, response);
        }

        var isSimulatedNg = point.PointNo % 11 == 0 || point.PointNo % 17 == 0;
        var simulatedX = point.ReviewTargetX - (isSimulatedNg ? 0.055 : 0.002);
        var simulatedY = point.ReviewTargetY + (isSimulatedNg ? -0.048 : -0.003);
        var simulatedJudge = string.IsNullOrWhiteSpace(judge) && isSimulatedNg
            ? "NG"
            : judge;

        return new ST_REVIEW_MEASURE_RESULT(simulatedX, simulatedY, simulatedJudge, response);
    }

    private static IReadOnlyDictionary<string, string> SplitResponse(string response)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = response
            .Split([';', ',', '|', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            {
                continue;
            }

            values[token[..separatorIndex].Trim()] = token[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    private static bool TryReadDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        out double value)
    {
        value = 0.0;

        return values.TryGetValue(key, out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatCommand(
        string command,
        params (string Key, string Value)[] arguments)
    {
        return string.Join(
            ";",
            new[] { command }.Concat(arguments.Select(argument => $"{argument.Key}={argument.Value}")));
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.000000", CultureInfo.InvariantCulture);
    }

    private void SetCurrentPlan(
        ST_REVIEW_PLAN plan,
        Action<ST_REVIEW_PLAN>? progress)
    {
        lock (_stateLock)
        {
            _currentPlan = plan;
        }

        progress?.Invoke(plan);
    }

    private static ST_REVIEW_PLAN ResetPlanForRun(ST_REVIEW_PLAN plan)
    {
        var firstHoleKey = OrderByReviewSequence(plan.ReviewPoints)
            .Select(point => point.HoleKey)
            .FirstOrDefault();

        return plan with
        {
            Points = plan.Points.Select(point => point.Use
                ? point with
                {
                    ErrorX = 0.0,
                    ErrorY = 0.0,
                    State = point.HoleKey.Equals(firstHoleKey, StringComparison.OrdinalIgnoreCase)
                        ? EN_REVIEW_POINT_STATE.Current
                        : EN_REVIEW_POINT_STATE.Ready,
                    Judge = "WAIT"
                }
                : point with
                {
                    State = EN_REVIEW_POINT_STATE.Skip,
                    Judge = "-"
                }).ToArray()
        };
    }

    private static ST_REVIEW_PLAN SetWaitingPointsReady(ST_REVIEW_PLAN plan)
    {
        return plan with
        {
            Points = plan.Points.Select(point => point.State == EN_REVIEW_POINT_STATE.Current
                ? point with { State = EN_REVIEW_POINT_STATE.Ready }
                : point).ToArray()
        };
    }

    private static ST_REVIEW_PLAN UpdatePoint(
        ST_REVIEW_PLAN plan,
        ST_REVIEW_PLAN_POINT point)
    {
        return plan with
        {
            Points = plan.Points.Select(item => item.HoleKey.Equals(point.HoleKey, StringComparison.OrdinalIgnoreCase) ? point : item).ToArray()
        };
    }

    private static ST_REVIEW_SEQUENCE_STATUS CreateStatus(
        ST_REVIEW_PLAN plan,
        EN_REVIEW_SEQUENCE_STATE state,
        string message)
    {
        var reviewPoints = plan.ReviewPoints;
        var completedCount = reviewPoints.Count(point => point.State is EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng);
        var ngCount = reviewPoints.Count(point => point.State == EN_REVIEW_POINT_STATE.Ng);

        return new ST_REVIEW_SEQUENCE_STATUS(
            state,
            reviewPoints.Count,
            completedCount,
            ngCount,
            message);
    }

    private async Task<ST_REVIEW_SEQUENCE_STATUS> RunRetryPoints(
        IReadOnlyList<ST_REVIEW_PLAN_POINT> retryPoints,
        string completedMessage,
        Action<ST_REVIEW_PLAN>? progress,
        CancellationToken cancellationToken)
    {
        if (!await _sequenceLock.WaitAsync(0, cancellationToken))
        {
            return CreateStatus(
                CurrentPlan!,
                EN_REVIEW_SEQUENCE_STATE.Running,
                "Review sequence is already running.");
        }

        try
        {
            var workingPlan = CurrentPlan!;
            _stopRequested = false;
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Running;

            foreach (var point in OrderByReviewSequence(retryPoints))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    return CreateStatus(workingPlan, SequenceState, "Review retry stopped.");
                }

                var currentPoint = point with
                {
                    State = EN_REVIEW_POINT_STATE.Current,
                    Judge = "WAIT"
                };
                workingPlan = UpdatePoint(workingPlan, currentPoint);
                SetCurrentPlan(workingPlan, progress);

                await MoveStageY(currentPoint, cancellationToken);
                await MoveVisionX(currentPoint, cancellationToken);
                var measurement = await MeasureVision(currentPoint, cancellationToken);

                if (_stopRequested)
                {
                    SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
                    workingPlan = UpdatePoint(workingPlan, currentPoint with { State = EN_REVIEW_POINT_STATE.Ready });
                    SetCurrentPlan(workingPlan, progress);
                    return CreateStatus(workingPlan, SequenceState, "Review retry stopped.");
                }

                var measuredPoint = ApplyMeasurement(workingPlan, currentPoint, measurement);
                workingPlan = UpdatePoint(workingPlan, measuredPoint);
                SetCurrentPlan(workingPlan, progress);
            }

            SequenceState = EN_REVIEW_SEQUENCE_STATE.Completed;
            await SaveResult(workingPlan, workingPlan.ReviewPoints, cancellationToken);
            return CreateStatus(workingPlan, SequenceState, completedMessage);
        }
        catch (OperationCanceledException)
        {
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Stopped;
            var stoppedPlan = SetWaitingPointsReady(CurrentPlan!);
            SetCurrentPlan(stoppedPlan, progress);
            return CreateStatus(stoppedPlan, SequenceState, "Review retry canceled.");
        }
        catch (Exception ex)
        {
            SequenceState = EN_REVIEW_SEQUENCE_STATE.Failed;
            var failedPlan = SetWaitingPointsReady(CurrentPlan!);
            SetCurrentPlan(failedPlan, progress);
            return CreateStatus(failedPlan, SequenceState, ex.Message);
        }
        finally
        {
            _sequenceLock.Release();
        }
    }

    private sealed record ST_REVIEW_MEASURE_RESULT(
        double X,
        double Y,
        string Judge,
        string RawResponse);

    private static IReadOnlyList<ST_REVIEW_PLAN_POINT> OrderByReviewSequence(
        IEnumerable<ST_REVIEW_PLAN_POINT> points)
    {
        var source = points
            .OrderBy(point => point.DesignY)
            .ThenBy(point => point.DesignX)
            .ThenBy(point => point.CellNo)
            .ThenBy(point => point.HoleNo)
            .ToArray();
        var rows = new List<List<ST_REVIEW_PLAN_POINT>>();

        foreach (var point in source)
        {
            if (rows.Count == 0 ||
                Math.Abs(point.DesignY - rows[^1][0].DesignY) > ReviewSequenceRowTolerance)
            {
                rows.Add(new List<ST_REVIEW_PLAN_POINT>());
            }

            rows[^1].Add(point);
        }

        var orderedPoints = new List<ST_REVIEW_PLAN_POINT>(source.Length);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var orderedRow = rowIndex % 2 == 0
                ? row
                    .OrderBy(point => point.DesignX)
                    .ThenBy(point => point.CellNo)
                    .ThenBy(point => point.HoleNo)
                : row
                    .OrderByDescending(point => point.DesignX)
                    .ThenBy(point => point.CellNo)
                    .ThenBy(point => point.HoleNo);

            orderedPoints.AddRange(orderedRow);
        }

        return orderedPoints;
    }

    private static int AssignHeadNo(
        double designX,
        int headCount,
        ST_REVIEW_HEAD_LAYOUT headLayout,
        double akMarginX)
    {
        if (headCount <= 0)
        {
            return 0;
        }

        for (var headNo = 1; headNo <= headCount; headNo++)
        {
            var field = headLayout.Fields.First(item => item.HeadNo == headNo);
            var centerX = akMarginX + field.PositionX;
            var halfWidth = field.ScanFieldWidthX / 2.0;
            var startX = centerX - halfWidth;
            var endX = centerX + halfWidth;
            if (designX >= startX && designX <= endX)
            {
                // Main preview and the MOF coordinate sample both check H1 -> H8.
                // In an overlapping Scan Field, the left Head therefore owns the Hole.
                return headNo;
            }
        }

        return 0;
    }

    private sealed record ST_REVIEW_HEAD_LAYOUT(
        IReadOnlyList<ST_REVIEW_HEAD_FIELD> Fields);

    private sealed record ST_REVIEW_HEAD_FIELD(
        int HeadNo,
        double PositionX,
        double ScanFieldWidthX);

    private sealed record ST_REVIEW_COORDINATE_SETTINGS(
        ST_REVIEW_HEAD_LAYOUT HeadLayout,
        EN_VISION_AXIS_MODE VisionAxisMode);

    public static string ToHoleKey(int cellNo, int holeNo)
    {
        return $"CELL{Math.Max(1, cellNo):000}_HOLE{Math.Max(1, holeNo):0000}";
    }

    public static string NormalizeHoleKey(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var holeIndex = text.IndexOf("_HOLE", StringComparison.OrdinalIgnoreCase);
        if (text.StartsWith("CELL", StringComparison.OrdinalIgnoreCase) && holeIndex > 4)
        {
            var cellDigits = new string(text[4..holeIndex].Where(char.IsDigit).ToArray());
            var holeDigits = new string(text[(holeIndex + 5)..].Where(char.IsDigit).ToArray());

            if (int.TryParse(cellDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellNo) &&
                int.TryParse(holeDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var holeNo) &&
                cellNo > 0 &&
                holeNo > 0)
            {
                return ToHoleKey(cellNo, holeNo);
            }
        }

        return "";
    }

    private static int ReadInt(
        ST_RECIPE_DATA recipe,
        int defaultValue,
        params string[] keys)
    {
        var value = ReadText(recipe, keys);

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            ? (int)Math.Round(doubleValue)
            : defaultValue;
    }

    private static int ReadRequiredPositiveInt(
        ST_RECIPE_DATA recipe,
        string key)
    {
        var value = ReadText(recipe, key);

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) &&
            intValue > 0)
        {
            return intValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) &&
            doubleValue > 0.0)
        {
            return (int)Math.Round(doubleValue);
        }

        throw new InvalidOperationException($"Recipe parameter {key} must be a positive number.");
    }

    private static double ReadDouble(
        ST_RECIPE_DATA recipe,
        double defaultValue,
        params string[] keys)
    {
        var value = ReadText(recipe, keys);

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            ? doubleValue
            : defaultValue;
    }

    private static string ReadText(
        ST_RECIPE_DATA recipe,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var parameter = recipe.Parameters.FirstOrDefault(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Equals(key, StringComparison.OrdinalIgnoreCase));

            if (parameter is not null && !string.IsNullOrWhiteSpace(parameter.Value))
            {
                return parameter.Value.Trim();
            }
        }

        return "";
    }
}

