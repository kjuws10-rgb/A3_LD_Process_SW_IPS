using System.Globalization;
using System.IO;
using System.Windows.Media;
using Drilling.Common.Managers;
using Drilling.Common.Review;
using Drilling.UI.Menu;
using Microsoft.Win32;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuCorrection : IMenu
{
    private static readonly string[] CorrectionTabs =
    [
        "REVIEW DATA",
        "ALIGN COMP",
        "OFFSET COMP",
        "APC / ICR",
        "ZERO DEFENSE",
        "OUTPUT / HISTORY"
    ];

    private readonly IReviewResultFile _reviewResultFile;
    private readonly CRecipeManager _recipeManager;
    private readonly CSettingManager _settingManager;
    private readonly Func<string> _selectedRecipeIdProvider;
    private readonly Action<string> _statusReporter;
    private readonly Func<Task> _refreshCurrentScreen;
    private readonly List<ST_CORRECTION_HISTORY_ROW> _reviewDataHistory = [];
    private readonly HashSet<string> _appliedReviewResultPaths = new(StringComparer.OrdinalIgnoreCase);
    private ST_REVIEW_RESULT_FILE_DATA? _loadedReviewResult;
    private bool _hasPendingReviewOffsetApply;
    private bool _isLoadedReviewResultApplied;
    private string _reviewLoadStatus = "Select a Review Result CSV.";
    private string _reviewLoadState = "WAIT";
    private string _currentSettingState = "Vision Flip: -";
    private string _currentReviewOffsetRecipeName = "Recipe: -";
    private string _selectedTab = "REVIEW DATA";

    public CMenuCorrection(
        IReviewResultFile reviewResultFile,
        CRecipeManager recipeManager,
        CSettingManager settingManager,
        Func<string> selectedRecipeIdProvider,
        Action<string> statusReporter,
        Func<Task> refreshCurrentScreen)
    {
        _reviewResultFile = reviewResultFile;
        _recipeManager = recipeManager;
        _settingManager = settingManager;
        _selectedRecipeIdProvider = selectedRecipeIdProvider;
        _statusReporter = statusReporter;
        _refreshCurrentScreen = refreshCurrentScreen;

        SelectTabCommand = new CButtonCommand(SelectTab);

        async void HandleExecuteCommand1(object? parameter)
        {
            await Execute(parameter);
        }

        ExecuteCommand = new CButtonCommand(HandleExecuteCommand1);
    }

    public EN_MENU Menu
    {
        get
        {
            return EN_MENU.Correction;
        }
    }

    public string Title
    {
        get
        {
            return $"CORRECTION / {_selectedTab}";
        }
    }

    public string Subtitle
    {
        get
        {
            return GetSubtitle(_selectedTab);
        }
    }

    public string SelectedTab
    {
        get
        {
            return _selectedTab;
        }
    }

    public bool IsReviewDataTab
    {
        get
        {
            return _selectedTab.Equals("REVIEW DATA", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsOtherCorrectionTab
    {
        get
        {
            return !IsReviewDataTab;
        }
    }

    public IReadOnlyList<ST_CORRECTION_TAB> Tabs { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> SummaryItems { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_SOURCE_ROW> SourceRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_VALUE_ROW> CandidateRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_VALUE_ROW> ApplyRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_HISTORY_ROW> HistoryRows { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> DetailItems { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_REVIEW_RESULT_ROW> ReviewResultRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_REVIEW_OFFSET_ROW> CalculatedOffsetRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_REVIEW_OFFSET_ROW> CurrentOffsetRows { get; private set; } = [];

    public IReadOnlyList<ST_CORRECTION_REVIEW_OFFSET_ROW> ApplyPreviewRows { get; private set; } = [];

    public string LoadedReviewFileName
    {
        get
        {
            return _loadedReviewResult?.FileName ?? "Not loaded";
        }
    }

    public string ReviewLoadStatus
    {
        get
        {
            return _reviewLoadStatus;
        }
    }

    public Brush ReviewLoadStatusBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(_reviewLoadState);
        }
    }

    public string CurrentSettingState
    {
        get
        {
            return _currentSettingState;
        }
    }

    public string CurrentReviewOffsetRecipeName
    {
        get
        {
            return _currentReviewOffsetRecipeName;
        }
    }

    public CButtonCommand SelectTabCommand { get; }

    public CButtonCommand ExecuteCommand { get; }

    public Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplyTabData(_selectedTab);

        var screen = new CScreenViewModel(
            EN_MENU.Correction,
            Title,
            Subtitle,
            [
                new("Selected", _selectedTab),
                new("Source", GetSourceName(_selectedTab)),
                new("State", GetStateName(_selectedTab))
            ],
            [
                new("Correction Source", []),
                new("Correction Candidate", []),
                new("Apply Preview", [])
            ],
            correction: this);

        return Task.FromResult(screen);
    }

    private void SelectTab(object? parameter)
    {
        var tabName = parameter?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(tabName))
        {
            return;
        }
        bool MatchTab2(string tab)
        {
            return tab.Equals(tabName, StringComparison.OrdinalIgnoreCase);
        }

        var normalizedTab = CorrectionTabs.FirstOrDefault(MatchTab2);

        if (normalizedTab is null)
        {
            return;
        }

        _selectedTab = normalizedTab;
        _statusReporter($"Correction tab selected: {_selectedTab}.");
        _ = _refreshCurrentScreen();
    }

    private async Task Execute(object? parameter)
    {
        var command = parameter?.ToString()?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (IsReviewDataTab && command == "LOAD")
        {
            await LoadReviewResult();
            return;
        }

        if (IsReviewDataTab && command == "CALCULATE")
        {
            await CalculateReviewOffsets();
            return;
        }

        if (IsReviewDataTab && command == "APPLY")
        {
            await ApplyReviewOffsets();
            return;
        }

        if (IsReviewDataTab && command == "SAVE")
        {
            await SaveAppliedReviewOffsets();
            return;
        }

        _statusReporter($"Correction {command} requested. Tab={_selectedTab}.");
    }

    private void ApplyTabData(string selectedTab)
    {
        ST_CORRECTION_TAB SelectTab3(string tab)
        {
            return new ST_CORRECTION_TAB(tab, tab.Equals(selectedTab, StringComparison.OrdinalIgnoreCase));
        }

        Tabs = CorrectionTabs
            .Select(SelectTab3)
            .ToArray();
        (IReadOnlyList<ST_DISPLAY_ITEM> Summary, IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source, IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate, IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply, IReadOnlyList<ST_DISPLAY_ITEM> Detail, IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) EvaluateSelectedTabSwitch1()
        {
            var switchValue = selectedTab;
            switch (switchValue)
            {
                case "ALIGN COMP":
                    return CreateAlignCompData();
                case "OFFSET COMP":
                    return CreateOffsetCompData();
                case "APC / ICR":
                    return CreateApcIcrData();
                case "ZERO DEFENSE":
                    return CreateZeroDefenseData();
                case "OUTPUT / HISTORY":
                    return CreateOutputHistoryData();
                default:
                    return CreateReviewData();
            }
        }

        (SummaryItems, SourceRows, CandidateRows, ApplyRows, DetailItems, HistoryRows) = EvaluateSelectedTabSwitch1();
    }

    private async Task LoadReviewResult()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Review Result",
            Filter = "Review Result CSV (*.csv)|*.csv|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(_reviewResultFile.RootPath)
                ? _reviewResultFile.RootPath
                : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            _statusReporter("Review Result load canceled.");
            return;
        }

        try
        {
            var result = await _reviewResultFile.Load(dialog.FileName);
            ValidateSelectedRecipe(result);

            _loadedReviewResult = result;
            _isLoadedReviewResultApplied = _appliedReviewResultPaths.Contains(
                GetReviewResultIdentity(result));
            _currentSettingState = await LoadCurrentSettingState();
            _currentReviewOffsetRecipeName = "Recipe: -";
            ST_CORRECTION_REVIEW_RESULT_ROW SelectRow4(ST_REVIEW_RESULT_FILE_ROW row)
            {
                return new ST_CORRECTION_REVIEW_RESULT_ROW(
                                    row.HoleKey,
                                    $"H{row.HeadNo:00}",
                                    row.CellNo,
                                    row.ErrorX,
                                    row.ErrorY,
                                    row.Judge);
            }

            ReviewResultRows = result.Rows
                .Select(SelectRow4)
                .ToArray();

            string? offsetLoadWarning = null;
            try
            {
                CurrentOffsetRows = await LoadCurrentReviewOffsets(result);
            }
            catch (Exception exception)
            {
                CurrentOffsetRows = [];
                offsetLoadWarning = exception.Message;
            }

            CalculatedOffsetRows = [];
            ApplyPreviewRows = [];
            _hasPendingReviewOffsetApply = false;
            _reviewLoadStatus = _isLoadedReviewResultApplied
                ? $"{result.Rows.Count} rows loaded. This Review Result was already saved; load a new result before recalculating."
                : offsetLoadWarning is null
                    ? $"{result.Rows.Count} rows loaded."
                    : $"{result.Rows.Count} rows loaded. Current Review Offset unavailable: {offsetLoadWarning}";
            _reviewLoadState = _isLoadedReviewResultApplied || offsetLoadWarning is not null
                ? "WARN"
                : "OK";
            _reviewDataHistory.Insert(0, new ST_CORRECTION_HISTORY_ROW(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.UserName,
                "REVIEW DATA",
                result.FileName,
                "-",
                $"{result.Rows.Count} Rows",
                "Loaded"));

            ApplyTabData(_selectedTab);
            _statusReporter(
                $"Review Result loaded: {result.FileName} / {result.Rows.Count} rows.");
            await _refreshCurrentScreen();
        }
        catch (Exception exception)
        {
            var message = $"Review Result load failed: {exception.Message}";
            _reviewLoadStatus = message;
            _reviewLoadState = "NG";
            _reviewDataHistory.Insert(0, new ST_CORRECTION_HISTORY_ROW(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.UserName,
                "REVIEW DATA",
                Path.GetFileName(dialog.FileName),
                "-",
                "Not Loaded",
                "NG"));
            ApplyTabData(_selectedTab);
            _statusReporter(message);
            await _refreshCurrentScreen();
        }
    }

    private async Task CalculateReviewOffsets()
    {
        if (_loadedReviewResult is null)
        {
            await ReportReviewDataCommand(
                "Calculate",
                "Load a Review Result CSV before calculating.",
                "WARN");
            return;
        }

        if (_isLoadedReviewResultApplied)
        {
            await ReportReviewDataCommand(
                "Calculate",
                "This Review Result was already saved. Load a new Review Result before recalculating.",
                "WARN");
            return;
        }

        if (_hasPendingReviewOffsetApply)
        {
            await ReportReviewDataCommand(
                "Calculate",
                "Review Offsets are pending save. Save or load the Review Result again before recalculating.",
                "WARN");
            return;
        }

        try
        {
            var axisMode = await LoadCurrentVisionAxisMode();
            _currentSettingState = $"Vision Flip: {CReviewCoordinateTransformer.FormatVisionAxisMode(axisMode)}";

            var calculatedRows = new List<ST_CORRECTION_REVIEW_OFFSET_ROW>();

            foreach (var row in _loadedReviewResult.Rows)
            {
                var holeKey = NormalizeReviewHoleKey(row.HoleKey, row.CellNo);
                var calculated = CReviewCoordinateTransformer.VisionErrorToScannerOffset(
                    row.ErrorX,
                    row.ErrorY,
                    row.HeadNo,
                    axisMode);
                var judge = row.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
                    ? "NG"
                    : "OK";

                calculatedRows.Add(new ST_CORRECTION_REVIEW_OFFSET_ROW(
                    holeKey,
                    $"H{row.HeadNo:00}",
                    calculated.X,
                    calculated.Y,
                    judge));
            }

            CalculatedOffsetRows = calculatedRows;
            ApplyPreviewRows = [];

            await ReportReviewDataCommand(
                "Calculate",
                $"{calculatedRows.Count} Review Offsets calculated. Select Apply to create Apply Preview.",
                "OK",
                $"{CReviewCoordinateTransformer.FormatVisionAxisMode(axisMode)} / {calculatedRows.Count} Rows");
        }
        catch (Exception exception)
        {
            CalculatedOffsetRows = [];
            ApplyPreviewRows = [];
            await ReportReviewDataCommand(
                "Calculate",
                $"Review Offset calculation failed: {exception.Message}",
                "NG");
        }
    }

    private async Task ApplyReviewOffsets()
    {
        if (_loadedReviewResult is null)
        {
            await ReportReviewDataCommand(
                "Apply",
                "Load and calculate a Review Result before applying.",
                "WARN");
            return;
        }

        if (_isLoadedReviewResultApplied)
        {
            await ReportReviewDataCommand(
                "Apply",
                "This Review Result was already saved. Load a new Review Result before applying again.",
                "WARN");
            return;
        }

        if (_hasPendingReviewOffsetApply)
        {
            await ReportReviewDataCommand(
                "Apply",
                "The calculated offsets are already applied to the Review Offsets and pending save.",
                "WARN");
            return;
        }

        if (CalculatedOffsetRows.Count == 0)
        {
            await ReportReviewDataCommand(
                "Apply",
                "Calculate the Review Offsets before applying.",
                "WARN");
            return;
        }

        if (CalculatedOffsetRows.Count != CurrentOffsetRows.Count)
        {
            await ReportReviewDataCommand(
                "Apply",
                $"Current Review Offset is incomplete: {CurrentOffsetRows.Count}/{CalculatedOffsetRows.Count} rows.",
                "NG");
            return;
        }
        string SelectRow5(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return row.HoleKey;
        }

        var currentKeys = CurrentOffsetRows
            .Select(SelectRow5)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool CheckRow6(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return !currentKeys.Contains(row.HoleKey);
        }

        if (CalculatedOffsetRows.Any(CheckRow6))
        {
            await ReportReviewDataCommand(
                "Apply",
                "Calculated Review Offset contains a Hole that is not present in the current Review Offsets.",
                "NG");
            return;
        }
        string HandleCurrentOffsets7(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return row.HoleKey;
        }

        string HandleCurrentOffsets8(IGrouping<string, ST_CORRECTION_REVIEW_OFFSET_ROW> group)
        {
            return group.Key;
        }

        ST_CORRECTION_REVIEW_OFFSET_ROW HandleCurrentOffsets9(IGrouping<string, ST_CORRECTION_REVIEW_OFFSET_ROW> group)
        {
            return group.Last();
        }

        var currentOffsets = CurrentOffsetRows
            .GroupBy(HandleCurrentOffsets7, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
HandleCurrentOffsets8,
HandleCurrentOffsets9,
                StringComparer.OrdinalIgnoreCase);
        ST_CORRECTION_REVIEW_OFFSET_ROW SelectRow10(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            var current = currentOffsets[row.HoleKey];
            var preview = row with
            {
                OffsetXValue = current.OffsetXValue + row.OffsetXValue,
                OffsetYValue = current.OffsetYValue + row.OffsetYValue
            };
            return preview with
            {
                State = HasOffsetChanged(current, preview) ? "PENDING" : "CURRENT"
            };
        }
        ApplyPreviewRows = CalculatedOffsetRows
            .Select(SelectRow10)
            .ToArray();
        bool CheckRow11(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return row.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase);
        }

        _hasPendingReviewOffsetApply = ApplyPreviewRows.Any(CheckRow11);

        if (!_hasPendingReviewOffsetApply)
        {
            await ReportReviewDataCommand(
                "Apply",
                "There are no Review Offset changes to apply.",
                "OK",
                "0 Rows / No Change");
            return;
        }
        bool CountRowCallback12(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return row.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase);
        }

        bool CountRowCallback13(ST_CORRECTION_REVIEW_OFFSET_ROW row)
        {
            return row.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase);
        }

        await ReportReviewDataCommand(
            "Apply",
            $"{ApplyPreviewRows.Count(CountRowCallback12)} calculated values are shown in Apply Preview. Save is required.",
            "WARN",
            $"{ApplyPreviewRows.Count(CountRowCallback13)} Rows / Pending Save");
    }

    private async Task SaveAppliedReviewOffsets()
    {
        if (_loadedReviewResult is null || !_hasPendingReviewOffsetApply)
        {
            await ReportReviewDataCommand(
                "Save",
                "Apply the calculated Review Offsets before saving.",
                "WARN");
            return;
        }

        try
        {
            var selectedRecipeId = NormalizeRecipeId(_selectedRecipeIdProvider());
            if (string.IsNullOrWhiteSpace(selectedRecipeId))
            {
                selectedRecipeId = NormalizeRecipeId(_loadedReviewResult.RecipeId);
            }
            bool MatchItem14(ST_RECIPE_DATA item)
            {
                return NormalizeRecipeId(item.Id).Equals(
                                        selectedRecipeId,
                                        StringComparison.OrdinalIgnoreCase);
            }

            var recipe = (await _recipeManager.LoadRecipes())
                .FirstOrDefault(MatchItem14)
                ?? throw new InvalidOperationException(
                    $"Selected recipe could not be loaded: {selectedRecipeId}.");
            var parameters = recipe.Parameters.ToList();
            bool HandleSavedCount15(ST_CORRECTION_REVIEW_OFFSET_ROW row)
            {
                return row.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase);
            }

            var savedCount = ApplyPreviewRows.Count(HandleSavedCount15);
            foreach (var row in ApplyPreviewRows)
            {
                UpsertReviewOffsetParameter(parameters, row, "X", row.OffsetXValue);
                UpsertReviewOffsetParameter(parameters, row, "Y", row.OffsetYValue);
            }

            await _recipeManager.SaveRecipe(recipe with { Parameters = parameters });
            ST_CORRECTION_REVIEW_OFFSET_ROW SelectRow16(ST_CORRECTION_REVIEW_OFFSET_ROW row)
            {
                return row with
                {
                    State = row.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase)
                                        ? "SAVED"
                                        : row.State
                };
            }

            ApplyPreviewRows = ApplyPreviewRows
                .Select(SelectRow16)
                .ToArray();
            ST_CORRECTION_REVIEW_OFFSET_ROW SelectRow17(ST_CORRECTION_REVIEW_OFFSET_ROW row)
            {
                return row with { State = "CURRENT" };
            }

            CurrentOffsetRows = ApplyPreviewRows
                .Select(SelectRow17)
                .ToArray();
            _hasPendingReviewOffsetApply = false;
            _isLoadedReviewResultApplied = true;
            _appliedReviewResultPaths.Add(GetReviewResultIdentity(_loadedReviewResult));
            _currentReviewOffsetRecipeName = $"Recipe: {recipe.Id}.csv";

            await ReportReviewDataCommand(
                "Save",
                $"{savedCount} Review Offsets saved to {recipe.Id}.csv.",
                "OK",
                $"{recipe.Id}.csv / {CurrentOffsetRows.Count} Rows");
        }
        catch (Exception exception)
        {
            await ReportReviewDataCommand(
                "Save",
                $"Review Offset save failed: {exception.Message}",
                "NG");
        }
    }

    private static void UpsertReviewOffsetParameter(
        List<ST_RECIPE_PARAM> parameters,
        ST_CORRECTION_REVIEW_OFFSET_ROW row,
        string axis,
        double value)
    {
        var normalizedAxis = axis.Equals("Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "X";
        var key = $"{row.HoleKey}_REVIEW_OFFSET_{normalizedAxis}";
        var valueText = value.ToString("0.000000", CultureInfo.InvariantCulture);
        bool HandleParameterIndex18(ST_RECIPE_PARAM parameter)
        {
            return parameter.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var parameterIndex = parameters.FindIndex(HandleParameterIndex18);

        if (parameterIndex >= 0)
        {
            parameters[parameterIndex] = parameters[parameterIndex] with { Value = valueText };
            return;
        }

        var separatorIndex = row.HoleKey.IndexOf('_');
        var holeName = separatorIndex >= 0 && separatorIndex < row.HoleKey.Length - 1
            ? row.HoleKey[(separatorIndex + 1)..]
            : row.HoleKey;
        parameters.Add(new ST_RECIPE_PARAM(
            $"Hole {holeName} Review Offset {normalizedAxis}",
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

    private static string GetReviewResultIdentity(ST_REVIEW_RESULT_FILE_DATA result)
    {
        return string.IsNullOrWhiteSpace(result.FilePath)
            ? result.FileName.Trim()
            : Path.GetFullPath(result.FilePath);
    }

    private static bool HasOffsetChanged(
        ST_CORRECTION_REVIEW_OFFSET_ROW before,
        ST_CORRECTION_REVIEW_OFFSET_ROW after)
    {
        const double comparisonTolerance = 0.0000005;
        return Math.Abs(before.OffsetXValue - after.OffsetXValue) >= comparisonTolerance ||
               Math.Abs(before.OffsetYValue - after.OffsetYValue) >= comparisonTolerance;
    }

    private async Task ReportReviewDataCommand(
        string item,
        string message,
        string result,
        string? historyAfter = null)
    {
        _reviewLoadStatus = message;
        _reviewLoadState = result;
        _reviewDataHistory.Insert(0, new ST_CORRECTION_HISTORY_ROW(
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Environment.UserName,
            "REVIEW DATA",
            item,
            LoadedReviewFileName,
            historyAfter ?? message,
            result));

        ApplyTabData(_selectedTab);
        _statusReporter(message);
        await _refreshCurrentScreen();
    }

    private async Task<IReadOnlyList<ST_CORRECTION_REVIEW_OFFSET_ROW>> LoadCurrentReviewOffsets(
        ST_REVIEW_RESULT_FILE_DATA result)
    {
        var selectedRecipeId = NormalizeRecipeId(_selectedRecipeIdProvider());
        if (string.IsNullOrWhiteSpace(selectedRecipeId))
        {
            selectedRecipeId = NormalizeRecipeId(result.RecipeId);
        }
        bool MatchItem19(ST_RECIPE_DATA item)
        {
            return NormalizeRecipeId(item.Id).Equals(selectedRecipeId, StringComparison.OrdinalIgnoreCase);
        }

        var recipe = (await _recipeManager.LoadRecipes())
            .FirstOrDefault(MatchItem19);
        if (recipe is null)
        {
            throw new InvalidOperationException(
                $"Selected recipe could not be loaded: {selectedRecipeId}.");
        }

        _currentReviewOffsetRecipeName = $"Recipe: {recipe.Id}.csv";
        ST_CORRECTION_REVIEW_OFFSET_ROW SelectRow20(ST_REVIEW_RESULT_FILE_ROW row)
        {
            var holeKey = NormalizeReviewHoleKey(row.HoleKey, row.CellNo);
            var offsetX = ReadRecipeDouble(
                recipe,
                0.0,
                $"{holeKey}_REVIEW_OFFSET_X");
            var offsetY = ReadRecipeDouble(
                recipe,
                0.0,
                $"{holeKey}_REVIEW_OFFSET_Y");

            return new ST_CORRECTION_REVIEW_OFFSET_ROW(
                holeKey,
                $"H{row.HeadNo:00}",
                offsetX,
                offsetY,
                "CURRENT");
        }
        return result.Rows
            .Select(SelectRow20)
            .ToArray();
    }

    private async Task<string> LoadCurrentSettingState()
    {
        var axisMode = await LoadCurrentVisionAxisMode();
        return $"Vision Flip: {CReviewCoordinateTransformer.FormatVisionAxisMode(axisMode)}";
    }

    private async Task<EN_VISION_AXIS_MODE> LoadCurrentVisionAxisMode()
    {
        var xFlipValue = await _settingManager.GetValue(
            EN_SETTING_TAB.Option,
            "VisionXFlip",
            "");
        var yFlipValue = await _settingManager.GetValue(
            EN_SETTING_TAB.Option,
            "VisionYFlip",
            "");
        var xyFlipValue = await _settingManager.GetValue(
            EN_SETTING_TAB.Option,
            "VisionXyFlip",
            "");

        return CReviewCoordinateTransformer.ParseVisionAxisMode(
            xFlipValue,
            yFlipValue,
            xyFlipValue);
    }

    private void ValidateSelectedRecipe(ST_REVIEW_RESULT_FILE_DATA result)
    {
        var selectedRecipeId = NormalizeRecipeId(_selectedRecipeIdProvider());
        if (string.IsNullOrWhiteSpace(selectedRecipeId))
        {
            return;
        }

        if (!selectedRecipeId.Equals(
                NormalizeRecipeId(result.RecipeId),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Review Result recipe mismatch. Selected={selectedRecipeId}, File={result.RecipeId}.");
        }
    }

    private (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateReviewData()
    {
        var result = _loadedReviewResult;
        bool HandleNgCount21(ST_REVIEW_RESULT_FILE_ROW row)
        {
            return row.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase);
        }

        var ngCount = result?.Rows.Count(HandleNgCount21) ?? 0;

        return (
            [
                new("Review Result", result?.FileName ?? "Not Loaded"),
                new("Hole Count", result?.Rows.Count.ToString(CultureInfo.InvariantCulture) ?? "0"),
                new("NG Count", ngCount.ToString(CultureInfo.InvariantCulture), ngCount > 0 ? "WARN" : "OK"),
                new("Recipe", result?.RecipeId ?? NormalizeRecipeId(_selectedRecipeIdProvider()))
            ],
            [],
            [],
            [],
            [],
            _reviewDataHistory.ToArray());
    }

    private static string NormalizeRecipeId(string value)
    {
        return Path.GetFileNameWithoutExtension(value?.Trim() ?? "");
    }

    private static string NormalizeReviewHoleKey(string value, int cellNo)
    {
        var normalized = (value ?? "")
            .Trim()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized.StartsWith("CELL", StringComparison.Ordinal)
            ? normalized
            : $"CELL{cellNo}_{normalized}";
    }

    private static double ReadRecipeDouble(
        ST_RECIPE_DATA recipe,
        double defaultValue,
        params string[] keys)
    {
        foreach (var parameter in recipe.Parameters)
        {
            bool CheckKey22(string key)
            {
                return key.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase) ||
                                    key.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (!keys.Any(CheckKey22))
            {
                continue;
            }

            return double.TryParse(
                parameter.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : defaultValue;
        }

        return defaultValue;
    }

    private static (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateAlignCompData()
    {
        return (
            [
                new("Align Result", "Ready"),
                new("Distortion Key", "6"),
                new("Theta", "+0.002 deg"),
                new("Weight", "X 0.80 / Y 0.80")
            ],
            [
                new("Align", "Front Key", "X +0.012 / Y -0.006", "OK", "Vision PC"),
                new("Align", "Rear Key", "X +0.009 / Y -0.004", "OK", "Vision PC"),
                new("Distortion", "KEY 01-06", "6 point", "Ready", "Vision Result")
            ],
            [
                new("Align X", "Weighted", "+0.010", "mm", "Ready"),
                new("Align Y", "Weighted", "-0.005", "mm", "Ready"),
                new("Align Theta", "Rotation", "+0.002", "deg", "Ready"),
                new("Distortion", "Max DA", "42.0", "um", "OK")
            ],
            [
                new("X Start Weight", "Recipe", "0.80", "-", "Ready"),
                new("X End Weight", "Recipe", "0.80", "-", "Ready"),
                new("Y Start Weight", "Recipe", "0.80", "-", "Ready"),
                new("Y End Weight", "Recipe", "0.80", "-", "Ready")
            ],
            [
                new("Target", "Align result compensation"),
                new("Apply To", "Process coordinate transform"),
                new("Distortion Key", "6 key display / result storage")
            ],
            CreateCommonHistory());
    }

    private static (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateOffsetCompData()
    {
        return (
            [
                new("Mode", "Recipe Offset"),
                new("Review Offset", "Ready"),
                new("Cell Shift", "50"),
                new("Default Offset", "Ready")
            ],
            [
                new("Recipe", "Hole Offset", "Cell / Hole", "Loaded", "JHMI_RCP"),
                new("Recipe", "Cell Shift", "CELL01-CELL50", "Loaded", "JHMI_RCP"),
                new("Setup", "Scanner Default Offset", "8 scanner", "Ready", "Default Offset")
            ],
            [
                new("CELL1_A1_RECIPE_OFFSET_X", "Hole", "+0.003", "mm", "Ready"),
                new("CELL7_A1_RECIPE_OFFSET_Y", "Hole", "+0.002", "mm", "Ready"),
                new("CELL12_ALIGN_X", "Cell", "-0.010", "mm", "Ready"),
                new("SCANNER_04_DEFAULT_X", "Default", "+0.006", "mm", "Ready")
            ],
            [
                new("Simple Offset", "Key-in", "Recipe value edit", "-", "Ready"),
                new("Rough Offset", "Cell 1 Point", "Cell batch apply", "-", "Pending"),
                new("Default Offset", "Scanner interval", "Head default apply", "-", "Ready")
            ],
            [
                new("Target", "Recipe offset parameter"),
                new("Simple Offset", "Direct key-in value"),
                new("Excluded", "Scanner Field Correction / Scan Comp")
            ],
            CreateCommonHistory());
    }

    private static (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateApcIcrData()
    {
        return (
            [
                new("APC", "OFF", "WAIT"),
                new("ICR", "OFF", "WAIT"),
                new("Precision", "+/-13 um"),
                new("Source", "CIM Share")
            ],
            [
                new("APC", "APC File", "Not Loaded", "WAIT", "CIM PC Share"),
                new("ICR", "ICR File", "Not Loaded", "WAIT", "CIM PC Share"),
                new("Setting", "OFFSET_PRECISION", "+/-13", "Ready", "EC_LD")
            ],
            [
                new("APC_USE", "External", "0", "-", "WAIT"),
                new("ICR_USE", "External", "0", "-", "WAIT"),
                new("OFFSET_PRECISION", "Limit", "13", "um", "Ready")
            ],
            [
                new("APC Apply", "Correction Table", "Standby", "-", "WAIT"),
                new("ICR Apply", "Correction Table", "Standby", "-", "WAIT"),
                new("Source Backup", "History", "Required", "-", "Ready")
            ],
            [
                new("Target", "External precision compensation"),
                new("Input", "CIM/shared folder file"),
                new("Apply To", "Correction table / process model")
            ],
            CreateCommonHistory());
    }

    private static (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateZeroDefenseData()
    {
        return (
            [
                new("0-Line Point", "5"),
                new("Mode", "Review"),
                new("Judge", "Ready"),
                new("Apply", "Standby")
            ],
            [
                new("Recipe", "ZERO_DEFENCE_REVIEW_POINT", "5", "Loaded", "JHMI_RCP"),
                new("Review", "0-Line Result", "5 point", "Ready", "Review Result"),
                new("Stage", "Line Move", "Stage PC", "Ready", "Melsec / Stage PC")
            ],
            [
                new("Line Error X", "Average", "+0.008", "mm", "OK"),
                new("Line Error Y", "Average", "-0.006", "mm", "OK"),
                new("Max Deviation", "0-Line", "0.018", "mm", "OK")
            ],
            [
                new("0-Line Offset X", "Recipe", "+0.008", "mm", "Ready"),
                new("0-Line Offset Y", "Recipe", "-0.006", "mm", "Ready"),
                new("Defense Result", "Interlock", "OK", "-", "Ready")
            ],
            [
                new("Target", "0-line defense review"),
                new("Review Point", "Recipe ZERO_DEFENCE_REVIEW_POINT"),
                new("Apply To", "Recipe offset / process model")
            ],
            CreateCommonHistory());
    }

    private static (
        IReadOnlyList<ST_DISPLAY_ITEM> Summary,
        IReadOnlyList<ST_CORRECTION_SOURCE_ROW> Source,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Candidate,
        IReadOnlyList<ST_CORRECTION_VALUE_ROW> Apply,
        IReadOnlyList<ST_DISPLAY_ITEM> Detail,
        IReadOnlyList<ST_CORRECTION_HISTORY_ROW> History) CreateOutputHistoryData()
    {
        return (
            [
                new("Output", "Correction Table"),
                new("Recipe", "DRILL_A01"),
                new("Last Apply", "10:24:36"),
                new("State", "Modified", "WARN")
            ],
            [
                new("Output", "Recipe", "DRILL_A01.csv", "Modified", "Config\\RECIPE"),
                new("Output", "Correction Table", "Current", "Ready", "Config\\Correction"),
                new("Log", "Correction History", "Today", "Ready", "Data\\Log")
            ],
            [
                new("Review Offset", "Recipe", "+0.006 / -0.004", "mm", "Ready"),
                new("Align Offset", "Transform", "+0.010 / -0.005", "mm", "Ready"),
                new("Zero Defense", "Recipe", "+0.008 / -0.006", "mm", "Ready")
            ],
            [
                new("Recipe Save", "DRILL_A01.csv", "Required", "-", "Pending"),
                new("Script Build", "Process Model", "Correction applied", "-", "Ready"),
                new("History Write", "Correction Log", "Required", "-", "Ready")
            ],
            [
                new("Target", "Final correction output"),
                new("Apply Order", "Review / Align / Offset / APC-ICR / Zero"),
                new("History", "Before / After / User / Time")
            ],
            [
                new("10:24:36", "ENG1", "ZERO DEFENSE", "Line Offset", "0.000 / 0.000", "+0.008 / -0.006", "Pending"),
                new("10:18:11", "ENG1", "OFFSET COMP", "CELL1_A1_RECIPE_OFFSET_X", "0.000", "+0.003", "Saved"),
                new("10:12:40", "ENG1", "ALIGN COMP", "Theta", "0.000", "+0.002", "Saved"),
                new("10:04:29", "ENG1", "REVIEW DATA", "Review Result", "-", "Loaded", "OK")
            ]);
    }

    private static IReadOnlyList<ST_CORRECTION_HISTORY_ROW> CreateCommonHistory()
    {
        return
        [
            new("10:24:36", "ENG1", "REVIEW DATA", "Review Result", "-", "Loaded", "OK"),
            new("10:18:11", "ENG1", "OFFSET COMP", "CELL1_A1_RECIPE_OFFSET_X", "0.000", "+0.003", "Saved"),
            new("10:12:40", "ENG1", "ALIGN COMP", "Theta", "0.000", "+0.002", "Saved"),
            new("10:05:09", "ENG1", "APC / ICR", "APC File", "-", "Standby", "WAIT")
        ];
    }

    private static string GetSubtitle(string selectedTab)
    {
        string EvaluateSelectedTabSwitch2()
        {
            var switchValue = selectedTab;
            switch (switchValue)
            {
                case "REVIEW DATA":
                    return "Review result load, measurement error review and compensation point selection";
                case "ALIGN COMP":
                    return "Align X/Y/Theta and distortion key compensation";
                case "OFFSET COMP":
                    return "Recipe offset, head offset, cell shift and scanner default offset";
                case "APC / ICR":
                    return "External APC / ICR precision correction file apply";
                case "ZERO DEFENSE":
                    return "0-line review point check and defense offset apply";
                case "OUTPUT / HISTORY":
                    return "Final correction output preview and apply history";
                default:
                    return "Correction operation";
            }
        }

        return EvaluateSelectedTabSwitch2();
    }

    private static string GetSourceName(string selectedTab)
    {
        string EvaluateSelectedTabSwitch3()
        {
            var switchValue = selectedTab;
            switch (switchValue)
            {
                case "APC / ICR":
                    return "External";
                case "ALIGN COMP":
                    return "Vision";
                case "OFFSET COMP":
                    return "Recipe";
                case "ZERO DEFENSE":
                    return "Review";
                case "OUTPUT / HISTORY":
                    return "Mixed";
                default:
                    return "Review";
            }
        }

        return EvaluateSelectedTabSwitch3();
    }

    private static string GetStateName(string selectedTab)
    {
        return selectedTab is "APC / ICR"
            ? "WAIT"
            : "Ready";
    }
}

public sealed record ST_CORRECTION_TAB(
    string Name,
    bool IsSelected);

public sealed record ST_CORRECTION_SOURCE_ROW(
    string Type,
    string Name,
    string Value,
    string State,
    string Source)
{
    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }
}

public sealed record ST_CORRECTION_VALUE_ROW(
    string Item,
    string Target,
    string Value,
    string Unit,
    string State)
{
    public Brush ValueBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }

    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }
}

public sealed record ST_CORRECTION_HISTORY_ROW(
    string Time,
    string User,
    string Tab,
    string Item,
    string Before,
    string After,
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

public sealed record ST_CORRECTION_REVIEW_RESULT_ROW(
    string HoleKey,
    string Head,
    int Cell,
    double ErrorXValue,
    double ErrorYValue,
    string Judge)
{
    public string ErrorX
    {
        get
        {
            return FormatDisplayValue(ErrorXValue);
        }
    }

    public string ErrorY
    {
        get
        {
            return FormatDisplayValue(ErrorYValue);
        }
    }

    public Brush JudgeBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(Judge);
        }
    }

    private static string FormatDisplayValue(double value)
    {
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }
}

public sealed record ST_CORRECTION_REVIEW_OFFSET_ROW(
    string HoleKey,
    string Head,
    double OffsetXValue,
    double OffsetYValue,
    string State)
{
    public string OffsetX
    {
        get
        {
            return FormatDisplayValue(OffsetXValue);
        }
    }

    public string OffsetY
    {
        get
        {
            return FormatDisplayValue(OffsetYValue);
        }
    }

    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }

    public Brush OffsetValueBrush
    {
        get
        {
            Brush EvaluateValueSwitch4()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "PENDING":
                        return CStatusBrush.Wait;
                    case "SAVED":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueSwitch4();
        }
    }

    private static string FormatDisplayValue(double value)
    {
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }
}
