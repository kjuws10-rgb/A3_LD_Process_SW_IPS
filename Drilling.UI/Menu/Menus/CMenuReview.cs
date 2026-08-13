using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Drilling.Common.Managers;
using Drilling.Common.Review;
using Drilling.UI.Menu;
using Drilling.UI.Popup;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuReview : IMenu
{
    private const int DefaultHeadCount = 8;
    private const int MaxHeadCount = 8;
    private const int DefaultCellCount = 20;
    private const string DefaultRuleFileName = "ALL_POINT.csv";

    private readonly IReviewManager _reviewManager;
    private readonly IReviewRuleFile _reviewRuleFile;
    private readonly IRecipeManager _recipeManager;
    private readonly Func<string> _selectedRecipeIdProvider;
    private readonly Action<string> _statusReporter;
    private readonly Action _refreshScreen;
    private readonly HashSet<string> _oneHoleAppliedMeasurementKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ST_REVIEW_PLAN_POINT> _oneHoleResults =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedSampleHoleKeys = new(StringComparer.OrdinalIgnoreCase);
    private int _headCount = DefaultHeadCount;
    private int _cellCount = DefaultCellCount;
    private int _sampleRuleHeadNo = 1;
    private int _sampleRuleCellNo = 1;
    private int _selectedSampleCellNo = 1;
    private int _selectedRunCellNo = 1;
    private int _selectedOneHoleCellNo = 1;
    private int _totalReviewPointCount;
    private int _activePlanPointCount;
    private int _sampleMapColumnCount = 8;
    private bool _isSampleCellDetailVisible;
    private bool _isRunCellDetailVisible;
    private bool _isOneHoleCellMap = true;
    private bool _isStartRequestPending;
    private string _displayedOneHoleMeasurementKey = "";
    private string _oneHoleKey = "";
    private string _oneHoleResultsRecipeId = "";
    private string _selectedRunHoleKey = "";
    private string _recipeId = "DRILL_A01";
    private string _sampleSelectionRecipeId = "";
    private string _selectedTab = "RUN";
    private string _selectedMode = "SAMPLE HOLE";
    private string _selectedRuleFile = DefaultRuleFileName;
    private string _selectionRuleText = "Default Sample";
    private EN_REVIEW_RULE_TYPE _selectedRuleType = EN_REVIEW_RULE_TYPE.SamplePoint;
    private ST_REVIEW_PLAN? _lastAllPlan;

    public CMenuReview(
        IReviewManager reviewManager,
        IReviewRuleFile reviewRuleFile,
        IRecipeManager recipeManager,
        Func<string> selectedRecipeIdProvider,
        Action<string> statusReporter,
        Action refreshScreen)
    {
        _reviewManager = reviewManager;
        _reviewRuleFile = reviewRuleFile;
        _recipeManager = recipeManager;
        _selectedRecipeIdProvider = selectedRecipeIdProvider;
        _statusReporter = statusReporter;
        _refreshScreen = refreshScreen;
        bool HandleSelectTabCommand1(object? _)
        {
            return !IsReviewExecutionActive;
        }

        SelectTabCommand = new CButtonCommand(SelectTab, HandleSelectTabCommand1);
        bool HandleSelectModeCommand2(object? _)
        {
            return !IsReviewExecutionActive;
        }

        SelectModeCommand = new CButtonCommand(SelectMode, HandleSelectModeCommand2);
        TogglePointCommand = new CButtonCommand(ToggleHole);
        SetSampleHoleSelectionCommand = new CButtonCommand(SetSampleHoleSelection);
        QuickSelectCommand = new CButtonCommand(ApplyQuickSelect);
        SelectOneHoleCellCommand = new CButtonCommand(SelectOneHoleCell);
        SelectOneHoleCommand = new CButtonCommand(SelectOneHole);
        BackToOneHoleCellMapCommand = new CButtonCommand(BackToOneHoleCellMap);
        void HandleApplyOneHoleReviewOffsetCommand3(object? _)
        {
            _ = ApplyOneHoleReviewOffset();
        }

        ApplyOneHoleReviewOffsetCommand = new CButtonCommand(HandleApplyOneHoleReviewOffsetCommand3);
        SelectRunCellCommand = new CButtonCommand(SelectRunCell);
        SelectRunHoleCommand = new CButtonCommand(SelectRunHole);
        BackToRunGlassPreviewCommand = new CButtonCommand(BackToRunGlassPreview);
        SelectSampleCellCommand = new CButtonCommand(SelectSampleCell);
        BackToSampleGlassPreviewCommand = new CButtonCommand(BackToSampleGlassPreview);
        void HandleStartCommand4(object? _)
        {
            _ = StartReviewSequence();
        }

        bool HandleStartCommand5(object? _)
        {
            return !IsReviewExecutionActive;
        }

        StartCommand = new CButtonCommand(
HandleStartCommand4,
HandleStartCommand5);
        void HandleStopCommand6(object? _)
        {
            StopReviewSequence();
        }

        StopCommand = new CButtonCommand(HandleStopCommand6);
        void HandleRetryCommand7(object? _)
        {
            _ = RetryRemainingReviewPoints();
        }

        RetryCommand = new CButtonCommand(HandleRetryCommand7);
        void HandleLoadRuleCommand8(object? _)
        {
            _ = LoadSelectedReviewRule();
        }

        LoadRuleCommand = new CButtonCommand(HandleLoadRuleCommand8);
        void HandleSaveRuleCommand9(object? _)
        {
            _ = SaveCurrentReviewRule();
        }

        SaveRuleCommand = new CButtonCommand(HandleSaveRuleCommand9);
    }

    public EN_MENU Menu
    {
        get
        {
            return EN_MENU.Review;
        }
    }

    public string Title
    {
        get
        {
            return "REVIEW / INSPECTION";
        }
    }

    public string Subtitle
    {
        get
        {
            return "Review hole selection, vision measurement result and re-measure workflow";
        }
    }

    public string PlanSummary
    {
        get
        {
            return $"{_activePlanPointCount} / {_totalReviewPointCount} holes";
        }
    }

    public int SampleMapColumnCount
    {
        get
        {
            return Math.Max(1, _sampleMapColumnCount);
        }
    }

    public bool IsRunTab
    {
        get
        {
            return _selectedTab.Equals("RUN", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsRunGlassPreviewVisible
    {
        get
        {
            return IsRunTab && !_isRunCellDetailVisible;
        }
    }

    public bool IsRunCellDetailVisible
    {
        get
        {
            return IsRunTab && _isRunCellDetailVisible;
        }
    }

    public string RunWorkspaceTitle
    {
        get
        {
            return _isRunCellDetailVisible
        ? $"Cell{_selectedRunCellNo} / Hole Detail"
        : "Glass / Cell Preview";
        }
    }

    public string RunWorkspaceSummary
    {
        get
        {
            return _isRunCellDetailVisible
        ? $"{RunCellHoleRows.Count} Holes"
        : RunGlassPreviewSummary;
        }
    }

    public bool IsSampleSelectTab
    {
        get
        {
            return _selectedTab.Equals("SAMPLE SELECT", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsSampleGlassPreviewVisible
    {
        get
        {
            return IsSampleSelectTab && !_isSampleCellDetailVisible;
        }
    }

    public bool IsSampleCellDetailVisible
    {
        get
        {
            return IsSampleSelectTab && _isSampleCellDetailVisible;
        }
    }

    public string SampleWorkspaceTitle
    {
        get
        {
            return _isSampleCellDetailVisible
        ? $"Cell{_selectedSampleCellNo} / Sample Hole Selection"
        : "Glass / Cell Preview";
        }
    }

    public string SampleWorkspaceSummary
    {
        get
        {
            bool CountRowCallback10(ST_REVIEW_POINT_SELECT_ROW row)
            {
                return row.Use;
            }

            return _isSampleCellDetailVisible
        ? $"{SampleCellHoleRows.Count(CountRowCallback10)} / {SampleCellHoleRows.Count} Holes Selected"
        : SampleGlassPreviewSummary;
        }
    }

    public string SampleRuleName
    {
        get
        {
            return GetSelectionRuleText();
        }
    }

    public IReadOnlyList<string> SampleHeadOptions
    {
        get
        {
            string SelectHeadNo11(int headNo)
            {
                return $"H{headNo:00}";
            }

            return Enumerable.Range(1, Math.Max(1, _headCount))
            .Select(SelectHeadNo11)
            .ToArray();
        }
    }

    public string SelectedSampleHead
    {
        get
        {
            return $"H{_sampleRuleHeadNo:00}";
        }

        set
        {
            var normalized = value?.Trim() ?? "";
            if (normalized.StartsWith('H'))
            {
                normalized = normalized[1..];
            }

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var headNo))
            {
                _sampleRuleHeadNo = Math.Clamp(headNo, 1, Math.Max(1, _headCount));
            }
        }
    }

    public IReadOnlyList<string> SampleCellOptions
    {
        get
        {
            string SelectCellNo12(int cellNo)
            {
                return $"Cell{cellNo}";
            }

            return Enumerable.Range(1, Math.Max(1, _cellCount))
            .Select(SelectCellNo12)
            .ToArray();
        }
    }

    public string SelectedSampleCell
    {
        get
        {
            return $"Cell{_sampleRuleCellNo}";
        }

        set
        {
            var normalized = value?.Trim() ?? "";
            if (normalized.StartsWith("Cell", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..];
            }

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellNo))
            {
                _sampleRuleCellNo = Math.Clamp(cellNo, 1, Math.Max(1, _cellCount));
            }
        }
    }

    public string SampleRuleSelectionSummary
    {
        get
        {
            bool FilterPoint13(ST_REVIEW_PLAN_POINT point)
            {
                return _selectedSampleHoleKeys.Contains(point.HoleKey);
            }

            int SelectPoint14(ST_REVIEW_PLAN_POINT point)
            {
                return point.CellNo;
            }

            return $"{_selectedSampleHoleKeys.Count} Holes / " +
        $"{_lastAllPlan?.Points.Where(FilterPoint13).Select(SelectPoint14).Distinct().Count() ?? 0} Cells";
        }
    }

    public string SampleRuleDescription
    {
        get
        {
            string Evaluate_selectedRuleTypeSwitch1()
            {
                var switchValue = _selectedRuleType;
                switch (switchValue)
                {
                    case EN_REVIEW_RULE_TYPE.AllPoint:
                        return "Select every Hole in every Cell.";
                    case EN_REVIEW_RULE_TYPE.Edge:
                        return "Select the outer row and column Holes of each Cell.";
                    case EN_REVIEW_RULE_TYPE.Center:
                        return "Select all inner Holes except the outer Edge row and column of each Cell.";
                    case EN_REVIEW_RULE_TYPE.HeadPoint:
                        return $"Select Holes assigned to H{_sampleRuleHeadNo:00}.";
                    case EN_REVIEW_RULE_TYPE.CellPoint:
                        return $"Select every Hole in Cell{_sampleRuleCellNo}.";
                    case EN_REVIEW_RULE_TYPE.ZeroLine:
                        return "Select reference line Holes.";
                    default:
                        return "Select or clear individual Holes in a Cell.";
                }
            }

            return Evaluate_selectedRuleTypeSwitch1();
        }
    }

    public bool IsOneHoleTab
    {
        get
        {
            return _selectedTab.Equals("ONE HOLE", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsOneHoleCellMap
    {
        get
        {
            return IsOneHoleTab && _isOneHoleCellMap;
        }
    }

    public bool IsOneHoleHoleMap
    {
        get
        {
            return IsOneHoleTab && !_isOneHoleCellMap;
        }
    }

    public string OneHoleWorkspaceTitle
    {
        get
        {
            return _isOneHoleCellMap
        ? "Glass / Cell Preview"
        : $"Cell{_selectedOneHoleCellNo} / Hole Selection";
        }
    }

    public string OneHoleWorkspaceSummary
    {
        get
        {
            return _isOneHoleCellMap
        ? OneHoleGlassPreviewSummary
        : $"{OneHoleCellHoleRows.Count} Holes / Selected {GetSelectedOneHoleName()}";
        }
    }

    public IReadOnlyList<ST_REVIEW_TAB_ITEM> Tabs { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_MODE_ITEM> Modes { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_SET_ROW> ReviewSets { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> TargetItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> SelectionSummaryItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> SelectedPointItems { get; private set; } = [];

    public ImageSource? RunGlassPreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> RunCellPreviewLabels { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_CURRENT_HOLE_MARKER> RunCurrentHoleMarkers { get; private set; } = [];

    public string RunGlassPreviewSummary { get; private set; } = "0 Cells / 0 Holes";

    public IReadOnlyList<ST_REVIEW_RUN_HOLE_ROW> RunCellHoleRows { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_RUN_HOLE_MATRIX_ROW> RunCellHoleMatrixRows { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_POINT_SELECT_ROW> PointSelectRows { get; private set; } = [];

    public ImageSource? SampleGlassPreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> SampleCellPreviewLabels { get; private set; } = [];

    public string SampleGlassPreviewSummary { get; private set; } = "0 Cells / 0 Holes Selected";

    public IReadOnlyList<ST_REVIEW_POINT_SELECT_ROW> SampleCellHoleRows { get; private set; } = [];

    public ImageSource? OneHoleGlassPreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> OneHoleCellPreviewLabels { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_CURRENT_HOLE_MARKER> OneHoleCurrentHoleMarkers { get; private set; } = [];

    public string OneHoleGlassPreviewSummary { get; private set; } = "0 Cells / 0 Holes";

    public IReadOnlyList<ST_REVIEW_RUN_HOLE_ROW> OneHoleCellHoleRows { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_RUN_HOLE_MATRIX_ROW> OneHoleCellHoleMatrixRows { get; private set; } = [];

    public bool CanApplyOneHoleReviewOffset { get; private set; }

    public IReadOnlyList<ST_REVIEW_RESULT_ROW> ResultRows { get; private set; } = [];

    public IReadOnlyList<ST_REVIEW_HISTORY_ROW> HistoryRows { get; private set; } = [];

    public IReadOnlyList<string> RuleFiles { get; private set; } = [];

    public string SelectedRuleFile
    {
        get
        {
            return _selectedRuleFile;
        }

        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _selectedRuleFile = NormalizeRuleFileInput(value);
            }
        }
    }

    public CButtonCommand SelectTabCommand { get; }

    public CButtonCommand SelectModeCommand { get; }

    public CButtonCommand TogglePointCommand { get; }

    public CButtonCommand SetSampleHoleSelectionCommand { get; }

    public CButtonCommand QuickSelectCommand { get; }

    public CButtonCommand SelectOneHoleCellCommand { get; }

    public CButtonCommand SelectOneHoleCommand { get; }

    public CButtonCommand BackToOneHoleCellMapCommand { get; }

    public CButtonCommand ApplyOneHoleReviewOffsetCommand { get; }

    public CButtonCommand SelectRunCellCommand { get; }

    public CButtonCommand SelectRunHoleCommand { get; }

    public CButtonCommand BackToRunGlassPreviewCommand { get; }

    public CButtonCommand SelectSampleCellCommand { get; }

    public CButtonCommand BackToSampleGlassPreviewCommand { get; }

    public CButtonCommand StartCommand { get; }

    public CButtonCommand StopCommand { get; }

    public CButtonCommand RetryCommand { get; }

    public CButtonCommand LoadRuleCommand { get; }

    public CButtonCommand SaveRuleCommand { get; }

    public void ResetForMenuOpen()
    {
        if (_reviewManager.SequenceState is EN_REVIEW_SEQUENCE_STATE.Running or EN_REVIEW_SEQUENCE_STATE.Stopping)
        {
            return;
        }

        _selectedTab = "RUN";
        _selectedMode = "ALL HOLE";
        _selectedRuleType = EN_REVIEW_RULE_TYPE.AllPoint;
        _selectionRuleText = "All Hole";
        _isRunCellDetailVisible = false;
        _isSampleCellDetailVisible = false;
    }

    public async Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recipe = await LoadSelectedRecipe(cancellationToken) ?? CreateFallbackRecipe();
        var allPlan = _reviewManager.CreatePlan(recipe, Array.Empty<string>());
        _lastAllPlan = allPlan;
        ApplyRecipeContext(recipe, allPlan);
        await RefreshRuleFiles(cancellationToken);
        ApplyScreenData(recipe, allPlan);

        return new CScreenViewModel(
            EN_MENU.Review,
            Title,
            Subtitle,
            [
                new("Mode", IsOneHoleTab ? "ONE HOLE" : _selectedMode),
                new("Hole", $"{_activePlanPointCount} / {_totalReviewPointCount}"),
                new("Result", "Ready")
            ],
            [
                new("Review Plan", []),
                new("Review Result", []),
                new("Review History", [])
            ],
            review: this);
    }

    private ST_RECIPE_DATA CreateFallbackRecipe()
    {
        return new ST_RECIPE_DATA(
            _recipeId,
            _recipeId,
            [],
            []);
    }

    private async Task<ST_RECIPE_DATA?> LoadSelectedRecipe(CancellationToken cancellationToken)
    {
        var recipes = await _recipeManager.LoadRecipes(cancellationToken);
        if (recipes.Count == 0)
        {
            return null;
        }

        var selectedRecipeId = _selectedRecipeIdProvider();
        if (!string.IsNullOrWhiteSpace(selectedRecipeId))
        {
            bool MatchRecipe15(ST_RECIPE_DATA recipe)
            {
                return recipe.Id.Equals(selectedRecipeId, StringComparison.OrdinalIgnoreCase);
            }

            var selectedRecipe = recipes.FirstOrDefault(MatchRecipe15);

            if (selectedRecipe is not null)
            {
                return selectedRecipe;
            }
        }
        bool MatchRecipe16(ST_RECIPE_DATA recipe)
        {
            return recipe.Id.Equals("DRILL_A01", StringComparison.OrdinalIgnoreCase);
        }

        return recipes.FirstOrDefault(MatchRecipe16)
            ?? recipes[0];
    }

    private void ApplyRecipeContext(
        ST_RECIPE_DATA recipe,
        ST_REVIEW_PLAN allPlan)
    {
        _headCount = Math.Clamp(allPlan.HeadCount, 1, MaxHeadCount);
        _sampleRuleHeadNo = Math.Clamp(_sampleRuleHeadNo, 1, _headCount);
        _cellCount = Math.Max(1, allPlan.CellCount);
        _sampleRuleCellNo = Math.Clamp(_sampleRuleCellNo, 1, _cellCount);
        _recipeId = string.IsNullOrWhiteSpace(recipe.Id) ? "DRILL_A01" : recipe.Id;
        var isSampleSelectionRecipeChanged =
            !_sampleSelectionRecipeId.Equals(_recipeId, StringComparison.OrdinalIgnoreCase);
        var isOneHoleResultRecipeChanged =
            !_oneHoleResultsRecipeId.Equals(_recipeId, StringComparison.OrdinalIgnoreCase);
        _totalReviewPointCount = allPlan.TotalPointCount;

        var recipeRuleFile = ReadRecipeString(recipe, "REVIEW_RULE_FILE");
        if (!string.IsNullOrWhiteSpace(recipeRuleFile))
        {
            _selectedRuleFile = NormalizeRuleFileInput(recipeRuleFile);
        }
        string SelectPoint17(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        var validKeys = allPlan.Points.Select(SelectPoint17).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (isOneHoleResultRecipeChanged)
        {
            _oneHoleAppliedMeasurementKeys.Clear();
            _oneHoleResults.Clear();
            _displayedOneHoleMeasurementKey = "";
            _oneHoleResultsRecipeId = _recipeId;
        }
        else
        {
            bool FilterKey18(string key)
            {
                return !validKeys.Contains(key);
            }

            foreach (var invalidKey in _oneHoleResults.Keys.Where(FilterKey18).ToArray())
            {
                _oneHoleAppliedMeasurementKeys.Remove(invalidKey);
                _oneHoleResults.Remove(invalidKey);
            }
        }

        if (isSampleSelectionRecipeChanged)
        {
            _selectedSampleHoleKeys.Clear();
            foreach (var holeKey in CreateDefaultSampleHoleKeys(allPlan))
            {
                _selectedSampleHoleKeys.Add(holeKey);
            }

            _sampleSelectionRecipeId = _recipeId;
        }
        else
        {
            bool RemoveWhereKeyCallback19(string key)
            {
                return !validKeys.Contains(key);
            }

            _selectedSampleHoleKeys.RemoveWhere(RemoveWhereKeyCallback19);
        }

        _selectedOneHoleCellNo = Math.Clamp(_selectedOneHoleCellNo, 1, _cellCount);
        _selectedSampleCellNo = Math.Clamp(_selectedSampleCellNo, 1, _cellCount);
        _sampleMapColumnCount = CalculateHoleMapColumnCount(allPlan, _selectedSampleCellNo);
        _selectedRunCellNo = Math.Clamp(_selectedRunCellNo, 1, _cellCount);

        if (!string.IsNullOrWhiteSpace(_selectedRunHoleKey) &&
            !validKeys.Contains(_selectedRunHoleKey))
        {
            _selectedRunHoleKey = "";
        }

        if (!string.IsNullOrWhiteSpace(_oneHoleKey) && !validKeys.Contains(_oneHoleKey))
        {
            _oneHoleKey = "";
            _isOneHoleCellMap = true;
        }

        if (!string.IsNullOrWhiteSpace(_displayedOneHoleMeasurementKey) &&
            !validKeys.Contains(_displayedOneHoleMeasurementKey))
        {
            _displayedOneHoleMeasurementKey = "";
        }
    }

    private static string ReadRecipeString(
        ST_RECIPE_DATA? recipe,
        params string[] keys)
    {
        if (recipe is null)
        {
            return "";
        }

        foreach (var key in keys)
        {
            bool MatchItem20(ST_RECIPE_PARAM item)
            {
                return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                item.Name.Equals(key, StringComparison.OrdinalIgnoreCase);
            }

            var parameter = recipe.Parameters.FirstOrDefault(MatchItem20);

            if (parameter is not null && !string.IsNullOrWhiteSpace(parameter.Value))
            {
                return parameter.Value.Trim();
            }
        }

        return "";
    }

    private async Task RefreshRuleFiles(CancellationToken cancellationToken)
    {
        RuleFiles = await _reviewRuleFile.List(cancellationToken);

        if (RuleFiles.Count == 0)
        {
            _selectedRuleFile = DefaultRuleFileName;
            return;
        }
        bool CheckFile21(string file)
        {
            return file.Equals(_selectedRuleFile, StringComparison.OrdinalIgnoreCase);
        }

        if (!RuleFiles.Any(CheckFile21))
        {
            _selectedRuleFile = RuleFiles[0];
        }
    }

    private void SelectTab(object? parameter)
    {
        if (IsReviewExecutionActive)
        {
            _statusReporter("Review is running. Stop the current review before changing the tab.");
            return;
        }

        if (parameter is not string tab || string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        _selectedTab = tab;
        if (IsOneHoleTab)
        {
            _isOneHoleCellMap = true;
            _oneHoleKey = "";
        }
        else if (IsSampleSelectTab)
        {
            _isSampleCellDetailVisible = false;
        }

        _statusReporter($"Review tab selected: {_selectedTab}");
        _refreshScreen();
    }

    private void SelectMode(object? parameter)
    {
        if (IsReviewExecutionActive)
        {
            _statusReporter("Review is running. Stop the current review before changing the mode.");
            return;
        }

        if (parameter is not string mode || string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        _selectedMode = mode;
        EN_REVIEW_RULE_TYPE EvaluateValueSwitch2()
        {
            var switchValue = mode.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "ALL HOLE":
                    return EN_REVIEW_RULE_TYPE.AllPoint;
                case "ZERO DEFENSE":
                    return EN_REVIEW_RULE_TYPE.ZeroLine;
                default:
                    return EN_REVIEW_RULE_TYPE.SamplePoint;
            }
        }

        _selectedRuleType = EvaluateValueSwitch2();
        string Evaluate_selectedRuleTypeSwitch3()
        {
            var switchValue = _selectedRuleType;
            switch (switchValue)
            {
                case EN_REVIEW_RULE_TYPE.AllPoint:
                    return "All Hole";
                case EN_REVIEW_RULE_TYPE.ZeroLine:
                    return "0-Line";
                default:
                    return _selectionRuleText is "All Hole" or "0-Line" ? "Manual Sample" : _selectionRuleText;
            }
        }

        _selectionRuleText = Evaluate_selectedRuleTypeSwitch3();
        _isRunCellDetailVisible = false;
        _selectedRunHoleKey = "";
        _statusReporter($"Review mode selected: {_selectedMode}");
        _refreshScreen();
    }

    private void ToggleHole(object? parameter)
    {
        var holeKey = CReviewManager.NormalizeHoleKey(parameter?.ToString() ?? "");
        if (string.IsNullOrWhiteSpace(holeKey))
        {
            return;
        }

        if (!_selectedSampleHoleKeys.Add(holeKey))
        {
            _selectedSampleHoleKeys.Remove(holeKey);
        }
        bool MatchItem22(ST_REVIEW_PLAN_POINT item)
        {
            return item.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase);
        }

        var point = _lastAllPlan?.Points.FirstOrDefault(MatchItem22);
        if (point is not null)
        {
            _sampleRuleHeadNo = point.HeadNo;
            _sampleRuleCellNo = point.CellNo;
        }

        _selectedMode = "SAMPLE HOLE";
        _selectedRuleType = EN_REVIEW_RULE_TYPE.SamplePoint;
        _selectionRuleText = "Manual Sample";
        _statusReporter($"Review hole selection updated: {_selectedSampleHoleKeys.Count} holes.");
        _refreshScreen();
    }

    private void SetSampleHoleSelection(object? parameter)
    {
        if (parameter is not ST_REVIEW_SAMPLE_DRAG_SELECTION selection ||
            selection.HoleKeys.Count == 0)
        {
            return;
        }

        ST_REVIEW_PLAN_POINT? lastPoint = null;
        var changedCount = 0;
        bool FilterKey23(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var holeKey in selection.HoleKeys
                     .Select(CReviewManager.NormalizeHoleKey)
                     .Where(FilterKey23)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool MatchItem24(ST_REVIEW_PLAN_POINT item)
            {
                return item.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase);
            }

            var point = _lastAllPlan?.Points.FirstOrDefault(MatchItem24);
            if (point is null)
            {
                continue;
            }

            var changed = selection.Use
                ? _selectedSampleHoleKeys.Add(point.HoleKey)
                : _selectedSampleHoleKeys.Remove(point.HoleKey);
            if (!changed)
            {
                continue;
            }

            changedCount++;
            lastPoint = point;
        }

        if (changedCount == 0)
        {
            return;
        }

        if (lastPoint is not null)
        {
            _sampleRuleHeadNo = lastPoint.HeadNo;
            _sampleRuleCellNo = lastPoint.CellNo;
        }

        _selectedMode = "SAMPLE HOLE";
        _selectedRuleType = EN_REVIEW_RULE_TYPE.SamplePoint;
        _selectionRuleText = "Manual Sample";
        _statusReporter(
            $"Review hole drag selection updated: {changedCount} holes, {_selectedSampleHoleKeys.Count} selected.");
        _refreshScreen();
    }

    private void ApplyQuickSelect(object? parameter)
    {
        var allPlan = _lastAllPlan;
        var rule = parameter?.ToString()?.Trim().ToUpperInvariant() ?? "";
        if (allPlan is null || string.IsNullOrWhiteSpace(rule))
        {
            return;
        }

        _selectedSampleHoleKeys.Clear();

        switch (rule)
        {
            case "ALL":
                string SelectPoint25(ST_REVIEW_PLAN_POINT point)
                {
                    return point.HoleKey;
                }

                foreach (var key in allPlan.Points.Select(SelectPoint25))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "ALL HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.AllPoint;
                _selectionRuleText = "All Hole";
                break;
            case "CLEAR":
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.SamplePoint;
                _selectionRuleText = "None";
                break;
            case "EDGE":
                foreach (var key in SelectEdgeKeys(allPlan))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.Edge;
                _selectionRuleText = "Edge";
                break;
            case "CENTER":
                foreach (var key in SelectCenterKeys(allPlan))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.Center;
                _selectionRuleText = "Center";
                break;
            case "HEAD":
                bool FilterPoint26(ST_REVIEW_PLAN_POINT point)
                {
                    return point.HeadNo == _sampleRuleHeadNo;
                }

                string SelectPoint27(ST_REVIEW_PLAN_POINT point)
                {
                    return point.HoleKey;
                }

                foreach (var key in allPlan.Points.Where(FilterPoint26).Select(SelectPoint27))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.HeadPoint;
                _selectionRuleText = $"Head Hole H{_sampleRuleHeadNo:00}";
                break;
            case "CELL":
                _selectedSampleCellNo = _sampleRuleCellNo;
                bool FilterPoint28(ST_REVIEW_PLAN_POINT point)
                {
                    return point.CellNo == _sampleRuleCellNo;
                }

                string SelectPoint29(ST_REVIEW_PLAN_POINT point)
                {
                    return point.HoleKey;
                }

                foreach (var key in allPlan.Points.Where(FilterPoint28).Select(SelectPoint29))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.CellPoint;
                _selectionRuleText = $"Cell Hole CELL{_sampleRuleCellNo:00}";
                break;
            case "ZERO":
                foreach (var key in SelectZeroLineKeys(allPlan, 0))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "ZERO DEFENSE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.ZeroLine;
                _selectionRuleText = "0-Line";
                break;
            default:
                foreach (var key in CreateDefaultSampleHoleKeys(allPlan))
                {
                    _selectedSampleHoleKeys.Add(key);
                }
                _selectedMode = "SAMPLE HOLE";
                _selectedRuleType = EN_REVIEW_RULE_TYPE.SamplePoint;
                _selectionRuleText = "Default Sample";
                break;
        }

        _statusReporter($"Review hole rule applied: {rule}, {_selectedSampleHoleKeys.Count} holes.");
        _refreshScreen();
    }

    private void SelectOneHoleCell(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellNo))
        {
            return;
        }

        _selectedOneHoleCellNo = Math.Clamp(cellNo, 1, _cellCount);
        _oneHoleKey = "";
        _isOneHoleCellMap = false;

        _statusReporter($"One Hole cell selected: CELL{_selectedOneHoleCellNo:00}");
        _refreshScreen();
    }

    private void SelectOneHole(object? parameter)
    {
        var holeKey = CReviewManager.NormalizeHoleKey(parameter?.ToString() ?? "");
        bool MatchItem30(ST_REVIEW_PLAN_POINT item)
        {
            return item.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase);
        }

        var point = _lastAllPlan?.Points.FirstOrDefault(MatchItem30);
        if (point is null)
        {
            return;
        }

        _oneHoleKey = point.HoleKey;
        _displayedOneHoleMeasurementKey = _oneHoleResults.ContainsKey(point.HoleKey)
            ? point.HoleKey
            : "";

        _selectedOneHoleCellNo = point.CellNo;
        _isOneHoleCellMap = false;
        _statusReporter($"One Hole selected: {point.CellName} / {point.PointName}");
        _refreshScreen();
    }

    private void BackToOneHoleCellMap(object? parameter)
    {
        _isOneHoleCellMap = true;
        _statusReporter("One Hole cell map opened.");
        _refreshScreen();
    }

    private async Task ApplyOneHoleReviewOffset()
    {
        if (!IsOneHoleTab || string.IsNullOrWhiteSpace(_displayedOneHoleMeasurementKey))
        {
            _statusReporter("Select and review one hole before applying Review Offset.");
            return;
        }

        if (_reviewManager.SequenceState is EN_REVIEW_SEQUENCE_STATE.Running or EN_REVIEW_SEQUENCE_STATE.Stopping)
        {
            _statusReporter("Review Offset cannot be applied while review measurement is running.");
            return;
        }

        if (_oneHoleAppliedMeasurementKeys.Contains(_displayedOneHoleMeasurementKey))
        {
            _statusReporter("Review Offset has already been applied to this measurement. Start a new measurement first.");
            _refreshScreen();
            return;
        }
        bool MatchPoint31(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(_displayedOneHoleMeasurementKey, StringComparison.OrdinalIgnoreCase);
        }

        var measuredPoint = _reviewManager.CurrentPlan?.ReviewPoints.FirstOrDefault(MatchPoint31)
            ?? _oneHoleResults.GetValueOrDefault(_displayedOneHoleMeasurementKey);
        if (measuredPoint is null ||
            measuredPoint.State is not (EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng))
        {
            _statusReporter("Review Offset can be applied after the displayed hole measurement is completed.");
            _refreshScreen();
            return;
        }

        var axisMode = _reviewManager.CurrentPlan?.VisionAxisMode ?? EN_VISION_AXIS_MODE.Normal;
        var reviewOffsetDelta = CReviewCoordinateTransformer.VisionErrorToScannerOffset(
            measuredPoint.ErrorX,
            measuredPoint.ErrorY,
            measuredPoint.HeadNo,
            axisMode);
        var pendingPoint = measuredPoint with
        {
            ReviewOffsetX = measuredPoint.ReviewOffsetX + reviewOffsetDelta.X,
            ReviewOffsetY = measuredPoint.ReviewOffsetY + reviewOffsetDelta.Y
        };

        if (!ConfirmReviewOffsetApply(measuredPoint, axisMode, reviewOffsetDelta, pendingPoint))
        {
            _statusReporter("Review Offset apply canceled.");
            _refreshScreen();
            return;
        }

        try
        {
            await SaveReviewOffsetToRecipe(pendingPoint);
        }
        catch (Exception ex)
        {
            _statusReporter($"Review Offset save failed: {ex.Message}");
            _refreshScreen();
            return;
        }

        var updatedPoint = _reviewManager.ApplyReviewOffset(_displayedOneHoleMeasurementKey)
            ?? pendingPoint;
        _oneHoleResults[updatedPoint.HoleKey] = updatedPoint;
        _oneHoleAppliedMeasurementKeys.Add(updatedPoint.HoleKey);
        _statusReporter(
            $"Review Offset applied and recipe saved: Cell{updatedPoint.CellNo}-{ToMatrixHoleName(updatedPoint)}, " +
            $"X {FormatSigned(updatedPoint.ReviewOffsetX)} / Y {FormatSigned(updatedPoint.ReviewOffsetY)} mm.");
        _refreshScreen();
    }

    private static bool ConfirmReviewOffsetApply(
        ST_REVIEW_PLAN_POINT measuredPoint,
        EN_VISION_AXIS_MODE axisMode,
        ST_REVIEW_COORDINATE_OFFSET reviewOffsetDelta,
        ST_REVIEW_PLAN_POINT pendingPoint)
    {
        var holeName = ToMatrixHoleName(measuredPoint);
        var conversionFormula = GetReviewOffsetConversionFormula(
            measuredPoint.HeadNo,
            axisMode);
        var message =
            $"Cell{measuredPoint.CellNo}-{holeName} / " +
            $"Head {measuredPoint.HeadNo:00}\n\n" +
            $"Error Amount\n" +
            $"X {FormatSigned(measuredPoint.ErrorX)} / " +
            $"Y {FormatSigned(measuredPoint.ErrorY)} mm\n\n" +
            $"Conversion\n" +
            $"GX = {conversionFormula.Gx} → {FormatSigned(reviewOffsetDelta.X)} mm\n" +
            $"GY = {conversionFormula.Gy} → {FormatSigned(reviewOffsetDelta.Y)} mm\n\n" +
            $"Review Offset\n" +
            $"GX {FormatSigned(measuredPoint.ReviewOffsetX)} " +
            $"{FormatCalculationOperand(reviewOffsetDelta.X)} = " +
            $"{FormatSigned(pendingPoint.ReviewOffsetX)} mm\n" +
            $"GY {FormatSigned(measuredPoint.ReviewOffsetY)} " +
            $"{FormatCalculationOperand(reviewOffsetDelta.Y)} = " +
            $"{FormatSigned(pendingPoint.ReviewOffsetY)} mm\n\n" +
            "Apply and save to the recipe?";
        var dialog = new CRecipeConfirmDialog(
            "Apply Review Offset",
            message,
            "APPLY",
            useDangerStyle: false)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    private static (string Gx, string Gy) GetReviewOffsetConversionFormula(
        int headNo,
        EN_VISION_AXIS_MODE axisMode)
    {
        var formula = CReviewCoordinateTransformer.VisionErrorToScannerFormula(
            headNo,
            axisMode);
        return (formula.Gx, formula.Gy);
    }

    private static string FormatCalculationOperand(double value)
    {
        return value < 0.0
            ? $"- {Math.Abs(value).ToString("0.000", CultureInfo.InvariantCulture)}"
            : $"+ {value.ToString("0.000", CultureInfo.InvariantCulture)}";
    }

    private async Task SaveReviewOffsetToRecipe(ST_REVIEW_PLAN_POINT point)
    {
        var recipe = await LoadSelectedRecipe(CancellationToken.None)
            ?? throw new InvalidOperationException("No recipe is selected.");
        var parameters = recipe.Parameters.ToList();

        UpsertReviewOffsetParameter(parameters, point, "X", point.ReviewOffsetX);
        UpsertReviewOffsetParameter(parameters, point, "Y", point.ReviewOffsetY);

        await _recipeManager.SaveRecipe(recipe with { Parameters = parameters });
    }

    private static void UpsertReviewOffsetParameter(
        List<ST_RECIPE_PARAM> parameters,
        ST_REVIEW_PLAN_POINT point,
        string axis,
        double value)
    {
        var normalizedAxis = axis.Equals("Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "X";
        var key = $"CELL{point.CellNo}_{point.HoleName}_REVIEW_OFFSET_{normalizedAxis}";
        var valueText = value.ToString("0.000000", CultureInfo.InvariantCulture);
        bool HandleParameterIndex32(ST_RECIPE_PARAM parameter)
        {
            return parameter.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var parameterIndex = parameters.FindIndex(HandleParameterIndex32);

        if (parameterIndex >= 0)
        {
            parameters[parameterIndex] = parameters[parameterIndex] with { Value = valueText };
        }
        else
        {
            parameters.Add(new ST_RECIPE_PARAM(
                $"Hole {point.HoleName} Review Offset {normalizedAxis}",
                valueText,
                "mm",
                "-100000 - 100000",
                "0",
                "CELL",
                "HOLE",
                key,
                "Per-hole Review Offset",
                true,
                true,
                0,
                EN_RECIPE_DATA_TYPE.Double,
                0.0,
                -100000.0,
                100000.0));
        }
    }

    private void SelectRunCell(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellNo))
        {
            return;
        }

        _selectedRunCellNo = Math.Clamp(cellNo, 1, _cellCount);
        _selectedRunHoleKey = "";
        _isRunCellDetailVisible = true;
        _statusReporter($"Review Cell selected: Cell{_selectedRunCellNo}");
        _refreshScreen();
    }

    private void SelectRunHole(object? parameter)
    {
        var holeKey = CReviewManager.NormalizeHoleKey(parameter?.ToString() ?? "");
        bool MatchItem33(ST_REVIEW_PLAN_POINT item)
        {
            return item.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase);
        }

        var point = _lastAllPlan?.Points.FirstOrDefault(MatchItem33);
        if (point is null)
        {
            return;
        }

        _selectedRunCellNo = point.CellNo;
        _selectedRunHoleKey = point.HoleKey;
        _isRunCellDetailVisible = true;
        _statusReporter($"Review Hole selected: Cell{point.CellNo} / {ToMatrixHoleName(point)}");
        _refreshScreen();
    }

    private void BackToRunGlassPreview(object? parameter)
    {
        _isRunCellDetailVisible = false;
        _statusReporter("Review Glass preview opened.");
        _refreshScreen();
    }

    private void SelectSampleCell(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellNo))
        {
            return;
        }

        _selectedSampleCellNo = Math.Clamp(cellNo, 1, _cellCount);
        _sampleRuleCellNo = _selectedSampleCellNo;
        _isSampleCellDetailVisible = true;
        _statusReporter($"Sample Cell selected: Cell{_selectedSampleCellNo}");
        _refreshScreen();
    }

    private void BackToSampleGlassPreview(object? parameter)
    {
        _isSampleCellDetailVisible = false;
        _statusReporter("Sample Glass preview opened.");
        _refreshScreen();
    }

    private ST_REVIEW_PLAN ResolveReviewPlan(
        ST_RECIPE_DATA recipe,
        ST_REVIEW_PLAN allPlan)
    {
        var targetKeys = GetPlanHoleKeys(allPlan).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentPlan = _reviewManager.CurrentPlan;
        string SelectPoint34(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        if (currentPlan is not null &&
            currentPlan.RecipeId.Equals(recipe.Id, StringComparison.OrdinalIgnoreCase) &&
            currentPlan.ReviewPoints
                .Select(SelectPoint34)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(targetKeys))
        {
            return currentPlan;
        }

        return _reviewManager.CreatePlan(recipe, targetKeys);
    }

    private async Task StartReviewSequence()
    {
        if (IsReviewExecutionActive)
        {
            _statusReporter("Review sequence is already running. Stop it before starting another review.");
            _refreshScreen();
            return;
        }

        _isStartRequestPending = true;
        NotifyReviewExecutionCommandStates();
        _refreshScreen();

        try
        {
            var recipe = await LoadSelectedRecipe(CancellationToken.None) ?? CreateFallbackRecipe();
            var allPlan = _reviewManager.CreatePlan(recipe, Array.Empty<string>());
            ApplyRecipeContext(recipe, allPlan);
            if (IsOneHoleTab && string.IsNullOrWhiteSpace(_oneHoleKey))
            {
                _statusReporter("Select one hole before starting One Hole review.");
                _refreshScreen();
                return;
            }

            var isOneHoleRun = IsOneHoleTab;
            var oneHoleRunKey = _oneHoleKey;
            var reviewPlan = _reviewManager.CreatePlan(recipe, GetPlanHoleKeys(allPlan));
            if (isOneHoleRun)
            {
                reviewPlan = RestoreOneHoleReviewOffset(reviewPlan, oneHoleRunKey);
            }
            void HandleStatus35(ST_REVIEW_PLAN plan)
            {
                if (isOneHoleRun)
                {
                    CaptureOneHoleResult(plan, oneHoleRunKey);
                }

                _refreshScreen();
            }
            var status = await _reviewManager.Start(
                reviewPlan,
HandleStatus35,
                CancellationToken.None);

            if (isOneHoleRun && _reviewManager.CurrentPlan is not null)
            {
                CaptureOneHoleResult(_reviewManager.CurrentPlan, oneHoleRunKey);
            }

            _statusReporter($"{status.Message} ({status.CompletedCount}/{status.TotalCount}, NG={status.NgCount})");
            _refreshScreen();
        }
        catch (Exception exception)
        {
            _statusReporter($"Review sequence start failed: {exception.Message}");
        }
        finally
        {
            _isStartRequestPending = false;
            NotifyReviewExecutionCommandStates();
            _refreshScreen();
        }
    }

    private bool IsReviewExecutionActive
    {
        get
        {
            return _isStartRequestPending ||
        _reviewManager.SequenceState is EN_REVIEW_SEQUENCE_STATE.Running or EN_REVIEW_SEQUENCE_STATE.Stopping;
        }
    }

    private void NotifyReviewExecutionCommandStates()
    {
        StartCommand.NotifyCanExecuteChanged();
        SelectTabCommand.NotifyCanExecuteChanged();
        SelectModeCommand.NotifyCanExecuteChanged();
    }

    private void StopReviewSequence()
    {
        _reviewManager.Stop();
        _statusReporter("Review sequence stop requested.");
        _refreshScreen();
    }

    private async Task LoadSelectedReviewRule()
    {
        try
        {
            await RefreshRuleFiles(CancellationToken.None);
            var rule = await _reviewRuleFile.Load(_selectedRuleFile, CancellationToken.None);
            ApplyReviewRule(rule);
            _statusReporter($"Review rule loaded: {rule.FileName} ({rule.RuleType}, {GetPlanHoleKeys(_lastAllPlan).Count} holes).");
            _refreshScreen();
        }
        catch (Exception exception)
        {
            _statusReporter($"Review rule load failed: {exception.Message}");
        }
    }

    private async Task SaveCurrentReviewRule()
    {
        try
        {
            await RefreshRuleFiles(CancellationToken.None);

            var initialValue = Path.GetFileNameWithoutExtension(_selectedRuleFile);
            string HandleRuleFileName36(string value)
            {
                return ValidateRuleFileName(NormalizeRuleFileInput(value));
            }

            var ruleFileName = ShowReviewRuleNameDialog(
                "Save Review Rule",
                "Enter review rule file name.",
                initialValue,
HandleRuleFileName36);

            if (string.IsNullOrWhiteSpace(ruleFileName))
            {
                return;
            }

            _selectedRuleFile = NormalizeRuleFileInput(ruleFileName);
            var rule = CreateCurrentRuleData(_selectedRuleFile);
            await _reviewRuleFile.Save(rule, CancellationToken.None);
            await RefreshRuleFiles(CancellationToken.None);
            _statusReporter($"Review rule saved: {_selectedRuleFile} ({rule.RuleType}, {rule.HoleKeys.Count} holes).");
            _refreshScreen();
        }
        catch (Exception exception)
        {
            _statusReporter($"Review rule save failed: {exception.Message}");
        }
    }

    private async Task RetryRemainingReviewPoints()
    {
        try
        {
            void HandleStatus37(ST_REVIEW_PLAN _)
            {
                _refreshScreen();
            }

            var status = await _reviewManager.RetryRemaining(
HandleStatus37,
                CancellationToken.None);

            _statusReporter($"{status.Message} ({status.CompletedCount}/{status.TotalCount}, NG={status.NgCount})");
            _refreshScreen();
        }
        catch (Exception exception)
        {
            _statusReporter($"Review retry failed: {exception.Message}");
        }
    }

    private void ApplyReviewRule(ST_REVIEW_RULE_DATA rule)
    {
        _selectedRuleFile = NormalizeRuleFileInput(rule.FileName);
        _selectedRuleType = rule.RuleType;
        _sampleRuleHeadNo = Math.Clamp(rule.HeadNo, 1, _headCount);
        _sampleRuleCellNo = Math.Clamp(rule.CellNo, 1, _cellCount);
        if (rule.RuleType == EN_REVIEW_RULE_TYPE.CellPoint)
        {
            _selectedSampleCellNo = _sampleRuleCellNo;
        }
        _selectedSampleHoleKeys.Clear();

        var allPlan = _lastAllPlan;
        IEnumerable<string> EvaluateRuleTypeSwitch4()
        {
            var switchValue = rule.RuleType;
            switch (switchValue)
            {
                case EN_REVIEW_RULE_TYPE.AllPoint when allPlan is not null:
                    string SelectPoint38(ST_REVIEW_PLAN_POINT point)
                    {
                        return point.HoleKey;
                    }

                    return allPlan.Points.Select(SelectPoint38);
                case EN_REVIEW_RULE_TYPE.Edge when allPlan is not null:
                    return SelectEdgeKeys(allPlan);
                case EN_REVIEW_RULE_TYPE.Center when allPlan is not null:
                    return SelectCenterKeys(allPlan);
                case EN_REVIEW_RULE_TYPE.HeadPoint when allPlan is not null:
                    bool FilterPoint39(ST_REVIEW_PLAN_POINT point)
                    {
                        return point.HeadNo == _sampleRuleHeadNo;
                    }

                    string SelectPoint40(ST_REVIEW_PLAN_POINT point)
                    {
                        return point.HoleKey;
                    }

                    return allPlan.Points.Where(FilterPoint39).Select(SelectPoint40);
                case EN_REVIEW_RULE_TYPE.CellPoint when allPlan is not null:
                    bool FilterPoint41(ST_REVIEW_PLAN_POINT point)
                    {
                        return point.CellNo == _sampleRuleCellNo;
                    }

                    string SelectPoint42(ST_REVIEW_PLAN_POINT point)
                    {
                        return point.HoleKey;
                    }

                    return allPlan.Points.Where(FilterPoint41).Select(SelectPoint42);
                case EN_REVIEW_RULE_TYPE.ZeroLine when allPlan is not null:
                    return SelectZeroLineKeys(allPlan, rule.ZeroPointCount);
                default:
                    return rule.HoleKeys;
            }
        }

        var selectedKeys = EvaluateRuleTypeSwitch4();
        bool FilterKey43(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var holeKey in selectedKeys.Select(CReviewManager.NormalizeHoleKey).Where(FilterKey43))
        {
            _selectedSampleHoleKeys.Add(holeKey);
        }
        string EvaluateRuleTypeSwitch5()
        {
            var switchValue = rule.RuleType;
            switch (switchValue)
            {
                case EN_REVIEW_RULE_TYPE.AllPoint:
                    return "ALL HOLE";
                case EN_REVIEW_RULE_TYPE.ZeroLine:
                    return "ZERO DEFENSE";
                default:
                    return "SAMPLE HOLE";
            }
        }

        _selectedMode = EvaluateRuleTypeSwitch5();
        _selectionRuleText = string.IsNullOrWhiteSpace(rule.RuleName)
            ? ToRuleText(rule.RuleType)
            : rule.RuleName;
    }

    private ST_REVIEW_RULE_DATA CreateCurrentRuleData(string ruleFileName)
    {
        var ruleType = _selectedMode.Equals("ALL HOLE", StringComparison.OrdinalIgnoreCase)
            ? EN_REVIEW_RULE_TYPE.AllPoint
            : _selectedMode.Equals("ZERO DEFENSE", StringComparison.OrdinalIgnoreCase)
                ? EN_REVIEW_RULE_TYPE.ZeroLine
                : _selectedRuleType;

        return new ST_REVIEW_RULE_DATA(
            NormalizeRuleFileInput(ruleFileName),
            GetSelectionRuleText(),
            ruleType,
            _sampleRuleHeadNo,
            _sampleRuleCellNo,
            ruleType == EN_REVIEW_RULE_TYPE.ZeroLine ? Math.Min(5, _totalReviewPointCount) : 0,
            GetPlanHoleKeys(_lastAllPlan).ToArray());
    }

    private void ApplyScreenData(
        ST_RECIPE_DATA recipe,
        ST_REVIEW_PLAN allPlan)
    {
        var reviewPlan = ResolveReviewPlan(recipe, allPlan);
        bool MatchPoint44(ST_REVIEW_PLAN_POINT point)
        {
            return point.State == EN_REVIEW_POINT_STATE.Current;
        }

        var currentPoint = reviewPlan.ReviewPoints.FirstOrDefault(MatchPoint44);

        _selectedRunCellNo = Math.Clamp(_selectedRunCellNo, 1, Math.Max(1, allPlan.CellCount));
        int SelectPoint45(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo;
        }

        IReadOnlySet<int>? visibleRunCellNos = _selectedMode.Equals(
            "SAMPLE HOLE",
            StringComparison.OrdinalIgnoreCase)
                ? reviewPlan.ReviewPoints
                    .Select(SelectPoint45)
                    .ToHashSet()
                : null;
        var glassPreview = CReviewGlassPreviewBuilder.Build(
            recipe,
            allPlan.CellCount,
            currentPoint?.CellNo ?? 0,
            currentPoint?.HoleNo ?? 0,
            reviewPlan.Points,
            visibleCellNos: visibleRunCellNos);
        _selectedSampleCellNo = Math.Clamp(_selectedSampleCellNo, 1, Math.Max(1, allPlan.CellCount));
        _sampleMapColumnCount = CalculateHoleMapColumnCount(allPlan, _selectedSampleCellNo);
        ST_REVIEW_PLAN_POINT SelectPoint46(ST_REVIEW_PLAN_POINT point)
        {
            var isSelected = _selectedSampleHoleKeys.Contains(point.HoleKey);
            return point with
            {
                Use = isSelected,
                State = isSelected ? EN_REVIEW_POINT_STATE.Ready : EN_REVIEW_POINT_STATE.Skip,
                Judge = "-"
            };
        }
        var samplePreviewPoints = allPlan.Points
            .Select(SelectPoint46)
            .ToArray();
        var sampleGlassPreview = CReviewGlassPreviewBuilder.Build(
            recipe,
            allPlan.CellCount,
            _isSampleCellDetailVisible ? _selectedSampleCellNo : 0,
            0,
            samplePreviewPoints,
            useSampleSelectionColors: true);
        var oneHolePreviewPoints = BuildOneHolePreviewPoints(allPlan, reviewPlan);
        bool MatchPoint47(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        var selectedOneHolePoint = oneHolePreviewPoints.FirstOrDefault(MatchPoint47);
        bool MatchPoint48(ST_REVIEW_PLAN_POINT point)
        {
            return point.State == EN_REVIEW_POINT_STATE.Current;
        }

        var currentOneHolePoint = reviewPlan.ReviewPoints.FirstOrDefault(MatchPoint48);
        var oneHoleGlassPreview = CReviewGlassPreviewBuilder.Build(
            recipe,
            allPlan.CellCount,
            currentOneHolePoint?.CellNo ?? selectedOneHolePoint?.CellNo ?? 0,
            currentOneHolePoint?.HoleNo ?? 0,
            oneHolePreviewPoints);
        _activePlanPointCount = reviewPlan.ReviewPointCount;
        Tabs =
        [
            new("RUN", IsRunTab, SelectTabCommand),
            new("SAMPLE SELECT", IsSampleSelectTab, SelectTabCommand),
            new("ONE HOLE", IsOneHoleTab, SelectTabCommand)
        ];

        Modes =
        [
            Mode("ALL HOLE", "Measure every review hole"),
            Mode("SAMPLE HOLE", "Selected sample holes"),
            Mode("ZERO DEFENSE", "Check reference line holes")
        ];

        ReviewSets =
        [
            new("ACTIVE", "CURRENT SAMPLE", reviewPlan.ReviewPointCount.ToString(CultureInfo.InvariantCulture), "Current"),
            new("SAVED", "ZERO DEFENSE", SelectZeroLineKeys(allPlan, 0).Count.ToString(CultureInfo.InvariantCulture), "Ready"),
            new("SAVED", "CELL CHECK", allPlan.CellCount.ToString(CultureInfo.InvariantCulture), "Ready"),
            new("SAVED", "ALL HOLE", allPlan.TotalPointCount.ToString(CultureInfo.InvariantCulture), "Ready")
        ];

        TargetItems =
        [
            new("Recipe", reviewPlan.RecipeId),
            new("Product", "MODEL_A3_LD"),
            new("Panel", "PNL-0001"),
            new("Head Scope", $"H01 - H{reviewPlan.HeadCount:00}"),
            new("Cell Scope", $"1 - {reviewPlan.CellCount}"),
            new("Tolerance", $"X +/-{reviewPlan.ToleranceX:0.000} / Y +/-{reviewPlan.ToleranceY:0.000} mm")
        ];

        SelectionSummaryItems =
        [
            new("Total Hole", allPlan.TotalPointCount.ToString(CultureInfo.InvariantCulture)),
            new("Selected", reviewPlan.ReviewPointCount.ToString(CultureInfo.InvariantCulture)),
            new("Review Mode", IsOneHoleTab ? "ONE HOLE" : _selectedMode),
            new("Selection Rule", IsOneHoleTab ? GetOneHoleSelectionText() : GetSelectionRuleText()),
            new("Expected Time", $"{Math.Max(1, reviewPlan.ReviewPointCount * 3)} sec"),
            new("Output", "Review Plan")
        ];

        PointSelectRows = CreatePointSelectRows(allPlan, _selectedSampleHoleKeys, TogglePointCommand);
        bool FilterPoint49(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo == _selectedSampleCellNo;
        }

        SampleCellHoleRows = CreatePointSelectRows(
            allPlan with
            {
                Points = allPlan.Points.Where(FilterPoint49).ToArray()
            },
            _selectedSampleHoleKeys,
            TogglePointCommand);
        bool MatchPoint50(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(_selectedRunHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        var selectedRunPoint = reviewPlan.ReviewPoints.FirstOrDefault(MatchPoint50);
        bool MatchPoint51(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        var selectedOneHoleReviewPoint = reviewPlan.ReviewPoints.FirstOrDefault(MatchPoint51);
        var displayedOneHoleMeasurement = GetDisplayedOneHoleMeasurement();
        CanApplyOneHoleReviewOffset =
            IsOneHoleTab &&
            _reviewManager.SequenceState is not (EN_REVIEW_SEQUENCE_STATE.Running or EN_REVIEW_SEQUENCE_STATE.Stopping) &&
            !_oneHoleAppliedMeasurementKeys.Contains(_displayedOneHoleMeasurementKey) &&
            displayedOneHoleMeasurement?.State is EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng;
        bool MatchPoint52(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchPoint53(ST_REVIEW_PLAN_POINT point)
        {
            return point.State is EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng;
        }

        SelectedPointItems = BuildSelectedPointItems(
            IsOneHoleTab
                ? displayedOneHoleMeasurement ??
                    currentOneHolePoint ??
                    selectedOneHoleReviewPoint ??
                    allPlan.Points.FirstOrDefault(MatchPoint52)
                : currentPoint ??
                    selectedRunPoint ??
                    reviewPlan.ReviewPoints.FirstOrDefault(MatchPoint53) ??
                    reviewPlan.ReviewPoints.FirstOrDefault());

        RunGlassPreviewImage = glassPreview.Image;
        RunCellPreviewLabels = glassPreview.CellLabels;
        RunCurrentHoleMarkers = glassPreview.CurrentHoleMarker is null
            ? []
            : [glassPreview.CurrentHoleMarker];
        RunGlassPreviewSummary = glassPreview.Summary;
        RunCellHoleMatrixRows = BuildRunCellHoleMatrixRows(reviewPlan, _selectedRunCellNo);
        IEnumerable<ST_REVIEW_RUN_HOLE_ROW> SelectRow54(ST_REVIEW_RUN_HOLE_MATRIX_ROW row)
        {
            return row.Holes;
        }

        RunCellHoleRows = RunCellHoleMatrixRows
            .SelectMany(SelectRow54)
            .ToArray();
        SampleGlassPreviewImage = sampleGlassPreview.Image;
        SampleCellPreviewLabels = sampleGlassPreview.CellLabels;
        bool FilterPoint55(ST_REVIEW_PLAN_POINT point)
        {
            return point.Use;
        }

        int SelectPoint56(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo;
        }

        SampleGlassPreviewSummary =
            $"{samplePreviewPoints.Where(FilterPoint55).Select(SelectPoint56).Distinct().Count()} Cells / " +
            $"{_selectedSampleHoleKeys.Count} Holes Selected";
        OneHoleGlassPreviewImage = oneHoleGlassPreview.Image;
        OneHoleCellPreviewLabels = oneHoleGlassPreview.CellLabels;
        OneHoleCurrentHoleMarkers = oneHoleGlassPreview.CurrentHoleMarker is null
            ? []
            : [oneHoleGlassPreview.CurrentHoleMarker];
        OneHoleGlassPreviewSummary = oneHoleGlassPreview.Summary;
        OneHoleCellHoleMatrixRows = BuildOneHoleCellHoleMatrixRows(oneHolePreviewPoints, _selectedOneHoleCellNo);
        IEnumerable<ST_REVIEW_RUN_HOLE_ROW> SelectRow57(ST_REVIEW_RUN_HOLE_MATRIX_ROW row)
        {
            return row.Holes;
        }

        OneHoleCellHoleRows = OneHoleCellHoleMatrixRows
            .SelectMany(SelectRow57)
            .ToArray();

        ResultRows = BuildResultRows(reviewPlan);

        HistoryRows =
        [
            new("10:24:23", "SAMPLE HOLE", CreateDefaultSampleHoleKeys(allPlan).Count.ToString(CultureInfo.InvariantCulture), "0", "OK"),
            new("10:18:51", "ZERO DEFENSE", SelectZeroLineKeys(allPlan, 0).Count.ToString(CultureInfo.InvariantCulture), "0", "OK"),
            new("10:12:09", "ONE HOLE", "1", "0", "OK"),
            new("10:05:42", "ALL HOLE", allPlan.TotalPointCount.ToString(CultureInfo.InvariantCulture), "0", "OK")
        ];
    }

    private static IReadOnlyList<ST_DISPLAY_ITEM> BuildSelectedPointItems(ST_REVIEW_PLAN_POINT? selectedPoint)
    {
        if (selectedPoint is null)
        {
            return
            [
                new("Measured Hole", "-"),
                new("Error Amount", "X - / Y -", "WAIT"),
                new("Review Offset", "X 0.000 / Y 0.000"),
                new("Judge", "WAIT")
            ];
        }

        var errorX = selectedPoint.ErrorX;
        var errorY = selectedPoint.ErrorY;
        var judge = string.IsNullOrWhiteSpace(selectedPoint.Judge) ? "WAIT" : selectedPoint.Judge;

        return
        [
            new("Measured Hole", $"Cell{selectedPoint.CellNo}-{ToMatrixHoleName(selectedPoint)}"),
            new("Error Amount", $"X {FormatSigned(errorX)} / Y {FormatSigned(errorY)}", judge),
            new("Review Offset", $"X {FormatSigned(selectedPoint.ReviewOffsetX)} / Y {FormatSigned(selectedPoint.ReviewOffsetY)}"),
            new("Judge", judge)
        ];
    }

    private IReadOnlyList<ST_REVIEW_RUN_HOLE_MATRIX_ROW> BuildRunCellHoleMatrixRows(
        ST_REVIEW_PLAN reviewPlan,
        int cellNo)
    {
        bool FilterPoint58(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo == cellNo;
        }

        int GetGroupSortKey59(IGrouping<int, ST_REVIEW_PLAN_POINT> group)
        {
            return group.Key;
        }

        ST_REVIEW_RUN_HOLE_MATRIX_ROW SelectGroup60(IGrouping<int, ST_REVIEW_PLAN_POINT> group)
        {
            ST_REVIEW_RUN_HOLE_ROW SelectPoint1(ST_REVIEW_PLAN_POINT point)
            {
                return new ST_REVIEW_RUN_HOLE_ROW(
                                                    point.HoleKey,
                                                    ToMatrixHoleName(point),
                                                    ToRunHoleDetail(point),
                                                    ToStateText(point.State),
                                                    point.State == EN_REVIEW_POINT_STATE.Current,
                                                    point.HoleKey.Equals(_selectedRunHoleKey, StringComparison.OrdinalIgnoreCase),
                                                    SelectRunHoleCommand);
            }

            return new ST_REVIEW_RUN_HOLE_MATRIX_ROW(
                            group.Key + 1,
                            group
                                .OrderBy(GetMatrixColumnIndex)
                                .Select(SelectPoint1)
                                .ToArray());
        }

        return reviewPlan.ReviewPoints
            .Where(FilterPoint58)
            .GroupBy(GetMatrixRowIndex)
            .OrderBy(GetGroupSortKey59)
            .Select(SelectGroup60)
            .ToArray();
    }

    private IReadOnlyList<ST_REVIEW_RUN_HOLE_MATRIX_ROW> BuildOneHoleCellHoleMatrixRows(
        IReadOnlyList<ST_REVIEW_PLAN_POINT> points,
        int cellNo)
    {
        bool FilterPoint61(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo == cellNo;
        }

        int GetGroupSortKey62(IGrouping<int, ST_REVIEW_PLAN_POINT> group)
        {
            return group.Key;
        }

        ST_REVIEW_RUN_HOLE_MATRIX_ROW SelectGroup63(IGrouping<int, ST_REVIEW_PLAN_POINT> group)
        {
            ST_REVIEW_RUN_HOLE_ROW SelectPoint2(ST_REVIEW_PLAN_POINT point)
            {
                var isSelected = point.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase);
                var detail = point.State is EN_REVIEW_POINT_STATE.Current or EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng
                    ? ToRunHoleDetail(point)
                    : isSelected
                        ? "SELECTED"
                        : "READY";

                return new ST_REVIEW_RUN_HOLE_ROW(
                    point.HoleKey,
                    ToMatrixHoleName(point),
                    detail,
                    ToStateText(point.State),
                    point.State == EN_REVIEW_POINT_STATE.Current,
                    isSelected,
                    SelectOneHoleCommand);
            }
            return new ST_REVIEW_RUN_HOLE_MATRIX_ROW(
                            group.Key + 1,
                            group
                                .OrderBy(GetMatrixColumnIndex)
                                .Select(SelectPoint2)
                                .ToArray());
        }

        return points
            .Where(FilterPoint61)
            .GroupBy(GetMatrixRowIndex)
            .OrderBy(GetGroupSortKey62)
            .Select(SelectGroup63)
            .ToArray();
    }

    private IReadOnlyList<ST_REVIEW_PLAN_POINT> BuildOneHolePreviewPoints(
        ST_REVIEW_PLAN allPlan,
        ST_REVIEW_PLAN reviewPlan)
    {
        string HandleReviewPointByKey64(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        var reviewPointByKey = reviewPlan.ReviewPoints
            .ToDictionary(HandleReviewPointByKey64, StringComparer.OrdinalIgnoreCase);
        ST_REVIEW_PLAN_POINT SelectPoint65(ST_REVIEW_PLAN_POINT point)
        {
            if (reviewPointByKey.TryGetValue(point.HoleKey, out var reviewPoint) &&
                reviewPoint.State is EN_REVIEW_POINT_STATE.Current or
                    EN_REVIEW_POINT_STATE.Ok or
                    EN_REVIEW_POINT_STATE.Ng)
            {
                return reviewPoint;
            }

            if (_oneHoleResults.TryGetValue(point.HoleKey, out var measuredPoint))
            {
                return measuredPoint;
            }

            return point with
            {
                Use = true,
                State = EN_REVIEW_POINT_STATE.Ready,
                Judge = "-"
            };
        }
        return allPlan.Points
            .Select(SelectPoint65)
            .ToArray();
    }

    private void CaptureOneHoleResult(
        ST_REVIEW_PLAN plan,
        string holeKey)
    {
        if (string.IsNullOrWhiteSpace(holeKey) ||
            !plan.RecipeId.Equals(_recipeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        bool MatchPoint66(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase) &&
                        point.State is EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng;
        }

        var measuredPoint = plan.ReviewPoints.FirstOrDefault(MatchPoint66);
        if (measuredPoint is null)
        {
            return;
        }

        _oneHoleResults[measuredPoint.HoleKey] = measuredPoint;
        _oneHoleAppliedMeasurementKeys.Remove(measuredPoint.HoleKey);
        _displayedOneHoleMeasurementKey = measuredPoint.HoleKey;
    }

    private ST_REVIEW_PLAN RestoreOneHoleReviewOffset(
        ST_REVIEW_PLAN plan,
        string holeKey)
    {
        if (string.IsNullOrWhiteSpace(holeKey) ||
            !_oneHoleResults.TryGetValue(holeKey, out var storedPoint))
        {
            return plan;
        }
        ST_REVIEW_PLAN_POINT SelectPoint67(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey.Equals(holeKey, StringComparison.OrdinalIgnoreCase)
                                ? point with
                                {
                                    ReviewOffsetX = storedPoint.ReviewOffsetX,
                                    ReviewOffsetY = storedPoint.ReviewOffsetY
                                }
                                : point;
        }

        return plan with
        {
            Points = plan.Points
                .Select(SelectPoint67)
                .ToArray()
        };
    }

    private ST_REVIEW_PLAN_POINT? GetDisplayedOneHoleMeasurement()
    {
        return string.IsNullOrWhiteSpace(_displayedOneHoleMeasurementKey)
            ? null
            : _oneHoleResults.GetValueOrDefault(_displayedOneHoleMeasurementKey);
    }

    private static int GetMatrixColumnIndex(ST_REVIEW_PLAN_POINT point)
    {
        return (Math.Max(1, point.HoleNo) - 1) % Math.Max(1, point.PixelCountX);
    }

    private static int GetMatrixRowIndex(ST_REVIEW_PLAN_POINT point)
    {
        return (Math.Max(1, point.HoleNo) - 1) / Math.Max(1, point.PixelCountX);
    }

    private static string ToRunHoleDetail(ST_REVIEW_PLAN_POINT point)
    {
        string EvaluateStateSwitch6()
        {
            var switchValue = point.State;
            switch (switchValue)
            {
                case EN_REVIEW_POINT_STATE.Ok or EN_REVIEW_POINT_STATE.Ng:
                    return $"X {FormatSigned(point.ErrorX)} / Y {FormatSigned(point.ErrorY)}";
                case EN_REVIEW_POINT_STATE.Current:
                    return "CURRENT";
                case EN_REVIEW_POINT_STATE.Skip:
                    return "SKIP";
                default:
                    return "READY";
            }
        }

        return EvaluateStateSwitch6();
    }

    private static int CalculateHoleMapColumnCount(
        ST_REVIEW_PLAN allPlan,
        int cellNo)
    {
        bool FilterPoint68(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo == cellNo;
        }

        var cellPoints = allPlan.Points
            .Where(FilterPoint68)
            .ToArray();

        if (cellPoints.Length == 0)
        {
            return 4;
        }
        double SelectPoint69(ST_REVIEW_PLAN_POINT point)
        {
            return Math.Round(point.DesignX, 6);
        }

        var distinctXCount = cellPoints
            .Select(SelectPoint69)
            .Distinct()
            .Count();

        return Math.Clamp(distinctXCount, 4, 80);
    }

    private IReadOnlyList<ST_REVIEW_POINT_SELECT_ROW> CreatePointSelectRows(
        ST_REVIEW_PLAN reviewPlan,
        IReadOnlySet<string> selectedKeys,
        CButtonCommand command)
    {
        var rows = new List<ST_REVIEW_POINT_SELECT_ROW>(reviewPlan.Points.Count);
        int GetPointSortKey70(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo;
        }

        int GetPointSortKey71(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleNo;
        }

        foreach (var point in reviewPlan.Points.OrderBy(GetPointSortKey70).ThenBy(GetPointSortKey71))
        {
            var use = selectedKeys.Contains(point.HoleKey);
            var reason = GetHoleReason(point, use);

            rows.Add(new ST_REVIEW_POINT_SELECT_ROW(
                point.PointNo,
                point.HoleKey,
                point.HeadName,
                point.CellName,
                ToMatrixHoleName(point),
                point.CellNo.ToString(CultureInfo.InvariantCulture),
                point.HoleNo.ToString(CultureInfo.InvariantCulture),
                FormatDouble(point.DesignX),
                FormatDouble(point.DesignY),
                FormatDouble(point.ReviewTargetX),
                FormatDouble(point.ReviewTargetY),
                use,
                reason,
                command,
                SetSampleHoleSelectionCommand));
        }

        return rows;
    }

    private IReadOnlyList<ST_REVIEW_RESULT_ROW> BuildResultRows(ST_REVIEW_PLAN reviewPlan)
    {
        var selectedRows = reviewPlan.ReviewPoints
            .Take(6)
            .ToArray();
        ST_REVIEW_RESULT_ROW SelectRow72(ST_REVIEW_PLAN_POINT row, int index)
        {
            return Result(
                            $"10:24:{12 + index * 2:00}.{120 + index * 31:000}",
                            row.HeadName,
                            row.CellName,
                            ToMatrixHoleName(row),
                            FormatSigned(row.ErrorX),
                            FormatSigned(row.ErrorY),
                            row.Judge.Equals("-", StringComparison.OrdinalIgnoreCase) ? "WAIT" : row.Judge);
        }

        return selectedRows
            .Select(SelectRow72)
            .ToArray();
    }

    private string GetSelectionRuleText()
    {
        if (_selectedMode.Equals("ALL HOLE", StringComparison.OrdinalIgnoreCase))
        {
            return "All Hole";
        }

        if (_selectedMode.Equals("ZERO DEFENSE", StringComparison.OrdinalIgnoreCase))
        {
            return "0-Line";
        }

        return _selectionRuleText;
    }

    private string GetOneHoleSelectionText()
    {
        return string.IsNullOrWhiteSpace(_oneHoleKey)
            ? $"CELL{_selectedOneHoleCellNo:00} / Select Hole"
            : _oneHoleKey;
    }

    private string GetSelectedOneHoleName()
    {
        if (string.IsNullOrWhiteSpace(_oneHoleKey))
        {
            return "-";
        }
        bool MatchItem73(ST_REVIEW_PLAN_POINT item)
        {
            return item.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        var point = _lastAllPlan?.Points.FirstOrDefault(MatchItem73);

        return point is null ? "-" : ToMatrixHoleName(point);
    }

    private IReadOnlyCollection<string> GetPlanHoleKeys(ST_REVIEW_PLAN? allPlan)
    {
        if (allPlan is null)
        {
            return [];
        }

        if (IsOneHoleTab)
        {
            return string.IsNullOrWhiteSpace(_oneHoleKey) ? [] : [_oneHoleKey];
        }

        if (_selectedMode.Equals("ALL HOLE", StringComparison.OrdinalIgnoreCase))
        {
            string SelectPoint74(ST_REVIEW_PLAN_POINT point)
            {
                return point.HoleKey;
            }

            return allPlan.Points.Select(SelectPoint74).ToArray();
        }

        if (_selectedMode.Equals("ZERO DEFENSE", StringComparison.OrdinalIgnoreCase))
        {
            return SelectZeroLineKeys(allPlan, 0);
        }
        bool FilterKey75(string key)
        {
            bool CheckPoint3(ST_REVIEW_PLAN_POINT point)
            {
                return point.HoleKey.Equals(key, StringComparison.OrdinalIgnoreCase);
            }

            return allPlan.Points.Any(CheckPoint3);
        }

        string GetKeySortKey76(string key)
        {
            return key;
        }

        return _selectedSampleHoleKeys
            .Where(FilterKey75)
            .OrderBy(GetKeySortKey76, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<string> CreateDefaultSampleHoleKeys(ST_REVIEW_PLAN allPlan)
    {
        int GroupByPointCallback77(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo;
        }

        IEnumerable<string> SelectGroup78(IGrouping<int, ST_REVIEW_PLAN_POINT> group)
        {
            int GetPointSortKey4(ST_REVIEW_PLAN_POINT point)
            {
                return point.HoleNo;
            }

            var holes = group.OrderBy(GetPointSortKey4).ToArray();
            if (holes.Length == 0)
            {
                return [];
            }

            return new[]
            {
                    holes.First().HoleKey,
                    holes[holes.Length / 2].HoleKey,
                    holes.Last().HoleKey
                }.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        return allPlan.Points
            .GroupBy(GroupByPointCallback77)
            .SelectMany(SelectGroup78)
            .ToArray();
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
        double HandleTargetY79(ST_REVIEW_PLAN_POINT point)
        {
            return point.DesignY;
        }

        double HandleTargetY80(ST_REVIEW_PLAN_POINT point)
        {
            return point.DesignY;
        }

        var targetY = (plan.Points.Min(HandleTargetY79) + plan.Points.Max(HandleTargetY80)) / 2.0;
        var count = zeroPointCount <= 0 ? Math.Min(5, plan.Points.Count) : Math.Min(zeroPointCount, plan.Points.Count);
        double GetPointSortKey81(ST_REVIEW_PLAN_POINT point)
        {
            return Math.Abs(point.DesignY - targetY);
        }

        double GetPointSortKey82(ST_REVIEW_PLAN_POINT point)
        {
            return point.DesignX;
        }

        string SelectPoint83(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        return plan.Points
            .OrderBy(GetPointSortKey81)
            .ThenBy(GetPointSortKey82)
            .Take(count)
            .Select(SelectPoint83)
            .ToArray();
    }

    private string GetHoleReason(ST_REVIEW_PLAN_POINT point, bool use)
    {
        if (!use)
        {
            return "-";
        }

        if (IsOneHoleTab && point.HoleKey.Equals(_oneHoleKey, StringComparison.OrdinalIgnoreCase))
        {
            return "ONE HOLE";
        }

        if (_selectedMode.Equals("ALL HOLE", StringComparison.OrdinalIgnoreCase))
        {
            return "ALL";
        }

        return _selectionRuleText.Equals("None", StringComparison.OrdinalIgnoreCase) ? "MANUAL" : _selectionRuleText.ToUpperInvariant();
    }

    private static string ToRuleText(EN_REVIEW_RULE_TYPE ruleType)
    {
        string EvaluateRuleTypeSwitch7()
        {
            var switchValue = ruleType;
            switch (switchValue)
            {
                case EN_REVIEW_RULE_TYPE.AllPoint:
                    return "All Hole";
                case EN_REVIEW_RULE_TYPE.Edge:
                    return "Edge";
                case EN_REVIEW_RULE_TYPE.Center:
                    return "Center";
                case EN_REVIEW_RULE_TYPE.HeadPoint:
                    return "Head Hole";
                case EN_REVIEW_RULE_TYPE.CellPoint:
                    return "Cell Hole";
                case EN_REVIEW_RULE_TYPE.ZeroLine:
                    return "0-Line";
                default:
                    return "Sample Hole";
            }
        }

        return EvaluateRuleTypeSwitch7();
    }

    private static string? ShowReviewRuleNameDialog(
        string title,
        string message,
        string initialValue,
        Func<string, string> validate)
    {
        var dialog = new CRecipeNameDialog(title, message, initialValue, validate)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? NormalizeRuleFileInput(dialog.RecipeName)
            : null;
    }

    private static string ValidateRuleFileName(string value)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(value);

        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
        {
            return "Review rule name is required.";
        }
        bool CheckCharacter84(char character)
        {
            return Path.GetInvalidFileNameChars().Contains(character);
        }

        return nameWithoutExtension.Any(CheckCharacter84)
            ? "Review rule name contains invalid file name characters."
            : "";
    }

    private static string NormalizeRuleFileInput(string value)
    {
        var normalized = Path.GetFileName(value.Trim()) ?? "";

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = DefaultRuleFileName;
        }

        if (!normalized.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{normalized}.csv";
        }

        return normalized;
    }

    private ST_REVIEW_MODE_ITEM Mode(string name, string detail)
    {
        return new ST_REVIEW_MODE_ITEM(
            name,
            detail,
            name.Equals(_selectedMode, StringComparison.OrdinalIgnoreCase),
            SelectModeCommand);
    }

    private static string ToStateText(EN_REVIEW_POINT_STATE state)
    {
        string EvaluateStateSwitch8()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_REVIEW_POINT_STATE.Current:
                    return "Current";
                case EN_REVIEW_POINT_STATE.Ok:
                    return "OK";
                case EN_REVIEW_POINT_STATE.Ng:
                    return "NG";
                case EN_REVIEW_POINT_STATE.Skip:
                    return "Skip";
                default:
                    return "Ready";
            }
        }

        return EvaluateStateSwitch8();
    }

    private static string ToMatrixHoleName(ST_REVIEW_PLAN_POINT point)
    {
        return point.HoleName;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }

    private static ST_REVIEW_RESULT_ROW Result(
        string time,
        string head,
        string cell,
        string point,
        string errorX,
        string errorY,
        string judge)
    {
        return new ST_REVIEW_RESULT_ROW(time, head, cell, point, errorX, errorY, judge);
    }
}

public sealed record ST_REVIEW_TAB_ITEM(
    string Name,
    bool IsSelected,
    CButtonCommand SelectCommand);

public sealed record ST_REVIEW_MODE_ITEM(
    string Name,
    string Detail,
    bool IsSelected,
    CButtonCommand SelectCommand);

public sealed record ST_REVIEW_SET_ROW(
    string Type,
    string Name,
    string Count,
    string State)
{
    public Brush StateBrush
    {
        get
        {
            return CReviewStatusBrush.ForState(State);
        }
    }
}

public sealed record ST_REVIEW_RUN_HOLE_ROW(
    string HoleKey,
    string HoleName,
    string Detail,
    string State,
    bool IsCurrent,
    bool IsSelected,
    CButtonCommand SelectCommand)
{
    public Brush StateBrush
    {
        get
        {
            return CReviewStatusBrush.ForState(State);
        }
    }

    public Brush BorderBrush
    {
        get
        {
            return IsCurrent || IsSelected
        ? CStatusBrush.Active
        : CStatusBrush.Frozen(0x3B, 0x4A, 0x5B);
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            return IsCurrent
        ? CStatusBrush.Frozen(0x18, 0x43, 0x63)
        : IsSelected
            ? CStatusBrush.Frozen(0x32, 0x47, 0x5A)
            : CStatusBrush.Frozen(0x18, 0x20, 0x29);
        }
    }
}

public sealed record ST_REVIEW_RUN_HOLE_MATRIX_ROW(
    int RowNo,
    IReadOnlyList<ST_REVIEW_RUN_HOLE_ROW> Holes);

internal static class CReviewStatusBrush
{
    private static readonly Brush Ready = CStatusBrush.Frozen(0xA8, 0xB6, 0xC5);
    private static readonly Brush Current = CStatusBrush.Frozen(0xFD, 0xE0, 0x47);
    private static readonly Brush Ok = CStatusBrush.Frozen(0x55, 0xB8, 0x7A);
    private static readonly Brush Ng = CStatusBrush.Frozen(0xF0, 0x5A, 0x5A);
    private static readonly Brush SampleSelected = CStatusBrush.Frozen(0x4F, 0xAF, 0xC4);
    private static readonly Brush SampleNotSelected = CStatusBrush.Frozen(0x37, 0x42, 0x4E);

    public static Brush ForState(string state)
    {
        Brush EvaluateValueSwitch9()
        {
            var switchValue = state.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "READY":
                    return Ready;
                case "CURRENT":
                    return Current;
                case "OK":
                    return Ok;
                case "NG":
                    return Ng;
                case "SKIP":
                    return CStatusBrush.Muted;
                default:
                    return CStatusBrush.ForDisplayState(state);
            }
        }

        return EvaluateValueSwitch9();
    }

    public static Brush ForState(EN_REVIEW_POINT_STATE state)
    {
        Brush EvaluateStateSwitch10()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_REVIEW_POINT_STATE.Ready:
                    return Ready;
                case EN_REVIEW_POINT_STATE.Current:
                    return Current;
                case EN_REVIEW_POINT_STATE.Ok:
                    return Ok;
                case EN_REVIEW_POINT_STATE.Ng:
                    return Ng;
                case EN_REVIEW_POINT_STATE.Skip:
                    return CStatusBrush.Muted;
                default:
                    return CStatusBrush.Muted;
            }
        }

        return EvaluateStateSwitch10();
    }

    public static Brush ForPreviewBaseState(EN_REVIEW_POINT_STATE state)
    {
        // The blinking marker supplies the yellow Current color.
        // Keep the base neutral so the blink remains visible.
        return state == EN_REVIEW_POINT_STATE.Current
            ? Ready
            : ForState(state);
    }

    public static Brush ForSampleSelection(EN_REVIEW_POINT_STATE state)
    {
        return state == EN_REVIEW_POINT_STATE.Skip
            ? SampleNotSelected
            : SampleSelected;
    }
}

public sealed record ST_REVIEW_POINT_SELECT_ROW(
    int No,
    string HoleKey,
    string Head,
    string Cell,
    string Point,
    string Row,
    string Column,
    string DesignX,
    string DesignY,
    string ReviewX,
    string ReviewY,
    bool Use,
    string Reason,
    CButtonCommand ToggleCommand,
    CButtonCommand DragSelectionCommand)
{
    public string UseText
    {
        get
        {
            return Use ? "USE" : "-";
        }
    }

    public string StateText
    {
        get
        {
            return Use ? "Selected" : "Skip";
        }
    }

    public Brush UseBrush
    {
        get
        {
            return Use ? CStatusBrush.Active : CStatusBrush.Muted;
        }
    }

    public Brush BorderBrush
    {
        get
        {
            return Use ? CStatusBrush.Active : CStatusBrush.Frozen(0x27, 0x32, 0x41);
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            return Use ? CStatusBrush.Frozen(0x0B, 0x3B, 0x78) : CStatusBrush.Frozen(0x0B, 0x11, 0x19);
        }
    }
}

public sealed record ST_REVIEW_SAMPLE_DRAG_SELECTION(
    IReadOnlyList<string> HoleKeys,
    bool Use);

public sealed record ST_REVIEW_RESULT_ROW(
    string Time,
    string Head,
    string Cell,
    string Point,
    string ErrorX,
    string ErrorY,
    string Judge)
{
    public Brush JudgeBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(Judge);
        }
    }
}

public sealed record ST_REVIEW_HISTORY_ROW(
    string Time,
    string Mode,
    string Total,
    string Ng,
    string Result)
{
    public Brush ResultBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(Result);
        }
    }
}
