using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Drilling.UI.Menu;
using Drilling.Common.Automation;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.Common.Recipe;
using Drilling.Common.Review;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuMain(
    CStationManager stationManager,
    CInterfaceManager interfaceManager,
    CAutomationManager automationManager,
    CRecipeManager recipeManager,
    CSettingManager settingManager,
    CReviewManager reviewManager,
    Func<string> selectedRecipeIdProvider,
    Func<IReadOnlySet<int>> selectedPreviewHeadNosProvider,
    CButtonCommand togglePreviewHeadCommand,
    Action<string> statusReporter,
    Func<Task> refreshCurrentScreen) : CMenuBase
{
    private const string AutoStepPowerCheck = "POWER_CHECK";
    private const string AutoStepAlign = "ALIGN";
    private const string AutoStepProcess = "PROCESS";
    private const string AutoStepInspection = "INSPECTION";
    private const string ParameterTabHead = "HEAD";
    private const string ParameterTabOptic = "OPTIC";
    private const string ParameterTabScanner = "SCANNER";

    private const string SettingAutoPowerCheckUse = "AUTO_POWER_CHECK_USE";
    private const string SettingAutoAlignUse = "AUTO_ALIGN_USE";
    private const string SettingAutoProcessUse = "AUTO_PROCESS_USE";
    private const string SettingAutoInspectionUse = "AUTO_INSPECTION_USE";
    private const double ReviewSequenceRowTolerance = 0.001;

    private static readonly IReadOnlyDictionary<string, string> OptionalAutoStepSettingKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AutoStepPowerCheck] = SettingAutoPowerCheckUse,
            [AutoStepAlign] = SettingAutoAlignUse,
            [AutoStepProcess] = SettingAutoProcessUse,
            [AutoStepInspection] = SettingAutoInspectionUse
        };

    private CButtonCommand? _toggleSequenceStepUseCommand;
    private CButtonCommand? _selectParameterTabCommand;
    private string _selectedParameterTabKey = ParameterTabHead;
    private IReadOnlyDictionary<string, bool> _autoStepOptions =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ST_DISPLAY_ITEM> _cycleItems = [];
    private IReadOnlyList<ST_DISPLAY_ITEM> _resultItems = [];
    private IReadOnlyList<ST_MAIN_PROCESS_SEQUENCE_ITEM> _processSequenceItems = [];
    private IReadOnlyList<ST_DISPLAY_ITEM> _currentStepDetails = [];
    private IReadOnlyList<ST_DISPLAY_ITEM> _processSummaryItems = [];
    private IReadOnlyList<ST_DISPLAY_ITEM> _lifecycleItems = [];
    private IReadOnlyList<ST_SCRIPT_TASK_STATUS_ITEM> _scriptTaskStatusItems = [];
    private IReadOnlyList<ST_INSPECTION_STATUS_ITEM> _inspectionStatusItems = [];
    private string _inspectionSummary = "0 / 0 holes";
    private string _inspectionModeText = "MODE : -";
    private string _inspectionRuleText = "RULE : -";
    private Visibility _inspectionRuleVisibility = Visibility.Collapsed;
    private IReadOnlyList<ST_INTERLOCK_ITEM> _interlockItems = [];
    private IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> _scannerStatusItems = [];
    private string _processStep = "";
    private string _scriptStatus = "";
    private string _scriptStatusText = "Script Status       -";
    private string _resultMessage = "";
    private string _totalPointsText = "Total Points        0";
    private string _moveCountText = "Move Count (G0)     0";
    private string _laserOnSegmentsText = "Laser On Segments   0";
    private string _estimatedTimeText = "Estimated Time       00:00:00";
    private string _elapsedTimeText = "Elapsed Time         00:00:00";
    private string _progressText = "Progress             0.0%";
    private string _progressPercentText = "0.0%";
    private double _progressPercent;
    private string _processResultValue = "PENDING";
    private Brush _processResultBrush = CStatusBrush.Wait;
    private readonly CScannerStatusPollingService _scannerStatusPollingService =
        new(automationManager, settingManager);

    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Main;
        }
    }

    public IReadOnlyList<ST_HEAD_PREVIEW> HeadPreviews { get; private set; } = [];

    public IReadOnlyList<ST_HEAD_PREVIEW> OddHeadPreviews { get; private set; } = [];

    public IReadOnlyList<ST_HEAD_PREVIEW> EvenHeadPreviews { get; private set; } = [];

    public IReadOnlyList<ST_HEAD_ASSIGNMENT_AREA> HeadAssignmentAreas { get; private set; } = [];

    public ST_GLASS_PREVIEW_FRAME GlassFrame { get; private set; } =
        new(44.0, 42.0, 772.0, 238.0);

    public string GlassPreviewSummary { get; private set; } = "0 heads / 0 points";

    public ImageSource? RecipePreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellPreviewLabels { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> CycleItems
    {
        get
        {
            return _cycleItems;
        }

        private set
        {
            SetProperty(ref _cycleItems, value);
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> ResultItems
    {
        get
        {
            return _resultItems;
        }

        private set
        {
            SetProperty(ref _resultItems, value);
        }
    }

    public IReadOnlyList<ST_MAIN_PROCESS_SEQUENCE_ITEM> ProcessSequenceItems
    {
        get
        {
            return _processSequenceItems;
        }

        private set
        {
            SetProperty(ref _processSequenceItems, value);
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> CurrentStepDetails
    {
        get
        {
            return _currentStepDetails;
        }

        private set
        {
            SetProperty(ref _currentStepDetails, value);
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> ProcessSummaryItems
    {
        get
        {
            return _processSummaryItems;
        }

        private set
        {
            SetProperty(ref _processSummaryItems, value);
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> LifecycleItems
    {
        get
        {
            return _lifecycleItems;
        }

        private set
        {
            SetProperty(ref _lifecycleItems, value);
        }
    }

    public IReadOnlyList<ST_SCRIPT_TASK_STATUS_ITEM> ScriptTaskStatusItems
    {
        get
        {
            return _scriptTaskStatusItems;
        }

        private set
        {
            SetProperty(ref _scriptTaskStatusItems, value);
        }
    }

    public IReadOnlyList<ST_INSPECTION_STATUS_ITEM> InspectionStatusItems
    {
        get
        {
            return _inspectionStatusItems;
        }

        private set
        {
            SetProperty(ref _inspectionStatusItems, value);
        }
    }

    public string InspectionSummary
    {
        get
        {
            return _inspectionSummary;
        }

        private set
        {
            SetProperty(ref _inspectionSummary, value);
        }
    }

    public string InspectionModeText
    {
        get
        {
            return _inspectionModeText;
        }

        private set
        {
            SetProperty(ref _inspectionModeText, value);
        }
    }

    public string InspectionRuleText
    {
        get
        {
            return _inspectionRuleText;
        }

        private set
        {
            SetProperty(ref _inspectionRuleText, value);
        }
    }

    public Visibility InspectionRuleVisibility
    {
        get
        {
            return _inspectionRuleVisibility;
        }

        private set
        {
            SetProperty(ref _inspectionRuleVisibility, value);
        }
    }

    public IReadOnlyList<ST_INTERLOCK_ITEM> InterlockItems
    {
        get
        {
            return _interlockItems;
        }

        private set
        {
            SetProperty(ref _interlockItems, value);
        }
    }

    public IReadOnlyList<ST_HEAD_PARAMETER> HeadParameters { get; private set; } = [];

    public IReadOnlyList<ST_MAIN_PARAMETER_TAB_ITEM> ParameterTabs { get; private set; } = [];

    public IReadOnlyList<ST_OPTIC_PARAMETER_GROUP> OpticParameterGroups { get; private set; } = [];

    public IReadOnlyList<ST_OPTIC_HEAD_PARAMETER> OpticHeadParameters { get; private set; } = [];

    public IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> ScannerStatusItems
    {
        get
        {
            return _scannerStatusItems;
        }

        private set
        {
            SetProperty(ref _scannerStatusItems, value);
        }
    }

    public Visibility HeadParameterVisibility { get; private set; } = Visibility.Visible;

    public Visibility OpticParameterVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility ScannerStatusVisibility { get; private set; } = Visibility.Collapsed;

    public bool IsScannerStatusSelected
    {
        get
        {
            return _selectedParameterTabKey.Equals(ParameterTabScanner, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string ProcessStep
    {
        get
        {
            return _processStep;
        }

        private set
        {
            SetProperty(ref _processStep, value);
        }
    }

    public string ScriptStatus
    {
        get
        {
            return _scriptStatus;
        }

        private set
        {
            SetProperty(ref _scriptStatus, value);
        }
    }

    public string ScriptStatusText
    {
        get
        {
            return _scriptStatusText;
        }

        private set
        {
            SetProperty(ref _scriptStatusText, value);
        }
    }

    public string ResultMessage
    {
        get
        {
            return _resultMessage;
        }

        private set
        {
            SetProperty(ref _resultMessage, value);
        }
    }

    public string TotalPointsText
    {
        get
        {
            return _totalPointsText;
        }

        private set
        {
            SetProperty(ref _totalPointsText, value);
        }
    }

    public string MoveCountText
    {
        get
        {
            return _moveCountText;
        }

        private set
        {
            SetProperty(ref _moveCountText, value);
        }
    }

    public string LaserOnSegmentsText
    {
        get
        {
            return _laserOnSegmentsText;
        }

        private set
        {
            SetProperty(ref _laserOnSegmentsText, value);
        }
    }

    public string EstimatedTimeText
    {
        get
        {
            return _estimatedTimeText;
        }

        private set
        {
            SetProperty(ref _estimatedTimeText, value);
        }
    }

    public string ElapsedTimeText
    {
        get
        {
            return _elapsedTimeText;
        }

        private set
        {
            SetProperty(ref _elapsedTimeText, value);
        }
    }

    public string ProgressText
    {
        get
        {
            return _progressText;
        }

        private set
        {
            SetProperty(ref _progressText, value);
        }
    }

    public string ProgressPercentText
    {
        get
        {
            return _progressPercentText;
        }

        private set
        {
            SetProperty(ref _progressPercentText, value);
        }
    }

    public double ProgressPercent
    {
        get
        {
            return _progressPercent;
        }

        private set
        {
            SetProperty(ref _progressPercent, value);
        }
    }

    public string ProcessResultValue
    {
        get
        {
            return _processResultValue;
        }

        private set
        {
            SetProperty(ref _processResultValue, value);
        }
    }

    public Brush ProcessResultBrush
    {
        get
        {
            return _processResultBrush;
        }

        private set
        {
            SetProperty(ref _processResultBrush, value);
        }
    }

    public CButtonCommand TogglePreviewHeadCommand { get; private set; } = CButtonCommand.NoOp;

    public CButtonCommand ToggleSequenceStepUseCommand
    {
        get
        {
            async void HandleToggleSequenceStepUseCommand1(object? parameter)
            {
                await ToggleSequenceStepUse(parameter);
            }

            return _toggleSequenceStepUseCommand ??= new CButtonCommand(HandleToggleSequenceStepUseCommand1);
        }
    }

    public CButtonCommand SelectParameterTabCommand
    {
        get
        {
            async void HandleSelectParameterTabCommand2(object? parameter)
            {
                await SelectParameterTab(parameter);
            }

            return _selectParameterTabCommand ??= new CButtonCommand(HandleSelectParameterTabCommand2);
        }
    }

    public async override Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        var snapshot = await stationManager.GetStatus(cancellationToken);
        var selectedHeadNos = selectedPreviewHeadNosProvider().ToHashSet();
        var previewParameters = await LoadPreviewParameters(snapshot, cancellationToken);
        var previewHeadLayout = await LoadPreviewHeadLayout(cancellationToken);
        var autoStepOptions = await LoadAutoStepOptions(cancellationToken);
        _autoStepOptions = autoStepOptions;
        var canToggleAutoStep = IsAutoStepOptionEditable(snapshot.ProcessStep);
        var inspectionStatus = BuildInspectionStatus(snapshot);
        var scriptTaskStatusItems = BuildScriptTaskStatusPlaceholders(snapshot, previewParameters);
        var opticParameterGroups = _selectedParameterTabKey.Equals(ParameterTabOptic, StringComparison.OrdinalIgnoreCase)
            ? await BuildOpticParameterGroups(previewParameters, cancellationToken)
            : [];
        var opticHeadParameters = _selectedParameterTabKey.Equals(ParameterTabOptic, StringComparison.OrdinalIgnoreCase)
            ? await BuildOpticHeadParameters(previewParameters, cancellationToken)
            : [];
        if (IsScannerStatusSelected)
        {
            _scannerStatusPollingService.Start();
        }

        var scannerStatusItems = IsScannerStatusSelected
            ? _scannerStatusPollingService.GetSnapshot()
            : [];

        var metrics = new List<ST_DISPLAY_ITEM>
        {
            new("Cycle State", snapshot.ProcessStep.ToString()),
            new("Script", FormatScriptStatus(snapshot.ScriptStatus)),
            new("Heads", snapshot.HeadPreviews.Count.ToString()),
            new("Result", snapshot.Result?.Message ?? "Waiting")
        };

        var headAssignmentMap = BuildHeadAssignmentMap(snapshot, previewParameters, selectedHeadNos, previewHeadLayout);
        var recipePreview = BuildRecipePreview(previewParameters, selectedHeadNos, previewHeadLayout);
        ST_HEAD_ASSIGNMENT_AREA SelectArea3(ST_HEAD_ASSIGNMENT_AREA area)
        {
            return area with
            {
                PointCount = recipePreview.HeadPointCounts.TryGetValue(area.HeadNo, out var count)
                                ? (int)Math.Min(int.MaxValue, count)
                                : 0
            };
        }

        var displayedHeadAreas = headAssignmentMap.Areas
            .Select(SelectArea3)
            .ToArray();
        var glassPreviewSummary =
            $"{displayedHeadAreas.Length}H / {recipePreview.TotalPointCount:N0}P / {FormatGlassSizeText(previewParameters)}" +
            (recipePreview.UnassignedPointCount > 0 ? $" / U:{recipePreview.UnassignedPointCount:N0}" : "");
        int HandleHeadStatusMap4(ST_HEAD_PATH_DATA head)
        {
            return head.HeadNo;
        }

        int HandleHeadStatusMap5(IGrouping<int, ST_HEAD_PATH_DATA> group)
        {
            return group.Key;
        }

        EN_HEAD_PROCESS_STATUS HandleHeadStatusMap6(IGrouping<int, ST_HEAD_PATH_DATA> group)
        {
            return group.Last().Status;
        }

        var headStatusMap = snapshot.HeadPreviews
            .GroupBy(HandleHeadStatusMap4)
            .ToDictionary(HandleHeadStatusMap5, HandleHeadStatusMap6);
        ST_DISPLAY_ITEM SelectHead7(ST_HEAD_ASSIGNMENT_AREA head)
        {
            return new ST_DISPLAY_ITEM(
                            $"Head {head.HeadNo:00}",
                            headStatusMap.TryGetValue(head.HeadNo, out var status) ? status.ToString() : "Ready",
                            "Head assignment pending");
        }

        var headItems = displayedHeadAreas
            .Select(SelectHead7)
            .ToArray();
        ST_HEAD_PREVIEW SelectHead8(ST_HEAD_ASSIGNMENT_AREA head)
        {
            return BuildHeadPreviewItem(
                            head.HeadNo,
                            headStatusMap.TryGetValue(head.HeadNo, out var status) ? status : EN_HEAD_PROCESS_STATUS.Ready,
                            selectedHeadNos);
        }

        var headPreviewItems = displayedHeadAreas
            .Select(SelectHead8)
            .ToArray();

        Apply(
            headPreviewItems,
            displayedHeadAreas,
            recipePreview.Frame,
            glassPreviewSummary,
            recipePreview.Image,
            recipePreview.CellLabels,
            [
                new("Cycle State", snapshot.ProcessStep.ToString()),
                new("Script Status", FormatScriptStatus(snapshot.ScriptStatus)),
                new("Automation", "Simulation"),
                new("Preview Source", "Recipe Cell drilling points")
            ],
            [
                new("Complete Time", snapshot.Result?.CompletedAt.ToString("HH:mm:ss") ?? "-"),
                new("Result", snapshot.Result?.IsSuccess == true ? "OK" : "Waiting"),
                new("Message", snapshot.Result?.Message ?? "Ready")
            ],
            [
                .. BuildProcessSequenceItems(snapshot.ProcessSequence, autoStepOptions, canToggleAutoStep)
            ],
            [
                .. snapshot.CurrentStepDetails.Select(ToDisplayItem)
            ],
            [
                .. snapshot.ProcessSummary.Select(ToDisplayItem)
            ],
            [
                .. snapshot.ScriptLifecycleItems.Select(ToDisplayItem)
            ],
            scriptTaskStatusItems,
            inspectionStatus.Items,
            inspectionStatus.Summary,
            inspectionStatus.ModeText,
            inspectionStatus.RuleText,
            inspectionStatus.RuleVisibility,
            [
                .. snapshot.InterlockItems.Select(ToInterlockItem)
            ],
            BuildHeadParameters(previewParameters),
            BuildParameterTabs(),
            opticParameterGroups,
            opticHeadParameters,
            scannerStatusItems,
            snapshot.ProcessStep.ToString(),
            FormatScriptStatus(snapshot.ScriptStatus),
            snapshot.Result?.Message ?? "Waiting for operator command.",
            snapshot.Statistics,
            FormatProcessResult(snapshot),
            togglePreviewHeadCommand);

        return new CScreenViewModel(
            EN_MENU.Main,
            "MAIN",
            "Automatic operation, 8-head path preview, script status, and cycle result.",
            metrics,
            [
                new("8 Head Preview Source", headItems)
            ],
            showCycleControls: true,
            this);
    }

    public async Task RefreshLiveStatus(CancellationToken cancellationToken = default)
    {
        var snapshot = await stationManager.GetStatus(cancellationToken);
        IReadOnlyDictionary<string, string> previewParameters =
            snapshot.ProcessModel?.Parameters ??
            snapshot.ProcessPlan?.Parameters ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inspectionStatus = BuildInspectionStatus(snapshot);
        var statistics = snapshot.Statistics;

        CycleItems =
        [
            new("Cycle State", snapshot.ProcessStep.ToString()),
            new("Script Status", FormatScriptStatus(snapshot.ScriptStatus)),
            new("Automation", "Simulation"),
            new("Preview Source", "Recipe Cell drilling points")
        ];
        ResultItems =
        [
            new("Complete Time", snapshot.Result?.CompletedAt.ToString("HH:mm:ss") ?? "-"),
            new("Result", snapshot.Result?.IsSuccess == true ? "OK" : "Waiting"),
            new("Message", snapshot.Result?.Message ?? "Ready")
        ];
        ProcessSequenceItems = BuildProcessSequenceItems(
            snapshot.ProcessSequence,
            _autoStepOptions,
            IsAutoStepOptionEditable(snapshot.ProcessStep));
        CurrentStepDetails = [.. snapshot.CurrentStepDetails.Select(ToDisplayItem)];
        ProcessSummaryItems = [.. snapshot.ProcessSummary.Select(ToDisplayItem)];
        LifecycleItems = [.. snapshot.ScriptLifecycleItems.Select(ToDisplayItem)];
        InspectionStatusItems = inspectionStatus.Items;
        InspectionSummary = inspectionStatus.Summary;
        InspectionModeText = inspectionStatus.ModeText;
        InspectionRuleText = inspectionStatus.RuleText;
        InspectionRuleVisibility = inspectionStatus.RuleVisibility;
        InterlockItems = [.. snapshot.InterlockItems.Select(ToInterlockItem)];
        ProcessStep = snapshot.ProcessStep.ToString();
        ScriptStatus = FormatScriptStatus(snapshot.ScriptStatus);
        ScriptStatusText = $"Script Status       {ScriptStatus}";
        ResultMessage = snapshot.Result?.Message ?? "Waiting for operator command.";
        TotalPointsText = $"Total Points        {statistics.TotalPoints:N0}";
        MoveCountText = $"Move Count (G0)     {statistics.MoveCount:N0}";
        LaserOnSegmentsText = $"Laser On Segments   {statistics.LaserOnSegments:N0}";
        EstimatedTimeText = $"Estimated Time       {FormatDuration(statistics.EstimatedTime)}";
        ElapsedTimeText = $"Elapsed Time         {FormatDuration(statistics.ElapsedTime)}";
        ProgressText = $"Progress             {statistics.ProgressPercent:F1}%";
        ProgressPercentText = $"{statistics.ProgressPercent:F1}%";
        ProgressPercent = statistics.ProgressPercent;
        ProcessResultValue = FormatProcessResult(snapshot);
        ProcessResultBrush = CStatusBrush.ForDisplayState(ProcessResultValue);

        if (IsScannerStatusSelected)
        {
            _scannerStatusPollingService.Start();
            ScannerStatusItems = _scannerStatusPollingService.GetSnapshot();
        }

        ScriptTaskStatusItems = await BuildScriptTaskStatusItems(
            snapshot,
            previewParameters,
            cancellationToken);
    }

    private void Apply(
        IReadOnlyList<ST_HEAD_PREVIEW> headPreviews,
        IReadOnlyList<ST_HEAD_ASSIGNMENT_AREA> headAssignmentAreas,
        ST_GLASS_PREVIEW_FRAME glassFrame,
        string glassPreviewSummary,
        ImageSource? recipePreviewImage,
        IReadOnlyList<ST_CELL_PREVIEW_LABEL> cellPreviewLabels,
        IReadOnlyList<ST_DISPLAY_ITEM> cycleItems,
        IReadOnlyList<ST_DISPLAY_ITEM> resultItems,
        IReadOnlyList<ST_MAIN_PROCESS_SEQUENCE_ITEM> processSequenceItems,
        IReadOnlyList<ST_DISPLAY_ITEM> currentStepDetails,
        IReadOnlyList<ST_DISPLAY_ITEM> processSummaryItems,
        IReadOnlyList<ST_DISPLAY_ITEM> lifecycleItems,
        IReadOnlyList<ST_SCRIPT_TASK_STATUS_ITEM> scriptTaskStatusItems,
        IReadOnlyList<ST_INSPECTION_STATUS_ITEM> inspectionStatusItems,
        string inspectionSummary,
        string inspectionModeText,
        string inspectionRuleText,
        Visibility inspectionRuleVisibility,
        IReadOnlyList<ST_INTERLOCK_ITEM> interlockItems,
        IReadOnlyList<ST_HEAD_PARAMETER> headParameters,
        IReadOnlyList<ST_MAIN_PARAMETER_TAB_ITEM> parameterTabs,
        IReadOnlyList<ST_OPTIC_PARAMETER_GROUP> opticParameterGroups,
        IReadOnlyList<ST_OPTIC_HEAD_PARAMETER> opticHeadParameters,
        IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> scannerStatusItems,
        string processStep,
        string scriptStatus,
        string resultMessage,
        ST_PROCESS_STATISTICS statistics,
        string processResultValue,
        CButtonCommand togglePreviewHeadCommand)
    {
        HeadPreviews = headPreviews;
        bool FilterHead9(ST_HEAD_PREVIEW head)
        {
            return head.HeadNo % 2 != 0;
        }

        int GetHeadSortKey10(ST_HEAD_PREVIEW head)
        {
            return head.HeadNo;
        }

        OddHeadPreviews = headPreviews
            .Where(FilterHead9)
            .OrderBy(GetHeadSortKey10)
            .ToArray();
        bool FilterHead11(ST_HEAD_PREVIEW head)
        {
            return head.HeadNo % 2 == 0;
        }

        int GetHeadSortKey12(ST_HEAD_PREVIEW head)
        {
            return head.HeadNo;
        }

        EvenHeadPreviews = headPreviews
            .Where(FilterHead11)
            .OrderBy(GetHeadSortKey12)
            .ToArray();
        HeadAssignmentAreas = headAssignmentAreas;
        GlassFrame = glassFrame;
        GlassPreviewSummary = glassPreviewSummary;
        RecipePreviewImage = recipePreviewImage;
        CellPreviewLabels = cellPreviewLabels;
        CycleItems = cycleItems;
        ResultItems = resultItems;
        ProcessSequenceItems = processSequenceItems;
        CurrentStepDetails = currentStepDetails;
        ProcessSummaryItems = processSummaryItems;
        LifecycleItems = lifecycleItems;
        ScriptTaskStatusItems = scriptTaskStatusItems;
        InspectionStatusItems = inspectionStatusItems;
        InspectionSummary = inspectionSummary;
        InspectionModeText = inspectionModeText;
        InspectionRuleText = inspectionRuleText;
        InspectionRuleVisibility = inspectionRuleVisibility;
        InterlockItems = interlockItems;
        HeadParameters = headParameters;
        ParameterTabs = parameterTabs;
        OpticParameterGroups = opticParameterGroups;
        OpticHeadParameters = OrderOpticHeadParameters(opticHeadParameters);
        ScannerStatusItems = scannerStatusItems;
        HeadParameterVisibility = _selectedParameterTabKey.Equals(ParameterTabHead, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpticParameterVisibility = _selectedParameterTabKey.Equals(ParameterTabOptic, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScannerStatusVisibility = _selectedParameterTabKey.Equals(ParameterTabScanner, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProcessStep = processStep;
        ScriptStatus = scriptStatus;
        ScriptStatusText = $"Script Status       {scriptStatus}";
        ResultMessage = resultMessage;
        TotalPointsText = $"Total Points        {statistics.TotalPoints:N0}";
        MoveCountText = $"Move Count (G0)     {statistics.MoveCount:N0}";
        LaserOnSegmentsText = $"Laser On Segments   {statistics.LaserOnSegments:N0}";
        EstimatedTimeText = $"Estimated Time       {FormatDuration(statistics.EstimatedTime)}";
        ElapsedTimeText = $"Elapsed Time         {FormatDuration(statistics.ElapsedTime)}";
        ProgressText = $"Progress             {statistics.ProgressPercent:F1}%";
        ProgressPercentText = $"{statistics.ProgressPercent:F1}%";
        ProgressPercent = statistics.ProgressPercent;
        ProcessResultValue = processResultValue;
        ProcessResultBrush = CStatusBrush.ForDisplayState(processResultValue);
        TogglePreviewHeadCommand = togglePreviewHeadCommand;
    }

    public static string FormatScriptStatus(EN_SCRIPT_STATUS status)
    {
        string EvaluateStatusSwitch1()
        {
            var switchValue = status;
            switch (switchValue)
            {
                case EN_SCRIPT_STATUS.NotCreated:
                    return "Not Created";
                default:
                    return status.ToString();
            }
        }

        return EvaluateStatusSwitch1();
    }

    private static ST_DISPLAY_ITEM ToDisplayItem(ST_PROCESS_DISPLAY_ITEM item)
    {
        return new ST_DISPLAY_ITEM(item.Name, item.Value, item.Detail);
    }

    private static IReadOnlyList<ST_MAIN_PROCESS_SEQUENCE_ITEM> BuildProcessSequenceItems(
        IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> processSequence,
        IReadOnlyDictionary<string, bool> autoStepOptions,
        bool canToggleAutoStep)
    {
        ST_MAIN_PROCESS_SEQUENCE_ITEM SelectItem13(ST_PROCESS_DISPLAY_ITEM item)
        {
            var stepKey = NormalizeAutoStepKey(item.Value);
            var isOptional = OptionalAutoStepSettingKeys.TryGetValue(stepKey, out var optionSettingKey);
            var isOptionOn = !isOptional ||
                (optionSettingKey is not null &&
                    autoStepOptions.TryGetValue(optionSettingKey, out var enabled) &&
                    enabled);

            return new ST_MAIN_PROCESS_SEQUENCE_ITEM(
                item.Name,
                item.Value,
                isOptional && !isOptionOn ? "SKIP" : item.Detail,
                stepKey,
                optionSettingKey ?? "",
                isOptional,
                isOptionOn,
                isOptional && canToggleAutoStep);
        }
        return processSequence
            .Select(SelectItem13)
            .ToArray();
    }

    private static string NormalizeAutoStepKey(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Replace(" ", "_", StringComparison.Ordinal);
        string EvaluateNormalizedSwitch2()
        {
            var switchValue = normalized;
            switch (switchValue)
            {
                case "POWERCHECK":
                    return AutoStepPowerCheck;
                case "POWER_CHECK":
                    return AutoStepPowerCheck;
                default:
                    return normalized;
            }
        }

        return EvaluateNormalizedSwitch2();
    }

    private async Task<IReadOnlyDictionary<string, bool>> LoadAutoStepOptions(CancellationToken cancellationToken)
    {
        var parameters = await settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken);
        string HandleValues14(ST_SYSTEM_PARAMETER parameter)
        {
            return string.IsNullOrWhiteSpace(parameter.Key) ? parameter.Name : parameter.Key;
        }

        string HandleValues15(ST_SYSTEM_PARAMETER parameter)
        {
            return parameter.Value;
        }

        var values = parameters.ToDictionary(
HandleValues14,
HandleValues15,
            StringComparer.OrdinalIgnoreCase);
        string ToDictionaryKeyCallback16(string key)
        {
            return key;
        }

        bool ToDictionaryKeyCallback17(string key)
        {
            return values.TryGetValue(key, out var value) ? ReadBoolOption(value, true) : true;
        }

        return OptionalAutoStepSettingKeys.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
ToDictionaryKeyCallback16,
ToDictionaryKeyCallback17,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task ToggleSequenceStepUse(object? parameter)
    {
        var settingKey = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return;
        }

        var options = await LoadAutoStepOptions(CancellationToken.None);
        var currentValue = options.TryGetValue(settingKey, out var enabled) && enabled;
        var nextValue = currentValue ? "OFF" : "ON";

        try
        {
            await settingManager.SetValue(EN_SETTING_TAB.Option, settingKey, nextValue);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            statusReporter($"Auto sequence option save failed. {exception.Message}");
            return;
        }

        statusReporter($"{FormatAutoStepOptionName(settingKey)} {nextValue}");
        await refreshCurrentScreen();
    }

    private async Task SelectParameterTab(object? parameter)
    {
        var tabKey = parameter?.ToString()?.Trim().ToUpperInvariant();
        if (tabKey is not ParameterTabHead and not ParameterTabOptic and not ParameterTabScanner)
        {
            return;
        }

        if (_selectedParameterTabKey.Equals(tabKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedParameterTabKey = tabKey;
        if (IsScannerStatusSelected)
        {
            _scannerStatusPollingService.Start();
        }

        await refreshCurrentScreen();
    }

    private IReadOnlyList<ST_MAIN_PARAMETER_TAB_ITEM> BuildParameterTabs()
    {
        return
        [
            new(ParameterTabHead, "Head Parameter", _selectedParameterTabKey.Equals(ParameterTabHead, StringComparison.OrdinalIgnoreCase)),
            new(ParameterTabOptic, "Optic Parameter", _selectedParameterTabKey.Equals(ParameterTabOptic, StringComparison.OrdinalIgnoreCase)),
            new(ParameterTabScanner, "Scanner Status", _selectedParameterTabKey.Equals(ParameterTabScanner, StringComparison.OrdinalIgnoreCase))
        ];
    }

    private async Task<IReadOnlyList<ST_SCRIPT_TASK_STATUS_ITEM>> BuildScriptTaskStatusItems(
        ST_STATION_PROCESS_STATUS snapshot,
        IReadOnlyDictionary<string, string> previewParameters,
        CancellationToken cancellationToken)
    {
        var definitions = BuildScriptTaskDefinitions(snapshot, previewParameters);
        var statusCache = new Dictionary<(int AutomationNo, int TaskNo), ST_SCRIPT_TASK_STATUS_VALUE>();
        var items = new List<ST_SCRIPT_TASK_STATUS_ITEM>(definitions.Count);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (definition.AutomationNo, definition.TaskNo);
            if (!statusCache.TryGetValue(key, out var status))
            {
                status = await ReadScriptTaskStatus(
                    definition.AutomationNo,
                    definition.TaskNo,
                    definition.ScriptFileName,
                    cancellationToken);
                statusCache[key] = status;
            }

            items.Add(new ST_SCRIPT_TASK_STATUS_ITEM(
                $"TASK {definition.HeadNo}",
                $"H{definition.HeadNo:00}",
                $"A{definition.AutomationNo}",
                $"T{definition.TaskNo}",
                status.State,
                string.IsNullOrWhiteSpace(status.FileName) ? definition.ScriptFileName : status.FileName,
                status.Detail,
                definition.TotalPoints));
        }

        return items;
    }

    private static IReadOnlyList<ST_SCRIPT_TASK_STATUS_ITEM> BuildScriptTaskStatusPlaceholders(
        ST_STATION_PROCESS_STATUS snapshot,
        IReadOnlyDictionary<string, string> previewParameters)
    {
        ST_SCRIPT_TASK_STATUS_ITEM SelectDefinition18(ST_SCRIPT_TASK_DEFINITION definition)
        {
            return new ST_SCRIPT_TASK_STATUS_ITEM(
                            $"TASK {definition.HeadNo}",
                            $"H{definition.HeadNo:00}",
                            $"A{definition.AutomationNo}",
                            $"T{definition.TaskNo}",
                            "WAIT",
                            definition.ScriptFileName,
                            "Waiting for status",
                            definition.TotalPoints);
        }

        return BuildScriptTaskDefinitions(snapshot, previewParameters)
            .Select(SelectDefinition18)
            .ToArray();
    }

    private static IReadOnlyList<ST_SCRIPT_TASK_DEFINITION> BuildScriptTaskDefinitions(
        ST_STATION_PROCESS_STATUS snapshot,
        IReadOnlyDictionary<string, string> previewParameters)
    {
        int GroupByHeadCallback19(ST_HEAD_PROCESS_DATA head)
        {
            return head.HeadNo;
        }

        int HandleHeadMap20(IGrouping<int, ST_HEAD_PROCESS_DATA> group)
        {
            return group.Key;
        }

        ST_HEAD_PROCESS_DATA HandleHeadMap21(IGrouping<int, ST_HEAD_PROCESS_DATA> group)
        {
            return group.Last();
        }

        var headMap = snapshot.ProcessModel?.Heads
            .GroupBy(GroupByHeadCallback19)
            .ToDictionary(HandleHeadMap20, HandleHeadMap21)
            ?? [];
        ST_SCRIPT_TASK_DEFINITION SelectHeadNo22(int headNo)
        {
            if (headMap.TryGetValue(headNo, out var head))
            {
                return new ST_SCRIPT_TASK_DEFINITION(
                    headNo,
                    head.AutomationNo,
                    head.TaskNo,
                    head.ScriptFileName,
                    head.ProcessPoints.Count);
            }

            return new ST_SCRIPT_TASK_DEFINITION(
                headNo,
                ReadMainHeadAutomationNo(previewParameters, headNo),
                ReadMainHeadAutomationTaskNo(previewParameters, headNo),
                $"PROCESS_H{headNo:00}.ascript",
                0);
        }
        return Enumerable.Range(1, 8)
            .Select(SelectHeadNo22)
            .ToArray();
    }

    private async Task<ST_SCRIPT_TASK_STATUS_VALUE> ReadScriptTaskStatus(
        int automationNo,
        int taskNo,
        string defaultFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (automationManager.IsSimul(automationNo))
            {
                return new ST_SCRIPT_TASK_STATUS_VALUE("SIM", defaultFileName, "Simulation mode");
            }

            if (!automationManager.IsConnect(automationNo))
            {
                return new ST_SCRIPT_TASK_STATUS_VALUE("OFFLINE", defaultFileName, "Automation1 is not connected");
            }

            var response = await automationManager.ReadTaskStatus(
                taskNo,
                automationNo,
                cancellationToken);

            return ParseScriptTaskStatus(response, defaultFileName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ST_SCRIPT_TASK_STATUS_VALUE("ERROR", defaultFileName, exception.Message);
        }
    }

    private static ST_SCRIPT_TASK_STATUS_VALUE ParseScriptTaskStatus(
        string response,
        string defaultFileName)
    {
        var fields = response.Split(':');
        if (fields.Length >= 4 &&
            fields[0].Equals("OK", StringComparison.OrdinalIgnoreCase) &&
            fields[1].Equals("TASK", StringComparison.OrdinalIgnoreCase))
        {
            var state = FormatScriptTaskState(fields[3]);
            var fileName = fields.Length >= 5 && !string.IsNullOrWhiteSpace(fields[4])
                ? fields[4].Trim()
                : defaultFileName;
            var error = fields.Length >= 6 ? fields[5].Trim() : "";
            var detail = string.IsNullOrWhiteSpace(error) ? "OK" : error;

            return new ST_SCRIPT_TASK_STATUS_VALUE(state, fileName, detail);
        }

        return response.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
            ? new ST_SCRIPT_TASK_STATUS_VALUE("OK", defaultFileName, response)
            : new ST_SCRIPT_TASK_STATUS_VALUE("ERROR", defaultFileName, response);
    }

    private static string FormatScriptTaskState(string state)
    {
        var normalized = state.Trim().Replace("_", " ", StringComparison.Ordinal).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "UNKNOWN";
        }

        if (normalized.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("FAULT", StringComparison.OrdinalIgnoreCase))
        {
            return "ERROR";
        }

        if (normalized.Contains("RUN", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("EXEC", StringComparison.OrdinalIgnoreCase))
        {
            return "RUNNING";
        }

        if (normalized.Contains("STOP", StringComparison.OrdinalIgnoreCase))
        {
            return "STOPPED";
        }

        if (normalized.Contains("IDLE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return "IDLE";
        }

        if (normalized.Contains("DONE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            return "DONE";
        }

        return normalized;
    }

    private static int ReadMainHeadAutomationNo(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        return Math.Max(
            0,
            ReadIntAny(
                parameters,
                headNo <= 4 ? 0 : 1,
                $"H{headNo:00}_AUTOMATION_NO"));
    }

    private static int ReadMainHeadAutomationTaskNo(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        return Math.Max(
            1,
            ReadIntAny(
                parameters,
                ((headNo - 1) % 4) + 1,
                $"H{headNo:00}_AUTOMATION_TASK_NO"));
    }

    private async Task<IReadOnlyList<ST_OPTIC_PARAMETER_GROUP>> BuildOpticParameterGroups(
        IReadOnlyDictionary<string, string> previewParameters,
        CancellationToken cancellationToken)
    {
        var groups = new List<ST_OPTIC_PARAMETER_GROUP>();

        groups.Add(await BuildChillerGroup(cancellationToken));

        return groups;
    }

    private async Task<IReadOnlyList<ST_OPTIC_HEAD_PARAMETER>> BuildOpticHeadParameters(
        IReadOnlyDictionary<string, string> previewParameters,
        CancellationToken cancellationToken)
    {
        var laserDevices = BuildOpticDeviceMap(EN_EQP_MODULE.TalonLaser);
        var attenuatorDevices = BuildOpticDeviceMap(EN_EQP_MODULE.Attenuator);
        var betDevices = BuildOpticDeviceMap(EN_EQP_MODULE.Bet);
        var items = new List<ST_OPTIC_HEAD_PARAMETER>();

        for (var headNo = 1; headNo <= 8; headNo++)
        {
            var laser = await BuildLaserHeadParameter(
                headNo,
                laserDevices,
                cancellationToken);
            var attenuator = await BuildAttenuatorHeadParameter(
                headNo,
                attenuatorDevices,
                previewParameters,
                cancellationToken);
            var bet = await BuildBetHeadParameter(
                headNo,
                betDevices,
                previewParameters,
                cancellationToken);

            var state = FormatOpticGroupState(
            [
                new ST_OPTIC_PARAMETER_ITEM("LASER", laser.Power, "", laser.State),
                new ST_OPTIC_PARAMETER_ITEM("ATT", attenuator.Current, attenuator.Target, attenuator.State),
                new ST_OPTIC_PARAMETER_ITEM("BET", bet.MagnificationCurrentStep, bet.MagnificationTargetStep, bet.State)
            ]);

            items.Add(new ST_OPTIC_HEAD_PARAMETER(
                $"H{headNo:00}",
                state,
                laser.Power,
                laser.Gate,
                laser.Shutter,
                laser.State,
                attenuator.Current,
                attenuator.Target,
                attenuator.State,
                bet.MagnificationCurrentStep,
                bet.MagnificationTargetStep,
                bet.DivergenceCurrentStep,
                bet.DivergenceTargetStep,
                bet.State));
        }

        return items;
    }

    private IReadOnlyDictionary<int, int> BuildOpticDeviceMap(EN_EQP_MODULE module)
    {
        int GroupByDeviceCallback23(ST_INTERFACE_DATA device)
        {
            return Math.Clamp(device.Number + 1, 1, 8);
        }

        int ToDictionaryGroupCallback24(IGrouping<int, ST_INTERFACE_DATA> group)
        {
            return group.Key;
        }

        int ToDictionaryGroupCallback25(IGrouping<int, ST_INTERFACE_DATA> group)
        {
            int GetDeviceSortKey1(ST_INTERFACE_DATA device)
            {
                return device.Number;
            }

            string GetDeviceSortKey2(ST_INTERFACE_DATA device)
            {
                return device.NickName;
            }

            return group
                                .OrderBy(GetDeviceSortKey1)
                                .ThenBy(GetDeviceSortKey2, StringComparer.OrdinalIgnoreCase)
                                .First()
                                .Number;
        }

        return interfaceManager.GetInterfaceList(module)
            .GroupBy(GroupByDeviceCallback23)
            .ToDictionary(
ToDictionaryGroupCallback24,
ToDictionaryGroupCallback25);
    }

    private async Task<ST_OPTIC_LASER_HEAD_PARAMETER> BuildLaserHeadParameter(
        int headNo,
        IReadOnlyDictionary<int, int> devices,
        CancellationToken cancellationToken)
    {
        if (!devices.TryGetValue(headNo, out var deviceNo))
        {
            return new ST_OPTIC_LASER_HEAD_PARAMETER(
                "-",
                "-",
                "-",
                "N/C");
        }

        try
        {
            var status = await interfaceManager.GetLaserStatus(deviceNo, cancellationToken);
            var power = status.PowerOn ? "ON" : "OFF";
            return new ST_OPTIC_LASER_HEAD_PARAMETER(
                power,
                status.GateOn ? "OPEN" : "CLOSE",
                status.ShutterOpen ? "OPEN" : "CLOSE",
                "OK");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ST_OPTIC_LASER_HEAD_PARAMETER(
                "-",
                "-",
                "-",
                "ERROR");
        }
    }

    private async Task<ST_OPTIC_AXIS_HEAD_PARAMETER> BuildAttenuatorHeadParameter(
        int headNo,
        IReadOnlyDictionary<int, int> devices,
        IReadOnlyDictionary<string, string> previewParameters,
        CancellationToken cancellationToken)
    {
        var target = ReadDoubleAny(
            previewParameters,
            23.50,
            $"H{headNo:00}_ATTENUATOR_POSITION");

        if (!devices.TryGetValue(headNo, out var deviceNo))
        {
            return new ST_OPTIC_AXIS_HEAD_PARAMETER(
                "-",
                FormatOpticValue(target, "deg"),
                "N/C");
        }

        try
        {
            var status = await interfaceManager.GetAttenuatorStatus(deviceNo, cancellationToken);
            var state = FormatOpticValueState(
                FormatAttenuatorState(status),
                status.CurrentPosition,
                target);
            return new ST_OPTIC_AXIS_HEAD_PARAMETER(
                FormatOpticValue(status.CurrentPosition, "deg"),
                FormatOpticValue(target, "deg"),
                state);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ST_OPTIC_AXIS_HEAD_PARAMETER(
                "-",
                FormatOpticValue(target, "deg"),
                "ERROR");
        }
    }

    private async Task<ST_OPTIC_BET_HEAD_PARAMETER> BuildBetHeadParameter(
        int headNo,
        IReadOnlyDictionary<int, int> devices,
        IReadOnlyDictionary<string, string> previewParameters,
        CancellationToken cancellationToken)
    {
        var targetMag = ReadDoubleAny(
            previewParameters,
            1.0,
            $"H{headNo:00}_BET_MAGNIFICATION",
            $"H{headNo:00}_BET_TARGET_MAGNIFICATION",
            "BET_MAGNIFICATION",
            "BET_TARGET_MAGNIFICATION",
            "BET_MAG_TARGET",
            "BET_MAG",
            "BEAM_EXPANDER_MAGNIFICATION");
        var targetDiv = ReadDoubleAny(
            previewParameters,
            1.0,
            $"H{headNo:00}_BET_DIVERGENCE",
            $"H{headNo:00}_BET_TARGET_DIVERGENCE",
            "BET_DIVERGENCE",
            "BET_TARGET_DIVERGENCE",
            "BET_DIV_TARGET",
            "BET_DIV",
            "BEAM_EXPANDER_DIVERGENCE");

        if (!devices.TryGetValue(headNo, out var deviceNo))
        {
            return new ST_OPTIC_BET_HEAD_PARAMETER(
                "-",
                FormatBetStep(targetMag),
                "-",
                FormatBetStep(targetDiv),
                "N/C");
        }

        try
        {
            var status = await interfaceManager.GetBETStatus(deviceNo, cancellationToken);
            targetMag = ReadDoubleAny(
                previewParameters,
                status.TargetMagnification,
                $"H{headNo:00}_BET_MAGNIFICATION",
                $"H{headNo:00}_BET_TARGET_MAGNIFICATION",
                "BET_MAGNIFICATION",
                "BET_TARGET_MAGNIFICATION",
                "BET_MAG_TARGET",
                "BET_MAG",
                "BEAM_EXPANDER_MAGNIFICATION");
            targetDiv = ReadDoubleAny(
                previewParameters,
                status.TargetDivergence,
                $"H{headNo:00}_BET_DIVERGENCE",
                $"H{headNo:00}_BET_TARGET_DIVERGENCE",
                "BET_DIVERGENCE",
                "BET_TARGET_DIVERGENCE",
                "BET_DIV_TARGET",
                "BET_DIV",
                "BEAM_EXPANDER_DIVERGENCE");
            var state = FormatOpticGroupState(
            [
                new ST_OPTIC_PARAMETER_ITEM("MAG", "", "", FormatBetState(status, status.CurrentMagnification, targetMag)),
                new ST_OPTIC_PARAMETER_ITEM("DIV", "", "", FormatBetState(status, status.CurrentDivergence, targetDiv))
            ]);

            return new ST_OPTIC_BET_HEAD_PARAMETER(
                FormatBetStep(status.MagnificationAxisPosition),
                FormatBetStep(targetMag),
                FormatBetStep(status.DivergenceAxisPosition),
                FormatBetStep(targetDiv),
                state);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ST_OPTIC_BET_HEAD_PARAMETER(
                "-",
                FormatBetStep(targetMag),
                "-",
                FormatBetStep(targetDiv),
                "ERROR");
        }
    }

    private async Task<ST_OPTIC_PARAMETER_GROUP> BuildChillerGroup(
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await interfaceManager.GetChillerStatus(cancellationToken);
            return BuildOpticGroup(
                "CHILLER",
                [
                    new ST_OPTIC_PARAMETER_ITEM(
                        "STATE",
                        status.RunState,
                        "",
                        status.AlarmOn ? "ALARM" : status.Running ? "OK" : "WARN"),
                    new ST_OPTIC_PARAMETER_ITEM(
                        "TEMP",
                        FormatOpticValue(status.Temperature, "C"),
                        "",
                        status.AlarmOn ? "ALARM" : IsNear(status.Temperature, status.SetTemperature, 0.5) ? "OK" : "WARN"),
                    new ST_OPTIC_PARAMETER_ITEM(
                        "ALARM",
                        status.AlarmOn
                            ? string.IsNullOrWhiteSpace(status.AlarmCode) ? "ON" : status.AlarmCode
                            : "OK",
                        "",
                        status.AlarmOn ? "ALARM" : "OK")
                ]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildOpticGroup("CHILLER", [new ST_OPTIC_PARAMETER_ITEM("COMM", "-", "RUN", "ERROR")]);
        }
    }

    private static ST_OPTIC_PARAMETER_GROUP BuildOpticGroup(
        string device,
        IReadOnlyList<ST_OPTIC_PARAMETER_ITEM> items)
    {
        return new ST_OPTIC_PARAMETER_GROUP(device, FormatOpticGroupState(items), items);
    }

    private static IReadOnlyList<int> CreateOpticHeadOrder()
    {
        return [1, 3, 5, 7, 2, 4, 6, 8];
    }

    private static IReadOnlyList<ST_OPTIC_HEAD_PARAMETER> OrderOpticHeadParameters(
        IReadOnlyList<ST_OPTIC_HEAD_PARAMETER> headParameters)
    {
        if (headParameters.Count == 0)
        {
            return [];
        }
        string HandleHeadMap26(ST_OPTIC_HEAD_PARAMETER head)
        {
            return head.Head;
        }

        ST_OPTIC_HEAD_PARAMETER HandleHeadMap27(ST_OPTIC_HEAD_PARAMETER head)
        {
            return head;
        }

        var headMap = headParameters.ToDictionary(
HandleHeadMap26,
HandleHeadMap27,
            StringComparer.OrdinalIgnoreCase);
        string SelectHeadNo28(int headNo)
        {
            return $"H{headNo:00}";
        }

        ST_OPTIC_HEAD_PARAMETER SelectHead29(string head)
        {
            return headMap[head];
        }

        var heads = CreateOpticHeadOrder()
            .Select(SelectHeadNo28)
            .Where(headMap.ContainsKey)
            .Select(SelectHead29)
            .ToArray();

        return heads;
    }

    private static string FormatOpticValueState(
        string deviceState,
        double current,
        double target,
        double tolerance = 0.001)
    {
        if (IsOpticState(deviceState, "ERROR", "ALARM", "NG", "OFFLINE", "N/C"))
        {
            return deviceState;
        }

        return IsNear(current, target, tolerance) ? "OK" : "WARN";
    }

    private static string FormatBetStep(double value)
    {
        var step = Math.Clamp((int)Math.Round(value), 0, 4500);
        return step.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatOpticGroupState(IReadOnlyList<ST_OPTIC_PARAMETER_ITEM> items)
    {
        bool CheckItem30(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "N/C");
        }

        if (items.Count > 0 && items.All(CheckItem30))
        {
            return "N/C";
        }
        bool CheckItem31(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "ERROR", "ALARM", "NG", "OFFLINE");
        }

        if (items.Any(CheckItem31))
        {
            return "ALARM";
        }
        bool CheckItem32(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "N/C");
        }

        if (items.Any(CheckItem32))
        {
            return "N/C";
        }
        bool CheckItem33(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "MOVING", "RUNNING");
        }

        if (items.Any(CheckItem33))
        {
            return "MOVING";
        }
        bool CheckItem34(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "WARN", "WAIT", "STOP");
        }

        if (items.Any(CheckItem34))
        {
            return "WARN";
        }
        bool CheckItem35(ST_OPTIC_PARAMETER_ITEM item)
        {
            return IsOpticState(item.State, "SAFE");
        }

        if (items.Any(CheckItem35))
        {
            return "SAFE";
        }

        return "OK";
    }

    private static bool IsOpticState(
        string state,
        params string[] expectedStates)
    {
        var normalized = state.Trim().ToUpperInvariant();
        bool CheckExpected36(string expected)
        {
            return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        return expectedStates.Any(CheckExpected36);
    }

    private static string FormatBetState(
        ST_BET_STATUS status,
        double current,
        double target)
    {
        if (status.AlarmOn || !status.CommOk)
        {
            return "ALARM";
        }

        if (status.IsMoving)
        {
            return "MOVING";
        }

        return IsNear(current, target) ? "OK" : "WARN";
    }

    private static string FormatAttenuatorState(ST_ATTENUATOR_STATUS status)
    {
        if (!status.CommOk || status.LastError != EN_CONEX_AGP_ERROR.Ok)
        {
            return "ALARM";
        }

        return string.IsNullOrWhiteSpace(status.CommandState)
            ? "OK"
            : status.CommandState.Trim().ToUpperInvariant();
    }

    private static bool IsNear(
        double current,
        double target,
        double tolerance = 0.001)
    {
        return Math.Abs(current - target) <= tolerance;
    }

    private static string FormatOpticValue(
        double value,
        string unit)
    {
        return $"{value.ToString("F3", CultureInfo.InvariantCulture)} {unit}";
    }

    private static bool IsAutoStepOptionEditable(EN_PROCESS_STEP processStep)
    {
        return processStep is EN_PROCESS_STEP.Idle or EN_PROCESS_STEP.Completed or EN_PROCESS_STEP.Stopped;
    }

    private static bool ReadBoolOption(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        bool EvaluateValueSwitch3()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "1" or "Y" or "YES" or "TRUE" or "ON" or "USE":
                    return true;
                case "0" or "N" or "NO" or "FALSE" or "OFF" or "SKIP":
                    return false;
                default:
                    return defaultValue;
            }
        }

        return EvaluateValueSwitch3();
    }

    private static string FormatAutoStepOptionName(string settingKey)
    {
        string EvaluateValueSwitch4()
        {
            var switchValue = settingKey.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case SettingAutoPowerCheckUse:
                    return "POWER CHECK";
                case SettingAutoAlignUse:
                    return "ALIGN";
                case SettingAutoProcessUse:
                    return "PROCESS";
                case SettingAutoInspectionUse:
                    return "INSPECTION";
                default:
                    return settingKey;
            }
        }

        return EvaluateValueSwitch4();
    }

    private (
        IReadOnlyList<ST_INSPECTION_STATUS_ITEM> Items,
        string Summary,
        string ModeText,
        string RuleText,
        Visibility RuleVisibility) BuildInspectionStatus(
        ST_STATION_PROCESS_STATUS snapshot)
    {
        var sequenceState = FormatReviewSequenceState(reviewManager.SequenceState);
        var plan = reviewManager.CurrentPlan;
        if (plan is null)
        {
            return (
                [],
                "No live inspection data",
                $"STATE : {sequenceState}",
                "PLAN : -",
                Visibility.Visible);
        }

        var items = BuildInspectionStatusItems(plan);
        bool HandleCompletedCount37(ST_INSPECTION_STATUS_ITEM item)
        {
            return item.State is "OK" or "NG";
        }

        var completedCount = items.Count(HandleCompletedCount37);
        bool HandleNgCount38(ST_INSPECTION_STATUS_ITEM item)
        {
            return item.State == "NG";
        }

        var ngCount = items.Count(HandleNgCount38);
        bool MatchItem39(ST_INSPECTION_STATUS_ITEM item)
        {
            return item.State == "Current";
        }

        var currentHole = items.FirstOrDefault(MatchItem39)?.Hole ?? "-";
        var summary = $"State {sequenceState} / Total {items.Count:N0} / Done {completedCount:N0} / NG {ngCount:N0} / Current {currentHole}";

        return (
            items,
            summary,
            $"STATE : {sequenceState}",
            $"RECIPE : {plan.RecipeId}",
            Visibility.Visible);
    }

    private static string FormatReviewSequenceState(EN_REVIEW_SEQUENCE_STATE state)
    {
        return state.ToString().ToUpperInvariant();
    }

    private static IReadOnlyList<ST_INSPECTION_STATUS_ITEM> BuildInspectionStatusItems(
        ST_REVIEW_PLAN plan)
    {
        var points = OrderByInspectionSequence(plan.ReviewPoints);

        if (points.Count == 0)
        {
            return [];
        }
        ST_INSPECTION_STATUS_ITEM SelectPoint40(ST_REVIEW_PLAN_POINT point, int index)
        {
            var state = ToInspectionStateText(point.State);
            string EvaluateStateSwitch1()
            {
                var switchValue = state;
                switch (switchValue)
                {
                    case "OK":
                        return "OK";
                    case "NG":
                        return "NG";
                    case "Current":
                        return "RUN";
                    default:
                        return point.Judge is "OK" or "NG" ? point.Judge : "WAIT";
                }
            }

            var judge = EvaluateStateSwitch1();

            return new ST_INSPECTION_STATUS_ITEM(
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                point.HeadName,
                point.CellName,
                point.HoleName,
                FormatInspectionErrorValue(point.ErrorX, state),
                FormatInspectionErrorValue(point.ErrorY, state),
                state,
                judge);
        }
        return points
            .Select(SelectPoint40)
            .ToArray();
    }

    private static string FormatInspectionErrorValue(
        double value,
        string state)
    {
        return state is "OK" or "NG"
            ? FormatSigned(value)
            : "-";
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ST_REVIEW_PLAN_POINT> OrderByInspectionSequence(
        IEnumerable<ST_REVIEW_PLAN_POINT> points)
    {
        double GetPointSortKey41(ST_REVIEW_PLAN_POINT point)
        {
            return point.DesignY;
        }

        double GetPointSortKey42(ST_REVIEW_PLAN_POINT point)
        {
            return point.DesignX;
        }

        int GetPointSortKey43(ST_REVIEW_PLAN_POINT point)
        {
            return point.CellNo;
        }

        int GetPointSortKey44(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleNo;
        }

        var source = points
            .OrderBy(GetPointSortKey41)
            .ThenBy(GetPointSortKey42)
            .ThenBy(GetPointSortKey43)
            .ThenBy(GetPointSortKey44)
            .ToArray();
        var rows = new List<List<ST_REVIEW_PLAN_POINT>>();

        foreach (var point in source)
        {
            if (rows.Count == 0 ||
                Math.Abs(point.DesignY - rows[^1][0].DesignY) > ReviewSequenceRowTolerance)
            {
                rows.Add([]);
            }

            rows[^1].Add(point);
        }

        var orderedPoints = new List<ST_REVIEW_PLAN_POINT>(source.Length);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            double GetPointSortKey45(ST_REVIEW_PLAN_POINT point)
            {
                return point.DesignX;
            }

            int GetPointSortKey46(ST_REVIEW_PLAN_POINT point)
            {
                return point.CellNo;
            }

            int GetPointSortKey47(ST_REVIEW_PLAN_POINT point)
            {
                return point.HoleNo;
            }

            double GetPointSortKey48(ST_REVIEW_PLAN_POINT point)
            {
                return point.DesignX;
            }

            int GetPointSortKey49(ST_REVIEW_PLAN_POINT point)
            {
                return point.CellNo;
            }

            int GetPointSortKey50(ST_REVIEW_PLAN_POINT point)
            {
                return point.HoleNo;
            }

            var orderedRow = rowIndex % 2 == 0
                ? row
                    .OrderBy(GetPointSortKey45)
                    .ThenBy(GetPointSortKey46)
                    .ThenBy(GetPointSortKey47)
                : row
                    .OrderByDescending(GetPointSortKey48)
                    .ThenBy(GetPointSortKey49)
                    .ThenBy(GetPointSortKey50);

            orderedPoints.AddRange(orderedRow);
        }

        return orderedPoints;
    }

    private static string ToInspectionStateText(EN_REVIEW_POINT_STATE state)
    {
        string EvaluateStateSwitch5()
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

        return EvaluateStateSwitch5();
    }

    private static ST_INTERLOCK_ITEM ToInterlockItem(Drilling.Common.InterLock.ST_INTERLOCK_ITEM item)
    {
        return new ST_INTERLOCK_ITEM(
            item.Signal,
            FormatInterLockState(item),
            item.Detail,
            "-");
    }

    private static string FormatInterLockState(Drilling.Common.InterLock.ST_INTERLOCK_ITEM item)
    {
        string EvaluateLevelSwitch6()
        {
            var switchValue = item.Level;
            switch (switchValue)
            {
                case EN_INTERLOCK_LEVEL.Ok:
                    return item.State;
                case EN_INTERLOCK_LEVEL.Warn:
                    return "WARN";
                case EN_INTERLOCK_LEVEL.Error:
                    return "ERROR";
                default:
                    return item.State;
            }
        }

        return EvaluateLevelSwitch6();
    }

    private static string FormatProcessResult(ST_STATION_PROCESS_STATUS snapshot)
    {
        if (snapshot.Result is null)
        {
            return "PENDING";
        }

        return snapshot.Result.IsSuccess ? "OK" : "NG";
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value == TimeSpan.Zero
            ? "00:00:00"
            : value.ToString(@"hh\:mm\:ss");
    }

    private static ST_HEAD_PREVIEW BuildHeadPreviewItem(
        int headNo,
        EN_HEAD_PROCESS_STATUS status,
        IReadOnlySet<int> selectedHeadNos)
    {
        return new ST_HEAD_PREVIEW(
            headNo,
            $"HEAD {headNo:00}",
            status.ToString(),
            selectedHeadNos.Contains(headNo));
    }

    private static ST_MAIN_RECIPE_PREVIEW BuildRecipePreview(
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlySet<int> selectedHeadNos,
        ST_PREVIEW_HEAD_LAYOUT headLayout)
    {
        const double canvasWidth = 860.0;
        const double canvasHeight = 520.0;
        var frame = CreateGlassFrame(parameters);
        var glassWidth = ReadDoubleAny(parameters, 0.0, "GLASS_SIZE_X");
        var glassHeight = ReadDoubleAny(parameters, 0.0, "GLASS_SIZE_Y");
        var akMarginX = ReadDoubleAny(parameters, 55.0, "AK_MARGIN_X");
        var akMarginY = ReadDoubleAny(parameters, 45.0, "AK_MARGIN_Y");
        double? HandleDistortionKeys51(string key)
        {
            return ReadNullableDoubleAny(parameters, key);
        }

        var distortionKeys = CCellPreviewDrawing.CreateDistortionKeyPreviews(
            glassWidth,
            glassHeight,
            akMarginX,
            akMarginY,
HandleDistortionKeys51);
        if (glassWidth <= 0 || glassHeight <= 0)
        {
            return new ST_MAIN_RECIPE_PREVIEW(
                null,
                frame,
                new Dictionary<int, long>(),
                0,
                0,
                []);
        }

        var cellCount = Math.Clamp(ReadIntAny(parameters, 1, "CELL_COUNT"), 1, 1000);
        const int headCount = 8;
        var scale = Math.Min(frame.Width / glassWidth, frame.Height / glassHeight);
        var drawing = new DrawingGroup();
        var outsideGeometry = new StreamGeometry();
        var outsidePixels = new HashSet<long>();
        var unassignedPixels = new HashSet<long>();
        int HandleHeadPixels52(int headNo)
        {
            return headNo;
        }

        Dictionary<long, double> HandleHeadPixels53(int _)
        {
            return new Dictionary<long, double>();
        }

        var headPixels = Enumerable.Range(1, headCount)
            .ToDictionary(HandleHeadPixels52, HandleHeadPixels53);
        int HandleHeadPointCounts54(int headNo)
        {
            return headNo;
        }

        long HandleHeadPointCounts55(int _)
        {
            return 0L;
        }

        var headPointCounts = Enumerable.Range(1, headCount)
            .ToDictionary(HandleHeadPointCounts54, HandleHeadPointCounts55);
        var labels = new List<ST_CELL_PREVIEW_LABEL>();
        long unassignedPointCount = 0;
        long totalPoints = 0;

        using (var context = drawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                new Rect(0, 0, canvasWidth, canvasHeight));

            for (var cellNo = 1; cellNo <= cellCount; cellNo++)
            {
                var firstX = ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X");
                var firstY = ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y");
                var rotation = ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_ROTATION");
                var countX = ReadIntAny(parameters, 0, $"CELL{cellNo}_NUM_OF_PIXEL_X");
                var countY = ReadIntAny(parameters, 0, $"CELL{cellNo}_NUM_OF_PIXEL_Y");
                var pitchX = ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_PITCH_X");
                var pitchY = ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_PITCH_Y");
                var pixelSize = Math.Max(0.0, ReadDoubleAny(parameters, 0.0, $"CELL{cellNo}_PIXEL_SIZE"));
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

                totalPoints += result.Points.Count;
                var radius = pixelSize / 2.0;
                // Main is an operational overview. Keep the physical point center/scale,
                // but enforce a readable marker size after the fixed preview is scaled down.
                var previewSize = Math.Clamp(pixelSize * scale, 3.0, 14.0);
                foreach (var point in result.Points)
                {
                    var canvasX = frame.CanvasLeft + (point.X * scale);
                    var canvasY = frame.CanvasTop + (point.Y * scale);
                    var pixelX = (int)Math.Round(canvasX);
                    var pixelY = (int)Math.Round(canvasY);
                    var pixelKey = ((long)pixelX << 32) | (uint)pixelY;
                    var inside = point.X - radius >= 0 && point.X + radius <= glassWidth &&
                        point.Y - radius >= 0 && point.Y + radius <= glassHeight;
                    if (!inside)
                    {
                        outsidePixels.Add(pixelKey);
                        continue;
                    }

                    var headNo = AssignPreviewHead(point.X, headCount, headLayout, akMarginX, headPointCounts);
                    if (headNo <= 0)
                    {
                        unassignedPointCount++;
                        unassignedPixels.Add(pixelKey);
                        continue;
                    }

                    headPointCounts[headNo]++;
                    if (!headPixels[headNo].TryGetValue(pixelKey, out var storedSize) || previewSize > storedSize)
                    {
                        headPixels[headNo][pixelKey] = previewSize;
                    }
                }
                var boundary = BuildRecipeCellBoundary(
                    akMarginX + firstX, akMarginY + firstY, rotation, countX, countY, pitchX, pitchY,
                    Math.Max(radius, previewSize / (2.0 * scale)) + (4.0 / scale),
                    frame, scale);
                // Cell Size is not defined yet. Use the point-pattern bounds only to
                // place the Cell label; drawing it would imply a physical Cell boundary.
                var label = CCellPreviewDrawing.CreateCellLabel(
                    cellNo,
                    boundary.Bounds,
                    canvasWidth,
                    canvasHeight);
                if (label is not null)
                {
                    labels.Add(label);
                }
            }

            foreach (var headNo in Enumerable.Range(1, headCount))
            {
                var geometry = new StreamGeometry();
                using (var geometryContext = geometry.Open())
                {
                    foreach (var item in headPixels[headNo])
                    {
                        AddPreviewCircle(
                            geometryContext,
                            (int)(item.Key >> 32),
                            (int)item.Key,
                            item.Value);
                    }
                }
                geometry.Freeze();
                // Head selection controls only the visible Scan Fields. Point ownership
                // remains equally readable, whether or not another Head is selected.
                var alpha = selectedHeadNos.Count == 0
                    ? (byte)225
                    : selectedHeadNos.Contains(headNo) ? (byte)255 : (byte)225;
                context.DrawGeometry(CreateHeadBrush(headNo, alpha), null, geometry);
            }

            var unassignedGeometry = new StreamGeometry();
            using (var unassignedContext = unassignedGeometry.Open())
            {
                foreach (var pixel in unassignedPixels)
                {
                    AddPreviewCircle(unassignedContext, (int)(pixel >> 32), (int)pixel, 4.0);
                }
            }
            unassignedGeometry.Freeze();
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(251, 113, 133)), null, unassignedGeometry);

            using (var outsideContext = outsideGeometry.Open())
            {
                foreach (var pixel in outsidePixels)
                {
                    AddPreviewCircle(outsideContext, (int)(pixel >> 32), (int)pixel, 4.0);
                }
            }
            outsideGeometry.Freeze();
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(248, 113, 113)), null, outsideGeometry);

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
        var image = new DrawingImage(drawing);
        image.Freeze();
        return new ST_MAIN_RECIPE_PREVIEW(
            image,
            frame,
            headPointCounts,
            totalPoints,
            unassignedPointCount,
            labels);
    }

    private static int AssignPreviewHead(
        double x,
        int headCount,
        ST_PREVIEW_HEAD_LAYOUT headLayout,
        double akMarginX,
        IReadOnlyDictionary<int, long> assignedHeadCounts)
    {
        if (headCount <= 0)
        {
            return 0;
        }
        ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE? SelectHeadNo56(int headNo)
        {
            var range = GetPreviewHeadRange(headNo, headLayout, akMarginX);
            if (x < range.StartX || x > range.EndX)
            {
                return (ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE?)null;
            }

            return new ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE(
                headNo,
                Math.Abs(x - range.CenterX));
        }
        bool FilterCandidate57(ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE? candidate)
        {
            return candidate.HasValue;
        }

        ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE SelectCandidate58(ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE? candidate)
        {
            return candidate!.Value;
        }

        var candidates = Enumerable.Range(1, headCount)
            .Select(SelectHeadNo56)
            .Where(FilterCandidate57)
            .Select(SelectCandidate58)
            .ToArray();
        double GetCandidateSortKey59(ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return candidate.Distance;
        }

        long GetCandidateSortKey60(ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return assignedHeadCounts.TryGetValue(candidate.HeadNo, out var count)
                                ? count
                                : 0;
        }

        int GetCandidateSortKey61(ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE candidate)
        {
            return candidate.HeadNo;
        }

        return candidates.Length == 0
            ? 0
            : candidates
                .OrderBy(GetCandidateSortKey59)
                .ThenBy(GetCandidateSortKey60)
                .ThenBy(GetCandidateSortKey61)
                .First()
                .HeadNo;
    }

    private static (double StartX, double EndX, double CenterX) GetPreviewHeadRange(
        int headNo,
        ST_PREVIEW_HEAD_LAYOUT headLayout,
        double akMarginX)
    {
        var field = headLayout.GetField(headNo);
        var centerX = akMarginX + field.PositionX;
        var halfWidth = field.ScanFieldWidthX / 2.0;
        return (centerX - halfWidth, centerX + halfWidth, centerX);
    }

    private static StreamGeometry BuildRecipeCellBoundary(
        double firstX, double firstY, double rotation, int countX, int countY,
        double pitchX, double pitchY, double padding, ST_GLASS_PREVIEW_FRAME frame, double scale)
    {
        var radians = rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var maxX = ((countX - 1) * pitchX) + padding;
        var maxY = ((countY - 1) * pitchY) + padding;
        var localCorners = new[]
        {
            new Point(-padding, -padding), new Point(maxX, -padding),
            new Point(maxX, maxY), new Point(-padding, maxY)
        };
        Point SelectLocal62(Point local)
        {
            var x = firstX + (local.X * cos) - (local.Y * sin);
            var y = firstY + (local.X * sin) + (local.Y * cos);
            return new Point(frame.CanvasLeft + (x * scale), frame.CanvasTop + (y * scale));
        }
        var corners = localCorners.Select(SelectLocal62).ToArray();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(corners[0], false, true);
            context.PolyLineTo(corners.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void AddPreviewCircle(StreamGeometryContext context, double x, double y, double size)
    {
        var radius = size / 2.0;
        var control = radius * 0.5522847498;
        context.BeginFigure(new Point(x + radius, y), true, true);
        context.BezierTo(new Point(x + radius, y + control), new Point(x + control, y + radius), new Point(x, y + radius), true, false);
        context.BezierTo(new Point(x - control, y + radius), new Point(x - radius, y + control), new Point(x - radius, y), true, false);
        context.BezierTo(new Point(x - radius, y - control), new Point(x - control, y - radius), new Point(x, y - radius), true, false);
        context.BezierTo(new Point(x + control, y - radius), new Point(x + radius, y - control), new Point(x + radius, y), true, false);
    }

    private static ST_HEAD_ASSIGNMENT_MAP BuildHeadAssignmentMap(
        ST_STATION_PROCESS_STATUS snapshot,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlySet<int> selectedHeadNos,
        ST_PREVIEW_HEAD_LAYOUT headLayout)
    {
        const int headCount = 8;
        var frame = CreateGlassFrame(parameters);
        var glassWidth = ReadDoubleAny(parameters, 0.0, "GLASS_SIZE_X");
        var akMarginX = ReadDoubleAny(parameters, 55.0, "AK_MARGIN_X");
        var areas = new List<ST_HEAD_ASSIGNMENT_AREA>(headCount);

        (double Left, double Top, double Width, double Height, double LabelLeft, double LabelWidth) GetHeadRect(int headNo)
        {
            if (glassWidth <= 0.0)
            {
                return (frame.CanvasLeft, frame.CanvasTop, 0.0, frame.Height, frame.CanvasLeft, 0.0);
            }

            var scale = frame.Width / glassWidth;
            var range = GetPreviewHeadRange(headNo, headLayout, akMarginX);
            // A Scan Field belongs to the fixed Head, not to the Glass. Draw its full
            // physical width even when part of the field lies outside the Glass frame.
            var left = frame.CanvasLeft + (range.StartX * scale);
            var top = frame.CanvasTop;
            var width = Math.Max(0.0, (range.EndX - range.StartX) * scale);
            var centerX = akMarginX + headLayout.GetField(headNo).PositionX;
            var centerCanvasX = frame.CanvasLeft + (centerX * scale);
            var labelWidth = Math.Clamp((range.EndX - range.StartX) * scale, 36.0, 96.0);
            var labelLeft = Math.Clamp(
                centerCanvasX - (labelWidth / 2.0),
                0.0,
                860.0 - labelWidth);

            return (left, top, width, Math.Max(4.0, frame.Height), labelLeft, labelWidth);
        }

        for (var headNo = 1; headNo <= headCount; headNo++)
        {
            var isSelected = selectedHeadNos.Contains(headNo);
            var rect = GetHeadRect(headNo);

            areas.Add(new ST_HEAD_ASSIGNMENT_AREA(
                headNo,
                $"HEAD {headNo:00}",
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                rect.LabelLeft,
                rect.LabelWidth,
                isSelected,
                0,
                CreateHeadBrush(headNo, isSelected ? (byte)36 : (byte)0),
                CreateHeadBrush(headNo, isSelected ? (byte)255 : (byte)150),
                new Thickness(isSelected ? 2.3 : 1.0),
                1.0));
        }

        return new ST_HEAD_ASSIGNMENT_MAP(
            areas);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadPreviewParameters(
        ST_STATION_PROCESS_STATUS snapshot,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(
            snapshot.ProcessModel?.Parameters
                ?? snapshot.ProcessPlan?.Parameters
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var recipes = await recipeManager.LoadRecipes(cancellationToken);
        if (recipes.Count == 0)
        {
            return parameters;
        }

        var recipeId = selectedRecipeIdProvider();
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            recipeId = snapshot.ProcessPlan?.RecipeId ?? "DRILL_A01";
        }
        bool MatchItem63(ST_RECIPE_DATA item)
        {
            return item.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchItem64(ST_RECIPE_DATA item)
        {
            return item.Id.Equals("DRILL_A01", StringComparison.OrdinalIgnoreCase);
        }

        var recipe = recipes.FirstOrDefault(MatchItem63)
            ?? recipes.FirstOrDefault(MatchItem64)
            ?? recipes[0];
        bool FilterParameter65(ST_RECIPE_PARAM parameter)
        {
            return !string.IsNullOrWhiteSpace(parameter.Key);
        }

        foreach (var parameter in recipe.Parameters.Where(FilterParameter65))
        {
            parameters[parameter.Key] = parameter.Value;
        }

        return parameters;
    }

    private async Task<ST_PREVIEW_HEAD_LAYOUT> LoadPreviewHeadLayout(CancellationToken cancellationToken)
    {
        var settings = await settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken);
        ST_PREVIEW_HEAD_FIELD SelectHeadNo66(int headNo)
        {
            var position = ReadHeadPositionX(settings, headNo);
            const double fallbackWidthX = 110.0;
            var widthX = ReadSettingDouble(
                settings,
                fallbackWidthX,
                $"H{headNo:00}_SCAN_FIELD_WIDTH_X",
                $"H{headNo:00}_HEAD_FIELD_WIDTH_X");
            return new ST_PREVIEW_HEAD_FIELD(headNo, position, widthX > 0.0 ? widthX : fallbackWidthX);
        }
        var fields = Enumerable.Range(1, 8)
            .Select(SelectHeadNo66)
            .ToArray();

        return new ST_PREVIEW_HEAD_LAYOUT(fields);
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
            bool CheckKey67(string key)
            {
                return key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase) ||
                                    key.Equals(setting.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (!keys.Any(CheckKey67))
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

    private static int ReadIntAny(
        IReadOnlyDictionary<string, string> parameters,
        int defaultValue,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static double ReadDoubleAny(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static double? ReadNullableDoubleAny(
        IReadOnlyDictionary<string, string> parameters,
        string key)
    {
        return parameters.TryGetValue(key, out var value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static ST_GLASS_PREVIEW_FRAME CreateGlassFrame(IReadOnlyDictionary<string, string> parameters)
    {
        const double maxLeft = 44.0;
        const double maxTop = 62.0;
        const double maxWidth = 772.0;
        const double maxHeight = 420.0;
        var glassSizeX = ReadDoubleAny(
            parameters,
            0.0,
            "GLASS_SIZE_X");
        var glassSizeY = ReadDoubleAny(
            parameters,
            0.0,
            "GLASS_SIZE_Y");

        if (glassSizeX <= 0.0 || glassSizeY <= 0.0)
        {
            return new ST_GLASS_PREVIEW_FRAME(maxLeft, maxTop, maxWidth, maxHeight);
        }

        var scale = Math.Min(maxWidth / glassSizeX, maxHeight / glassSizeY);
        var width = glassSizeX * scale;
        var height = glassSizeY * scale;
        var left = maxLeft + (maxWidth - width) / 2.0;
        var top = maxTop + (maxHeight - height) / 2.0;
        return new ST_GLASS_PREVIEW_FRAME(left, top, width, height);
    }

    private static string FormatGlassSizeText(IReadOnlyDictionary<string, string> parameters)
    {
        var glassSizeX = ReadDoubleAny(
            parameters,
            0.0,
            "GLASS_SIZE_X");
        var glassSizeY = ReadDoubleAny(
            parameters,
            0.0,
            "GLASS_SIZE_Y");

        return glassSizeX > 0.0 && glassSizeY > 0.0
            ? $"Glass {glassSizeX:0.#} x {glassSizeY:0.#} mm"
            : "Glass size fallback";
    }

    public static Brush CreateHeadBrush(int headNo, byte alpha = 255)
    {
        var palette = new[]
        {
            Color.FromRgb(96, 132, 164),
            Color.FromRgb(105, 150, 126),
            Color.FromRgb(161, 132, 83),
            Color.FromRgb(151, 105, 123),
            Color.FromRgb(95, 142, 140),
            Color.FromRgb(131, 123, 164),
            Color.FromRgb(151, 116, 86),
            Color.FromRgb(92, 119, 156)
        };
        var color = palette[Math.Clamp(headNo, 1, palette.Length) - 1];
        return CreateBrush(color.R, color.G, color.B, alpha);
    }

    private static Brush CreateBrush(byte red, byte green, byte blue, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static IReadOnlyList<ST_HEAD_PARAMETER> BuildHeadParameters(
        IReadOnlyDictionary<string, string> parameters)
    {
        const int headCount = 8;
        ST_HEAD_PARAMETER SelectHeadNo68(int headNo)
        {
            var prefix = $"H{headNo:00}";

            return new ST_HEAD_PARAMETER(
                prefix,
                ReadDoubleAny(parameters, 1.2, $"{prefix}_LASER_POWER"),
                ReadDoubleAny(parameters, 20.0, $"{prefix}_LASER_FREQUENCY"),
                ReadIntAny(parameters, 10, $"{prefix}_SHOT_COUNT"),
                ReadDoubleAny(parameters, 0.0, $"{prefix}_SCANNER_JUMP_SPEED"),
                ReadDoubleAny(parameters, 0.0, $"{prefix}_DOE_Z_POSITION"));
        }
        return Enumerable.Range(1, headCount)
            .Select(SelectHeadNo68)
            .ToArray();
    }
}

internal sealed record ST_HEAD_ASSIGNMENT_MAP(
    IReadOnlyList<ST_HEAD_ASSIGNMENT_AREA> Areas);

internal sealed record ST_MAIN_RECIPE_PREVIEW(
    ImageSource? Image,
    ST_GLASS_PREVIEW_FRAME Frame,
    IReadOnlyDictionary<int, long> HeadPointCounts,
    long TotalPointCount,
    long UnassignedPointCount,
    IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellLabels);

internal sealed record ST_PREVIEW_HEAD_LAYOUT(
    IReadOnlyList<ST_PREVIEW_HEAD_FIELD> Fields)
{
    public ST_PREVIEW_HEAD_FIELD GetField(int headNo)
    {
        var normalizedHeadNo = Math.Clamp(headNo, 1, 8);
        bool MatchField69(ST_PREVIEW_HEAD_FIELD field)
        {
            return field.HeadNo == normalizedHeadNo;
        }

        return Fields.First(MatchField69);
    }
}

internal sealed record ST_PREVIEW_HEAD_FIELD(
    int HeadNo,
    double PositionX,
    double ScanFieldWidthX);

public sealed record ST_GLASS_PREVIEW_FRAME(
    double CanvasLeft,
    double CanvasTop,
    double Width,
    double Height);

public sealed record ST_HEAD_PREVIEW(
    int HeadNo,
    string HeadName,
    string Status,
    bool IsSelected)
{
    public Brush StatusBrush
    {
        get
        {
            return CStatusBrush.ForHeadStatus(Status);
        }
    }
}

public sealed record ST_MAIN_PROCESS_SEQUENCE_ITEM(
    string Order,
    string StepName,
    string State,
    string StepKey,
    string OptionSettingKey,
    bool IsOptional,
    bool IsOptionOn,
    bool CanToggleOption)
{
    public string OptionText
    {
        get
        {
            return IsOptional ? IsOptionOn ? "ON" : "OFF" : "";
        }
    }

    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }

    public Brush OptionBrush
    {
        get
        {
            return IsOptionOn ? CStatusBrush.CommandGreen : CStatusBrush.CommandDark;
        }
    }

    public Brush OptionBorderBrush
    {
        get
        {
            return IsOptionOn ? CStatusBrush.CommandGreenBorder : CStatusBrush.CommandDarkBorder;
        }
    }

    public Visibility OptionVisibility
    {
        get
        {
            return IsOptional ? Visibility.Visible : Visibility.Hidden;
        }
    }

    public double OptionOpacity
    {
        get
        {
            return CanToggleOption ? 1.0 : 0.45;
        }
    }
}

public sealed record ST_HEAD_ASSIGNMENT_AREA(
    int HeadNo,
    string HeadName,
    double CanvasLeft,
    double CanvasTop,
    double Width,
    double Height,
    double LabelCanvasLeft,
    double LabelWidth,
    bool IsSelected,
    int PointCount,
    Brush FillBrush,
    Brush StrokeBrush,
    Thickness BorderThicknessValue,
    double Opacity)
{
    public double LabelCanvasTop
    {
        get
        {
            return CanvasTop - 24.0;
        }
    }

    public Visibility RangeVisibility
    {
        get
        {
            return IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public string DisplayLabel
    {
        get
        {
            return $"H{HeadNo:00}";
        }
    }
}

internal readonly record struct ST_PREVIEW_HEAD_ASSIGNMENT_CANDIDATE(
    int HeadNo,
    double Distance);

internal readonly record struct ST_SCANNER_AXIS_SETTING(
    int HeadNo,
    int AutomationNo,
    string AxisName,
    int AxisNo);

internal readonly record struct ST_SCRIPT_TASK_DEFINITION(
    int HeadNo,
    int AutomationNo,
    int TaskNo,
    string ScriptFileName,
    int TotalPoints);

internal readonly record struct ST_SCRIPT_TASK_STATUS_VALUE(
    string State,
    string FileName,
    string Detail);

public sealed record ST_SCRIPT_TASK_STATUS_ITEM(
    string Task,
    string Head,
    string Automation,
    string TaskNo,
    string State,
    string File,
    string Detail,
    int Points)
{
    public string ControllerTaskText
    {
        get
        {
            return $"{Automation} / {TaskNo}";
        }
    }

    public string FileText
    {
        get
        {
            return string.IsNullOrWhiteSpace(File) ? "-" : File;
        }
    }

    public string DetailText
    {
        get
        {
            return string.IsNullOrWhiteSpace(Detail) ? "-" : Detail;
        }
    }

    public string PointText
    {
        get
        {
            return $"{Points.ToString("N0", CultureInfo.InvariantCulture)}P";
        }
    }

    public Brush StateBrush
    {
        get
        {
            return TaskStateBrush(State);
        }
    }

    private static Brush TaskStateBrush(string state)
    {
        Brush EvaluateValueSwitch7()
        {
            var switchValue = state.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "RUNNING":
                    return CStatusBrush.Wait;
                case "OK" or "DONE" or "READY":
                    return CStatusBrush.Online;
                case "IDLE" or "SIM" or "UNKNOWN":
                    return CStatusBrush.Muted;
                case "STOP" or "STOPPED" or "ERROR" or "FAULT" or "OFFLINE":
                    return CStatusBrush.Offline;
                default:
                    return CStatusBrush.Muted;
            }
        }

        return EvaluateValueSwitch7();
    }
}

public sealed record ST_INSPECTION_STATUS_ITEM(
    string No,
    string Head,
    string Cell,
    string Hole,
    string ErrorX,
    string ErrorY,
    string State,
    string Judge)
{
    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(State);
        }
    }

    public Brush JudgeBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(Judge);
        }
    }
}

public sealed record ST_INTERLOCK_ITEM(
    string Signal,
    string State,
    string Detail,
    string Result);

public sealed record ST_MAIN_PARAMETER_TAB_ITEM(
    string Key,
    string Name,
    bool IsSelected)
{
    public Brush BackgroundBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.CommandBlue : CStatusBrush.CommandDark;
        }
    }

    public Brush BorderBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.CommandBlueBorder : CStatusBrush.CommandDarkBorder;
        }
    }

    public Brush TextBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.PrimaryText : CStatusBrush.Muted;
        }
    }
}

public sealed record ST_HEAD_PARAMETER(
    string Head,
    double Power,
    double Frequency,
    int Shot,
    double JumpSpeed,
    double DoeZPosition);

public sealed record ST_OPTIC_HEAD_PARAMETER(
    string Head,
    string State,
    string LaserPower,
    string LaserGate,
    string LaserShutter,
    string LaserState,
    string AttenuatorCurrent,
    string AttenuatorTarget,
    string AttenuatorState,
    string BetMagnificationCurrentStep,
    string BetMagnificationTargetStep,
    string BetDivergenceCurrentStep,
    string BetDivergenceTargetStep,
    string BetState)
{
    public string LaserPowerText
    {
        get
        {
            return $"P {LaserPower}";
        }
    }

    public string LaserGateText
    {
        get
        {
            return $"G {LaserGate}";
        }
    }

    public string LaserShutterText
    {
        get
        {
            return $"S {LaserShutter}";
        }
    }

    public string AttenuatorText
    {
        get
        {
            return $"{AttenuatorCurrent} / {AttenuatorTarget}";
        }
    }

    public string BetMagnificationText
    {
        get
        {
            return $"MAG {BetMagnificationCurrentStep} / {BetMagnificationTargetStep}";
        }
    }

    public string BetDivergenceText
    {
        get
        {
            return $"DIV {BetDivergenceCurrentStep} / {BetDivergenceTargetStep}";
        }
    }

    public Brush StateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(State);
        }
    }

    public Brush LaserStateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(LaserState);
        }
    }

    public Brush LaserPowerBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(LaserPower);
        }
    }

    public Brush LaserGateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(LaserGate);
        }
    }

    public Brush LaserShutterBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(LaserShutter);
        }
    }

    public Brush AttenuatorStateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(AttenuatorState);
        }
    }

    public Brush BetStateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(BetState);
        }
    }
}

public sealed record ST_OPTIC_PARAMETER_GROUP(
    string Device,
    string State,
    IReadOnlyList<ST_OPTIC_PARAMETER_ITEM> Items)
{
    public Brush StateBrush
    {
        get
        {
            return ST_OPTIC_PARAMETER_ITEM.OpticStateBrush(State);
        }
    }
}

public sealed record ST_OPTIC_PARAMETER_ITEM(
    string Item,
    string Current,
    string RecipeTarget,
    string State)
{
    public Brush StateBrush
    {
        get
        {
            return OpticStateBrush(State);
        }
    }

    public static Brush OpticStateBrush(string state)
    {
        Brush EvaluateValueSwitch8()
        {
            var switchValue = state.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "OK" or "ON" or "OPEN" or "RUN" or "READY" or "SAFE" or "CLOSE" or "DONE":
                    return CStatusBrush.Online;
                case "MOVING" or "WARN" or "WAIT" or "STOP":
                    return CStatusBrush.Wait;
                case "ALARM" or "ERROR" or "NG" or "OFFLINE":
                    return CStatusBrush.Offline;
                case "N/C":
                    return CStatusBrush.Muted;
                default:
                    return CStatusBrush.Muted;
            }
        }

        return EvaluateValueSwitch8();
    }
}

internal readonly record struct ST_OPTIC_LASER_HEAD_PARAMETER(
    string Power,
    string Gate,
    string Shutter,
    string State);

internal readonly record struct ST_OPTIC_AXIS_HEAD_PARAMETER(
    string Current,
    string Target,
    string State);

internal readonly record struct ST_OPTIC_BET_HEAD_PARAMETER(
    string MagnificationCurrentStep,
    string MagnificationTargetStep,
    string DivergenceCurrentStep,
    string DivergenceTargetStep,
    string State);

public sealed record ST_SCANNER_AXIS_STATUS_ITEM(
    string Head,
    string Automation,
    string Axis,
    string AxisNo,
    string Able,
    string Position,
    string AuxFeedback,
    string Home,
    string Error,
    string Detail)
{
    public Brush AbleBrush
    {
        get
        {
            return ScannerStateBrush(Able);
        }
    }

    public Brush HomeBrush
    {
        get
        {
            return ScannerStateBrush(Home);
        }
    }

    public Brush ErrorBrush
    {
        get
        {
            return ScannerStateBrush(Error);
        }
    }

    private static Brush ScannerStateBrush(string state)
    {
        Brush EvaluateValueSwitch9()
        {
            var switchValue = state.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "ABLE" or "HOME" or "OK":
                    return CStatusBrush.Online;
                case "DISABLE" or "WAIT":
                    return CStatusBrush.Wait;
                case "ERROR" or "OFFLINE":
                    return CStatusBrush.Offline;
                default:
                    return CStatusBrush.Muted;
            }
        }

        return EvaluateValueSwitch9();
    }
}
