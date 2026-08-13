using System.Globalization;
using System.IO;
using System.Windows;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Product;
using Drilling.Common.Recipe;
using Drilling.Common.Review;
using Drilling.Common.Station;
using Drilling.UI.Popup;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuMonitor : CMenuBase
{
    private readonly CInterfaceManager _interfaceManager;
    private readonly CMotionManager _motionManager;
    private readonly CInterLockManager _interLockManager;
    private readonly CProductManager _productManager;
    private readonly CRecipeManager _recipeManager;
    private readonly CSettingManager _settingManager;
    private readonly Func<string> _selectedRecipeIdProvider;
    private readonly Func<string> _selectedTabAccessor;
    private readonly Action<string> _selectedTabSetter;
    private readonly Action<string> _setStatusMessage;
    private readonly Action _refreshShellStatus;
    private readonly Func<Task> _refreshCurrentScreen;
    private readonly CMonitorStatusPollingService _statusPollingService;
    private readonly Dictionary<string, string> _operationFieldValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _melsecWriteValues = new(StringComparer.OrdinalIgnoreCase);
    private const int LaserHeadCount = 8;
    private string _selectedAxisId = "GX";
    private int _selectedLaserNumber;
    private int _selectedAttenuatorNumber;
    private int _selectedBetNumber;
    private string _selectedPowerMeterProcessName = "";
    private int _selectedPowerMeterStepNo = 1;
    private int _selectedPicoMotorNo = 1;
    private bool _picoMotorIsConnected;
    private string _coordinateBasis = "DESIGN";
    private int _coordinateSelectedCellNo = 1;
    private string _coordinateSelectedHoleKey = "";
    private bool _coordinateIsCellDetailVisible;
    private readonly HashSet<int> _picoAllMoveMotorNos = [1];
    private readonly SemaphoreSlim _picoJogCommandLock = new(1, 1);
    private CancellationTokenSource? _picoJogCancellationSource;
    private Task? _picoAllMoveTask;
    private Task? _picoMotionTask;
    private CancellationTokenSource? _powerMeterMeasureCts;
    private string _selectedMelsecGroup = "ALL";
    private bool _liveRefreshRunning;

    private static readonly string[] MonitorTabs =
    [
        "IO",
        "MOTOR",
        "LASER",
        "CHILLER",
        "ATTENUATOR",
        "BET",
        "POWER METER",
        "PICO MOTOR",
        "PRODUCT",
        "MELSEC",
        "COORDINATE VIEWER"
    ];

    public CMenuMonitor(
        CInterfaceManager interfaceManager,
        CMotionManager motionManager,
        CInterLockManager interLockManager,
        CProductManager productManager,
        CRecipeManager recipeManager,
        CSettingManager settingManager,
        Func<string> selectedRecipeIdProvider,
        Func<string> selectedTabAccessor,
        Action<string> selectedTabSetter,
        Action<string> setStatusMessage,
        Action refreshShellStatus,
        Func<Task> refreshCurrentScreen)
    {
        _interfaceManager = interfaceManager;
        _motionManager = motionManager;
        _interLockManager = interLockManager;
        _productManager = productManager;
        _recipeManager = recipeManager;
        _settingManager = settingManager;
        _selectedRecipeIdProvider = selectedRecipeIdProvider;
        _selectedTabAccessor = selectedTabAccessor;
        _selectedTabSetter = selectedTabSetter;
        _setStatusMessage = setStatusMessage;
        _refreshShellStatus = refreshShellStatus;
        _refreshCurrentScreen = refreshCurrentScreen;
        _statusPollingService = new CMonitorStatusPollingService(_interfaceManager, _motionManager);

        async void HandleSelectTabCommand1(object? parameter)
        {
            await SelectTab(parameter);
        }

        SelectTabCommand = new CButtonCommand(HandleSelectTabCommand1);

        async void HandleSelectHeadDeviceCommand2(object? parameter)
        {
            await SelectHeadDevice(parameter);
        }

        SelectHeadDeviceCommand = new CButtonCommand(HandleSelectHeadDeviceCommand2);

        async void HandleExecuteOperationCommand3(object? parameter)
        {
            await ExecuteOperation(parameter);
        }

        ExecuteOperationCommand = new CButtonCommand(HandleExecuteOperationCommand3);

        async void HandleSetOutputOnCommand4(object? parameter)
        {
            await SetOutput(parameter, true);
        }

        SetOutputOnCommand = new CButtonCommand(HandleSetOutputOnCommand4);

        async void HandleSetOutputOffCommand5(object? parameter)
        {
            await SetOutput(parameter, false);
        }

        SetOutputOffCommand = new CButtonCommand(HandleSetOutputOffCommand5);

        async void HandleSelectMelsecGroupCommand6(object? parameter)
        {
            await SelectMelsecGroup(parameter);
        }

        SelectMelsecGroupCommand = new CButtonCommand(HandleSelectMelsecGroupCommand6);

        async void HandleWriteMelsecCommand7(object? parameter)
        {
            await WriteMelsec(parameter);
        }

        WriteMelsecCommand = new CButtonCommand(HandleWriteMelsecCommand7);

        async void HandlePicoJogStartCommand8(object? parameter)
        {
            await StartPicoJog(parameter);
        }

        PicoJogStartCommand = new CButtonCommand(HandlePicoJogStartCommand8);

        async void HandlePicoJogStopCommand9(object? _)
        {
            await StopPicoJog();
        }

        PicoJogStopCommand = new CButtonCommand(HandlePicoJogStopCommand9);

        async void HandleSelectCoordinateBasisCommand10(object? parameter)
        {
            await SelectCoordinateBasis(parameter);
        }

        SelectCoordinateBasisCommand = new CButtonCommand(HandleSelectCoordinateBasisCommand10);

        async void HandleSelectCoordinateCellCommand11(object? parameter)
        {
            await SelectCoordinateCell(parameter);
        }

        SelectCoordinateCellCommand = new CButtonCommand(HandleSelectCoordinateCellCommand11);

        async void HandleSelectCoordinateHoleCommand12(object? parameter)
        {
            await SelectCoordinateHole(parameter);
        }

        SelectCoordinateHoleCommand = new CButtonCommand(HandleSelectCoordinateHoleCommand12);

        async void HandleBackToCoordinateGlassPreviewCommand13(object? _)
        {
            await BackToCoordinateGlassPreview();
        }

        BackToCoordinateGlassPreviewCommand = new CButtonCommand(HandleBackToCoordinateGlassPreviewCommand13);
    }

    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Monitor;
        }
    }

    public IReadOnlyList<ST_SCREEN_SECTION> DeviceTabs { get; private set; } = [];

    public string SelectedTab { get; private set; } = "IO";

    public string Title { get; private set; } = "";

    public string Subtitle { get; private set; } = "";

    public string StatusPanelTitle { get; private set; } = "";

    public string OperationPanelTitle { get; private set; } = "";

    public string ParameterPanelTitle { get; private set; } = "";

    public string TrendPanelTitle { get; private set; } = "";

    public string HistoryPanelTitle { get; private set; } = "";

    public IReadOnlyList<ST_MONITOR_TAB> Tabs { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_IO_ROW> InputRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_IO_ROW> OutputRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_AXIS_ROW> AxisRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_COMMAND_HISTORY_ROW> CommandHistoryRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_STATUS_ROW> StatusRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> OperationButtons { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_PARAMETER_ROW> OperationFields { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_HEAD_SELECT_ROW> HeadSelectRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_LASER_CONTROL_ROW> LaserControlRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_PARAMETER_ROW> ParameterRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_BET_TABLE_ROW> BetTableRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_TREND_POINT> TrendPoints { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_SUMMARY_ITEM> SummaryItems { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_POSITION_ROW> PositionRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_PRODUCT_ITEM> ProductItems { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_PRODUCT_HEAD_ROW> ProductHeadRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_PRODUCT_HISTORY_ROW> ProductHistoryRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_MELSEC_GROUP> MelsecGroups { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_MELSEC_ROW> MelsecRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_MELSEC_ROW> MelsecReadRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_MELSEC_ROW> MelsecWriteRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_COORDINATE_BASIS_OPTION> CoordinateBasisOptions { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_COORDINATE_VALUE_ROW> CoordinateValueRows { get; private set; } = [];

    public string CoordinateSelectedRecipeName { get; private set; } = "-";

    public string CoordinateSelectedBasisName { get; private set; } = "Design";

    public string CoordinateBasisDescription { get; private set; } = "Recipe design coordinate from Align Key / Glass";

    public ImageSource? CoordinateGlassPreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> CoordinateCellPreviewLabels { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW> CoordinateHoleMatrixRows { get; private set; } = [];

    public string CoordinateGlassPreviewSummary { get; private set; } = "0 Cells / 0 Holes";

    public bool CoordinateIsGlassPreviewVisible
    {
        get
        {
            return !_coordinateIsCellDetailVisible;
        }
    }

    public bool CoordinateIsCellDetailVisible
    {
        get
        {
            return _coordinateIsCellDetailVisible;
        }
    }

    public string CoordinateSelectedCellName
    {
        get
        {
            return $"Cell{Math.Max(1, _coordinateSelectedCellNo):000}";
        }
    }

    public string CoordinateSelectedHoleName
    {
        get
        {
            return string.IsNullOrWhiteSpace(_coordinateSelectedHoleKey)
        ? "-"
        : _coordinateSelectedHoleKey;
        }
    }

    public IReadOnlyList<ST_PWM_PROCESS_ROW> PwmProcessRows { get; private set; } = [];

    public IReadOnlyList<ST_PWM_STEP_ROW> PwmStepRows { get; private set; } = [];

    public IReadOnlyList<ST_PWM_SETTING_ROW> PwmSettingRows { get; private set; } = [];

    public IReadOnlyList<ST_PWM_DEVICE_ROW> PwmDeviceRows { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PwmProcessButtons { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PwmStepButtons { get; private set; } = [];

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PwmRunButtons { get; private set; } = [];

    public CButtonCommand SelectTabCommand { get; }

    public CButtonCommand SelectHeadDeviceCommand { get; }

    public CButtonCommand ExecuteOperationCommand { get; }

    public CButtonCommand SetOutputOnCommand { get; }

    public CButtonCommand SetOutputOffCommand { get; }

    public CButtonCommand SelectMelsecGroupCommand { get; }

    public CButtonCommand WriteMelsecCommand { get; }

    public CButtonCommand PicoJogStartCommand { get; }

    public CButtonCommand PicoJogStopCommand { get; }

    public CButtonCommand SelectCoordinateBasisCommand { get; }

    public CButtonCommand SelectCoordinateCellCommand { get; }

    public CButtonCommand SelectCoordinateHoleCommand { get; }

    public CButtonCommand BackToCoordinateGlassPreviewCommand { get; }

    public string SelectedAxisId
    {
        get
        {
            return _selectedAxisId;
        }
    }

    public int SelectedLaserNumber
    {
        get
        {
            return _selectedLaserNumber;
        }
    }

    public string SelectedLaserName
    {
        get
        {
            return $"H{_selectedLaserNumber + 1:00}";
        }
    }

    public string SelectedHeadDeviceName
    {
        get
        {
            return $"H{GetSelectedHeadNumber(SelectedTab) + 1:00}";
        }
    }

    public string HeadDeviceSelectorTitle
    {
        get
        {
            string EvaluateSelectedTabSwitch1()
            {
                var switchValue = SelectedTab;
                switch (switchValue)
                {
                    case "ATTENUATOR":
                        return "Attenuator Head";
                    case "BET":
                        return "BET Head";
                    default:
                        return "Laser Head";
                }
            }

            return EvaluateSelectedTabSwitch1();
        }
    }

    public ST_MONITOR_AXIS_ROW? SelectedAxisRow
    {
        get
        {
            bool MatchRow14(ST_MONITOR_AXIS_ROW row)
            {
                return row.Axis.Equals(_selectedAxisId, StringComparison.OrdinalIgnoreCase);
            }

            return AxisRows.FirstOrDefault(MatchRow14);
        }

        set
        {
            if (value is not null && !string.IsNullOrWhiteSpace(value.Axis))
            {
                _selectedAxisId = value.Axis;
            }
        }
    }

    public ST_PWM_PROCESS_ROW? SelectedPwmProcessRow
    {
        get
        {
            bool MatchRow15(ST_PWM_PROCESS_ROW row)
            {
                return row.IsSelected;
            }

            return PwmProcessRows.FirstOrDefault(MatchRow15);
        }

        set
        {
            if (value is null ||
                string.IsNullOrWhiteSpace(value.ProcessName) ||
                value.ProcessName.Equals(_selectedPowerMeterProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = SelectPowerMeterProcess(value.ProcessName);
        }
    }

    public ST_PWM_STEP_ROW? SelectedPwmStepRow
    {
        get
        {
            bool MatchRow16(ST_PWM_STEP_ROW row)
            {
                return row.IsSelected;
            }

            return PwmStepRows.FirstOrDefault(MatchRow16);
        }

        set
        {
            if (value is null ||
                !int.TryParse(value.Step, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stepNo) ||
                stepNo == _selectedPowerMeterStepNo)
            {
                return;
            }

            _ = SelectPowerMeterStep(stepNo);
        }
    }

    public bool IsIo
    {
        get
        {
            return SelectedTab == "IO";
        }
    }

    public bool IsMotor
    {
        get
        {
            return SelectedTab == "MOTOR";
        }
    }

    public bool IsLaser
    {
        get
        {
            return SelectedTab == "LASER";
        }
    }

    public bool IsChiller
    {
        get
        {
            return SelectedTab == "CHILLER";
        }
    }

    public bool IsAttenuator
    {
        get
        {
            return SelectedTab == "ATTENUATOR";
        }
    }

    public bool IsBet
    {
        get
        {
            return SelectedTab == "BET";
        }
    }

    public bool IsPowerMeter
    {
        get
        {
            return SelectedTab == "POWER METER";
        }
    }

    public bool IsPicoMotor
    {
        get
        {
            return SelectedTab == "PICO MOTOR";
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoConnectionButtons
    {
        get
        {
            return [
        new("CONNECT", "Servo", _picoMotorIsConnected ? "Dark" : "Green"),
        new("DISCONNECT", "Stop", _picoMotorIsConnected ? "Red" : "Dark")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoMotorSelectButtons
    {
        get
        {
            ST_MONITOR_OPERATION_BUTTON SelectNumber17(int number)
            {
                return new ST_MONITOR_OPERATION_BUTTON($"MOTOR {number}", "Move", _selectedPicoMotorNo == number ? "Blue" : "Dark");
            }

            return Enumerable.Range(1, 4)
        .Select(SelectNumber17)
        .ToArray();
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoMotionStopButtons
    {
        get
        {
            return [
        new("STOP MOTION", "Stop", "Red")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoMotionMoveButtons
    {
        get
        {
            return [
        new("HOME", "Home", "Blue"),
        new("ABS MOVE", "Abs", "Blue")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoMotionJogButtons
    {
        get
        {
            return [
        new("JOG -", "Rel", "Dark"),
        new("JOG +", "Rel", "Dark")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoMotionRelButtons
    {
        get
        {
            return [
        new("REL -", "Rel", "Dark"),
        new("REL +", "Rel", "Blue")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoAllMotorSelectButtons
    {
        get
        {
            ST_MONITOR_OPERATION_BUTTON SelectNumber18(int number)
            {
                return new ST_MONITOR_OPERATION_BUTTON(
                            $"MOTOR {number}",
                            "Move",
                            _picoAllMoveMotorNos.Contains(number) ? "Blue" : "Dark",
                            $"ALL MOTOR {number}");
            }

            return Enumerable.Range(1, 4)
        .Select(SelectNumber18)
        .ToArray();
        }
    }

    public IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> PicoAllMotorCommandButtons
    {
        get
        {
            return [
        new("START", "Run", "Green"),
        new("STOP", "Stop", "Red")
    ];
        }
    }

    public IReadOnlyList<ST_MONITOR_PARAMETER_ROW> PicoErrorRows
    {
        get
        {
            return CreatePicoErrorRows();
        }
    }

    public bool IsProduct
    {
        get
        {
            return SelectedTab == "PRODUCT";
        }
    }

    public bool IsMelsec
    {
        get
        {
            return SelectedTab == "MELSEC";
        }
    }

    public bool IsCoordinateViewer
    {
        get
        {
            return SelectedTab == "COORDINATE VIEWER";
        }
    }

    public bool IsControlDevice
    {
        get
        {
            return false;
        }
    }

    public bool IsGenericDevice
    {
        get
        {
            return !IsIo && !IsMotor && !IsLaser && !IsChiller && !IsAttenuator && !IsBet && !IsPowerMeter && !IsPicoMotor && !IsProduct && !IsMelsec && !IsCoordinateViewer;
        }
    }

    public async override Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        var selectedTab = NormalizeMonitorTab(_selectedTabAccessor());
        _selectedLaserNumber = Math.Clamp(_selectedLaserNumber, 0, LaserHeadCount - 1);
        _selectedAttenuatorNumber = Math.Clamp(_selectedAttenuatorNumber, 0, LaserHeadCount - 1);
        _selectedBetNumber = Math.Clamp(_selectedBetNumber, 0, LaserHeadCount - 1);
        if (selectedTab == "COORDINATE VIEWER")
        {
            return await BuildCoordinateViewerScreen(cancellationToken);
        }

        UpdatePollingContext(selectedTab);
        _statusPollingService.Start();
        var monitorSnapshot = _statusPollingService.GetSnapshot();
        var snapshot = monitorSnapshot.DeviceStatus;
        var communication = monitorSnapshot.Communication;
        var selectedModule = GetMonitorModule(selectedTab);
        var selectedHistoryNickName = GetSelectedHeadInterfaceData(selectedTab)?.NickName ?? "";
        var interfaceHistory = selectedModule is null
            ? []
            : await _interfaceManager.ReadInterfaceHistory(
                selectedModule.Value,
                selectedHistoryNickName,
                maxRows: 12,
                cancellationToken: cancellationToken);
        var betTable = selectedTab == "BET"
            ? await _interfaceManager.LoadBETData(cancellationToken)
            : [];
        var powerMeterTable = selectedTab == "POWER METER"
            ? await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName, cancellationToken)
            : ST_POWER_METER_TABLE_DATA.Empty;
        var picoMotorStatus = selectedTab == "PICO MOTOR"
            ? monitorSnapshot.PicoMotorStatus
            : ST_PICO_MOTOR_STATUS.Empty;
        if (selectedTab == "PICO MOTOR")
        {
            _selectedPicoMotorNo = Math.Clamp(picoMotorStatus.SelectedMotorNo, 1, 4);
            _picoMotorIsConnected = picoMotorStatus.IsConnected;
        }
        if (selectedTab == "POWER METER")
        {
            _selectedPowerMeterProcessName = powerMeterTable.SelectedFileName;
        }
        var (product, productHistory, productError) = selectedTab == "PRODUCT"
            ? await LoadProductDisplay(cancellationToken)
            : (null, [], "");
        ST_MONITOR_TAB SelectTab19(string tab)
        {
            return new ST_MONITOR_TAB(tab, tab == selectedTab);
        }

        var tabs = MonitorTabs
            .Select(SelectTab19)
            .ToArray();
        var melsecGroups = CreateMelsecGroups(selectedTab);
        SaveMelsecWriteValues();
        var melsecRows = CreateMelsecRows(selectedTab, monitorSnapshot.MelsecValues);
        var coordinateViewerData = ST_MONITOR_COORDINATE_VIEWER_DATA.Empty;
        if (OperationFields.Count > 0 && SelectedTab == selectedTab)
        {
            SaveOperationFieldValues(selectedTab);
        }
        var operationFields = CreateOperationFields(selectedTab, picoMotorStatus);
        var talonStatus = selectedTab == "LASER"
            ? monitorSnapshot.TalonStatus
            : ST_TALON_STATUS.Empty;
        var selectedLaserStatus = snapshot.Laser;

        var axisRows = CreateAxisRows(snapshot, _selectedAxisId);
        bool CheckRow20(ST_MONITOR_AXIS_ROW row)
        {
            return row.IsSelected;
        }

        if (axisRows.Count > 0 && !axisRows.Any(CheckRow20))
        {
            _selectedAxisId = axisRows[0].Axis;
            axisRows = CreateAxisRows(snapshot, _selectedAxisId);
        }

        HeadSelectRows = CreateHeadSelectRows(
            selectedTab,
            GetSelectedHeadNumber(selectedTab),
            GetHeadDeviceModule(selectedTab),
            GetHeadDeviceNickPrefix(selectedTab));
        LaserControlRows = CreateLaserControlRows(selectedTab, selectedLaserStatus, talonStatus, operationFields);

        Apply(
            CreateTabSections(tabs),
            selectedTab,
            $"MONITOR / {selectedTab}",
            GetSubtitle(selectedTab),
            GetStatusPanelTitle(selectedTab),
            GetOperationPanelTitle(selectedTab),
            GetParameterPanelTitle(selectedTab),
            GetTrendPanelTitle(selectedTab),
            GetHistoryPanelTitle(selectedTab),
            tabs,
            CreateInputRows(snapshot),
            CreateOutputRows(snapshot),
            axisRows,
            CreateCommandHistoryRows(selectedTab, interfaceHistory),
            CreateStatusRows(selectedTab, snapshot, communication, GetSelectedHeadConnectionText(selectedTab)),
            CreateOperationButtons(selectedTab, selectedLaserStatus),
            operationFields,
            CreateParameterRows(selectedTab),
            CreateBetTableRows(selectedTab, betTable, snapshot),
            CreateTrendPoints(selectedTab),
            CreateSummaryItems(selectedTab, snapshot),
            CreatePositionRows(selectedTab, snapshot),
            CreateProductItems(product, productError),
            CreateProductHeadRows(product),
            CreateProductHistoryRows(productHistory),
            CreatePwmProcessRows(selectedTab, powerMeterTable),
            CreatePwmStepRows(selectedTab, snapshot, powerMeterTable, _selectedPowerMeterStepNo),
            CreatePwmSettingRows(selectedTab, powerMeterTable, _selectedPowerMeterStepNo),
            CreatePwmDeviceRows(selectedTab, snapshot),
            CreatePwmProcessButtons(selectedTab),
            CreatePwmStepButtons(selectedTab),
            CreatePwmRunButtons(selectedTab));
        MelsecGroups = melsecGroups;
        SetMelsecRows(melsecRows);
        ApplyCoordinateViewerData(coordinateViewerData);
        if (selectedTab == "PICO MOTOR")
        {
            StatusRows = CreatePicoMotorStatusRows(picoMotorStatus);
            OperationButtons = CreatePicoMotorOperationButtons();
            ParameterRows = CreatePicoErrorRows();
            PositionRows = CreatePicoMotorPositionRows(picoMotorStatus);
        }
        ST_DISPLAY_ITEM SelectRow21(ST_MONITOR_IO_ROW row)
        {
            return new ST_DISPLAY_ITEM(row.Address, row.State, row.Name);
        }

        ST_DISPLAY_ITEM SelectRow22(ST_MONITOR_AXIS_ROW row)
        {
            return new ST_DISPLAY_ITEM(row.Axis, row.CurrentPosition, row.State);
        }

        ST_DISPLAY_ITEM SelectRow23(ST_MONITOR_STATUS_ROW row)
        {
            return new ST_DISPLAY_ITEM(row.Item, row.Value, row.State);
        }

        return new CScreenViewModel(
            EN_MENU.Monitor,
            Title,
            Subtitle,
            [
                new("Tab", selectedTab),
                new("Input", InputRows.Count.ToString()),
                new("Output", OutputRows.Count.ToString())
            ],
            [
                new("IO", InputRows.Select(SelectRow21).ToArray()),
                new("MOTOR", AxisRows.Select(SelectRow22).ToArray()),
                new("LASER", StatusRows.Select(SelectRow23).ToArray())
            ],
            monitor: this);
    }

    private async Task<CScreenViewModel> BuildCoordinateViewerScreen(CancellationToken cancellationToken)
    {
        var selectedTab = "COORDINATE VIEWER";
        ST_MONITOR_TAB SelectTab24(string tab)
        {
            return new ST_MONITOR_TAB(tab, tab == selectedTab);
        }

        var tabs = MonitorTabs
            .Select(SelectTab24)
            .ToArray();
        var coordinateViewerData = await BuildCoordinateViewerData(cancellationToken);

        HeadSelectRows = [];
        LaserControlRows = [];
        MelsecGroups = [];
        SetMelsecRows([]);
        Apply(
            CreateTabSections(tabs),
            selectedTab,
            $"MONITOR / {selectedTab}",
            GetSubtitle(selectedTab),
            GetStatusPanelTitle(selectedTab),
            GetOperationPanelTitle(selectedTab),
            GetParameterPanelTitle(selectedTab),
            GetTrendPanelTitle(selectedTab),
            GetHistoryPanelTitle(selectedTab),
            tabs,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);
        ApplyCoordinateViewerData(coordinateViewerData);
        ST_DISPLAY_ITEM SelectRow25(ST_MONITOR_COORDINATE_VALUE_ROW row)
        {
            return new ST_DISPLAY_ITEM(row.Parameter, row.Value, row.Unit);
        }

        return new CScreenViewModel(
            EN_MENU.Monitor,
            Title,
            Subtitle,
            [
                new("Tab", selectedTab),
                new("Basis", CoordinateSelectedBasisName),
                new("Cell", CoordinateSelectedCellName)
            ],
            [
                new("COORDINATE VIEWER", CoordinateValueRows
                    .Take(8)
                    .Select(SelectRow25)
                    .ToArray())
            ],
            monitor: this);
    }

    public Task RefreshLiveData(CancellationToken cancellationToken = default)
    {
        if (_liveRefreshRunning)
        {
            return Task.CompletedTask;
        }

        _liveRefreshRunning = true;

        try
        {
            var selectedTab = NormalizeMonitorTab(_selectedTabAccessor());

            if (!selectedTab.Equals(SelectedTab, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            if (selectedTab == "COORDINATE VIEWER")
            {
                return Task.CompletedTask;
            }

            if (OperationFields.Count > 0)
            {
                SaveOperationFieldValues(selectedTab);
            }

            UpdatePollingContext(selectedTab);
            _statusPollingService.Start();
            var monitorSnapshot = _statusPollingService.GetSnapshot();
            var snapshot = monitorSnapshot.DeviceStatus;
            var communication = monitorSnapshot.Communication;

            if (selectedTab == "PICO MOTOR")
            {
                var picoStatus = monitorSnapshot.PicoMotorStatus;
                _selectedPicoMotorNo = Math.Clamp(picoStatus.SelectedMotorNo, 1, 4);
                _picoMotorIsConnected = picoStatus.IsConnected;
                StatusRows = CreatePicoMotorStatusRows(picoStatus);
                ParameterRows = CreatePicoErrorRows();
                PositionRows = CreatePicoMotorPositionRows(picoStatus);
                UpdatePicoMotorOperationFields(picoStatus);
                NotifyMonitorLiveProperties(nameof(StatusRows), nameof(ParameterRows), nameof(PositionRows), nameof(OperationFields), nameof(PicoConnectionButtons), nameof(PicoMotorSelectButtons));
                return Task.CompletedTask;
            }

            if (selectedTab == "LASER")
            {
                var talonStatus = monitorSnapshot.TalonStatus;
                var selectedLaserStatus = snapshot.Laser;

                StatusRows = CreateStatusRows(selectedTab, snapshot, communication, GetSelectedHeadConnectionText(selectedTab));
                OperationButtons = CreateOperationButtons(selectedTab, selectedLaserStatus);
                HeadSelectRows = CreateHeadSelectRows(
                    selectedTab,
                    _selectedLaserNumber,
                    EN_EQP_MODULE.TalonLaser,
                    "TALON");
                LaserControlRows = CreateLaserControlRows(selectedTab, selectedLaserStatus, talonStatus, OperationFields);
                SummaryItems = CreateSummaryItems(selectedTab, snapshot);
                NotifyMonitorLiveProperties(
                    nameof(StatusRows),
                    nameof(OperationButtons),
                    nameof(HeadSelectRows),
                    nameof(LaserControlRows),
                    nameof(SelectedHeadDeviceName),
                    nameof(SummaryItems));
                return Task.CompletedTask;
            }

            StatusRows = CreateStatusRows(selectedTab, snapshot, communication, GetSelectedHeadConnectionText(selectedTab));

            switch (selectedTab)
            {
                case "IO":
                    InputRows = CreateInputRows(snapshot);
                    OutputRows = CreateOutputRows(snapshot);
                    NotifyMonitorLiveProperties(nameof(InputRows), nameof(OutputRows), nameof(StatusRows));
                    break;

                case "MOTOR":
                    var axisRows = CreateAxisRows(snapshot, _selectedAxisId);
                    bool CheckRow26(ST_MONITOR_AXIS_ROW row)
                    {
                        return row.IsSelected;
                    }

                    if (axisRows.Count > 0 && !axisRows.Any(CheckRow26))
                    {
                        _selectedAxisId = axisRows[0].Axis;
                        axisRows = CreateAxisRows(snapshot, _selectedAxisId);
                        OnPropertyChanged(nameof(SelectedAxisId));
                        OnPropertyChanged(nameof(SelectedAxisRow));
                    }

                    AxisRows = axisRows;
                    NotifyMonitorLiveProperties(nameof(AxisRows), nameof(StatusRows));
                    break;

                case "CHILLER":
                    SummaryItems = CreateSummaryItems(selectedTab, snapshot);
                    NotifyMonitorLiveProperties(nameof(SummaryItems), nameof(StatusRows));
                    break;

                case "ATTENUATOR":
                    HeadSelectRows = CreateHeadSelectRows(
                        selectedTab,
                        _selectedAttenuatorNumber,
                        EN_EQP_MODULE.Attenuator,
                        "ATT");
                    PositionRows = CreatePositionRows(selectedTab, snapshot);
                    NotifyMonitorLiveProperties(
                        nameof(StatusRows),
                        nameof(HeadSelectRows),
                        nameof(SelectedHeadDeviceName),
                        nameof(PositionRows));
                    break;

                case "BET":
                    HeadSelectRows = CreateHeadSelectRows(
                        selectedTab,
                        _selectedBetNumber,
                        EN_EQP_MODULE.Bet,
                        "BET");
                    SummaryItems = CreateSummaryItems(selectedTab, snapshot);
                    PositionRows = CreatePositionRows(selectedTab, snapshot);
                    NotifyMonitorLiveProperties(
                        nameof(StatusRows),
                        nameof(HeadSelectRows),
                        nameof(SelectedHeadDeviceName),
                        nameof(SummaryItems),
                        nameof(PositionRows));
                    break;

                case "POWER METER":
                    SummaryItems = CreateSummaryItems(selectedTab, snapshot);
                    PwmDeviceRows = CreatePwmDeviceRows(selectedTab, snapshot);
                    NotifyMonitorLiveProperties(nameof(StatusRows), nameof(SummaryItems), nameof(PwmDeviceRows));
                    break;

                case "MELSEC":
                    SaveMelsecWriteValues();
                    SetMelsecRows(CreateMelsecRows(selectedTab, monitorSnapshot.MelsecValues));
                    NotifyMonitorLiveProperties(
                        nameof(MelsecRows),
                        nameof(MelsecReadRows),
                        nameof(MelsecWriteRows),
                        nameof(StatusRows));
                    break;

                default:
                    NotifyMonitorLiveProperties(nameof(StatusRows));
                    break;
            }
        }
        finally
        {
            _liveRefreshRunning = false;
        }

        return Task.CompletedTask;
    }

    private void UpdatePollingContext(string selectedTab)
    {
        _statusPollingService.UpdateContext(
            selectedTab,
            _selectedLaserNumber,
            _selectedAttenuatorNumber,
            _selectedBetNumber,
            selectedTab == "MELSEC" ? "ALL" : _selectedMelsecGroup);
    }

    private async Task<ST_DEVICE_STATUS> GetDeviceStatus(CancellationToken cancellationToken)
    {
        return await GetDeviceStatus("ALL", cancellationToken);
    }

    private async Task<ST_DEVICE_STATUS> GetDeviceStatus(
        string selectedTab,
        CancellationToken cancellationToken)
    {
        var tab = NormalizeMonitorTab(selectedTab);
        var io = tab is "ALL" or "IO"
            ? await _motionManager.GetIoStatus(cancellationToken)
            : [];
        var motors = tab is "ALL" or "MOTOR"
            ? await _motionManager.GetAxisStatus(cancellationToken)
            : [];
        var laserStatus = tab is "ALL"
            ? await _interfaceManager.GetLaserStatus(_selectedLaserNumber, cancellationToken)
            : CreateEmptyLaserStatus();
        var chillerStatus = tab is "ALL" or "CHILLER"
            ? await _interfaceManager.GetChillerStatus(cancellationToken)
            : CreateEmptyChillerStatus();
        var attenuatorStatus = tab is "ALL" or "ATTENUATOR"
            ? await _interfaceManager.GetAttenuatorStatus(_selectedAttenuatorNumber, cancellationToken)
            : CreateEmptyAttenuatorStatus();
        var betStatus = tab is "ALL" or "BET"
            ? await _interfaceManager.GetBETStatus(_selectedBetNumber, cancellationToken)
            : CreateEmptyBETStatus();
        var powerMeterStatus = tab is "ALL" or "POWER METER"
            ? await _interfaceManager.GetPowerMeterStatus(cancellationToken)
            : ST_POWER_METER_STATUS.Empty;

        return new ST_DEVICE_STATUS(
            io,
            motors,
            laserStatus,
            chillerStatus,
            attenuatorStatus,
            betStatus,
            powerMeterStatus);
    }

    private static ST_LASER_STATUS CreateEmptyLaserStatus()
    {
        return new ST_LASER_STATUS(false, false, false, 0.0);
    }

    private static ST_CHILLER_STATUS CreateEmptyChillerStatus()
    {
        return new ST_CHILLER_STATUS(false, 0.0, 0.0, 0.0, false);
    }

    private static ST_ATTENUATOR_STATUS CreateEmptyAttenuatorStatus()
    {
        return new ST_ATTENUATOR_STATUS(0.0, 0.0, "IDLE", false);
    }

    private static ST_BET_STATUS CreateEmptyBETStatus()
    {
        return new ST_BET_STATUS(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, false, false, false, false, false);
    }

    private async Task SelectTab(object? parameter)
    {
        if (parameter is not string tab || string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        var selectedTab = NormalizeMonitorTab(tab);
        _selectedTabSetter(selectedTab);
        _setStatusMessage($"Monitor tab {selectedTab} selected.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task SelectHeadDevice(object? parameter)
    {
        int EvaluateParameterSwitch2()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_MONITOR_HEAD_SELECT_ROW row:
                    return row.Number;
                case int value:
                    return value;
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value):
                    return value;
                default:
                    return GetSelectedHeadNumber(SelectedTab);
            }
        }

        var number = EvaluateParameterSwitch2();

        number = Math.Clamp(number, 0, LaserHeadCount - 1);

        switch (SelectedTab)
        {
            case "ATTENUATOR":
                _selectedAttenuatorNumber = number;
                break;
            case "BET":
                _selectedBetNumber = number;
                break;
            default:
                _selectedLaserNumber = number;
                break;
        }

        _setStatusMessage($"Monitor {SelectedTab} H{number + 1:00} selected.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task SelectCoordinateBasis(object? parameter)
    {
        string EvaluateParameterSwitch3()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_MONITOR_COORDINATE_BASIS_OPTION option:
                    return option.Key;
                case string text:
                    return text;
                default:
                    return "";
            }
        }

        var basis = EvaluateParameterSwitch3();

        var normalized = NormalizeCoordinateBasis(basis);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _coordinateBasis = normalized;
        _setStatusMessage($"Coordinate viewer basis {GetCoordinateBasisName(normalized)} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectCoordinateCell(object? parameter)
    {
        int EvaluateParameterSwitch4()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case int value:
                    return value;
                case string text when int.TryParse(text.Replace("CELL", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value):
                    return value;
                default:
                    return _coordinateSelectedCellNo;
            }
        }

        var cellNo = EvaluateParameterSwitch4();

        _coordinateSelectedCellNo = Math.Max(1, cellNo);
        _coordinateSelectedHoleKey = "";
        _coordinateIsCellDetailVisible = true;
        _setStatusMessage($"Coordinate viewer Cell{_coordinateSelectedCellNo:000} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectCoordinateHole(object? parameter)
    {
        string EvaluateParameterSwitch5()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_MONITOR_COORDINATE_HOLE_BUTTON row:
                    return row.HoleKey;
                case string text:
                    return text;
                default:
                    return "";
            }
        }

        var holeKey = EvaluateParameterSwitch5();

        if (string.IsNullOrWhiteSpace(holeKey))
        {
            return;
        }

        _coordinateSelectedHoleKey = holeKey.Trim();
        _setStatusMessage($"Coordinate viewer {_coordinateSelectedHoleKey} selected.");
        await _refreshCurrentScreen();
    }

    private async Task BackToCoordinateGlassPreview()
    {
        _coordinateIsCellDetailVisible = false;
        _setStatusMessage("Coordinate viewer glass preview selected.");
        await _refreshCurrentScreen();
    }

    private async Task ExecuteOperation(object? parameter)
    {
        var label = GetMonitorOperationLabel(parameter);

        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        SaveOperationFieldValues(SelectedTab);

        ST_DEVICE_COMMAND_RESULT result;
        switch (SelectedTab)
        {
            case "MOTOR":
                result = await ExecuteMotorOperation(label);
                break;
            case "LASER":
                result = await ExecuteLaserOperation(label);
                break;
            case "CHILLER":
                result = await ExecuteChillerOperation(label);
                break;
            case "ATTENUATOR":
                result = await ExecuteAttenuatorOperation(label);
                break;
            case "BET":
                result = await ExecuteBETOperation(label);
                break;
            case "POWER METER":
                result = await ExecutePowerMeterOperation(label);
                break;
            case "PICO MOTOR":
                result = await ExecutePicoMotorOperation(label);
                break;
            default:
                result = new ST_DEVICE_COMMAND_RESULT(
                    false,
                    $"Monitor {SelectedTab} command is not connected yet: {label}");
                break;
        }

        _setStatusMessage(result.Message);

        if (SelectedTab is "MOTOR" or "LASER" or "CHILLER" or "ATTENUATOR" or "BET" or "POWER METER" or "PICO MOTOR")
        {
            _refreshShellStatus();
            await _refreshCurrentScreen();
        }
    }

    private async Task SetOutput(
        object? parameter,
        bool isOn)
    {
        if (parameter is not string address || string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var result = await _motionManager.SetOutputCommand(address, isOn);
        _setStatusMessage(result.Message);
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task SelectMelsecGroup(object? parameter)
    {
        if (parameter is ST_MONITOR_MELSEC_GROUP group)
        {
            _selectedMelsecGroup = group.Name;
        }
        else if (parameter is string groupName && !string.IsNullOrWhiteSpace(groupName))
        {
            _selectedMelsecGroup = groupName.Trim().ToUpperInvariant();
        }
        else
        {
            return;
        }

        _setStatusMessage($"MELSEC group {_selectedMelsecGroup} selected.");
        await _refreshCurrentScreen();
    }

    private async Task WriteMelsec(object? parameter)
    {
        if (parameter is not ST_MONITOR_MELSEC_ROW row)
        {
            return;
        }

        var result = await ExecuteMelsecWrite(row);
        _setStatusMessage(result.Message);
        _refreshShellStatus();
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteMelsecWrite(ST_MONITOR_MELSEC_ROW row)
    {
        try
        {
            if (!row.CanWrite)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, $"MELSEC WRITE is blocked by ACCESS={row.Access}: {row.Id}");
            }

            var value = row.WriteValue.Trim();
            _melsecWriteValues[row.Id] = value;

            switch (row.DataType)
            {
                case "BIT":
                    await _interfaceManager.Melsec.WriteBit(row.Id, ParseMelsecBit(value));
                    break;
                case "WORD":
                case "DWORD":
                    await _interfaceManager.Melsec.WriteWord(row.Id, int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));
                    break;
                case "DOUBLE":
                case "FLOAT":
                    await _interfaceManager.Melsec.WriteDouble(row.Id, double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    break;
                case "STRING":
                    await _interfaceManager.Melsec.WriteString(row.Id, value);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported MELSEC write type: {row.DataType}");
            }

            return new ST_DEVICE_COMMAND_RESULT(true, $"MELSEC WRITE {row.Id}({row.Address}) = {value}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or TimeoutException or IOException or FormatException)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"MELSEC WRITE {row.Id} failed. {ex.Message}");
        }
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteMotorOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"Motion {_selectedAxisId} status refreshed.");
        }
        (EN_MOTION_COMMAND Command, double Parameter, string Name) EvaluateKeySwitch6()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "SERVOON":
                    return (Command: EN_MOTION_COMMAND.ServoOn, Parameter: 0.0, Name: "SERVO ON");
                case "SERVOOFF":
                    return (Command: EN_MOTION_COMMAND.ServoOff, Parameter: 0.0, Name: "SERVO OFF");
                case "HOME":
                    return (Command: EN_MOTION_COMMAND.Home, Parameter: 0.0, Name: "HOME");
                case "ABSMOVE":
                    return (Command: EN_MOTION_COMMAND.MoveAbs, Parameter: ReadOperationField("Target Position", 0.0), Name: "ABS MOVE");
                case "RELMOVE":
                    return (Command: EN_MOTION_COMMAND.MoveRel, Parameter: ReadOperationField("Relative Distance", 0.0), Name: "REL MOVE");
                case "STOP":
                    return (Command: EN_MOTION_COMMAND.Stop, Parameter: 0.0, Name: "STOP");
                case "RESETALARM":
                    return (Command: EN_MOTION_COMMAND.ResetAlarm, Parameter: 0.0, Name: "RESET ALARM");
                default:
                    return (Command: EN_MOTION_COMMAND.Refresh, Parameter: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch6();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown motion monitor command: {label}");
        }

        if (command.Command is EN_MOTION_COMMAND.Home or EN_MOTION_COMMAND.MoveAbs or EN_MOTION_COMMAND.MoveRel)
        {
            var interLock = _interLockManager.Evaluate(await GetDeviceStatus(CancellationToken.None));

            if (!interLock.CanManualMove)
            {
                bool MatchItem27(Common.InterLock.ST_INTERLOCK_ITEM item)
                {
                    return item.Level != EN_INTERLOCK_LEVEL.Ok;
                }

                var blockedItem = interLock.Items.FirstOrDefault(MatchItem27);
                var detail = blockedItem is null
                    ? "InterLock is not ready."
                    : $"{blockedItem.Signal}: {blockedItem.Detail}";

                return new ST_DEVICE_COMMAND_RESULT(
                    false,
                    $"Motion {_selectedAxisId} {command.Name} blocked by InterLock. {detail}");
            }
        }

        var result = await _motionManager.ExecuteMotionCommand(
            _selectedAxisId,
            command.Command,
            command.Parameter);

        var message = result.IsSuccess
            ? $"Motion {_selectedAxisId} {command.Name} command OK."
            : result.Message;

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteLaserOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            var status = await _interfaceManager.RefreshTalonLaserStatus(_selectedLaserNumber);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"{SelectedLaserName} Talon laser refreshed. Power {status.OutputPower.ToString("F3", CultureInfo.InvariantCulture)} W.");
        }
        (EN_TALON_COMMAND Command, double Parameter, string Name) EvaluateKeySwitch7()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "LASERON":
                    return (Command: EN_TALON_COMMAND.SetLaserOnOff, Parameter: 1.0, Name: "LASER ON");
                case "LASEROFF":
                    return (Command: EN_TALON_COMMAND.SetLaserOnOff, Parameter: 0.0, Name: "LASER OFF");
                case "GATEON":
                    return (Command: EN_TALON_COMMAND.SetGateOpenClose, Parameter: 1.0, Name: "GATE ON");
                case "GATEOFF":
                    return (Command: EN_TALON_COMMAND.SetGateOpenClose, Parameter: 0.0, Name: "GATE OFF");
                case "SHUTTEROPEN":
                    return (Command: EN_TALON_COMMAND.SetShutterOpenClose, Parameter: 1.0, Name: "SHUTTER OPEN");
                case "SHUTTERCLOSE":
                    return (Command: EN_TALON_COMMAND.SetShutterOpenClose, Parameter: 0.0, Name: "SHUTTER CLOSE");
                case "SETQSW":
                    return (Command: EN_TALON_COMMAND.SetQsw, Parameter: ReadOperationField("QSW", 20000.0), Name: "SET QSW");
                case "SETEPRF":
                    return (Command: EN_TALON_COMMAND.SetEprf, Parameter: ReadOperationField("EPRF", 20000.0), Name: "SET EPRF");
                case "SETSHG":
                    return (Command: EN_TALON_COMMAND.SetShg, Parameter: ReadOperationField("SHG Count", 0.0), Name: "SET SHG");
                case "SETQMODE":
                    return (Command: EN_TALON_COMMAND.SetQMode, Parameter: ReadOperationField("Q Mode", 0.0), Name: "SET Q MODE");
                case "SAVE":
                    return (Command: EN_TALON_COMMAND.RequestSave, Parameter: 0.0, Name: "SAVE");
                default:
                    return (Command: EN_TALON_COMMAND.RequestStatusString, Parameter: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch7();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown Talon monitor command: {label}");
        }

        var result = await _interfaceManager.ExecuteTalonLaserCommand(_selectedLaserNumber, command.Command, command.Parameter);

        if (result.IsSuccess)
        {
            await _interfaceManager.RefreshTalonLaserStatus(_selectedLaserNumber);
        }

        var message = result.IsSuccess
            ? $"{SelectedLaserName} Talon {command.Name} command OK. Response: {result.Message}"
            : $"{SelectedLaserName} Talon {command.Name} command failed. {result.Message}";

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteChillerOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            var status = await _interfaceManager.RefreshChillerStatus();
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"Chiller refreshed. Temp {status.LiquidTempC.ToString("F1", CultureInfo.InvariantCulture)} C.");
        }
        (EN_CHILLER_COMMAND Command, double Parameter, string Name) EvaluateKeySwitch8()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "RUN":
                    return (Command: EN_CHILLER_COMMAND.Run, Parameter: 0.0, Name: "RUN");
                case "STOP":
                    return (Command: EN_CHILLER_COMMAND.Stop, Parameter: 0.0, Name: "STOP");
                case "PUMPONLY":
                    return (Command: EN_CHILLER_COMMAND.PumpOnly, Parameter: 0.0, Name: "PUMP ONLY");
                case "SETTEMP":
                    return (Command: EN_CHILLER_COMMAND.SetTemperature, Parameter: ReadOperationField("Set Temperature", 22.0), Name: "SET TEMP");
                case "RESETALARM":
                    return (Command: EN_CHILLER_COMMAND.ResetAlarm, Parameter: 0.0, Name: "RESET ALARM");
                default:
                    return (Command: EN_CHILLER_COMMAND.PollLiquidTemp, Parameter: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch8();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown Chiller monitor command: {label}");
        }

        var result = await _interfaceManager.ExecuteChillerCommand(command.Command, command.Parameter);

        if (result.IsSuccess)
        {
            await _interfaceManager.RefreshChillerStatus();
        }

        var message = result.IsSuccess
            ? $"Chiller {command.Name} command OK. Response: {result.Message}"
            : $"Chiller {command.Name} command failed. {result.Message}";

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteAttenuatorOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            var status = await _interfaceManager.RefreshAttenuatorStatus(_selectedAttenuatorNumber);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"H{_selectedAttenuatorNumber + 1:00} CONEX_AGP refreshed. Position {status.CurrentPosition.ToString("F3", CultureInfo.InvariantCulture)} DEG.");
        }
        (EN_ATTENUATOR_COMMAND Command, double Parameter, string Name) EvaluateKeySwitch9()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "MOVEABS":
                    return (Command: EN_ATTENUATOR_COMMAND.MoveAbs, Parameter: ReadOperationField("Target Position", 55.0), Name: "MOVE ABS");
                case "MOVEREL":
                    return (Command: EN_ATTENUATOR_COMMAND.MoveRel, Parameter: ReadOperationField("Relative Move", 0.0), Name: "MOVE REL");
                case "HOME":
                    return (Command: EN_ATTENUATOR_COMMAND.Home, Parameter: 0.0, Name: "HOME");
                case "STOP":
                    return (Command: EN_ATTENUATOR_COMMAND.Stop, Parameter: 0.0, Name: "STOP");
                case "RESETALARM":
                    return (Command: EN_ATTENUATOR_COMMAND.ResetAlarm, Parameter: 0.0, Name: "RESET ALARM");
                default:
                    return (Command: EN_ATTENUATOR_COMMAND.Refresh, Parameter: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch9();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown CONEX_AGP monitor command: {label}");
        }

        var result = await _interfaceManager.ExecuteAttenuatorCommand(_selectedAttenuatorNumber, command.Command, command.Parameter);

        if (result.IsSuccess)
        {
            await _interfaceManager.RefreshAttenuatorStatus(_selectedAttenuatorNumber);
        }

        var message = result.IsSuccess
            ? $"H{_selectedAttenuatorNumber + 1:00} CONEX_AGP {command.Name} command OK. Response: {result.Message}"
            : $"H{_selectedAttenuatorNumber + 1:00} CONEX_AGP {command.Name} command failed. {result.Message}";

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecuteBETOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            var status = await _interfaceManager.RefreshBETStatus(_selectedBetNumber);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"H{_selectedBetNumber + 1:00} BET refreshed. MAG {status.CurrentMagnification.ToString("F3", CultureInfo.InvariantCulture)}, DIV {status.CurrentDivergence.ToString("F3", CultureInfo.InvariantCulture)}.");
        }

        if (key == "SAVETABLE")
        {
            await _interfaceManager.SaveBETData(CreateBETTableData(BetTableRows));
            return new ST_DEVICE_COMMAND_RESULT(true, "BET TABLE saved to JHMI_BET.csv.");
        }

        if (key.StartsWith("MOVETABLE", StringComparison.OrdinalIgnoreCase))
        {
            var tableNoText = key["MOVETABLE".Length..];

            if (!int.TryParse(
                    tableNoText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var tableNo))
            {
                return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown BET table move command: {label}");
            }

            var tableIndex = Math.Max(0, tableNo);
            var tableResult = await _interfaceManager.ExecuteBETCommand(
                _selectedBetNumber,
                EN_BET_COMMAND.MoveTable,
                tableIndex);

            if (tableResult.IsSuccess)
            {
                await _interfaceManager.RefreshBETStatus(_selectedBetNumber);
            }

            var tableMessage = tableResult.IsSuccess
                ? $"H{_selectedBetNumber + 1:00} BET MOVE TABLE {tableIndex} command OK. Response: {tableResult.Message}"
                : $"H{_selectedBetNumber + 1:00} BET MOVE TABLE {tableIndex} command failed. {tableResult.Message}";

            return new ST_DEVICE_COMMAND_RESULT(tableResult.IsSuccess, tableMessage);
        }

        var targetMag = ReadOperationField("Target MAG POS", 1020.0);
        var targetDiv = ReadOperationField("Target DIV POS", 1626.0);
        (EN_BET_COMMAND Command, double Parameter1, double Parameter2, string Name) EvaluateKeySwitch10()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "SETMAG" or "MOVEMAG":
                    return (Command: EN_BET_COMMAND.MoveMagnification, Parameter1: targetMag, Parameter2: 0.0, Name: "MOVE MAG");
                case "SETDIV" or "MOVEDIV":
                    return (Command: EN_BET_COMMAND.MoveDivergence, Parameter1: 0.0, Parameter2: targetDiv, Name: "MOVE DIV");
                case "HOME":
                    return (Command: EN_BET_COMMAND.InitMotor, Parameter1: 0.0, Parameter2: 0.0, Name: "HOME");
                case "STOP":
                    return (Command: EN_BET_COMMAND.Stop, Parameter1: 0.0, Parameter2: 0.0, Name: "STOP");
                case "RESETALARM":
                    return (Command: EN_BET_COMMAND.ResetAlarm, Parameter1: 0.0, Parameter2: 0.0, Name: "RESET ALARM");
                default:
                    return (Command: EN_BET_COMMAND.Refresh, Parameter1: 0.0, Parameter2: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch10();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown BET monitor command: {label}");
        }

        var result = await _interfaceManager.ExecuteBETCommand(_selectedBetNumber, command.Command, command.Parameter1, command.Parameter2);

        if (result.IsSuccess)
        {
            await _interfaceManager.RefreshBETStatus(_selectedBetNumber);
        }

        var message = result.IsSuccess
            ? $"H{_selectedBetNumber + 1:00} BET {command.Name} command OK. Response: {result.Message}"
            : $"H{_selectedBetNumber + 1:00} BET {command.Name} command failed. {result.Message}";

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecutePowerMeterOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key == "REFRESH")
        {
            var status = await _interfaceManager.RefreshPowerMeterStatus();
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"PowerMeter refreshed. Power {status.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture)} {status.Unit}.");
        }

        if (key == "CREATEPROCESS")
        {
            return await CreatePowerMeterProcess();
        }

        if (key == "DELETEPROCESS")
        {
            return await DeletePowerMeterProcess();
        }

        if (key == "RENAMEPROCESS")
        {
            return await RenamePowerMeterProcess();
        }

        if (key == "SAVEPROCESS")
        {
            return await CommitCurrentPowerMeterStepEdit(refreshScreen: true);
        }

        if (key == "ADDSTEP")
        {
            return await AddPowerMeterStep(copySelectedStep: false);
        }

        if (key == "COPYSTEP")
        {
            return await AddPowerMeterStep(copySelectedStep: true);
        }

        if (key == "DELETESTEP")
        {
            return await DeleteSelectedPowerMeterStep();
        }

        if (key == "DELETEALL")
        {
            return await DeleteAllPowerMeterSteps();
        }

        if (key == "START")
        {
            return await RunPowerMeterMeasureSequence();
        }

        if (key == "STOP")
        {
            _powerMeterMeasureCts?.Cancel();
            var stopResult = await _interfaceManager.ExecutePowerMeterCommand(EN_POWER_METER_COMMAND.StopStreaming);
            return stopResult.IsSuccess
                ? new ST_DEVICE_COMMAND_RESULT(true, "PowerMeter measure sequence stopped.")
                : new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter stop failed. {stopResult.Message}");
        }
        (EN_POWER_METER_COMMAND Command, double Parameter, string Name) EvaluateKeySwitch11()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "GETPOWER" or "READPOWER":
                    return (Command: EN_POWER_METER_COMMAND.ReadPower, Parameter: 0.0, Name: "GET POWER");
                case "GETSERIAL":
                    return (Command: EN_POWER_METER_COMMAND.QuerySerialNumber, Parameter: 0.0, Name: "GET SERIAL");
                case "GETWAVELENGTH":
                    return (Command: EN_POWER_METER_COMMAND.QueryWaveLength, Parameter: 0.0, Name: "GET WAVELENGTH");
                case "SETWAVELENGTH" or "SETWAVE":
                    return (Command: EN_POWER_METER_COMMAND.SetWaveLength, Parameter: ReadPwmSetting("WAVELENGTH", 355.0), Name: "SET WAVELENGTH");
                case "RESET":
                    return (Command: EN_POWER_METER_COMMAND.Reset, Parameter: 0.0, Name: "RESET");
                default:
                    return (Command: EN_POWER_METER_COMMAND.Refresh, Parameter: 0.0, Name: "");
            }
        }

        var command = EvaluateKeySwitch11();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown PowerMeter monitor command: {label}");
        }

        var result = await _interfaceManager.ExecutePowerMeterCommand(command.Command, command.Parameter);

        if (result.IsSuccess)
        {
            await _interfaceManager.RefreshPowerMeterStatus();
        }

        var message = result.IsSuccess
            ? $"PowerMeter {command.Name} command OK. Response: {result.Message}"
            : $"PowerMeter {command.Name} command failed. {result.Message}";

        return new ST_DEVICE_COMMAND_RESULT(result.IsSuccess, message);
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorOperation(string label)
    {
        var key = NormalizeMonitorOperation(label);

        if (key is "ALLMOTOR1" or "ALLMOTOR2" or "ALLMOTOR3" or "ALLMOTOR4")
        {
            var motorNo = key[^1] - '0';
            if (!_picoAllMoveMotorNos.Add(motorNo))
            {
                _picoAllMoveMotorNos.Remove(motorNo);
            }

            OnPropertyChanged(nameof(PicoAllMotorSelectButtons));
            return new ST_DEVICE_COMMAND_RESULT(true, $"PICO_MOTOR ALL MOVE MOTOR {motorNo} selection changed.");
        }

        if (key is "MOTOR1" or "MOTOR2" or "MOTOR3" or "MOTOR4")
        {
            _selectedPicoMotorNo = key[^1] - '0';
            OnPropertyChanged(nameof(PicoMotorSelectButtons));
            return await _interfaceManager.ExecutePicoMotorCommand(
                EN_PICO_MOTOR_COMMAND.SelectMotor,
                _selectedPicoMotorNo);
        }

        if (key == "START")
        {
            if (!_picoMotorIsConnected)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR is disconnected. Connect first.");
            }

            if (_picoAllMoveTask is { IsCompleted: false })
            {
                return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR ALL_MOVE is already running.");
            }
            int GetNumberSortKey28(int number)
            {
                return number;
            }

            var selectedMotors = _picoAllMoveMotorNos.OrderBy(GetNumberSortKey28).ToArray();
            var setCount = Math.Max(0, (int)Math.Round(ReadOperationField("Set Count", 0)));
            var position = ReadOperationField("Position", 0.0);

            if (selectedMotors.Length == 0)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, "Select at least one motor for PICO_MOTOR ALL_MOVE.");
            }

            if (setCount <= 0)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR ALL_MOVE Set Count must be greater than 0.");
            }

            _picoAllMoveTask = RunPicoMotorAllMove(selectedMotors, position, setCount);
            return new ST_DEVICE_COMMAND_RESULT(true, "PICO_MOTOR ALL_MOVE started.");
        }
        (EN_PICO_MOTOR_COMMAND, double) EvaluateKeySwitch12()
        {
            var switchValue = key;
            switch (switchValue)
            {
                case "CONNECT":
                    return (EN_PICO_MOTOR_COMMAND.Connect, 0.0);
                case "DISCONNECT":
                    return (EN_PICO_MOTOR_COMMAND.Disconnect, 0.0);
                case "HOME":
                    return (EN_PICO_MOTOR_COMMAND.Home, 0.0);
                case "STOPMOTION":
                    return (EN_PICO_MOTOR_COMMAND.StopMotion, 0.0);
                case "STOP" or "ALLSTOP":
                    return (EN_PICO_MOTOR_COMMAND.AllMotorStop, 0.0);
                case "REL" or "REL+":
                    return label.Contains('+', StringComparison.Ordinal)
                        ? (EN_PICO_MOTOR_COMMAND.MoveRelativePositive, Math.Abs(ReadOperationField("Relative Move", 0.0)))
                        : (EN_PICO_MOTOR_COMMAND.MoveRelativeNegative, Math.Abs(ReadOperationField("Relative Move", 0.0)));
                case "ABSMOVE":
                    return (EN_PICO_MOTOR_COMMAND.MoveAbsolute, ReadOperationField("Absolute Move", 0.0));
                case "SETVEL" or "SETVELOCITY" or "PICOSETVEL":
                    return (EN_PICO_MOTOR_COMMAND.SetVelocity, ReadOperationField("Set Velocity", 0.0));
                case "SETACC" or "SETACCELERATION" or "PICOSETACC":
                    return (EN_PICO_MOTOR_COMMAND.SetAcceleration, ReadOperationField("Set Acceleration", 0.0));
                case "REFRESH":
                    return (EN_PICO_MOTOR_COMMAND.Refresh, 0.0);
                default:
                    return ((EN_PICO_MOTOR_COMMAND)(-1), 0.0);
            }
        }

        var command = EvaluateKeySwitch12();

        if ((int)command.Item1 < 0)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Unknown PicoMotor command: {label}");
        }

        if (!_picoMotorIsConnected &&
            command.Item1 is not EN_PICO_MOTOR_COMMAND.Connect and
                not EN_PICO_MOTOR_COMMAND.Disconnect and
                not EN_PICO_MOTOR_COMMAND.SelectMotor)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR is disconnected. Connect first.");
        }

        var isPositionMove = command.Item1 is EN_PICO_MOTOR_COMMAND.Home
            or EN_PICO_MOTOR_COMMAND.MoveRelativeNegative
            or EN_PICO_MOTOR_COMMAND.MoveRelativePositive
            or EN_PICO_MOTOR_COMMAND.MoveAbsolute;

        if (isPositionMove && _picoMotionTask is { IsCompleted: false })
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR motion is already running.");
        }

        if (isPositionMove)
        {
            _picoMotionTask = RunPicoMotorPositionMove(command.Item1, _selectedPicoMotorNo, command.Item2);
            return new ST_DEVICE_COMMAND_RESULT(true, $"PICO_MOTOR {label} started.");
        }

        var result = await _interfaceManager.ExecutePicoMotorCommand(
            command.Item1,
            _selectedPicoMotorNo,
            command.Item2);

        if (result.IsSuccess)
        {
            if (command.Item1 == EN_PICO_MOTOR_COMMAND.SetVelocity)
            {
                SetOperationFieldValue("Set Velocity", "0.00");
            }
            else if (command.Item1 == EN_PICO_MOTOR_COMMAND.SetAcceleration)
            {
                SetOperationFieldValue("Set Acceleration", "0.00");
            }
        }

        return result;
    }

    private async Task StartPicoJog(object? parameter)
    {
        if (SelectedTab != "PICO MOTOR" || !_picoMotorIsConnected)
        {
            _setStatusMessage("PICO_MOTOR is disconnected. Connect first.");
            return;
        }

        var label = GetMonitorOperationLabel(parameter);
        var command = label.Contains('+', StringComparison.Ordinal)
            ? EN_PICO_MOTOR_COMMAND.JogPositive
            : EN_PICO_MOTOR_COMMAND.JogNegative;

        await _picoJogCommandLock.WaitAsync();
        try
        {
            _picoJogCancellationSource?.Cancel();
            _picoJogCancellationSource?.Dispose();
            _picoJogCancellationSource = new CancellationTokenSource();

            var result = await _interfaceManager.ExecutePicoMotorCommand(command, _selectedPicoMotorNo);
            _setStatusMessage(result.IsSuccess
                ? $"PICO_MOTOR {label} started."
                : $"PICO_MOTOR {label} failed. {result.Message}");

            if (result.IsSuccess && IsPicoMotorSimulation())
            {
                _ = RunPicoJogSimulation(command, _picoJogCancellationSource.Token);
            }
        }
        finally
        {
            _picoJogCommandLock.Release();
        }
    }

    private async Task StopPicoJog()
    {
        if (SelectedTab != "PICO MOTOR")
        {
            return;
        }

        await _picoJogCommandLock.WaitAsync();
        try
        {
            _picoJogCancellationSource?.Cancel();
            _picoJogCancellationSource?.Dispose();
            _picoJogCancellationSource = null;

            var result = await _interfaceManager.ExecutePicoMotorCommand(
                EN_PICO_MOTOR_COMMAND.StopMotion,
                _selectedPicoMotorNo);
            _setStatusMessage(result.IsSuccess
                ? "PICO_MOTOR JOG stopped."
                : $"PICO_MOTOR JOG stop failed. {result.Message}");
        }
        finally
        {
            _picoJogCommandLock.Release();
        }
    }

    private bool IsPicoMotorSimulation()
    {
        int GetItemSortKey29(ST_INTERFACE_DATA item)
        {
            return item.Number;
        }

        var data = _interfaceManager.GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(GetItemSortKey29)
            .FirstOrDefault();
        return data is not null && _interfaceManager.IsSimul(data.Device, data.Number);
    }

    private async Task RunPicoJogSimulation(
        EN_PICO_MOTOR_COMMAND command,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && SelectedTab == "PICO MOTOR")
            {
                await Task.Delay(300, cancellationToken);
                await _interfaceManager.ExecutePicoMotorCommand(
                    command,
                    _selectedPicoMotorNo,
                    0.001,
                    cancellationToken);
                await RefreshPicoMotorValuesIfVisible(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunPicoMotorAllMove(
        IReadOnlyList<int> selectedMotors,
        double position,
        int setCount)
    {
        try
        {
            var operation = _interfaceManager.ExecutePicoMotorAllMove(selectedMotors, position, setCount);
            while (!operation.IsCompleted)
            {
                await Task.Delay(500);
                await RefreshPicoMotorValuesIfVisible(CancellationToken.None);
            }

            var result = await operation;
            _setStatusMessage(result.Message);
        }
        catch (Exception ex)
        {
            _setStatusMessage($"PICO_MOTOR ALL_MOVE failed. {ex.Message}");
        }
        finally
        {
            await RefreshPicoMotorValuesIfVisible(CancellationToken.None);
        }
    }

    private async Task RunPicoMotorPositionMove(
        EN_PICO_MOTOR_COMMAND command,
        int motorNo,
        double parameter)
    {
        try
        {
            var operation = _interfaceManager.ExecutePicoMotorCommand(command, motorNo, parameter);
            while (!operation.IsCompleted)
            {
                await Task.Delay(100);
                await RefreshPicoMotorValuesIfVisible(CancellationToken.None);
            }

            var result = await operation;
            _setStatusMessage(result.Message);
        }
        catch (Exception ex)
        {
            _setStatusMessage($"PICO_MOTOR motion failed. {ex.Message}");
        }
        finally
        {
            await RefreshPicoMotorValuesIfVisible(CancellationToken.None);
        }
    }

    private async Task RefreshPicoMotorValuesIfVisible(CancellationToken cancellationToken)
    {
        if (SelectedTab != "PICO MOTOR")
        {
            return;
        }

        var status = await _interfaceManager.GetPicoMotorStatus(cancellationToken);
        _selectedPicoMotorNo = Math.Clamp(status.SelectedMotorNo, 1, 4);
        _picoMotorIsConnected = status.IsConnected;
        StatusRows = CreatePicoMotorStatusRows(status);
        PositionRows = CreatePicoMotorPositionRows(status);
        UpdatePicoMotorOperationFields(status);
        NotifyMonitorLiveProperties(
            nameof(StatusRows),
            nameof(PositionRows),
            nameof(OperationFields),
            nameof(PicoConnectionButtons),
            nameof(PicoMotorSelectButtons));
    }

    private void UpdatePicoMotorOperationFields(ST_PICO_MOTOR_STATUS status)
    {
        SetOperationFieldValue("Current Velocity", status.CurrentVelocity.ToString("F2", CultureInfo.InvariantCulture));
        SetOperationFieldValue("Current Acceleration", status.CurrentAcceleration.ToString("F2", CultureInfo.InvariantCulture));
        SetOperationFieldValue("Home Position", status.HomePosition.ToString(CultureInfo.InvariantCulture));
        SetOperationFieldValue("Cur Count", status.AllMoveCurrentCount.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<ST_MONITOR_COORDINATE_VIEWER_DATA> BuildCoordinateViewerData(CancellationToken cancellationToken)
    {
        var recipe = await LoadSelectedCoordinateRecipe(cancellationToken);
        var settings = new List<ST_SYSTEM_PARAMETER>();
        settings.AddRange(await _settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken));
        settings.AddRange(await _settingManager.LoadSection(EN_SETTING_TAB.Motor, cancellationToken));
        var basis = NormalizeCoordinateBasis(_coordinateBasis);
        _coordinateBasis = string.IsNullOrWhiteSpace(basis) ? "DESIGN" : basis;

        if (recipe is null)
        {
            return new ST_MONITOR_COORDINATE_VIEWER_DATA(
                CreateCoordinateBasisOptions(_coordinateBasis),
                [new("RECIPE", "SELECTED", "Recipe", "-", "", "No recipe is loaded.")],
                null,
                [],
                [],
                "0 Cells / 0 Holes",
                "-",
                GetCoordinateBasisName(_coordinateBasis),
                GetCoordinateBasisDescription(_coordinateBasis));
        }

        var parameterMap = BuildCoordinateParameterMap(recipe, settings);
        var recipeCellCount = Math.Max(1, ReadCoordinateInt(parameterMap, 1, "CELL_COUNT"));
        _coordinateSelectedCellNo = Math.Clamp(
            _coordinateSelectedCellNo,
            1,
            recipeCellCount);
        ST_RECIPE_HOLE_PLAN? holePlan = null;
        var valueRows = BuildCoordinateValueRows(recipe, settings, recipeCellCount, parameterMap).ToList();

        try
        {
            holePlan = CRecipeHolePlan.Build(parameterMap);
        }
        catch (Exception ex)
        {
            valueRows.Insert(0, new ST_MONITOR_COORDINATE_VALUE_ROW(
                "PLAN",
                "ERROR",
                "Hole Plan",
                ex.Message,
                "",
                "CRecipeHolePlan.Build"));
        }

        var cellCount = Math.Max(1, holePlan?.CellCount ?? ReadCoordinateInt(parameterMap, 1, "CELL_COUNT"));
        _coordinateSelectedCellNo = Math.Clamp(_coordinateSelectedCellNo, 1, cellCount);
        bool FilterPoint30(ST_RECIPE_HOLE_POINT point)
        {
            return point.CellNo == _coordinateSelectedCellNo;
        }

        int GetPointSortKey31(ST_RECIPE_HOLE_POINT point)
        {
            return point.Row;
        }

        int GetPointSortKey32(ST_RECIPE_HOLE_POINT point)
        {
            return point.Column;
        }

        int GetPointSortKey33(ST_RECIPE_HOLE_POINT point)
        {
            return point.HoleNo;
        }

        var selectedCellPoints = holePlan?.Points
            .Where(FilterPoint30)
            .OrderBy(GetPointSortKey31)
            .ThenBy(GetPointSortKey32)
            .ThenBy(GetPointSortKey33)
            .ToArray() ?? [];
        bool CheckPoint34(ST_RECIPE_HOLE_POINT point)
        {
            return point.HoleKey.Equals(_coordinateSelectedHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        if (selectedCellPoints.Length == 0)
        {
            _coordinateSelectedHoleKey = "";
        }
        else if (string.IsNullOrWhiteSpace(_coordinateSelectedHoleKey) ||
                 !selectedCellPoints.Any(CheckPoint34))
        {
            _coordinateSelectedHoleKey = selectedCellPoints[0].HoleKey;
        }
        bool MatchPoint35(ST_RECIPE_HOLE_POINT point)
        {
            return point.HoleKey.Equals(_coordinateSelectedHoleKey, StringComparison.OrdinalIgnoreCase);
        }

        var selectedPoint = holePlan?.Points.FirstOrDefault(MatchPoint35);
        var previewPoints = holePlan is null
            ? Array.Empty<ST_REVIEW_PLAN_POINT>()
            : CreateCoordinatePreviewPoints(holePlan.Points, selectedPoint?.HoleKey, _coordinateBasis);
        var glassPreview = CReviewGlassPreviewBuilder.Build(
            recipe,
            cellCount,
            _coordinateSelectedCellNo,
            0,
            previewPoints,
            axisIndicators: CreateCoordinateAxisIndicators(_coordinateBasis, parameterMap));
        var holeMatrixRows = holePlan is null
            ? Array.Empty<ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW>()
            : BuildCoordinateHoleMatrixRows(holePlan, parameterMap);

        return new ST_MONITOR_COORDINATE_VIEWER_DATA(
            CreateCoordinateBasisOptions(_coordinateBasis),
            valueRows,
            glassPreview.Image,
            glassPreview.CellLabels,
            holeMatrixRows,
            glassPreview.Summary,
            $"{recipe.Id} / {recipe.Name}",
            GetCoordinateBasisName(_coordinateBasis),
            GetCoordinateBasisDescription(_coordinateBasis));
    }

    private async Task<ST_RECIPE_DATA?> LoadSelectedCoordinateRecipe(CancellationToken cancellationToken)
    {
        var recipes = await _recipeManager.LoadRecipes(cancellationToken);
        var selectedRecipeId = (_selectedRecipeIdProvider() ?? "").Trim();
        bool MatchRecipe36(ST_RECIPE_DATA recipe)
        {
            return recipe.Id.Equals(selectedRecipeId, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchRecipe37(ST_RECIPE_DATA recipe)
        {
            return recipe.Name.Equals(selectedRecipeId, StringComparison.OrdinalIgnoreCase);
        }

        return recipes.FirstOrDefault(MatchRecipe36) ??
            recipes.FirstOrDefault(MatchRecipe37) ??
            recipes.FirstOrDefault();
    }

    private static Dictionary<string, string> BuildCoordinateParameterMap(
        ST_RECIPE_DATA recipe,
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in recipe.Parameters)
        {
            SetCoordinateParameter(parameters, parameter.Key, parameter.Value);
            SetCoordinateParameter(parameters, parameter.Name, parameter.Value);
        }

        foreach (var setting in settings)
        {
            SetCoordinateParameter(parameters, setting.Key, setting.Value);
            SetCoordinateParameter(parameters, setting.Name, setting.Value);
        }

        parameters.TryAdd("HEAD_COUNT", "8");
        return parameters;
    }

    private static void SetCoordinateParameter(
        IDictionary<string, string> parameters,
        string key,
        string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        parameters[key.Trim()] = value;
    }

    private IReadOnlyList<ST_MONITOR_COORDINATE_VALUE_ROW> BuildCoordinateValueRows(
        ST_RECIPE_DATA recipe,
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        int cellCount,
        IReadOnlyDictionary<string, string> parameters)
    {
        var rows = new List<ST_MONITOR_COORDINATE_VALUE_ROW>
        {
            new("RECIPE", "SELECTED", "Recipe", $"{recipe.Id} / {recipe.Name}", "", "RECIPE_ID")
        };
        EN_SETTING_TAB GetSettingSortKey38(ST_SYSTEM_PARAMETER setting)
        {
            return setting.Section;
        }

        int GetSettingSortKey39(ST_SYSTEM_PARAMETER setting)
        {
            return setting.DisplayOrder;
        }

        ST_MONITOR_COORDINATE_VALUE_ROW SelectSetting40(ST_SYSTEM_PARAMETER setting)
        {
            return new ST_MONITOR_COORDINATE_VALUE_ROW(
                            "SETTING",
                            string.IsNullOrWhiteSpace(setting.Group) ? "-" : setting.Group,
                            GetCoordinateDisplayName(setting.Name, setting.Key),
                            setting.Value,
                            setting.Unit,
                            GetCoordinateDisplayKey(setting.Key, setting.Name));
        }

        rows.AddRange(settings
            .Where(IsCoordinateSetting)
            .OrderBy(GetSettingSortKey38)
            .ThenBy(GetSettingSortKey39)
            .Select(SelectSetting40));
        bool FilterParameter41(ST_RECIPE_PARAM parameter)
        {
            return IsCoordinateRecipeParameter(parameter, cellCount);
        }

        string GetParameterSortKey42(ST_RECIPE_PARAM parameter)
        {
            return parameter.Tab;
        }

        string GetParameterSortKey43(ST_RECIPE_PARAM parameter)
        {
            return parameter.Group;
        }

        int GetParameterSortKey44(ST_RECIPE_PARAM parameter)
        {
            return parameter.DisplayOrder;
        }

        ST_MONITOR_COORDINATE_VALUE_ROW SelectParameter45(ST_RECIPE_PARAM parameter)
        {
            return new ST_MONITOR_COORDINATE_VALUE_ROW(
                            "RECIPE",
                            string.IsNullOrWhiteSpace(parameter.Group) ? "-" : parameter.Group,
                            GetCoordinateRecipeDisplayName(parameter.Name, parameter.Key),
                            parameter.Value,
                            parameter.Unit,
                            GetCoordinateDisplayKey(parameter.Key, parameter.Name));
        }

        rows.AddRange(recipe.Parameters
            .Where(FilterParameter41)
            .OrderBy(GetParameterSortKey42)
            .ThenBy(GetParameterSortKey43)
            .ThenBy(GetParameterSortKey44)
            .Select(SelectParameter45));

        rows.AddRange(BuildCoordinateCalculatedRows(parameters));
        return rows;
    }

    private IReadOnlyList<ST_MONITOR_COORDINATE_VALUE_ROW> BuildCoordinateCalculatedRows(
        IReadOnlyDictionary<string, string> parameters)
    {
        if (NormalizeCoordinateBasis(_coordinateBasis) != "STAGE")
        {
            return [];
        }

        var reviewCameraAlignKeyPosY = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_CAMERA_ALIGN_KEY_POS_Y");
        var reviewToHead1GapY = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_TO_HEAD1_GAP_Y");
        var headGapY = ReadCoordinateDouble(
            parameters,
            0.0,
            "HeadGapY");
        var akMarginX = ReadCoordinateDouble(
            parameters,
            0.0,
            "AK_MARGIN_X");
        var h01AkPositionX = ReadCoordinateDouble(
            parameters,
            0.0,
            "H01_AK_POSITION_X");
        var head1AkStageY = reviewCameraAlignKeyPosY + reviewToHead1GapY;
        var evenHeadAkStageY = head1AkStageY + headGapY;
        var h01CenterX = akMarginX + h01AkPositionX;

        return
        [
            new("CALC", "STAGE", "H01/H03/H05/H07 AK Stage Y", FormatCoordinateValue(head1AkStageY), "mm", "REVIEW_CAMERA_ALIGN_KEY_POS_Y + REVIEW_TO_HEAD1_GAP_Y"),
            new("CALC", "STAGE", "H02/H04/H06/H08 AK Stage Y", FormatCoordinateValue(evenHeadAkStageY), "mm", "HEAD1_AK_STAGE_Y + HeadGapY"),
            new("CALC", "STAGE", "H01 Center X From AK", FormatCoordinateValue(h01CenterX), "mm", "AK_MARGIN_X + H01_AK_POSITION_X")
        ];
    }

    private IReadOnlyList<ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW> BuildCoordinateHoleMatrixRows(
        ST_RECIPE_HOLE_PLAN holePlan,
        IReadOnlyDictionary<string, string> parameters)
    {
        bool FilterPoint46(ST_RECIPE_HOLE_POINT point)
        {
            return point.CellNo == _coordinateSelectedCellNo;
        }

        int GroupByPointCallback47(ST_RECIPE_HOLE_POINT point)
        {
            return point.Row;
        }

        int GetGroupSortKey48(IGrouping<int, ST_RECIPE_HOLE_POINT> group)
        {
            return group.Key;
        }

        ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW SelectGroup49(IGrouping<int, ST_RECIPE_HOLE_POINT> group)
        {
            int GetPointSortKey1(ST_RECIPE_HOLE_POINT point)
            {
                return point.Column;
            }

            int GetPointSortKey2(ST_RECIPE_HOLE_POINT point)
            {
                return point.HoleNo;
            }

            ST_MONITOR_COORDINATE_HOLE_BUTTON SelectPoint3(ST_RECIPE_HOLE_POINT point)
            {
                return new ST_MONITOR_COORDINATE_HOLE_BUTTON(
                                                    point.HoleKey,
                                                    CReviewHoleNameFormatter.ToMatrixName(point.HoleNo, point.PixelCountX),
                                                    point.HeadNo > 0 ? $"H{point.HeadNo:00}" : "-",
                                                    CreateCoordinateHoleButtonDetail(point, parameters),
                                                    point.HoleKey.Equals(_coordinateSelectedHoleKey, StringComparison.OrdinalIgnoreCase),
                                                    SelectCoordinateHoleCommand);
            }

            return new ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW(
                            group.Key + 1,
                            group
                                .OrderBy(GetPointSortKey1)
                                .ThenBy(GetPointSortKey2)
                                .Select(SelectPoint3)
                                .ToArray());
        }

        return holePlan.Points
            .Where(FilterPoint46)
            .GroupBy(GroupByPointCallback47)
            .OrderBy(GetGroupSortKey48)
            .Select(SelectGroup49)
            .ToArray();
    }

    private static IReadOnlyList<ST_REVIEW_PLAN_POINT> CreateCoordinatePreviewPoints(
        IReadOnlyList<ST_RECIPE_HOLE_POINT> points,
        string? selectedHoleKey,
        string basis)
    {
        ST_REVIEW_PLAN_POINT SelectPoint50(ST_RECIPE_HOLE_POINT point)
        {
            var designPosition = GetCoordinatePreviewPosition(point, basis);
            return new ST_REVIEW_PLAN_POINT(
                point.SequenceNo,
                point.HoleKey,
                point.HeadNo,
                point.CellNo,
                point.HoleNo,
                point.PixelCountX,
                point.PixelCountY,
                true,
                designPosition.X,
                designPosition.Y,
                designPosition.X,
                designPosition.Y,
                0.0,
                0.0,
                !string.IsNullOrWhiteSpace(selectedHoleKey) &&
                    point.HoleKey.Equals(selectedHoleKey, StringComparison.OrdinalIgnoreCase)
                        ? EN_REVIEW_POINT_STATE.Current
                        : EN_REVIEW_POINT_STATE.Ready,
                "READY")
            {
                ReviewOffsetX = point.ReviewOffsetX,
                ReviewOffsetY = point.ReviewOffsetY
            };
        }
        return points
            .Select(SelectPoint50)
            .ToArray();
    }

    private string CreateCoordinateHoleButtonDetail(
        ST_RECIPE_HOLE_POINT point,
        IReadOnlyDictionary<string, string> parameters)
    {
        var headName = point.HeadNo > 0 ? $"H{point.HeadNo:00}" : "-";
        if (NormalizeCoordinateBasis(_coordinateBasis) == "STAGE")
        {
            var stageY = GetAbsoluteStageY(point, parameters);
            return $"{headName}\nStage Y\n{FormatCoordinateValue(stageY)}";
        }

        if (NormalizeCoordinateBasis(_coordinateBasis) == "SCANNER")
        {
            var scannerPosition = GetViewerScannerPosition(point);
            return $"{headName}\nGX {FormatCoordinateValue(scannerPosition.Gx)}\nGY {FormatCoordinateValue(scannerPosition.Gy)}";
        }

        if (NormalizeCoordinateBasis(_coordinateBasis) == "REVIEW_CAMERA")
        {
            var reviewPosition = GetReviewCameraPosition(point, parameters);
            return $"{headName}\nCam X {FormatCoordinateValue(reviewPosition.CameraX)}\nStage Y {FormatCoordinateValue(reviewPosition.StageY)}";
        }

        var basisPosition = GetCoordinateBasisPosition(_coordinateBasis, point);
        return $"{headName}\nX {FormatCoordinateValue(basisPosition.X)}\nY {FormatCoordinateValue(basisPosition.Y)}";
    }

    private void ApplyCoordinateViewerData(ST_MONITOR_COORDINATE_VIEWER_DATA data)
    {
        CoordinateBasisOptions = data.BasisOptions;
        CoordinateValueRows = data.ValueRows;
        CoordinateGlassPreviewImage = data.GlassPreviewImage;
        CoordinateCellPreviewLabels = data.CellPreviewLabels;
        CoordinateHoleMatrixRows = data.HoleMatrixRows;
        CoordinateGlassPreviewSummary = data.GlassPreviewSummary;
        CoordinateSelectedRecipeName = data.RecipeName;
        CoordinateSelectedBasisName = data.BasisName;
        CoordinateBasisDescription = data.BasisDescription;
        NotifyMonitorLiveProperties(
            nameof(CoordinateBasisOptions),
            nameof(CoordinateValueRows),
            nameof(CoordinateGlassPreviewImage),
            nameof(CoordinateCellPreviewLabels),
            nameof(CoordinateHoleMatrixRows),
            nameof(CoordinateGlassPreviewSummary),
            nameof(CoordinateIsGlassPreviewVisible),
            nameof(CoordinateIsCellDetailVisible),
            nameof(CoordinateSelectedRecipeName),
            nameof(CoordinateSelectedBasisName),
            nameof(CoordinateBasisDescription),
            nameof(CoordinateSelectedCellName),
            nameof(CoordinateSelectedHoleName));
    }

    private static IReadOnlyList<ST_MONITOR_COORDINATE_BASIS_OPTION> CreateCoordinateBasisOptions(string selectedBasis)
    {
        return
        [
            CreateCoordinateBasisOption("DESIGN", selectedBasis),
            CreateCoordinateBasisOption("STAGE", selectedBasis),
            CreateCoordinateBasisOption("SCANNER", selectedBasis),
            CreateCoordinateBasisOption("REVIEW_CAMERA", selectedBasis)
        ];
    }

    private static ST_MONITOR_COORDINATE_BASIS_OPTION CreateCoordinateBasisOption(
        string key,
        string selectedBasis)
    {
        return new ST_MONITOR_COORDINATE_BASIS_OPTION(
            key,
            GetCoordinateBasisName(key),
            GetCoordinateBasisDescription(key),
            key.Equals(selectedBasis, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ST_REVIEW_GLASS_AXIS_INDICATOR> CreateCoordinateAxisIndicators(
        string basis,
        IReadOnlyDictionary<string, string> parameters)
    {
        IReadOnlyList<ST_REVIEW_GLASS_AXIS_INDICATOR> EvaluateValueSwitch13()
        {
            var switchValue = NormalizeCoordinateBasis(basis);
            switch (switchValue)
            {
                case "DESIGN":
                    return [new ST_REVIEW_GLASS_AXIS_INDICATOR("X+", "Y+", true, true)];
                case "STAGE":
                    return [new ST_REVIEW_GLASS_AXIS_INDICATOR("", "Y+", true, true)];
                case "SCANNER":
                    return [
                        CreateScannerAxisIndicator(parameters, 1, "TOP_RIGHT", "H01/03/05/07"),
                CreateScannerAxisIndicator(parameters, 2, "BOTTOM_LEFT", "H02/04/06/08")
                    ];
                case "REVIEW_CAMERA":
                    return [new ST_REVIEW_GLASS_AXIS_INDICATOR("Cam X+", "", true, true)];
                default:
                    return [];
            }
        }

        return EvaluateValueSwitch13();
    }

    private static ST_REVIEW_GLASS_AXIS_INDICATOR CreateScannerAxisIndicator(
        IReadOnlyDictionary<string, string> parameters,
        int representativeHeadNo,
        string anchor,
        string title)
    {
        var defaultGxSign = representativeHeadNo % 2 == 0 ? 1.0 : -1.0;
        var defaultGySign = representativeHeadNo % 2 == 0 ? -1.0 : 1.0;
        var gxSign = ReadCoordinateDirection(
            parameters,
            defaultGxSign,
            $"H{representativeHeadNo:00}_STAGE_X_TO_GX_SIGN",
            $"H{representativeHeadNo:00}_STAGE_X_TO_GX_DIRECTION",
            "STAGE_X_TO_GX_SIGN",
            "STAGE_X_TO_GX_DIRECTION");
        var gySign = ReadCoordinateDirection(
            parameters,
            defaultGySign,
            $"H{representativeHeadNo:00}_STAGE_Y_TO_GY_SIGN",
            $"H{representativeHeadNo:00}_STAGE_Y_TO_GY_DIRECTION",
            "STAGE_Y_TO_GY_SIGN",
            "STAGE_Y_TO_GY_DIRECTION");

        return new ST_REVIEW_GLASS_AXIS_INDICATOR(
            "GX+",
            "GY+",
            gxSign >= 0.0,
            gySign >= 0.0,
            anchor,
            title);
    }

    private bool IsCoordinateSetting(ST_SYSTEM_PARAMETER setting)
    {
        var key = NormalizeCoordinateKey(GetCoordinateDisplayKey(setting.Key, setting.Name));
        if (IsCoordinateOffsetKey(key))
        {
            return false;
        }
        bool EvaluateValueSwitch14()
        {
            var switchValue = NormalizeCoordinateBasis(_coordinateBasis);
            switch (switchValue)
            {
                case "STAGE":
                    return key.Contains("STAGESTARTPOS", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("STAGESCANDIRECTION", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("HEADGAP", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("REVIEWTOHEAD", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("AKPOSITION", StringComparison.OrdinalIgnoreCase);
                case "SCANNER":
                    return key.Contains("HEADGAP", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("REVIEWTOHEAD", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("AKPOSITION", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("SCANENCODER", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("STAGESCANDIRECTION", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("STAGEXTOGX", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("STAGEYTOGY", StringComparison.OrdinalIgnoreCase);
                case "REVIEW_CAMERA":
                    return false;
                default:
                    return false;
            }
        }

        return EvaluateValueSwitch14();
    }

    private bool IsCoordinateRecipeParameter(
        ST_RECIPE_PARAM parameter,
        int cellCount)
    {
        var key = NormalizeCoordinateKey(GetCoordinateDisplayKey(parameter.Key, parameter.Name));
        var basis = NormalizeCoordinateBasis(_coordinateBasis);
        if (IsCoordinateOffsetKey(key))
        {
            return basis == "DESIGN" &&
                _coordinateIsCellDetailVisible &&
                IsSelectedCoordinateCellParameter(key);
        }

        if (key is "AKMARGINX" or "AKMARGINY")
        {
            return basis is "DESIGN" or "STAGE" or "SCANNER" or "REVIEW_CAMERA";
        }

        if (key is "HEADCOUNT" or "CELLCOUNT" or "GLASSSIZEX" or "GLASSSIZEY")
        {
            return basis is "DESIGN" or "SCANNER" or "REVIEW_CAMERA";
        }

        if (key is "REVIEWCAMERAALIGNKEYPOSX" or "REVIEWCAMERAALIGNKEYPOSY")
        {
            return basis is "STAGE" or "REVIEW_CAMERA";
        }

        if (key.StartsWith("DISTORTIONKEY", StringComparison.OrdinalIgnoreCase))
        {
            return basis is "DESIGN" or "REVIEW_CAMERA";
        }

        var isCellGeometry = TryGetCoordinateCellNo(key, out var cellNo) &&
            cellNo >= 1 &&
            cellNo <= cellCount &&
            (!_coordinateIsCellDetailVisible || cellNo == _coordinateSelectedCellNo) &&
            (key.Contains("ALIGNTO1STPIXEL", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("HOLECOUNT", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("NUMOFPIXEL", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("PITCH", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("PIXELSIZE", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("ROTATION", StringComparison.OrdinalIgnoreCase));
        return isCellGeometry &&
            basis is "DESIGN" or "SCANNER" or "REVIEW_CAMERA";
    }

    private bool IsSelectedCoordinateCellParameter(string normalizedKey)
    {
        return TryGetCoordinateCellNo(normalizedKey, out var cellNo) &&
            cellNo == _coordinateSelectedCellNo;
    }

    private static bool IsCoordinateOffsetKey(string normalizedKey)
    {
        return normalizedKey.Contains("OFFSET", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCoordinateDisplayName(string name, string key)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return string.IsNullOrWhiteSpace(key) ? "-" : key.Trim();
    }

    private static string GetCoordinateRecipeDisplayName(string name, string key)
    {
        var displayKey = GetCoordinateDisplayKey(key, name);
        if (NormalizeCoordinateKey(displayKey).StartsWith("CELL", StringComparison.OrdinalIgnoreCase))
        {
            return displayKey;
        }

        return GetCoordinateDisplayName(name, key);
    }

    private static string GetCoordinateDisplayKey(string key, string name)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    private static string NormalizeCoordinateKey(string key)
    {
        return key
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();
    }

    private static bool TryGetCoordinateCellNo(
        string normalizedKey,
        out int cellNo)
    {
        cellNo = 0;
        if (!normalizedKey.StartsWith("CELL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = 4;
        var end = start;
        while (end < normalizedKey.Length && char.IsDigit(normalizedKey[end]))
        {
            end++;
        }

        return end > start &&
            int.TryParse(normalizedKey[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellNo);
    }

    private static int ReadCoordinateInt(
        IReadOnlyDictionary<string, string> parameters,
        int defaultValue,
        string key)
    {
        return parameters.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static double ReadCoordinateDouble(
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

    private static double ReadCoordinateDirection(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        var value = ReadCoordinateDouble(parameters, defaultValue, keys);
        return value < 0.0 ? -1.0 : 1.0;
    }

    private static string NormalizeCoordinateBasis(string basis)
    {
        var normalized = (basis ?? "").Trim().Replace("_", " ", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        string EvaluateNormalizedSwitch15()
        {
            var switchValue = normalized;
            switch (switchValue)
            {
                case "DESIGN":
                    return "DESIGN";
                case "STAGE":
                    return "STAGE";
                case "HEAD" or "SCANNER":
                    return "SCANNER";
                case "CAMERA" or "VISION" or "REVIEW CAMERA" or "REVIEWCAMERA":
                    return "REVIEW_CAMERA";
                default:
                    return "";
            }
        }

        return EvaluateNormalizedSwitch15();
    }

    private static string GetCoordinateBasisName(string basis)
    {
        string EvaluateValueSwitch16()
        {
            var switchValue = NormalizeCoordinateBasis(basis);
            switch (switchValue)
            {
                case "STAGE":
                    return "Stage";
                case "SCANNER":
                    return "Scanner";
                case "REVIEW_CAMERA":
                    return "Review Camera";
                default:
                    return "Design";
            }
        }

        return EvaluateValueSwitch16();
    }

    private static string GetCoordinateBasisDescription(string basis)
    {
        string EvaluateValueSwitch17()
        {
            var switchValue = NormalizeCoordinateBasis(basis);
            switch (switchValue)
            {
                case "STAGE":
                    return "Stage coordinate used for process movement";
                case "SCANNER":
                    return "Head local scanner coordinate: GX / GY";
                case "REVIEW_CAMERA":
                    return "Review camera X and stage Y target coordinate";
                default:
                    return "Recipe design coordinate from Align Key / Glass";
            }
        }

        return EvaluateValueSwitch17();
    }

    private static (double X, double Y) GetCoordinateBasisPosition(
        string basis,
        ST_RECIPE_HOLE_POINT point)
    {
        (double X, double Y) EvaluateValueSwitch18()
        {
            var switchValue = NormalizeCoordinateBasis(basis);
            switch (switchValue)
            {
                case "STAGE":
                    return (point.StageX, point.StageY);
                case "SCANNER":
                    return GetViewerScannerPosition(point);
                case "REVIEW_CAMERA":
                    return (point.DesignX, point.DesignY);
                default:
                    return GetOffsetAppliedDesignPosition(point);
            }
        }

        return EvaluateValueSwitch18();
    }

    private static (double Gx, double Gy) GetViewerScannerPosition(ST_RECIPE_HOLE_POINT point)
    {
        return (point.ScannerGx, point.ScannerOffsetY);
    }

    private static (double CameraX, double StageY) GetReviewCameraPosition(
        ST_RECIPE_HOLE_POINT point,
        IReadOnlyDictionary<string, string> parameters)
    {
        var reviewCameraAlignKeyPosX = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_CAMERA_ALIGN_KEY_POS_X");
        var reviewCameraAlignKeyPosY = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_CAMERA_ALIGN_KEY_POS_Y");

        return (
            reviewCameraAlignKeyPosX + GetDesignXFromAlignKey(point, parameters),
            reviewCameraAlignKeyPosY - GetDesignYFromAlignKey(point, parameters));
    }

    private static (double X, double Y) GetCoordinatePreviewPosition(
        ST_RECIPE_HOLE_POINT point,
        string basis)
    {
        return NormalizeCoordinateBasis(basis) == "DESIGN"
            ? GetOffsetAppliedDesignPosition(point)
            : (point.DesignX, point.DesignY);
    }

    private static (double X, double Y) GetOffsetAppliedDesignPosition(ST_RECIPE_HOLE_POINT point)
    {
        return (
            point.DesignX + point.RecipeOffsetX + point.ReviewOffsetX,
            point.DesignY + point.RecipeOffsetY + point.ReviewOffsetY);
    }

    private static double GetAbsoluteStageY(
        ST_RECIPE_HOLE_POINT point,
        IReadOnlyDictionary<string, string> parameters)
    {
        return GetHeadAlignKeyStageY(point.HeadNo, parameters) -
            GetDesignYFromAlignKey(point, parameters);
    }

    private static double GetHeadAlignKeyStageY(
        int headNo,
        IReadOnlyDictionary<string, string> parameters)
    {
        var reviewCameraAlignKeyPosY = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_CAMERA_ALIGN_KEY_POS_Y");
        var reviewToHead1GapY = ReadCoordinateDouble(
            parameters,
            0.0,
            "REVIEW_TO_HEAD1_GAP_Y");
        var headGapY = ReadCoordinateDouble(
            parameters,
            0.0,
            "HeadGapY");
        var head1AlignKeyStageY = reviewCameraAlignKeyPosY + reviewToHead1GapY;

        return headNo > 0 && headNo % 2 == 0
            ? head1AlignKeyStageY + headGapY
            : head1AlignKeyStageY;
    }

    private static double GetDesignYFromAlignKey(
        ST_RECIPE_HOLE_POINT point,
        IReadOnlyDictionary<string, string> parameters)
    {
        var akMarginY = ReadCoordinateDouble(
            parameters,
            0.0,
            "AK_MARGIN_Y");
        return point.DesignY - akMarginY;
    }

    private static double GetDesignXFromAlignKey(
        ST_RECIPE_HOLE_POINT point,
        IReadOnlyDictionary<string, string> parameters)
    {
        var akMarginX = ReadCoordinateDouble(
            parameters,
            0.0,
            "AK_MARGIN_X");
        return point.DesignX - akMarginX;
    }

    private static string FormatCoordinateValue(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private void SetOperationFieldValue(string parameter, string value)
    {
        bool MatchItem51(ST_MONITOR_PARAMETER_ROW item)
        {
            return item.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase);
        }

        var field = OperationFields.FirstOrDefault(MatchItem51);
        if (field is not null)
        {
            field.Value = value;
        }
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> CreatePowerMeterProcess()
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        string SelectProcess52(ST_POWER_METER_PROCESS_DATA process)
        {
            return process.FileName;
        }

        var existing = table.Processes.Select(SelectProcess52).ToArray();
        string HandleProcessName53(string value)
        {
            return ValidatePowerMeterProcessName(value, existing);
        }

        var processName = ShowPowerMeterNameDialog(
            "Create PowerMeter Process",
            "Enter the PowerMeter process file name.",
            "",
HandleProcessName53);

        if (processName is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter process create canceled.");
        }

        try
        {
            await _interfaceManager.CreatePowerMeterData(processName);
            _selectedPowerMeterProcessName = NormalizePowerMeterFileName(processName);
            _selectedPowerMeterStepNo = 1;
            await _refreshCurrentScreen();
            return new ST_DEVICE_COMMAND_RESULT(true, $"PowerMeter process created: {_selectedPowerMeterProcessName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter process create failed. {ex.Message}");
        }
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> DeletePowerMeterProcess()
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        var processName = string.IsNullOrWhiteSpace(_selectedPowerMeterProcessName)
            ? table.SelectedFileName
            : _selectedPowerMeterProcessName;

        if (string.IsNullOrWhiteSpace(processName))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter process delete skipped. No process is selected.");
        }

        if (!ConfirmPowerMeterDelete(processName, "process file"))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter process delete canceled.");
        }

        try
        {
            await _interfaceManager.DeletePowerMeterData(processName);
            _selectedPowerMeterProcessName = "";
            _selectedPowerMeterStepNo = 1;
            await _refreshCurrentScreen();
            return new ST_DEVICE_COMMAND_RESULT(true, $"PowerMeter process deleted: {processName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter process delete failed. {ex.Message}");
        }
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> RenamePowerMeterProcess()
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        var oldName = string.IsNullOrWhiteSpace(_selectedPowerMeterProcessName)
            ? table.SelectedFileName
            : _selectedPowerMeterProcessName;

        if (string.IsNullOrWhiteSpace(oldName))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter process rename skipped. No process is selected.");
        }
        string SelectProcess54(ST_POWER_METER_PROCESS_DATA process)
        {
            return process.FileName;
        }

        var existing = table.Processes.Select(SelectProcess54).ToArray();
        string HandleNewName55(string value)
        {
            return ValidatePowerMeterProcessName(value, existing, oldName);
        }

        var newName = ShowPowerMeterNameDialog(
            "Rename PowerMeter Process",
            "Enter the new PowerMeter process file name.",
            Path.GetFileNameWithoutExtension(oldName),
HandleNewName55);

        if (newName is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter process rename canceled.");
        }

        try
        {
            await _interfaceManager.RenamePowerMeterData(oldName, newName);
            _selectedPowerMeterProcessName = NormalizePowerMeterFileName(newName);
            await _refreshCurrentScreen();
            return new ST_DEVICE_COMMAND_RESULT(true, $"PowerMeter process renamed: {oldName} -> {_selectedPowerMeterProcessName}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter process rename failed. {ex.Message}");
        }
    }

    private async Task SelectPowerMeterProcess(string processName)
    {
        _selectedPowerMeterProcessName = processName;
        _selectedPowerMeterStepNo = 1;
        _setStatusMessage($"PowerMeter process {processName} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectPowerMeterStep(int stepNo)
    {
        _selectedPowerMeterStepNo = stepNo;
        _setStatusMessage($"PowerMeter step {stepNo:000} selected.");
        await _refreshCurrentScreen();
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> CommitCurrentPowerMeterStepEdit(
        bool refreshScreen = false)
    {
        if (PwmSettingRows.Count == 0)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter step save skipped. No editable step is loaded.");
        }

        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        bool MatchStep56(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo == _selectedPowerMeterStepNo;
        }

        var selectedStep = table.Steps.FirstOrDefault(MatchStep56);

        if (selectedStep is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter step save skipped. No step is selected.");
        }

        var editedStep = CreatePowerMeterStepFromSettingRows(selectedStep);
        ST_POWER_METER_STEP_DATA SelectStep57(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo == editedStep.StepNo ? editedStep : step;
        }

        int GetStepSortKey58(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        var steps = table.Steps
            .Select(SelectStep57)
            .OrderBy(GetStepSortKey58)
            .ToArray();

        await _interfaceManager.SavePowerMeterData(table.SelectedFileName, steps);
        if (refreshScreen)
        {
            await _refreshCurrentScreen();
        }

        return new ST_DEVICE_COMMAND_RESULT(true, $"PowerMeter step {editedStep.StepNo:000} updated.");
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> AddPowerMeterStep(bool copySelectedStep)
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        int HandleNextStepNo59(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        var nextStepNo = table.Steps.Count == 0 ? 1 : table.Steps.Max(HandleNextStepNo59) + 1;
        bool MatchStep60(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo == _selectedPowerMeterStepNo;
        }

        var source = table.Steps.FirstOrDefault(MatchStep60) ??
            table.Steps.LastOrDefault();

        if (copySelectedStep && source is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter step copy skipped. No step is selected.");
        }

        var stepSource = copySelectedStep
            ? source!
            : PwmSettingRows.Count > 0
                ? CreatePowerMeterStepFromSettingRows(source ?? CreateDefaultPowerMeterStep(nextStepNo))
                : CreateDefaultPowerMeterStep(nextStepNo);

        var newStep = stepSource with
        {
            StepNo = nextStepNo,
            OptionName = copySelectedStep
                ? $"{stepSource.OptionName}_COPY"
                : stepSource.OptionName,
            MeasurePower = null,
            State = "WAIT"
        };
        int GetStepSortKey61(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        var steps = table.Steps
            .Append(newStep)
            .OrderBy(GetStepSortKey61)
            .ToArray();

        await _interfaceManager.SavePowerMeterData(table.SelectedFileName, steps);
        _selectedPowerMeterStepNo = newStep.StepNo;
        await _refreshCurrentScreen();

        return new ST_DEVICE_COMMAND_RESULT(true, $"PowerMeter step {newStep.StepNo:000} added.");
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> DeleteSelectedPowerMeterStep()
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);
        bool CheckStep62(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo != _selectedPowerMeterStepNo;
        }

        if (table.Steps.All(CheckStep62))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter step delete skipped. No step is selected.");
        }
        bool FilterStep63(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo != _selectedPowerMeterStepNo;
        }

        var steps = RenumberPowerMeterSteps(table.Steps
            .Where(FilterStep63)
            .ToArray());
        await _interfaceManager.SavePowerMeterData(table.SelectedFileName, steps);
        _selectedPowerMeterStepNo = Math.Clamp(_selectedPowerMeterStepNo, 1, Math.Max(1, steps.Count));
        await _refreshCurrentScreen();

        return new ST_DEVICE_COMMAND_RESULT(true, "PowerMeter selected step deleted.");
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> DeleteAllPowerMeterSteps()
    {
        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);

        if (!ConfirmPowerMeterDelete(table.SelectedFileName, "all measure steps"))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter step delete all canceled.");
        }

        await _interfaceManager.SavePowerMeterData(table.SelectedFileName, []);
        _selectedPowerMeterStepNo = 1;
        await _refreshCurrentScreen();

        return new ST_DEVICE_COMMAND_RESULT(true, "PowerMeter all steps deleted.");
    }

    private async Task<ST_DEVICE_COMMAND_RESULT> RunPowerMeterMeasureSequence()
    {
        if (_powerMeterMeasureCts is not null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter measure sequence is already running.");
        }

        if (PwmSettingRows.Count > 0)
        {
            var saveResult = await CommitCurrentPowerMeterStepEdit(refreshScreen: false);
            if (!saveResult.IsSuccess)
            {
                return saveResult;
            }
        }

        var table = await _interfaceManager.LoadPowerMeterData(_selectedPowerMeterProcessName);

        if (table.Steps.Count == 0)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter measure step is empty.");
        }

        _selectedPowerMeterProcessName = table.SelectedFileName;
        var measureCts = new CancellationTokenSource();
        _powerMeterMeasureCts = measureCts;
        var cancellationToken = measureCts.Token;

        try
        {
            var waveLength = ReadPwmSetting("WAVELENGTH", 355.0);
            var waveLengthResult = await _interfaceManager.ExecutePowerMeterCommand(
                EN_POWER_METER_COMMAND.SetWaveLength,
                waveLength,
                cancellationToken);

            if (!waveLengthResult.IsSuccess)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter wavelength set failed. {waveLengthResult.Message}");
            }
            int GetStepSortKey64(ST_POWER_METER_STEP_DATA step)
            {
                return step.StepNo;
            }

            ST_POWER_METER_STEP_DATA SelectStep65(ST_POWER_METER_STEP_DATA step, int index)
            {
                return step with
                {
                    State = index == 0 ? "READY" : "WAIT",
                    MeasurePower = null
                };
            }

            var steps = table.Steps
                .OrderBy(GetStepSortKey64)
                .Select(SelectStep65)
                .ToList();
            var measuredCount = 0;
            var lastPower = 0.0;

            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[stepIndex];
                _selectedPowerMeterStepNo = step.StepNo;

                if (!step.PowerOut)
                {
                    steps[stepIndex] = step with { State = "SKIP" };
                    await SavePowerMeterRunState(table.SelectedFileName, steps, cancellationToken);
                    continue;
                }

                steps[stepIndex] = step with { State = "RUN" };
                await SavePowerMeterRunState(table.SelectedFileName, steps, cancellationToken);
                await DelayPowerMeterStep(step.StartDelayMs, cancellationToken);

                var samples = new List<double>();
                var cycleCount = step.MeasureCycle;
                if (cycleCount <= 0)
                {
                    steps[stepIndex] = step with { State = "ERROR" };
                    await SavePowerMeterRunState(table.SelectedFileName, steps, cancellationToken);
                    return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter measure cycle must be greater than 0 at step {step.StepNo:000}.");
                }

                for (var cycle = 0; cycle < cycleCount; cycle++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DelayPowerMeterStep(step.MeasureTimeMs, cancellationToken);

                    var result = await _interfaceManager.ExecutePowerMeterCommand(
                        EN_POWER_METER_COMMAND.ReadPower,
                        cancellationToken: cancellationToken);

                    if (!result.IsSuccess)
                    {
                        steps[stepIndex] = step with { State = "ERROR" };
                        await SavePowerMeterRunState(table.SelectedFileName, steps, cancellationToken);
                        return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter read failed at step {step.StepNo:000}. {result.Message}");
                    }

                    var status = await _interfaceManager.GetPowerMeterStatus(cancellationToken);
                    samples.Add(status.MeasuredPower);

                    if (cycle + 1 < cycleCount)
                    {
                        await DelayPowerMeterStep(step.MeasureIntervalMs, cancellationToken);
                    }
                }

                lastPower = samples.Count == 0 ? 0.0 : samples.Average();
                measuredCount++;
                steps[stepIndex] = step with
                {
                    MeasurePower = lastPower,
                    State = "OK"
                };
                await SavePowerMeterRunState(table.SelectedFileName, steps, cancellationToken);
                await DelayPowerMeterStep(step.CoolingTimeMs, cancellationToken);
            }

            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"PowerMeter measure completed. Step={measuredCount}, LastPower={lastPower.ToString("F4", CultureInfo.InvariantCulture)} W.");
        }
        catch (OperationCanceledException)
        {
            await _refreshCurrentScreen();
            return new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter measure sequence stopped.");
        }
        finally
        {
            if (ReferenceEquals(_powerMeterMeasureCts, measureCts))
            {
                _powerMeterMeasureCts.Dispose();
                _powerMeterMeasureCts = null;
            }
        }
    }

    private double ReadOperationField(string parameter, double defaultValue)
    {
        bool MatchField66(ST_MONITOR_PARAMETER_ROW field)
        {
            return field.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase);
        }

        var value = OperationFields
            .FirstOrDefault(MatchField66)
            ?.Value;

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : defaultValue;
    }

    private double ReadPwmSetting(string parameter, double defaultValue)
    {
        bool MatchRow67(ST_PWM_SETTING_ROW row)
        {
            return row.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase);
        }

        var value = PwmSettingRows
            .FirstOrDefault(MatchRow67)
            ?.Value;

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : defaultValue;
    }

    private async Task SavePowerMeterRunState(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken)
    {
        await _interfaceManager.SavePowerMeterData(processFile, steps, cancellationToken);
        await _refreshCurrentScreen();
    }

    private static async Task DelayPowerMeterStep(
        int milliseconds,
        CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
    }

    private ST_POWER_METER_STEP_DATA CreatePowerMeterStepFromSettingRows(ST_POWER_METER_STEP_DATA source)
    {
        return source with
        {
            OptionName = ReadPwmText("OPTION NAME", source.OptionName),
            PowerOut = ReadPwmBool("POWER OUT", source.PowerOut),
            PowerUnit = ReadPwmText("POWER UNIT", source.PowerUnit),
            SettingAtt = ReadPwmDouble("SETTING ATT", source.SettingAtt),
            SettingPower = ReadPwmDouble("SETTING POWER", source.SettingPower),
            SettingFreq = ReadPwmDouble("SETTING FREQ", source.SettingFreq),
            MeasureCycle = ReadPwmInt("MEASURE CYCLE", source.MeasureCycle),
            MeasureTimeMs = ReadPwmInt("MEASURE TIME", source.MeasureTimeMs),
            MeasureIntervalMs = ReadPwmInt("MEASURE INTERVAL", source.MeasureIntervalMs),
            StartDelayMs = ReadPwmInt("START DELAY", source.StartDelayMs),
            CoolingTimeMs = ReadPwmInt("COOLING TIME", source.CoolingTimeMs),
            State = string.IsNullOrWhiteSpace(source.State) ? "WAIT" : source.State
        };
    }

    private string ReadPwmText(string parameter, string defaultValue)
    {
        bool MatchRow68(ST_PWM_SETTING_ROW row)
        {
            return row.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase);
        }

        return PwmSettingRows
            .FirstOrDefault(MatchRow68)
            ?.Value
            .Trim() is { Length: > 0 } value
            ? value
            : defaultValue;
    }

    private bool ReadPwmBool(string parameter, bool defaultValue)
    {
        var value = ReadPwmText(parameter, defaultValue ? "ON" : "OFF");
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("USE", StringComparison.OrdinalIgnoreCase);
    }

    private int ReadPwmInt(string parameter, int defaultValue)
    {
        var value = ReadPwmText(parameter, defaultValue.ToString(CultureInfo.InvariantCulture));
        if (int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleResult)
            ? (int)Math.Round(doubleResult, MidpointRounding.AwayFromZero)
            : defaultValue;
    }

    private double ReadPwmDouble(string parameter, double defaultValue)
    {
        var value = ReadPwmText(parameter, defaultValue.ToString(CultureInfo.InvariantCulture));
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static IReadOnlyList<ST_POWER_METER_STEP_DATA> RenumberPowerMeterSteps(
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps)
    {
        int GetStepSortKey69(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        ST_POWER_METER_STEP_DATA SelectStep70(ST_POWER_METER_STEP_DATA step, int index)
        {
            return step with { StepNo = index + 1 };
        }

        return steps
            .OrderBy(GetStepSortKey69)
            .Select(SelectStep70)
            .ToArray();
    }

    private static ST_POWER_METER_STEP_DATA CreateDefaultPowerMeterStep(int stepNo)
    {
        return new ST_POWER_METER_STEP_DATA(
            stepNo,
            $"PWM_CHECK_HEAD{stepNo:00}",
            true,
            "W",
            23.50,
            1.200,
            20.0,
            3,
            1000,
            100,
            500,
            300,
            0.0000,
            null,
            "WAIT");
    }

    private static string? ShowPowerMeterNameDialog(
        string title,
        string message,
        string initialValue,
        Func<string, string>? validate = null)
    {
        var dialog = new CRecipeNameDialog(title, message, initialValue, validate)
        {
            Owner = GetActiveWindow()
        };

        return dialog.ShowDialog() == true
            ? NormalizePowerMeterFileName(dialog.RecipeName)
            : null;
    }

    private static bool ConfirmPowerMeterDelete(
        string name,
        string target)
    {
        var dialog = new CRecipeConfirmDialog(
            "Delete PowerMeter Data",
            $"Delete {target}?\n{name}",
            "DELETE")
        {
            Owner = GetActiveWindow()
        };

        return dialog.ShowDialog() == true;
    }

    private static Window? GetActiveWindow()
    {
        bool MatchWindow71(Window window)
        {
            return window.IsActive;
        }

        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(MatchWindow71);
    }

    private static string NormalizePowerMeterFileName(string value)
    {
        var fileName = Path.GetFileName(value.Trim());

        if (fileName.EndsWith(".pwm", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        return $"{fileName.Trim()}.pwm";
    }

    private static string ValidatePowerMeterProcessName(
        string value,
        IReadOnlyList<string> existingFiles,
        string currentFile = "")
    {
        var normalized = value.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "PowerMeter process name is required.";
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "PowerMeter process name contains invalid file name characters.";
        }

        var fileName = NormalizePowerMeterFileName(normalized);
        bool CheckFile72(string file)
        {
            return file.Equals(fileName, StringComparison.OrdinalIgnoreCase);
        }

        if (!fileName.Equals(currentFile, StringComparison.OrdinalIgnoreCase) &&
            existingFiles.Any(CheckFile72))
        {
            return "PowerMeter process file already exists.";
        }

        return "";
    }

    private static string GetMonitorOperationLabel(object? parameter)
    {
        string EvaluateParameterSwitch19()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_MONITOR_OPERATION_BUTTON button:
                    return string.IsNullOrWhiteSpace(button.CommandKey)
                        ? button.Label
                        : button.CommandKey;
                case ST_MONITOR_BET_TABLE_ROW row:
                    return row.MoveCommandLabel;
                case string text:
                    return text;
                default:
                    return "";
            }
        }

        return EvaluateParameterSwitch19();
    }

    private static string NormalizeMonitorOperation(string label)
    {
        return label.Replace("\r", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\n", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();
    }

    private void Apply(
        IReadOnlyList<ST_SCREEN_SECTION> deviceTabs,
        string selectedTab,
        string title,
        string subtitle,
        string statusPanelTitle,
        string operationPanelTitle,
        string parameterPanelTitle,
        string trendPanelTitle,
        string historyPanelTitle,
        IReadOnlyList<ST_MONITOR_TAB> tabs,
        IReadOnlyList<ST_MONITOR_IO_ROW> inputRows,
        IReadOnlyList<ST_MONITOR_IO_ROW> outputRows,
        IReadOnlyList<ST_MONITOR_AXIS_ROW> axisRows,
        IReadOnlyList<ST_MONITOR_COMMAND_HISTORY_ROW> commandHistoryRows,
        IReadOnlyList<ST_MONITOR_STATUS_ROW> statusRows,
        IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> operationButtons,
        IReadOnlyList<ST_MONITOR_PARAMETER_ROW> operationFields,
        IReadOnlyList<ST_MONITOR_PARAMETER_ROW> parameterRows,
        IReadOnlyList<ST_MONITOR_BET_TABLE_ROW> betTableRows,
        IReadOnlyList<ST_MONITOR_TREND_POINT> trendPoints,
        IReadOnlyList<ST_MONITOR_SUMMARY_ITEM> summaryItems,
        IReadOnlyList<ST_MONITOR_POSITION_ROW> positionRows,
        IReadOnlyList<ST_MONITOR_PRODUCT_ITEM> productItems,
        IReadOnlyList<ST_MONITOR_PRODUCT_HEAD_ROW> productHeadRows,
        IReadOnlyList<ST_MONITOR_PRODUCT_HISTORY_ROW> productHistoryRows,
        IReadOnlyList<ST_PWM_PROCESS_ROW> pwmProcessRows,
        IReadOnlyList<ST_PWM_STEP_ROW> pwmStepRows,
        IReadOnlyList<ST_PWM_SETTING_ROW> pwmSettingRows,
        IReadOnlyList<ST_PWM_DEVICE_ROW> pwmDeviceRows,
        IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> pwmProcessButtons,
        IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> pwmStepButtons,
        IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> pwmRunButtons)
    {
        DeviceTabs = deviceTabs;
        SelectedTab = selectedTab;
        Title = title;
        Subtitle = subtitle;
        StatusPanelTitle = statusPanelTitle;
        OperationPanelTitle = operationPanelTitle;
        ParameterPanelTitle = parameterPanelTitle;
        TrendPanelTitle = trendPanelTitle;
        HistoryPanelTitle = historyPanelTitle;
        Tabs = tabs;
        InputRows = inputRows;
        OutputRows = outputRows;
        AxisRows = axisRows;
        CommandHistoryRows = commandHistoryRows;
        StatusRows = statusRows;
        OperationButtons = operationButtons;
        OperationFields = operationFields;
        ParameterRows = parameterRows;
        BetTableRows = betTableRows;
        TrendPoints = trendPoints;
        SummaryItems = summaryItems;
        PositionRows = positionRows;
        ProductItems = productItems;
        ProductHeadRows = productHeadRows;
        ProductHistoryRows = productHistoryRows;
        PwmProcessRows = pwmProcessRows;
        PwmStepRows = pwmStepRows;
        PwmSettingRows = pwmSettingRows;
        PwmDeviceRows = pwmDeviceRows;
        PwmProcessButtons = pwmProcessButtons;
        PwmStepButtons = pwmStepButtons;
        PwmRunButtons = pwmRunButtons;

        NotifyMonitorScreenProperties();
    }

    private void NotifyMonitorScreenProperties()
    {
        NotifyMonitorLiveProperties(
            nameof(DeviceTabs),
            nameof(SelectedTab),
            nameof(Title),
            nameof(Subtitle),
            nameof(StatusPanelTitle),
            nameof(OperationPanelTitle),
            nameof(ParameterPanelTitle),
            nameof(TrendPanelTitle),
            nameof(HistoryPanelTitle),
            nameof(Tabs),
            nameof(IsIo),
            nameof(IsMotor),
            nameof(IsLaser),
            nameof(IsChiller),
            nameof(IsAttenuator),
            nameof(IsBet),
            nameof(IsPowerMeter),
            nameof(IsProduct),
            nameof(IsMelsec),
            nameof(IsCoordinateViewer),
            nameof(IsGenericDevice),
            nameof(SelectedLaserName),
            nameof(SelectedHeadDeviceName),
            nameof(HeadDeviceSelectorTitle),
            nameof(InputRows),
            nameof(OutputRows),
            nameof(AxisRows),
            nameof(CommandHistoryRows),
            nameof(StatusRows),
            nameof(OperationButtons),
            nameof(OperationFields),
            nameof(HeadSelectRows),
            nameof(LaserControlRows),
            nameof(ParameterRows),
            nameof(BetTableRows),
            nameof(TrendPoints),
            nameof(SummaryItems),
            nameof(PositionRows),
            nameof(ProductItems),
            nameof(ProductHeadRows),
            nameof(ProductHistoryRows),
            nameof(MelsecGroups),
            nameof(MelsecRows),
            nameof(MelsecReadRows),
            nameof(MelsecWriteRows),
            nameof(CoordinateBasisOptions),
            nameof(CoordinateValueRows),
            nameof(CoordinateGlassPreviewImage),
            nameof(CoordinateCellPreviewLabels),
            nameof(CoordinateHoleMatrixRows),
            nameof(CoordinateGlassPreviewSummary),
            nameof(CoordinateIsGlassPreviewVisible),
            nameof(CoordinateIsCellDetailVisible),
            nameof(CoordinateSelectedRecipeName),
            nameof(CoordinateSelectedBasisName),
            nameof(CoordinateBasisDescription),
            nameof(CoordinateSelectedCellName),
            nameof(CoordinateSelectedHoleName),
            nameof(PwmProcessRows),
            nameof(PwmStepRows),
            nameof(PwmSettingRows),
            nameof(PwmDeviceRows),
            nameof(PwmProcessButtons),
            nameof(PwmStepButtons),
            nameof(PwmRunButtons));
    }

    private void NotifyMonitorLiveProperties(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private static IReadOnlyList<ST_SCREEN_SECTION> CreateTabSections(IReadOnlyList<ST_MONITOR_TAB> tabs)
    {
        ST_SCREEN_SECTION SelectTab73(ST_MONITOR_TAB tab)
        {
            return new ST_SCREEN_SECTION(tab.Name, Array.Empty<ST_DISPLAY_ITEM>());
        }

        return tabs
            .Select(SelectTab73)
            .ToArray();
    }

    private static string NormalizeMonitorTab(string? tab)
    {
        var normalized = (tab ?? "IO").Trim().ToUpperInvariant();
        string EvaluateNormalizedSwitch20()
        {
            var switchValue = normalized;
            switch (switchValue)
            {
                case "ATT":
                    return "ATTENUATOR";
                case "POWER" or "POWERMETER" or "POWER_METER":
                    return "POWER METER";
                case "PICO" or "PICOMOTOR" or "PICO_MOTOR":
                    return "PICO MOTOR";
                case "COORDINATE" or "COORDINATE_VIEWER":
                    return "COORDINATE VIEWER";
                case "IO" or "MOTOR" or "LASER" or "CHILLER" or "ATTENUATOR" or "BET" or "POWER METER" or "PICO MOTOR" or "PRODUCT" or "MELSEC" or "COORDINATE VIEWER":
                    return normalized;
                default:
                    return "IO";
            }
        }

        return EvaluateNormalizedSwitch20();
    }

    private static string GetSubtitle(string tab)
    {
        string EvaluateTabSwitch21()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "IO":
                    return "Digital input/output monitor and direct ON/OFF operation";
                case "MOTOR":
                    return "Axis position monitor and motor service operation";
                case "LASER":
                    return "Talon laser status monitor and laser service operation";
                case "CHILLER":
                    return "Chiller status monitor and service operation";
                case "ATTENUATOR":
                    return "Conex AGP attenuator position monitor and service operation";
                case "BET":
                    return "Beam expander magnification and divergence monitor";
                case "POWER METER":
                    return "Power meter measurement monitor and Stage PC measurement-position command";
                case "PICO MOTOR":
                    return "PicoMotor controller position and service operation";
                case "PRODUCT":
                    return "Active product status, head result, and product history monitor";
                case "MELSEC":
                    return "PLC MELSEC map monitor and direct read/write operation";
                case "COORDINATE VIEWER":
                    return "Coordinate system viewer";
                default:
                    return "Auxiliary monitor status and service information";
            }
        }

        return EvaluateTabSwitch21();
    }

    private static string GetStatusPanelTitle(string tab)
    {
        string EvaluateTabSwitch22()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "LASER":
                    return "Laser Status";
                case "CHILLER":
                    return "Chiller Status";
                case "ATTENUATOR":
                    return "Attenuator Status";
                case "BET":
                    return "BET Status";
                case "POWER METER":
                    return "PowerMeter Status";
                case "PICO MOTOR":
                    return "PicoMotor Status";
                case "PRODUCT":
                    return "Product Status";
                case "MELSEC":
                    return "MELSEC Status";
                default:
                    return "Monitor Status";
            }
        }

        return EvaluateTabSwitch22();
    }

    private static string GetOperationPanelTitle(string tab)
    {
        string EvaluateTabSwitch23()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return "Axis Operation";
                case "LASER":
                    return "Laser Operation";
                case "CHILLER":
                    return "Chiller Operation";
                case "ATTENUATOR":
                    return "Attenuator Operation";
                case "BET":
                    return "BET Operation";
                case "POWER METER":
                    return "PowerMeter Operation";
                case "PICO MOTOR":
                    return "PicoMotor Operation";
                case "MELSEC":
                    return "MELSEC Read / Write";
                default:
                    return "Monitor Operation";
            }
        }

        return EvaluateTabSwitch23();
    }

    private static string GetParameterPanelTitle(string tab)
    {
        string EvaluateTabSwitch24()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return "Motor Parameter";
                case "LASER":
                    return "Laser Parameter";
                case "CHILLER":
                    return "Chiller Parameter";
                case "ATTENUATOR":
                    return "Attenuator Parameter";
                case "BET":
                    return "BET Table";
                case "POWER METER":
                    return "PowerMeter Parameter";
                case "PICO MOTOR":
                    return "PicoMotor Position";
                case "PRODUCT":
                    return "Head Result";
                case "MELSEC":
                    return "MELSEC Map";
                default:
                    return "Monitor Parameter";
            }
        }

        return EvaluateTabSwitch24();
    }

    private static string GetTrendPanelTitle(string tab)
    {
        string EvaluateTabSwitch25()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "LASER":
                    return "Laser Trend";
                case "CHILLER":
                    return "Temperature / Flow Trend";
                case "ATTENUATOR":
                    return "Current Position";
                case "BET":
                    return "Beam Expander Position";
                case "POWER METER":
                    return "Power Trend";
                case "PICO MOTOR":
                    return "Motor Position";
                case "MELSEC":
                    return "MELSEC Signal";
                default:
                    return "Signal Trend";
            }
        }

        return EvaluateTabSwitch25();
    }

    private static string GetHistoryPanelTitle(string tab)
    {
        string EvaluateTabSwitch26()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return "Motor Command History";
                case "LASER":
                    return "Laser Command History";
                case "CHILLER":
                    return "Chiller Command History";
                case "ATTENUATOR":
                    return "Attenuator Command History";
                case "BET":
                    return "BET Command History";
                case "POWER METER":
                    return "PowerMeter Command History";
                case "PICO MOTOR":
                    return "PicoMotor Command History";
                case "PRODUCT":
                    return "Product History";
                case "MELSEC":
                    return "MELSEC Command History";
                default:
                    return "Command History";
            }
        }

        return EvaluateTabSwitch26();
    }

    private static IReadOnlyList<ST_MONITOR_IO_ROW> CreateInputRows(ST_DEVICE_STATUS snapshot)
    {
        bool FilterChannel74(ST_IO_STATUS channel)
        {
            return !channel.IsOutput;
        }

        ST_MONITOR_IO_ROW SelectChannel75(ST_IO_STATUS channel)
        {
            return new ST_MONITOR_IO_ROW(
                            channel.Id,
                            channel.Address,
                            channel.Name,
                            OnOffText(channel.IsOn),
                            "",
                            "",
                            "");
        }

        return snapshot.Io
            .Where(FilterChannel74)
            .Select(SelectChannel75)
            .ToArray();
    }

    private static IReadOnlyList<ST_MONITOR_IO_ROW> CreateOutputRows(ST_DEVICE_STATUS snapshot)
    {
        bool FilterChannel76(ST_IO_STATUS channel)
        {
            return channel.IsOutput;
        }

        ST_MONITOR_IO_ROW SelectChannel77(ST_IO_STATUS channel, int index)
        {
            return new ST_MONITOR_IO_ROW(
                            channel.Id,
                            channel.Address,
                            channel.Name,
                            OnOffText(channel.IsOn),
                            "",
                            "",
                            "",
                            index == 0);
        }

        return snapshot.Io
            .Where(FilterChannel76)
            .Select(SelectChannel77)
            .ToArray();
    }

    private static IReadOnlyList<ST_MONITOR_AXIS_ROW> CreateAxisRows(
        ST_DEVICE_STATUS snapshot,
        string selectedAxisId)
    {
        ST_MONITOR_AXIS_ROW SelectAxis78(ST_MOTOR_AXIS_STATUS axis)
        {
            return new ST_MONITOR_AXIS_ROW(
                            axis.AxisId,
                            axis.Name,
                            FormatAxisPosition(axis.AxisId, axis.CurrentPosition),
                            FormatAxisPosition(axis.AxisId, axis.TargetPosition),
                            FormatAxisPosition(axis.AxisId, axis.CommandPosition),
                            OnOffText(axis.ServoOn),
                            axis.HomeCompleted ? "YES" : "NO",
                            axis.LimitPlusOn ? "ON" : "OK",
                            axis.LimitMinusOn ? "ON" : "OK",
                            axis.AlarmOn ? "ALARM" : "-",
                            axis.AlarmOn ? "ALARM" : "READY",
                            axis.AxisId.Equals(selectedAxisId, StringComparison.OrdinalIgnoreCase));
        }

        return snapshot.Motors
            .Select(SelectAxis78)
            .ToArray();
    }

    private static IReadOnlyList<ST_MONITOR_STATUS_ROW> CreateStatusRows(
        string tab,
        ST_DEVICE_STATUS snapshot,
        IReadOnlyList<ST_DEVICE_COMM_STATUS> communication,
        string? selectedConnectionText = null)
    {
        var module = GetMonitorModule(tab);
        var communicationText = !string.IsNullOrWhiteSpace(selectedConnectionText)
            ? selectedConnectionText
            : module is null
            ? "-"
            : ToCommunicationText(GetModuleState(communication, module.Value));
        IReadOnlyList<ST_MONITOR_STATUS_ROW> EvaluateTabSwitch27()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "LASER":
                    return [
                        new("Output Power", snapshot.Laser.OutputPower.ToString("F3"), snapshot.Laser.PowerOn ? "ON" : "SAFE", "W", "Measured output power"),
                new("Set Power", "1.200", "-", "W", "Target output power"),
                new("Frequency", "20.000", "-", "kHz", "Pulse frequency"),
                new("Gate", snapshot.Laser.GateOn ? "OPEN" : "CLOSE", snapshot.Laser.GateOn ? "OPEN" : "CLOSE", "-", "Laser gate state"),
                new("Shutter", snapshot.Laser.ShutterOpen ? "OPEN" : "CLOSE", snapshot.Laser.ShutterOpen ? "OPEN" : "CLOSE", "-", "Laser shutter state"),
                new("Laser Mode", snapshot.Laser.PowerOn ? "ON" : "SAFE", snapshot.Laser.PowerOn ? "ON" : "SAFE", "-", "Laser operating mode"),
                new("Diode State", "OK", "OK", "-", "Diode status"),
                new("Temperature", "24.6", "OK", "C", "Laser head temperature")
                    ];
                case "CHILLER":
                    return [
                        new("Connection", communicationText, communicationText, "-", "Controller connection"),
                new("Run State", snapshot.Chiller.RunState, snapshot.Chiller.Running ? "RUN" : snapshot.Chiller.RunState, "-", "Run mode"),
                new("Cur Temp", snapshot.Chiller.Temperature.ToString("F1"), snapshot.Chiller.AlarmOn ? "ALARM" : "NORMAL", "C", "Current water temperature"),
                new("Set Temp", snapshot.Chiller.SetTemperature.ToString("F1"), "SET", "C", "Target water temperature"),
                new("Alarm Code", string.IsNullOrWhiteSpace(snapshot.Chiller.AlarmCode) ? "0" : snapshot.Chiller.AlarmCode, snapshot.Chiller.AlarmOn ? "ALARM" : "NORMAL", "-", "Active alarm code")
                    ];
                case "ATTENUATOR":
                    return [
                        new("Connection", communicationText, communicationText, "-", "Controller connection state"),
                new("Controller", "CONEX_AGP", "OK", "-", "Attenuator controller"),
                new("Current Position", snapshot.Attenuator.CurrentPosition.ToString("F3"), "OK", "DEG", "Current attenuator position"),
                new("Target Position", snapshot.Attenuator.TargetPosition.ToString("F3"), "OK", "DEG", "Target attenuator position"),
                new("Moving", snapshot.Attenuator.CommandState, snapshot.Attenuator.CommandState, "-", "Motion state"),
                new("In Position", IsInPosition(snapshot.Attenuator.CurrentPosition, snapshot.Attenuator.TargetPosition) ? "YES" : "NO", IsInPosition(snapshot.Attenuator.CurrentPosition, snapshot.Attenuator.TargetPosition) ? "YES" : "WARN", "-", "In position status"),
                new("Home State", "DONE", "DONE", "-", "Home completion status"),
                new("Positive Limit", "OFF", "OFF", "-", "Positive limit sensor"),
                new("Negative Limit", "OFF", "OFF", "-", "Negative limit sensor"),
                new("Alarm Code", "0", "OK", "-", "Current alarm code"),
                new("Last Command", snapshot.Attenuator.CommandState, "OK", "-", "Last command name"),
                new("Communication State", communicationText, communicationText, "-", "Communication status")
                    ];
                case "BET":
                    return [
                        new("Connection", communicationText, communicationText, "-", "Beam expander controller link"),
                new("Controller", "BET_CTRL", "OK", "-", "Beam expander controller"),
                new("MAG POS", snapshot.Bet.CurrentMagnification.ToString("F3"), "", "step", "Current MAG motor position"),
                new("DIV POS", snapshot.Bet.CurrentDivergence.ToString("F3"), "", "step", "Current DIV motor position"),
                new("Target MAG POS", snapshot.Bet.TargetMagnification.ToString("F3"), "", "step", "Target MAG motor position"),
                new("Target DIV POS", snapshot.Bet.TargetDivergence.ToString("F3"), "", "step", "Target DIV motor position"),
                new("Moving", snapshot.Bet.IsMoving ? "MOVING" : "IDLE", snapshot.Bet.IsMoving ? "MOVING" : "IDLE", "-", "Motion state"),
                new("Mag Home", snapshot.Bet.MagHomeCompleted ? "DONE" : "NO", snapshot.Bet.MagHomeCompleted ? "DONE" : "WARN", "-", "Magnification home state"),
                new("Div Home", snapshot.Bet.DivHomeCompleted ? "DONE" : "NO", snapshot.Bet.DivHomeCompleted ? "DONE" : "WARN", "-", "Divergence home state"),
                new("Alarm Code", snapshot.Bet.AlarmOn ? "1" : "0", snapshot.Bet.AlarmOn ? "ALARM" : "OK", "-", "Current alarm code"),
                new("Last Command", string.IsNullOrWhiteSpace(snapshot.Bet.LastCommand) ? "-" : snapshot.Bet.LastCommand, "", "-", "Last command name"),
                new("Communication State", communicationText, communicationText, "-", "Communication status")
                    ];
                case "POWER METER":
                    return [
                        new("Connection", communicationText, communicationText, "-", "Power meter controller connection"),
                new("Model", snapshot.PowerMeter.ModelName, string.IsNullOrWhiteSpace(snapshot.PowerMeter.ModelName) ? "WARN" : "OK", "-", "Power meter model"),
                new("Serial No", snapshot.PowerMeter.SerialNumber, string.IsNullOrWhiteSpace(snapshot.PowerMeter.SerialNumber) || snapshot.PowerMeter.SerialNumber == "-" ? "WARN" : "OK", "-", "Power meter serial number"),
                new("Current Power", snapshot.PowerMeter.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture), "OK", snapshot.PowerMeter.Unit, "Latest measured power"),
                new("Average Power", snapshot.PowerMeter.AveragePower.ToString("F4", CultureInfo.InvariantCulture), "OK", snapshot.PowerMeter.Unit, "Average measured power"),
                new("Min Power", snapshot.PowerMeter.MinPower.ToString("F4", CultureInfo.InvariantCulture), "OK", snapshot.PowerMeter.Unit, "Minimum measured power"),
                new("Max Power", snapshot.PowerMeter.MaxPower.ToString("F4", CultureInfo.InvariantCulture), "OK", snapshot.PowerMeter.Unit, "Maximum measured power"),
                new("WaveLength", snapshot.PowerMeter.WaveLengthNm.ToString("F1", CultureInfo.InvariantCulture), "OK", "nm", "Sensor wavelength setting"),
                new("Beam Pos X", snapshot.PowerMeter.BeamPositionX.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Measured beam X position"),
                new("Beam Pos Y", snapshot.PowerMeter.BeamPositionY.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Measured beam Y position"),
                new("Sample Count", snapshot.PowerMeter.SampleCount.ToString(CultureInfo.InvariantCulture), "OK", "ea", "Accumulated sample count"),
                new("Measure State", snapshot.PowerMeter.IsMeasuring ? "RUN" : "IDLE", snapshot.PowerMeter.IsMeasuring ? "RUN" : "IDLE", "-", "Measurement state"),
                new("Last Command", string.IsNullOrWhiteSpace(snapshot.PowerMeter.LastCommand) ? "-" : snapshot.PowerMeter.LastCommand, snapshot.PowerMeter.LastError == EN_POWER_METER_ERROR.Ok ? "OK" : "ERROR", "-", "Last command name")
                    ];
                case "MELSEC":
                    return [
                        new("Connection", communicationText, communicationText, "-", "MELSEC PLC connection"),
                new("Device", "MELSEC_0", communicationText, "-", "Selected MELSEC device"),
                new("Map Source", "JHMI_MELSEC_MAP.csv", "OK", "-", "MELSEC map file"),
                new("Read", "ID based", "READY", "-", "Read by map ID"),
                new("Write", "ACCESS W/RW", "READY", "-", "Write is enabled only for writable rows")
                    ];
                default:
                    return [
                        new("System Mode", "SIM", "SIM", "-", "Device-free monitor mode"),
                new("Update Rate", "500", "OK", "ms", "Monitor refresh interval"),
                new("Data Source", "Simulation", "OK", "-", "Current data provider"),
                new("Operator", "Engineer", "OK", "-", "Current user level")
                    ];
            }
        }

        return EvaluateTabSwitch27();
    }

    private static EN_EQP_MODULE? GetMonitorModule(string tab)
    {
        EN_EQP_MODULE? EvaluateTabSwitch28()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return EN_EQP_MODULE.Motion;
                case "LASER":
                    return EN_EQP_MODULE.TalonLaser;
                case "CHILLER":
                    return EN_EQP_MODULE.Chiller;
                case "ATTENUATOR":
                    return EN_EQP_MODULE.Attenuator;
                case "BET":
                    return EN_EQP_MODULE.Bet;
                case "POWER METER":
                    return EN_EQP_MODULE.PowerMeter;
                case "PICO MOTOR":
                    return EN_EQP_MODULE.PicoMotor;
                case "MELSEC":
                    return EN_EQP_MODULE.Melsec;
                default:
                    return null;
            }
        }

        return EvaluateTabSwitch28();
    }

    private static EN_COMM_STATE GetModuleState(
        IReadOnlyList<ST_DEVICE_COMM_STATUS> communication,
        EN_EQP_MODULE module)
    {
        bool MatchStatus79(ST_DEVICE_COMM_STATUS status)
        {
            return status.Module == module;
        }

        return communication.FirstOrDefault(MatchStatus79)?.ConnectionState
            ?? EN_COMM_STATE.Offline;
    }

    private static string ToCommunicationText(EN_COMM_STATE state)
    {
        string EvaluateStateSwitch29()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_COMM_STATE.Online:
                    return "ONLINE";
                case EN_COMM_STATE.Simulation:
                    return "SIMULATION";
                default:
                    return "OFFLINE";
            }
        }

        return EvaluateStateSwitch29();
    }

    private static IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreateOperationButtons(
        string tab,
        ST_LASER_STATUS laserStatus)
    {
        IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> EvaluateTabSwitch30()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return [
                        new("SERVO ON", "Servo", "Green"),
                new("SERVO OFF", "Servo", "Dark"),
                new("HOME", "Home", "Blue"),
                new("ABS MOVE", "Abs", "Blue"),
                new("REL MOVE", "Rel", "Dark"),
                new("STOP", "Stop", "Red"),
                new("RESET ALARM", "Alarm", "Dark"),
                new("REFRESH", "Refresh", "Dark")
                    ];
                case "LASER":
                    return CreateLaserOperationButtons(laserStatus);
                case "CHILLER":
                    return [
                        new("RUN", "Run", "Green"),
                new("STOP", "Stop", "Red"),
                new("PUMP ONLY", "Pump", "Blue")
                    ];
                case "ATTENUATOR":
                    return [
                        new("MOVE ABS", "Move", "Blue"),
                new("MOVE REL", "Move", "Blue"),
                new("HOME", "Home", "Blue"),
                new("STOP", "Stop", "Red"),
                new("RESET ALARM", "Alarm", "Dark")
                    ];
                case "BET":
                    return [
                        new("HOME", "Home", "Blue"),
                new("STOP", "Stop", "Red"),
                new("RESET ALARM", "Alarm", "Dark")
                    ];
                default:
                    return [
                        new("REFRESH", "Refresh", "Blue"),
                new("RESET", "Reset", "Dark")
                    ];
            }
        }

        return EvaluateTabSwitch30();
    }

    private static IReadOnlyList<ST_MONITOR_PARAMETER_ROW> CreateParameterRows(string tab)
    {
        IReadOnlyList<ST_MONITOR_PARAMETER_ROW> EvaluateTabSwitch31()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return [
                        new("Home Speed", "100.000", "mm/sec", "Warn"),
                new("Move Speed", "300.000", "mm/sec", "Warn"),
                new("Accel", "500.000", "mm/sec2", "Warn"),
                new("Decel", "500.000", "mm/sec2", "Warn"),
                new("In Position Range", "0.010", "mm"),
                new("Home Offset", "0.000", "mm", "Warn"),
                new("Positive Limit", "120.000", "mm", "Warn"),
                new("Negative Limit", "-120.000", "mm"),
                new("On Delay", "10", "ms"),
                new("Off Delay", "10", "ms")
                    ];
                case "LASER":
                    return [
                        new("Laser Power", "1.200", "W", "Warn"),
                new("Frequency", "20.000", "kHz", "Warn"),
                new("Mark Speed", "900", "mm/s", "Warn"),
                new("Jump Speed", "1500", "mm/s"),
                new("Laser On Delay", "8", "us", "Warn"),
                new("Laser Off Delay", "12", "us", "Warn"),
                new("Shot Count", "48000", "count"),
                new("Time Mode", "10", "ms"),
                new("Count Mode", "48000", "count")
                    ];
                case "CHILLER":
                    return [
                        new("Set Temperature", "22.0", "C", "Warn")
                    ];
                case "ATTENUATOR":
                    return [
                        new("Process Target Position", "55.000", "DEG", "Warn"),
                new("Position Tolerance", "0.100", "DEG", "Warn"),
                new("Negative Limit", "-120.000", "DEG"),
                new("Positive Limit", "360.000", "DEG"),
                new("Move Timeout", "30.000", "sec", "Warn")
                    ];
                case "BET":
                    return [
                        new("Default Magnification", "1.000", "x", "Warn"),
                new("Default Divergence", "1.000", "x", "Warn"),
                new("Mag Move Speed", "0.250", "x/sec", "Warn"),
                new("Div Move Speed", "0.250", "x/sec", "Warn"),
                new("Mag Tolerance", "0.001", "x"),
                new("Div Tolerance", "0.001", "x"),
                new("Positive Limit", "4.000", "x"),
                new("Negative Limit", "0.250", "x"),
                new("Move Timeout", "20.000", "sec", "Warn"),
                new("On Delay", "20", "ms"),
                new("Off Delay", "20", "ms")
                    ];
                case "POWER METER":
                    return [
                        new("WaveLength", "355.0", "nm", "Warn"),
                new("Power High Limit", "1.5000", "W", "Warn"),
                new("Power Low Limit", "0.8000", "W", "Warn"),
                new("Average Count", "10", "count", "Warn"),
                new("Measure Time", "1000", "ms"),
                new("Measure Interval", "100", "ms"),
                new("Stage Move Timeout", "30.000", "sec", "Warn"),
                new("Stage X Target", "0.000", "mm"),
                new("Stage Y Target", "0.000", "mm"),
                new("Stage Z Target", "0.000", "mm"),
                new("Read Command", "pw?", "-"),
                new("Position Command", "pos", "-")
                    ];
                case "MELSEC":
                    return [
                        new("Map File", "JHMI_MELSEC_MAP.csv", "-"),
                new("Call Rule", "ID", "-", "Accent"),
                new("Bit Write", "ON/OFF or 1/0", "-"),
                new("Numeric Write", "Invariant Number", "-"),
                new("Read Access", "R / RW", "-"),
                new("Write Access", "W / RW", "-"),
                new("Scale", "CSV SCALE", "-"),
                new("Device No", "MELSEC instance number", "-")
                    ];
                default:
                    return [
                        new("Refresh Interval", "500", "ms", "Warn"),
                new("Log Retention", "30", "day")
                    ];
            }
        }

        return EvaluateTabSwitch31();
    }

    private IReadOnlyList<ST_MONITOR_PARAMETER_ROW> CreateOperationFields(
        string tab,
        ST_PICO_MOTOR_STATUS picoStatus)
    {
        IReadOnlyList<ST_MONITOR_PARAMETER_ROW> EvaluateTabSwitch32()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return [
                        CreateOperationField(tab, "Target Position", "12.340", ""),
                CreateOperationField(tab, "Relative Distance", "0.000", ""),
                CreateOperationField(tab, "Speed", "300.000", ""),
                CreateOperationField(tab, "Accel", "500.000", ""),
                CreateOperationField(tab, "Decel", "500.000", "")
                    ];
                case "LASER":
                    return [
                        CreateOperationField(tab, "QSW", "20000", "Hz"),
                CreateOperationField(tab, "EPRF", "20000", "Hz"),
                CreateOperationField(tab, "SHG Count", "0", "count"),
                CreateOperationField(tab, "Q Mode", "0", "-")
                    ];
                case "CHILLER":
                    return [
                        CreateOperationField(tab, "Set Temperature", "22.0", "C")
                    ];
                case "ATTENUATOR":
                    return [
                        CreateOperationField(tab, "Target Position", "55.000", "DEG"),
                CreateOperationField(tab, "Relative Move", "0.000", "DEG")
                    ];
                case "BET":
                    return [
                        CreateOperationField(tab, "Target MAG POS", "1020.000", "step"),
                CreateOperationField(tab, "Target DIV POS", "1626.000", "step")
                    ];
                case "POWER METER":
                    return [
                        CreateOperationField(tab, "WaveLength", "355.0", "nm"),
                CreateOperationField(tab, "Stage X", "0.000", "mm"),
                CreateOperationField(tab, "Stage Y", "0.000", "mm"),
                CreateOperationField(tab, "Stage Z", "0.000", "mm"),
                CreateOperationField(tab, "Measure Time", "1000", "ms"),
                CreateOperationField(tab, "Sample Count", "10", "ea")
                    ];
                case "PICO MOTOR":
                    return [
                        new("Current Velocity", picoStatus.CurrentVelocity.ToString("F2", CultureInfo.InvariantCulture), "mm/sec"),
                CreateOperationField(tab, "Set Velocity", "0.00", "mm/sec"),
                new("Current Acceleration", picoStatus.CurrentAcceleration.ToString("F2", CultureInfo.InvariantCulture), "msec"),
                CreateOperationField(tab, "Set Acceleration", "0.00", "msec"),
                new("Home Position", picoStatus.HomePosition.ToString(CultureInfo.InvariantCulture), "step"),
                CreateOperationField(tab, "Relative Move", "0.000", "mm"),
                CreateOperationField(tab, "Absolute Move", "0.000", "mm"),
                new("Cur Count", picoStatus.AllMoveCurrentCount.ToString(CultureInfo.InvariantCulture), "count"),
                CreateOperationField(tab, "Set Count", "1", "count"),
                CreateOperationField(tab, "Position", "0.000", "mm")
                    ];
                default:
                    return [
                        CreateOperationField(tab, "Refresh Interval", "500", "ms"),
                CreateOperationField(tab, "Timeout", "3000", "ms")
                    ];
            }
        }

        return EvaluateTabSwitch32();
    }

    private static IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreateLaserOperationButtons(ST_LASER_STATUS status)
    {
        return
        [
            new("LASER ON", "", status.PowerOn ? "Green" : "Dark"),
            new("SHUTTER OPEN", "", status.ShutterOpen ? "Green" : "Dark"),
            new("GATE ON", "", status.GateOn ? "Green" : "Dark"),
            new("LASER OFF", "", status.PowerOn ? "Dark" : "Red"),
            new("SHUTTER CLOSE", "", status.ShutterOpen ? "Dark" : "Red"),
            new("GATE OFF", "", status.GateOn ? "Dark" : "Red")
        ];
    }

    private IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreatePicoMotorOperationButtons()
    {
        return
        [
            .. PicoConnectionButtons,
            .. PicoMotorSelectButtons,
            new("SET VEL", "Save", "Blue"), new("SET ACC", "Save", "Blue"),
            new("STOP MOTION", "Stop", "Red"), new("HOME", "Home", "Blue"),
            new("ABS MOVE", "Abs", "Blue"), new("JOG -", "Rel", "Dark"),
            new("JOG +", "Rel", "Dark"), new("REL -", "Rel", "Dark"),
            new("REL +", "Rel", "Blue"),
            .. PicoAllMotorSelectButtons,
            new("START", "Run", "Green"), new("STOP", "Stop", "Red"),
            new("REFRESH", "Refresh", "Dark")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_STATUS_ROW> CreatePicoMotorStatusRows(ST_PICO_MOTOR_STATUS status)
    {
        return
        [
            new("Connection", status.IsConnected ? "CONNECTED" : "DISCONNECTED", status.IsConnected ? "ONLINE" : "OFFLINE", "-", "PicoMotor controller connection"),
            new("Controller", status.Controller, status.CommOk ? "OK" : "ERROR", "-", "Controller identification"),
            new("Motor 1 Position", status.Motor1Position.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Motor 1 position"),
            new("Motor 2 Position", status.Motor2Position.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Motor 2 position"),
            new("Motor 3 Position", status.Motor3Position.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Motor 3 position"),
            new("Motor 4 Position", status.Motor4Position.ToString("F3", CultureInfo.InvariantCulture), "OK", "mm", "Motor 4 position"),
            new("Current Velocity", status.CurrentVelocity.ToString("F2", CultureInfo.InvariantCulture), "OK", "mm/sec", "Current velocity"),
            new("Current Acceleration", status.CurrentAcceleration.ToString("F2", CultureInfo.InvariantCulture), "OK", "msec", "Current acceleration"),
            new("Motion State", status.MotionState, status.MotionState, "-", "Motion state"),
            new("Error Code", status.ErrorCode.ToString(CultureInfo.InvariantCulture), status.ErrorCode == 0 ? "OK" : "ERROR", "-", status.LastError.ToString())
        ];
    }

    private static IReadOnlyList<ST_MONITOR_PARAMETER_ROW> CreatePicoErrorRows()
    {
        return
        [
            new("0", "YASKAWA_ERROR_NO_ERROR", "-", "Ok"),
            new("1", "YASKAWA_ERROR_OVER_TEMP", "-", "Normal"),
            new("2", "YASKAWA_ERROR_COMMAND_NOT_EXIST", "-", "Normal"),
            new("3", "YASKAWA_ERROR_PARAMETER_OUT_OF_RANGE", "-", "Normal"),
            new("4", "YASKAWA_ERROR_AXIS_NO_OUT_OF_RANGE", "-", "Normal"),
            new("5", "YASKAWA_ERROR_EEPROM_WRITE_FAIL", "-", "Normal"),
            new("6", "YASKAWA_ERROR_EEPROM_READ_FAIL", "-", "Normal"),
            new("7", "YASKAWA_ERROR_AXIS_NO_MISSING", "-", "Normal")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_PARAMETER_ROW> CreatePicoMotorParameterRows(ST_PICO_MOTOR_STATUS status)
    {
        return
        [
            new("Velocity", status.CurrentVelocity.ToString("F6", CultureInfo.InvariantCulture), "mm/s"),
            new("Acceleration", status.CurrentAcceleration.ToString("F6", CultureInfo.InvariantCulture), "mm/s2"),
            new("Home Position", CPicoMotor.StepToMillimeter(status.HomePosition).ToString("F6", CultureInfo.InvariantCulture), "mm"),
            new("Step Scale", CPicoMotor.StepPerMillimeter.ToString(CultureInfo.InvariantCulture), "step/mm")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_POSITION_ROW> CreatePicoMotorPositionRows(ST_PICO_MOTOR_STATUS status)
    {
        return
        [
            new("Motor 1", status.Motor1Position.ToString("F6", CultureInfo.InvariantCulture), "mm", status.SelectedMotorNo == 1 ? "Accent" : "Ok"),
            new("Motor 2", status.Motor2Position.ToString("F6", CultureInfo.InvariantCulture), "mm", status.SelectedMotorNo == 2 ? "Accent" : "Ok"),
            new("Motor 3", status.Motor3Position.ToString("F6", CultureInfo.InvariantCulture), "mm", status.SelectedMotorNo == 3 ? "Accent" : "Ok"),
            new("Motor 4", status.Motor4Position.ToString("F6", CultureInfo.InvariantCulture), "mm", status.SelectedMotorNo == 4 ? "Accent" : "Ok")
        ];
    }

    private ST_MONITOR_PARAMETER_ROW CreateOperationField(
        string tab,
        string parameter,
        string defaultValue,
        string unit)
    {
        return new ST_MONITOR_PARAMETER_ROW(
            parameter,
            _operationFieldValues.TryGetValue(CreateOperationFieldKey(tab, parameter), out var value)
                ? value
                : defaultValue,
            unit);
    }

    private void SaveOperationFieldValues(string tab)
    {
        foreach (var field in OperationFields)
        {
            _operationFieldValues[CreateOperationFieldKey(tab, field.Parameter)] = field.Value;
        }
    }

    private static string CreateOperationFieldKey(string tab, string parameter)
    {
        return $"{NormalizeMonitorTab(tab)}::{parameter.Trim().ToUpperInvariant()}";
    }

    private int GetSelectedHeadNumber(string tab)
    {
        int EvaluateValueSwitch33()
        {
            var switchValue = NormalizeMonitorTab(tab);
            switch (switchValue)
            {
                case "ATTENUATOR":
                    return _selectedAttenuatorNumber;
                case "BET":
                    return _selectedBetNumber;
                default:
                    return _selectedLaserNumber;
            }
        }

        return EvaluateValueSwitch33();
    }

    private ST_INTERFACE_DATA? GetSelectedHeadInterfaceData(string tab)
    {
        ST_INTERFACE_DATA? EvaluateValueSwitch34()
        {
            var switchValue = NormalizeMonitorTab(tab);
            switch (switchValue)
            {
                case "LASER":
                    return _interfaceManager.GetInterfaceData(EN_EQP_MODULE.TalonLaser, _selectedLaserNumber);
                case "ATTENUATOR":
                    return _interfaceManager.GetInterfaceData(EN_EQP_MODULE.Attenuator, _selectedAttenuatorNumber);
                case "BET":
                    return _interfaceManager.GetInterfaceData(EN_EQP_MODULE.Bet, _selectedBetNumber);
                default:
                    return null;
            }
        }

        return EvaluateValueSwitch34();
    }

    private string? GetSelectedHeadConnectionText(string tab)
    {
        var module = GetHeadDeviceModule(tab);

        if (module is null)
        {
            return null;
        }

        var number = GetSelectedHeadNumber(tab);
        bool MatchItem80(ST_INTERFACE_COMM_STATUS item)
        {
            return item.Number == number;
        }

        var status = _interfaceManager
            .GetInterfaceCommunicationList(module.Value)
            .FirstOrDefault(MatchItem80);

        return status is null
            ? "OFFLINE"
            : ToCommunicationText(status.ConnectionState);
    }

    private static EN_EQP_MODULE? GetHeadDeviceModule(string tab)
    {
        EN_EQP_MODULE? EvaluateValueSwitch35()
        {
            var switchValue = NormalizeMonitorTab(tab);
            switch (switchValue)
            {
                case "LASER":
                    return EN_EQP_MODULE.TalonLaser;
                case "ATTENUATOR":
                    return EN_EQP_MODULE.Attenuator;
                case "BET":
                    return EN_EQP_MODULE.Bet;
                default:
                    return null;
            }
        }

        return EvaluateValueSwitch35();
    }

    private static string GetHeadDeviceNickPrefix(string tab)
    {
        string EvaluateValueSwitch36()
        {
            var switchValue = NormalizeMonitorTab(tab);
            switch (switchValue)
            {
                case "ATTENUATOR":
                    return "ATT";
                case "BET":
                    return "BET";
                default:
                    return "TALON";
            }
        }

        return EvaluateValueSwitch36();
    }

    private IReadOnlyList<ST_MONITOR_HEAD_SELECT_ROW> CreateHeadSelectRows(
        string tab,
        int selectedHeadNumber,
        EN_EQP_MODULE? module,
        string nickPrefix)
    {
        if (module is null)
        {
            return [];
        }
        int HandleCommunicationMap81(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Number;
        }

        ST_INTERFACE_COMM_STATUS HandleCommunicationMap82(ST_INTERFACE_COMM_STATUS status)
        {
            return status;
        }

        var communicationMap = _interfaceManager
            .GetInterfaceCommunicationList(module.Value)
            .ToDictionary(
HandleCommunicationMap81,
HandleCommunicationMap82);
        ST_MONITOR_HEAD_SELECT_ROW SelectNumber83(int number)
        {
            var registered = communicationMap.TryGetValue(number, out var status);
            var state = registered
                ? ToCommunicationText(status!.ConnectionState)
                : "OFFLINE";
            var nickName = registered && !string.IsNullOrWhiteSpace(status!.NickName)
                ? status.NickName
                : $"{nickPrefix}_{number}";

            return new ST_MONITOR_HEAD_SELECT_ROW(
                number,
                $"H{number + 1:00}",
                nickName,
                state,
                registered,
                number == selectedHeadNumber);
        }
        return Enumerable.Range(0, LaserHeadCount)
            .Select(SelectNumber83)
            .ToArray();
    }

    private static IReadOnlyList<ST_MONITOR_LASER_CONTROL_ROW> CreateLaserControlRows(
        string tab,
        ST_LASER_STATUS laserStatus,
        ST_TALON_STATUS talonStatus,
        IReadOnlyList<ST_MONITOR_PARAMETER_ROW> operationFields)
    {
        if (tab != "LASER")
        {
            return [];
        }
        string HandleFields84(ST_MONITOR_PARAMETER_ROW field)
        {
            return field.Parameter;
        }

        var fields = operationFields.ToDictionary(
HandleFields84,
            StringComparer.OrdinalIgnoreCase);

        ST_MONITOR_PARAMETER_ROW Field(string parameter)
        {
            return fields.TryGetValue(parameter, out var field)
                ? field
                : new ST_MONITOR_PARAMETER_ROW(parameter, "", "");
        }

        return
        [
            new(
                "Diode Current",
                talonStatus.DiodeCurrent.ToString("F4", CultureInfo.InvariantCulture),
                "A",
                new ST_MONITOR_PARAMETER_ROW("Diode Current", "-", "-"),
                "-",
                "READ ONLY",
                "Dark",
                false),
            new(
                "Diode Temp",
                talonStatus.DiodeTemp.ToString("F4", CultureInfo.InvariantCulture),
                "C",
                new ST_MONITOR_PARAMETER_ROW("Diode Temp", "-", "-"),
                "-",
                "READ ONLY",
                "Dark",
                false),
            new(
                "Tower Temp",
                talonStatus.TowerTemp.ToString("F4", CultureInfo.InvariantCulture),
                "C",
                new ST_MONITOR_PARAMETER_ROW("Tower Temp", "-", "-"),
                "-",
                "READ ONLY",
                "Dark",
                false),
            new(
                "Output Power",
                laserStatus.OutputPower.ToString("F4", CultureInfo.InvariantCulture),
                "W",
                new ST_MONITOR_PARAMETER_ROW("Output Power", "-", "-"),
                "-",
                "READ ONLY",
                "Dark",
                false),
            new(
                "QSW",
                talonStatus.Qsw.ToString(CultureInfo.InvariantCulture),
                "Hz",
                Field("QSW"),
                "Hz",
                "SET QSW",
                "Warn"),
            new(
                "EPRF",
                talonStatus.Eprf.ToString(CultureInfo.InvariantCulture),
                "Hz",
                Field("EPRF"),
                "Hz",
                "SET EPRF"),
            new(
                "SHG Count",
                talonStatus.ShgReadBackCount.ToString(CultureInfo.InvariantCulture),
                "count",
                Field("SHG Count"),
                "count",
                "SET SHG"),
            new(
                "Q Mode",
                talonStatus.QMode.ToString(CultureInfo.InvariantCulture),
                "-",
                Field("Q Mode"),
                "-",
                "SET Q MODE")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_BET_TABLE_ROW> CreateBetTableRows(
        string tab,
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        ST_DEVICE_STATUS snapshot)
    {
        if (tab != "BET")
        {
            return [];
        }
        int GetRowSortKey85(ST_BET_TABLE_DATA row)
        {
            return row.Index;
        }

        ST_MONITOR_BET_TABLE_ROW SelectRow86(ST_BET_TABLE_DATA row)
        {
            var active = IsInPosition(row.Magnification, snapshot.Bet.CurrentMagnification) &&
                IsInPosition(row.Divergence, snapshot.Bet.CurrentDivergence);

            return new ST_MONITOR_BET_TABLE_ROW(
                row.Index.ToString(CultureInfo.InvariantCulture),
                row.Description,
                row.Magnification.ToString("F3"),
                row.Divergence.ToString("F3"),
                active ? "ACTIVE" : "",
                active);
        }
        return table
            .OrderBy(GetRowSortKey85)
            .Select(SelectRow86)
            .ToArray();
    }

    private static IReadOnlyList<ST_BET_TABLE_DATA> CreateBETTableData(
        IReadOnlyList<ST_MONITOR_BET_TABLE_ROW> rows)
    {
        ST_BET_TABLE_DATA SelectRow87(ST_MONITOR_BET_TABLE_ROW row, int order)
        {
            return new ST_BET_TABLE_DATA(
                            ReadBETInt(row.No, order),
                            ReadBETDouble(row.Mag, 0.0),
                            ReadBETDouble(row.Div, 0.0),
                            row.Description?.Trim() ?? "");
        }

        int GetRowSortKey88(ST_BET_TABLE_DATA row)
        {
            return row.Index;
        }

        return rows
            .Select(SelectRow87)
            .OrderBy(GetRowSortKey88)
            .ToArray();
    }

    private static int ReadBETInt(
        string value,
        int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static double ReadBETDouble(
        string value,
        double defaultValue)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static IReadOnlyList<ST_MONITOR_COMMAND_HISTORY_ROW> CreateCommandHistoryRows(
        string tab,
        IReadOnlyList<ST_INTERFACE_HISTORY> interfaceHistory)
    {
        bool FilterItem89(ST_INTERFACE_HISTORY item)
        {
            return IsCommandHistoryVisible(tab, item);
        }

        DateTimeOffset GetItemSortKey90(ST_INTERFACE_HISTORY item)
        {
            return item.OccurredAt;
        }

        ST_MONITOR_COMMAND_HISTORY_ROW SelectItem91(ST_INTERFACE_HISTORY item)
        {
            return new ST_MONITOR_COMMAND_HISTORY_ROW(
                            item.OccurredAt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            "LOG",
                            item.NickName,
                            FormatInterfaceHistoryCommand(item),
                            FormatInterfaceHistoryTarget(item),
                            FormatInterfaceHistoryResult(item));
        }

        var rows = interfaceHistory
            .Where(FilterItem89)
            .OrderByDescending(GetItemSortKey90)
            .Take(12)
            .Select(SelectItem91)
            .ToArray();

        if (rows.Length > 0)
        {
            return rows;
        }
        IReadOnlyList<ST_MONITOR_COMMAND_HISTORY_ROW> EvaluateTabSwitch37()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "MOTOR":
                    return [
                        new("2026-05-15 10:24:12", "ENG1", "GX", "ABS MOVE", "12.340", "OK"),
                new("2026-05-15 10:23:58", "ENG1", "GY", "HOME", "0.000", "OK"),
                new("2026-05-15 10:23:43", "ENG1", "ATTENUATOR", "STOP", "-", "OK"),
                new("2026-05-15 10:23:28", "ENG1", "SCANNER_01_GX", "ABS MOVE", "12.340", "OK"),
                new("2026-05-15 10:23:15", "ENG1", "SCANNER_02_GY", "REL MOVE", "-2.000", "OK")
                    ];
                case "LASER":
                    return [
                        new("10:24:12.345", "ENG1", "LASER OFF", "-", "-", "OK"),
                new("10:23:58.112", "ENG1", "GATE OFF", "-", "-", "OK"),
                new("10:23:35.876", "ENG1", "SET QSW", "20000 Hz", "-", "OK"),
                new("10:23:18.552", "ENG1", "SHUTTER CLOSE", "-", "-", "OK"),
                new("10:22:45.231", "ENG1", "LASER ON", "-", "-", "OK"),
                new("10:22:32.009", "ENG1", "RESET ERROR", "0", "-", "OK"),
                new("10:22:01.678", "ENG1", "SET Q MODE", "0", "-", "OK"),
                new("10:21:44.210", "ENG1", "GATE ON", "-", "-", "OK")
                    ];
                case "CHILLER":
                    return [
                        new("2026-05-15 10:24:12", "ENG1", "RUN", "-", "-", "OK"),
                new("2026-05-15 10:22:45", "ENG1", "SET TEMP", "22.0 C", "-", "OK"),
                new("2026-05-15 10:21:31", "ENG1", "RESET ALARM", "-", "-", "OK"),
                new("2026-05-15 10:20:18", "ENG1", "PUMP ONLY", "-", "-", "OK"),
                new("2026-05-15 10:18:55", "ENG1", "STOP", "-", "-", "OK"),
                new("2026-05-15 10:17:32", "ENG1", "REFRESH", "-", "-", "OK"),
                new("2026-05-15 10:16:05", "ENG1", "SET TEMP", "21.5 C", "-", "OK"),
                new("2026-05-15 10:14:41", "ENG1", "RUN", "-", "-", "OK")
                    ];
                case "ATTENUATOR":
                    return [
                        new("10:24:12.345", "ENG1", "MOVE ABS", "55.000", "-", "OK"),
                new("10:23:45.112", "ENG1", "HOME", "-", "-", "OK"),
                new("10:23:12.876", "ENG1", "STOP", "-", "-", "OK"),
                new("10:22:58.552", "ENG1", "RESET ALARM", "-", "-", "OK"),
                new("10:22:31.231", "ENG1", "REFRESH", "-", "-", "OK"),
                new("10:22:10.009", "ENG1", "MOVE REL", "+10.000", "-", "OK"),
                new("10:21:44.210", "ENG1", "MOVE ABS", "40.000", "-", "OK")
                    ];
                case "BET":
                    return [
                        new("10:24:12.345", "ENG1", "BET_CTRL", "#1:1626! / #2:1020!", "OK", "OK"),
                new("10:23:45.112", "ENG1", "BET_CTRL", "#1:1118! / #2:2351!", "OK", "OK"),
                new("10:23:12.876", "ENG1", "HOME", "-", "-", "OK"),
                new("10:22:58.552", "ENG1", "RESET ALARM", "-", "-", "OK"),
                new("10:22:31.231", "ENG1", "REFRESH", "-", "-", "OK"),
                new("10:22:10.009", "ENG1", "BET_CTRL", "#7:$7:500", "M1:1626.000", "OK"),
                new("10:21:44.210", "ENG1", "BET_CTRL", "#8:$8:500", "M2:1020.000", "OK")
                    ];
                case "POWER METER":
                    return [
                        new("10:24:12.345", "ENG1", "READ POWER", "1.2040 W", "-", "OK"),
                new("10:23:58.112", "ENG1", "SET WAVE", "355.0 nm", "-", "OK"),
                new("10:23:35.876", "ENG1", "GET SERIAL", "PM-20260515-01", "-", "OK"),
                new("10:23:18.552", "ENG1", "GET WAVELENGTH", "355.0 nm", "-", "OK"),
                new("10:22:45.231", "ENG1", "STEP ADD", "PWM_HEAD05", "-", "OK"),
                new("10:22:32.009", "ENG1", "START", "POWER_CHECK.PWM", "-", "OK"),
                new("10:22:01.678", "ENG1", "STOP", "POWER_CHECK.PWM", "-", "OK")
                    ];
                case "MELSEC":
                    return [
                        new("10:24:12.345", "ENG1", "MELSEC_0", "READ BIT", "PROCESS_ALIVE", "OK"),
                new("10:23:58.112", "ENG1", "MELSEC_0", "WRITE BIT", "REVIEW_START_REQ=ON", "OK"),
                new("10:23:35.876", "ENG1", "MELSEC_0", "WRITE DOUBLE", "STAGE_Y_TARGET_POS=0.000", "OK"),
                new("10:23:18.552", "ENG1", "MELSEC_0", "READ STRING", "GLASS_ID", "OK")
                    ];
                default:
                    return [
                        new("10:24:12.345", "ENG1", tab, "REFRESH", "-", "OK"),
                new("10:23:58.112", "ENG1", tab, "STATUS READ", "-", "OK"),
                new("10:23:35.876", "ENG1", tab, "PARAMETER LOAD", "-", "OK")
                    ];
            }
        }

        return EvaluateTabSwitch37();
    }

    private static bool IsCommandHistoryVisible(
        string tab,
        ST_INTERFACE_HISTORY item)
    {
        var isHistoryAction =
            item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase) ||
            item.Action.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
            item.Action.Contains("CONNECT", StringComparison.OrdinalIgnoreCase) ||
            item.Action.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase);

        if (!isHistoryAction)
        {
            return false;
        }

        if (tab == "CHILLER" &&
            item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase) &&
            IsChillerPollingCommand(item.BeforeState))
        {
            return false;
        }

        if (tab == "ATTENUATOR" &&
            item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase) &&
            IsAttenuatorPollingCommand(item.BeforeState))
        {
            return false;
        }

        if (tab == "BET" &&
            item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase) &&
            IsBetPollingCommand(item.BeforeState))
        {
            return false;
        }

        return true;
    }

    private static bool IsChillerPollingCommand(string command)
    {
        var value = command.Trim();

        if (value.StartsWith("ORION:POLL:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.StartsWith("TX HEX ", StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith(" 05", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains(" 02 ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttenuatorPollingCommand(string command)
    {
        var value = command.Trim().TrimEnd('?');
        return value.Equals("TP", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TH", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TS", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("TP", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("TH", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("TS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBetPollingCommand(string command)
    {
        var value = command.Trim();
        return value.Equals("#8:", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("#7:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("#8:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("#7:", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("#8:", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("#7:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatInterfaceHistoryCommand(ST_INTERFACE_HISTORY item)
    {
        if (item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase))
        {
            var command = string.IsNullOrWhiteSpace(item.BeforeState)
                ? "SEND"
                : item.BeforeState
                    .Replace("\r\n", " / ", StringComparison.OrdinalIgnoreCase)
                    .Replace("\n", " / ", StringComparison.OrdinalIgnoreCase)
                    .Replace("\r", " / ", StringComparison.OrdinalIgnoreCase);

            return item.Module == EN_EQP_MODULE.Bet
                ? FormatBETCommandDisplay(command)
                : command;
        }

        return item.Action.ToUpperInvariant();
    }

    private static string FormatBETCommandDisplay(string command)
    {
        var parts = command.Split(':', StringSplitOptions.TrimEntries);

        if (parts.Length >= 3 &&
            parts[0].Equals("MOVE", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mag) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var div))
        {
            return $"#1:{Math.Clamp((int)Math.Round(div), 0, 4500).ToString(CultureInfo.InvariantCulture)}! / " +
                $"#2:{Math.Clamp((int)Math.Round(mag), 0, 4500).ToString(CultureInfo.InvariantCulture)}!";
        }

        return command;
    }

    private static string FormatInterfaceHistoryTarget(ST_INTERFACE_HISTORY item)
    {
        if (item.Action.Equals("COMMAND", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(item.AfterState) ? "-" : item.AfterState;
        }

        return string.IsNullOrWhiteSpace(item.BeforeState)
            ? "-"
            : $"{item.BeforeState} -> {item.AfterState}";
    }

    private static string FormatInterfaceHistoryResult(ST_INTERFACE_HISTORY item)
    {
        if (item.Action.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return "ERROR";
        }

        if (!string.IsNullOrWhiteSpace(item.Detail) &&
            item.Detail.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return "ERROR";
        }

        if (item.AfterState.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase))
        {
            return "NG";
        }

        return "OK";
    }

    private static IReadOnlyList<ST_MONITOR_TREND_POINT> CreateTrendPoints(string tab)
    {
        IReadOnlyList<ST_MONITOR_TREND_POINT> EvaluateTabSwitch38()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "CHILLER":
                    return [
                        new("09:54", 128, 132, 154),
                new("10:00", 126, 132, 154),
                new("10:06", 127, 132, 154),
                new("10:12", 125, 132, 154),
                new("10:18", 126, 132, 154),
                new("10:24", 126, 132, 154)
                    ];
                case "POWER METER":
                    return [
                        new("09:54", 126, 130, 134),
                new("10:00", 124, 129, 132),
                new("10:06", 128, 131, 136),
                new("10:12", 122, 128, 131),
                new("10:18", 125, 130, 135),
                new("10:24", 127, 132, 138)
                    ];
                default:
                    return [
                        new("09:54", 112, 134, 0),
                new("10:00", 110, 132, 0),
                new("10:06", 108, 134, 0),
                new("10:12", 106, 132, 0),
                new("10:18", 108, 133, 0),
                new("10:24", 107, 132, 0)
                    ];
            }
        }

        return EvaluateTabSwitch38();
    }

    private static IReadOnlyList<ST_MONITOR_SUMMARY_ITEM> CreateSummaryItems(string tab, ST_DEVICE_STATUS snapshot)
    {
        IReadOnlyList<ST_MONITOR_SUMMARY_ITEM> EvaluateTabSwitch39()
        {
            var switchValue = tab;
            switch (switchValue)
            {
                case "LASER":
                    return [
                        new("Output Power", snapshot.Laser.OutputPower.ToString("F3"), "W", "Accent"),
                new("Temperature", "24.6", "C", "Warn")
                    ];
                case "CHILLER":
                    return [
                        new("Cur Temp", snapshot.Chiller.Temperature.ToString("F1"), "C", "Accent"),
                new("Set Temp", snapshot.Chiller.SetTemperature.ToString("F1"), "C", "Warn"),
                new("Run State", snapshot.Chiller.RunState, "", snapshot.Chiller.Running ? "Ok" : "Warn"),
                new("Alarm", snapshot.Chiller.AlarmOn ? "OCCUR" : "CLEAR", "", snapshot.Chiller.AlarmOn ? "Warn" : "Ok")
                    ];
                case "ATTENUATOR":
                    return [
                        new("Cur Pos", snapshot.Attenuator.CurrentPosition.ToString("F3"), "DEG", "Accent"),
                new("In Position", IsInPosition(snapshot.Attenuator.CurrentPosition, snapshot.Attenuator.TargetPosition) ? "YES" : "NO", "", IsInPosition(snapshot.Attenuator.CurrentPosition, snapshot.Attenuator.TargetPosition) ? "Ok" : "Warn"),
                new("State", snapshot.Attenuator.CommandState, "", IsAttenuatorMoving(snapshot.Attenuator.CommandState) ? "Warn" : "Ok"),
                new("Moving", IsAttenuatorMoving(snapshot.Attenuator.CommandState) ? "MOVING" : "IDLE", "", IsAttenuatorMoving(snapshot.Attenuator.CommandState) ? "Warn" : "Ok")
                    ];
                case "BET":
                    return [
                        new("MAG Pos", snapshot.Bet.CurrentMagnification.ToString("F3"), "step", "Accent"),
                new("DIV Pos", snapshot.Bet.CurrentDivergence.ToString("F3"), "step", "Accent"),
                new("State", snapshot.Bet.IsMoving ? "MOVING" : "IDLE", "", snapshot.Bet.IsMoving ? "Warn" : "Ok"),
                new("Alarm", snapshot.Bet.AlarmOn ? "OCCUR" : "CLEAR", "", snapshot.Bet.AlarmOn ? "Warn" : "Ok")
                    ];
                case "POWER METER":
                    return [
                        new("Current Power", snapshot.PowerMeter.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture), snapshot.PowerMeter.Unit, "Accent"),
                new("Average", snapshot.PowerMeter.AveragePower.ToString("F4", CultureInfo.InvariantCulture), snapshot.PowerMeter.Unit, "Ok"),
                new("Max", snapshot.PowerMeter.MaxPower.ToString("F4", CultureInfo.InvariantCulture), snapshot.PowerMeter.Unit, "Warn"),
                new("Min", snapshot.PowerMeter.MinPower.ToString("F4", CultureInfo.InvariantCulture), snapshot.PowerMeter.Unit, "Accent")
                    ];
                default:
                    return [];
            }
        }

        return EvaluateTabSwitch39();
    }

    private static IReadOnlyList<ST_MONITOR_POSITION_ROW> CreatePositionRows(string tab, ST_DEVICE_STATUS snapshot)
    {
        if (tab == "BET")
        {
            return
            [
                new("Target Magnification", snapshot.Bet.TargetMagnification.ToString("F3"), "x", "Warn"),
                new("Target Divergence", snapshot.Bet.TargetDivergence.ToString("F3"), "x", "Warn"),
                new("Mag Error", Math.Abs(snapshot.Bet.CurrentMagnification - snapshot.Bet.TargetMagnification).ToString("F3"), "x", "Warn"),
                new("Div Error", Math.Abs(snapshot.Bet.CurrentDivergence - snapshot.Bet.TargetDivergence).ToString("F3"), "x", "Warn"),
                new("In Position", IsInPosition(snapshot.Bet.CurrentMagnification, snapshot.Bet.TargetMagnification) && IsInPosition(snapshot.Bet.CurrentDivergence, snapshot.Bet.TargetDivergence) ? "YES" : "NO", "", "Ok"),
                new("Moving", snapshot.Bet.IsMoving ? "MOVING" : "IDLE", "", "Ok"),
                new("Last Update", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "")
            ];
        }

        if (tab != "ATTENUATOR")
        {
            return [];
        }

        return
        [
            new("Target Position", snapshot.Attenuator.TargetPosition.ToString("F3"), "DEG", "Warn"),
            new("Position Error", Math.Abs(snapshot.Attenuator.CurrentPosition - snapshot.Attenuator.TargetPosition).ToString("F3"), "DEG", "Warn"),
            new("In Position", IsInPosition(snapshot.Attenuator.CurrentPosition, snapshot.Attenuator.TargetPosition) ? "YES" : "NO", "", "Ok"),
            new("Moving", snapshot.Attenuator.CommandState, "", "Ok"),
            new("Last Update", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "")
        ];
    }

    private IReadOnlyList<ST_MONITOR_MELSEC_GROUP> CreateMelsecGroups(string tab)
    {
        if (tab != "MELSEC")
        {
            return [];
        }
        string SelectData92(ST_MELSEC_MAP_DATA data)
        {
            return data.Group;
        }

        bool FilterGroup93(string group)
        {
            return !string.IsNullOrWhiteSpace(group);
        }

        string GetGroupSortKey94(string group)
        {
            return group;
        }

        var mapGroups = _interfaceManager.Melsec.Map
            .Select(SelectData92)
            .Where(FilterGroup93)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetGroupSortKey94, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!_selectedMelsecGroup.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
            !mapGroups.Contains(_selectedMelsecGroup, StringComparer.OrdinalIgnoreCase))
        {
            _selectedMelsecGroup = "ALL";
        }
        ST_MONITOR_MELSEC_GROUP SelectGroup95(string group)
        {
            return new ST_MONITOR_MELSEC_GROUP(
                            group,
                            group.Equals(_selectedMelsecGroup, StringComparison.OrdinalIgnoreCase),
                            group.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                                ? _interfaceManager.Melsec.Map.Count
                                : _interfaceManager.Melsec.GetMapList(group).Count);
        }

        return new[] { "ALL" }
            .Concat(mapGroups)
            .Select(SelectGroup95)
            .ToArray();
    }

    private IReadOnlyList<ST_MONITOR_MELSEC_ROW> CreateMelsecRows(
        string tab,
        IReadOnlyDictionary<string, ST_MONITOR_MELSEC_VALUE> values)
    {
        if (tab != "MELSEC")
        {
            return [];
        }

        var rows = _interfaceManager.Melsec.Map;
        string GetDataSortKey96(ST_MELSEC_MAP_DATA data)
        {
            return data.Group;
        }

        int GetDataSortKey97(ST_MELSEC_MAP_DATA data)
        {
            return data.DeviceNo;
        }

        string GetDataSortKey98(ST_MELSEC_MAP_DATA data)
        {
            return data.Id;
        }

        ST_MONITOR_MELSEC_ROW SelectData99(ST_MELSEC_MAP_DATA data)
        {
            values.TryGetValue(data.Id, out var value);

            return new ST_MONITOR_MELSEC_ROW(
                data.Id,
                data.Group,
                data.Name,
                data.DeviceNo.ToString(CultureInfo.InvariantCulture),
                data.Address,
                data.DataType.ToString().ToUpperInvariant(),
                data.Direction.ToString().ToUpperInvariant(),
                MelsecAccessText(data.Access),
                data.Scale.ToString("G", CultureInfo.InvariantCulture),
                data.Length.ToString(CultureInfo.InvariantCulture),
                data.PollMs <= 0 ? "-" : data.PollMs.ToString(CultureInfo.InvariantCulture),
                value?.Value ?? "-",
                MelsecWriteValue(data),
                value?.State ?? MelsecRowState(data),
                value?.Message ?? data.Description);
        }
        return rows
            .OrderBy(GetDataSortKey96, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetDataSortKey97)
            .ThenBy(GetDataSortKey98, StringComparer.OrdinalIgnoreCase)
            .Select(SelectData99)
            .ToArray();
    }

    private void SetMelsecRows(IReadOnlyList<ST_MONITOR_MELSEC_ROW> rows)
    {
        MelsecRows = rows;
        bool FilterRow100(ST_MONITOR_MELSEC_ROW row)
        {
            return row.CanRead;
        }

        MelsecReadRows = rows
            .Where(FilterRow100)
            .ToArray();
        bool FilterRow101(ST_MONITOR_MELSEC_ROW row)
        {
            return row.CanWrite;
        }

        MelsecWriteRows = rows
            .Where(FilterRow101)
            .ToArray();
    }

    private void SaveMelsecWriteValues()
    {
        bool FilterRow102(ST_MONITOR_MELSEC_ROW row)
        {
            return row.CanWrite;
        }

        foreach (var row in MelsecWriteRows.Count > 0
                     ? MelsecWriteRows
                     : MelsecRows.Where(FilterRow102))
        {
            _melsecWriteValues[row.Id] = row.WriteValue;
        }
    }

    private async Task<(ST_PRODUCT_DATA? Product, IReadOnlyList<ST_PRODUCT_HISTORY> History, string Error)> LoadProductDisplay(
        CancellationToken cancellationToken)
    {
        try
        {
            var product = _productManager.Current ?? await _productManager.LoadActive(cancellationToken);
            var history = await _productManager.LoadHistory(80, 14, cancellationToken);
            return (product, history, "");
        }
        catch (Exception ex)
        {
            return (null, [], ex.Message);
        }
    }

    private static IReadOnlyList<ST_MONITOR_PRODUCT_ITEM> CreateProductItems(
        ST_PRODUCT_DATA? product,
        string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return
            [
                new("State", "DATA ERROR", "Error"),
                new("Message", error, "Error")
            ];
        }

        if (product is null)
        {
            return
            [
                new("Product ID", "-", "Normal"),
                new("State", "NO ACTIVE PRODUCT", "Warn"),
                new("Result", "-", "Normal")
            ];
        }
        bool HandleCompletedHeads103(ST_PRODUCT_HEAD_RESULT head)
        {
            return head.Result == EN_PRODUCT_RESULT.OK;
        }

        var completedHeads = product.Heads.Count(HandleCompletedHeads103);
        bool HandleNgHeads104(ST_PRODUCT_HEAD_RESULT head)
        {
            return head.Result == EN_PRODUCT_RESULT.NG;
        }

        var ngHeads = product.Heads.Count(HandleNgHeads104);

        return
        [
            new("Product ID", product.ProductId, "Accent"),
            new("Panel ID", product.PanelId, "Normal"),
            new("Lot ID", product.LotId, "Normal"),
            new("Process ID", product.ProcessId, "Normal"),
            new("Recipe", product.RecipeId, "Accent"),
            new("State", product.State.ToString().ToUpperInvariant(), ProductStateTone(product.State, product.Result)),
            new("Result", product.Result.ToString().ToUpperInvariant(), ProductResultTone(product.Result)),
            new("Head OK", completedHeads.ToString(CultureInfo.InvariantCulture), "Ok"),
            new("Head NG", ngHeads.ToString(CultureInfo.InvariantCulture), ngHeads > 0 ? "Error" : "Normal"),
            new("Created", FormatProductDateTime(product.CreatedAt), "Normal"),
            new("Started", FormatProductDateTime(product.StartedAt), "Normal"),
            new("Completed", FormatProductDateTime(product.CompletedAt), "Normal")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_PRODUCT_HEAD_ROW> CreateProductHeadRows(ST_PRODUCT_DATA? product)
    {
        if (product is null)
        {
            return [];
        }
        int GetHeadSortKey105(ST_PRODUCT_HEAD_RESULT head)
        {
            return head.HeadNo;
        }

        ST_MONITOR_PRODUCT_HEAD_ROW SelectHead106(ST_PRODUCT_HEAD_RESULT head)
        {
            return new ST_MONITOR_PRODUCT_HEAD_ROW(
                            $"H{head.HeadNo:00}",
                            head.State.ToString().ToUpperInvariant(),
                            head.TotalPoints.ToString("N0", CultureInfo.InvariantCulture),
                            head.CompletedPoints.ToString("N0", CultureInfo.InvariantCulture),
                            head.Result.ToString().ToUpperInvariant(),
                            string.IsNullOrWhiteSpace(head.ErrorCode) ? "-" : head.ErrorCode,
                            string.IsNullOrWhiteSpace(head.Message) ? "-" : head.Message);
        }

        return product.Heads
            .OrderBy(GetHeadSortKey105)
            .Select(SelectHead106)
            .ToArray();
    }

    private static IReadOnlyList<ST_PWM_PROCESS_ROW> CreatePwmProcessRows(
        string tab,
        ST_POWER_METER_TABLE_DATA table)
    {
        if (tab != "POWER METER")
        {
            return [];
        }
        ST_PWM_PROCESS_ROW SelectProcess107(ST_POWER_METER_PROCESS_DATA process, int index)
        {
            return new ST_PWM_PROCESS_ROW(
                            (index + 1).ToString("00", CultureInfo.InvariantCulture),
                            process.FileName,
                            process.IsSelected
                                ? "ON"
                                : "",
                            process.IsSelected
                                ? "LOADED"
                                : "",
                            "",
                            process.IsSelected);
        }

        return table.Processes
            .Select(SelectProcess107)
            .ToArray();
    }

    private static IReadOnlyList<ST_PWM_STEP_ROW> CreatePwmStepRows(
        string tab,
        ST_DEVICE_STATUS snapshot,
        ST_POWER_METER_TABLE_DATA table,
        int selectedStepNo)
    {
        if (tab != "POWER METER")
        {
            return [];
        }

        var measured = snapshot.PowerMeter.MeasuredPower <= 0
            ? 1.2040
            : snapshot.PowerMeter.MeasuredPower;
        ST_PWM_STEP_ROW SelectStep108(ST_POWER_METER_STEP_DATA step, int index)
        {
            var measurePower = index == 0 && measured > 0
                ? measured.ToString("F4", CultureInfo.InvariantCulture)
                : step.MeasurePower?.ToString("F4", CultureInfo.InvariantCulture) ?? "-";

            return new ST_PWM_STEP_ROW(
                step.StepNo.ToString("000", CultureInfo.InvariantCulture),
                step.OptionName,
                step.PowerOut ? "ON" : "OFF",
                step.PowerUnit,
                step.SettingAtt.ToString("F2", CultureInfo.InvariantCulture),
                step.SettingPower.ToString("F3", CultureInfo.InvariantCulture),
                step.SettingFreq.ToString("F1", CultureInfo.InvariantCulture),
                step.MeasureCycle.ToString(CultureInfo.InvariantCulture),
                step.MeasureTimeMs.ToString(CultureInfo.InvariantCulture),
                step.MeasureIntervalMs.ToString(CultureInfo.InvariantCulture),
                step.StartDelayMs.ToString(CultureInfo.InvariantCulture),
                step.CoolingTimeMs.ToString(CultureInfo.InvariantCulture),
                step.Rotator.ToString("F4", CultureInfo.InvariantCulture),
                measurePower,
                step.State,
                step.StepNo == selectedStepNo);
        }
        return table.Steps
            .Select(SelectStep108)
            .ToArray();
    }

    private static IReadOnlyList<ST_PWM_SETTING_ROW> CreatePwmSettingRows(
        string tab,
        ST_POWER_METER_TABLE_DATA table,
        int selectedStepNo)
    {
        if (tab != "POWER METER")
        {
            return [];
        }
        bool MatchStep109(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo == selectedStepNo;
        }

        var selectedStep = table.Steps.FirstOrDefault(MatchStep109) ??
            table.Steps.FirstOrDefault() ??
            CreateDefaultPowerMeterStep(Math.Max(1, selectedStepNo));

        return
        [
            new("OPTION NAME", selectedStep.OptionName, "-"),
            new("POWER OUT", selectedStep.PowerOut ? "ON" : "OFF", "-"),
            new("POWER UNIT", selectedStep.PowerUnit, "-"),
            new("SETTING ATT", selectedStep.SettingAtt.ToString("F2", CultureInfo.InvariantCulture), "%"),
            new("SETTING POWER", selectedStep.SettingPower.ToString("F3", CultureInfo.InvariantCulture), "W"),
            new("SETTING FREQ", selectedStep.SettingFreq.ToString("F1", CultureInfo.InvariantCulture), "kHz"),
            new("MEASURE CYCLE", selectedStep.MeasureCycle.ToString(CultureInfo.InvariantCulture), "count"),
            new("MEASURE TIME", selectedStep.MeasureTimeMs.ToString(CultureInfo.InvariantCulture), "ms"),
            new("MEASURE INTERVAL", selectedStep.MeasureIntervalMs.ToString(CultureInfo.InvariantCulture), "ms"),
            new("START DELAY", selectedStep.StartDelayMs.ToString(CultureInfo.InvariantCulture), "ms"),
            new("COOLING TIME", selectedStep.CoolingTimeMs.ToString(CultureInfo.InvariantCulture), "ms"),
            new("WAVELENGTH", "355.0", "nm")
        ];
    }

    private static IReadOnlyList<ST_PWM_DEVICE_ROW> CreatePwmDeviceRows(string tab, ST_DEVICE_STATUS snapshot)
    {
        if (tab != "POWER METER")
        {
            return [];
        }

        var serial = string.IsNullOrWhiteSpace(snapshot.PowerMeter.SerialNumber)
            ? "-"
            : snapshot.PowerMeter.SerialNumber;
        var power = snapshot.PowerMeter.MeasuredPower <= 0
            ? "1.2040"
            : snapshot.PowerMeter.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture);

        return
        [
            new("SEL PWM", "POWER_METER", "-", "REFRESH"),
            new("GET SERIAL", serial, "-", "GET SERIAL"),
            new("GET WAVELENGTH", snapshot.PowerMeter.WaveLengthNm.ToString("F1", CultureInfo.InvariantCulture), "nm", "GET WAVELENGTH"),
            new("SET WAVELENGTH", "355.0", "nm", "SET WAVELENGTH"),
            new("GET POWER", power, snapshot.PowerMeter.Unit, "GET POWER")
        ];
    }

    private static IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreatePwmProcessButtons(string tab)
    {
        return tab == "POWER METER"
            ?
            [
                new("CREATE PROCESS", "Add", "Blue"),
                new("DELETE PROCESS", "Delete", "Red"),
                new("RENAME PROCESS", "Edit", "Dark"),
                new("SAVE PROCESS", "Save", "Green")
            ]
            : [];
    }

    private static IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreatePwmStepButtons(string tab)
    {
        return tab == "POWER METER"
            ?
            [
                new("ADD STEP", "Add", "Blue"),
                new("COPY STEP", "Add", "Blue"),
                new("DELETE STEP", "Delete", "Red"),
                new("DELETE ALL", "Delete", "Red")
            ]
            : [];
    }

    private static IReadOnlyList<ST_MONITOR_OPERATION_BUTTON> CreatePwmRunButtons(string tab)
    {
        return tab == "POWER METER"
            ?
            [
                new("START", "Run", "Green"),
                new("STOP", "Stop", "Red")
            ]
            : [];
    }

    private static IReadOnlyList<ST_MONITOR_PRODUCT_HISTORY_ROW> CreateProductHistoryRows(
        IReadOnlyList<ST_PRODUCT_HISTORY> history)
    {
        DateTimeOffset GetItemSortKey110(ST_PRODUCT_HISTORY item)
        {
            return item.OccurredAt;
        }

        ST_MONITOR_PRODUCT_HISTORY_ROW SelectItem111(ST_PRODUCT_HISTORY item)
        {
            return new ST_MONITOR_PRODUCT_HISTORY_ROW(
                            item.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            item.ProductId,
                            item.RecipeId,
                            item.Action,
                            item.State,
                            item.Result,
                            item.Detail);
        }

        return history
            .OrderByDescending(GetItemSortKey110)
            .Select(SelectItem111)
            .ToArray();
    }

    private static string ProductStateTone(
        EN_PRODUCT_STATE state,
        EN_PRODUCT_RESULT result)
    {
        if (result == EN_PRODUCT_RESULT.NG)
        {
            return "Error";
        }
        string EvaluateStateSwitch40()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_PRODUCT_STATE.Running:
                    return "Accent";
                case EN_PRODUCT_STATE.Completed:
                    return "Ok";
                case EN_PRODUCT_STATE.Error or EN_PRODUCT_STATE.Scrapped or EN_PRODUCT_STATE.Stopped:
                    return "Error";
                default:
                    return "Warn";
            }
        }

        return EvaluateStateSwitch40();
    }

    private static string ProductResultTone(EN_PRODUCT_RESULT result)
    {
        string EvaluateResultSwitch41()
        {
            var switchValue = result;
            switch (switchValue)
            {
                case EN_PRODUCT_RESULT.OK:
                    return "Ok";
                case EN_PRODUCT_RESULT.NG:
                    return "Error";
                default:
                    return "Warn";
            }
        }

        return EvaluateResultSwitch41();
    }

    private static string FormatProductDateTime(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
    }

    private static string OnOffText(bool value)
    {
        return value ? "ON" : "OFF";
    }

    private static bool ParseMelsecBit(string value)
    {
        bool EvaluateValueSwitch42()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "1" or "TRUE" or "ON" or "YES":
                    return true;
                case "0" or "FALSE" or "OFF" or "NO":
                    return false;
                default:
                    throw new FormatException($"MELSEC BIT value must be ON/OFF or 1/0: {value}");
            }
        }

        return EvaluateValueSwitch42();
    }

    private static string MelsecAccessText(EN_MELSEC_ACCESS access)
    {
        string EvaluateAccessSwitch43()
        {
            var switchValue = access;
            switch (switchValue)
            {
                case EN_MELSEC_ACCESS.Read:
                    return "R";
                case EN_MELSEC_ACCESS.Write:
                    return "W";
                case EN_MELSEC_ACCESS.ReadWrite:
                    return "RW";
                default:
                    return access.ToString().ToUpperInvariant();
            }
        }

        return EvaluateAccessSwitch43();
    }

    private static string DefaultMelsecWriteValue(ST_MELSEC_MAP_DATA data)
    {
        if (data.Access == EN_MELSEC_ACCESS.Read)
        {
            return "";
        }
        string EvaluateDataTypeSwitch44()
        {
            var switchValue = data.DataType;
            switch (switchValue)
            {
                case EN_MELSEC_DATA_TYPE.Bit:
                    return "ON";
                case EN_MELSEC_DATA_TYPE.String:
                    return "";
                default:
                    return "0";
            }
        }

        return EvaluateDataTypeSwitch44();
    }

    private string MelsecWriteValue(ST_MELSEC_MAP_DATA data)
    {
        if (data.Access == EN_MELSEC_ACCESS.Read)
        {
            return "";
        }

        return _melsecWriteValues.TryGetValue(data.Id, out var value)
            ? value
            : DefaultMelsecWriteValue(data);
    }

    private static string MelsecRowState(ST_MELSEC_MAP_DATA data)
    {
        if (!data.Use)
        {
            return "DISABLED";
        }
        string EvaluateAccessSwitch45()
        {
            var switchValue = data.Access;
            switch (switchValue)
            {
                case EN_MELSEC_ACCESS.Read:
                    return "READ";
                case EN_MELSEC_ACCESS.Write:
                    return "WRITE";
                default:
                    return "READY";
            }
        }

        return EvaluateAccessSwitch45();
    }

    private static string FormatAxisPosition(string axisId, double value)
    {
        return axisId.StartsWith("BET_", StringComparison.OrdinalIgnoreCase)
            ? $"{value:F3} step"
            : value.ToString("F3");
    }

    private static bool IsInPosition(double current, double target)
    {
        return Math.Abs(current - target) <= 0.001;
    }

    private static bool IsAttenuatorMoving(string state)
    {
        return state.Contains("MOV", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("HOM", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ST_MONITOR_TAB(
    string Name,
    bool IsSelected);

public sealed record ST_MONITOR_COORDINATE_VIEWER_DATA(
    IReadOnlyList<ST_MONITOR_COORDINATE_BASIS_OPTION> BasisOptions,
    IReadOnlyList<ST_MONITOR_COORDINATE_VALUE_ROW> ValueRows,
    ImageSource? GlassPreviewImage,
    IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellPreviewLabels,
    IReadOnlyList<ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW> HoleMatrixRows,
    string GlassPreviewSummary,
    string RecipeName,
    string BasisName,
    string BasisDescription)
{
    public static ST_MONITOR_COORDINATE_VIEWER_DATA Empty { get; } = new(
        [],
        [],
        null,
        [],
        [],
        "0 Cells / 0 Holes",
        "-",
        "Design",
        "Recipe design coordinate from Align Key / Glass");
}

public sealed record ST_MONITOR_COORDINATE_BASIS_OPTION(
    string Key,
    string Name,
    string Description,
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
            return IsSelected ? Brushes.White : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_MONITOR_COORDINATE_VALUE_ROW(
    string Source,
    string Group,
    string Parameter,
    string Value,
    string Unit,
    string Key);

public sealed record ST_MONITOR_COORDINATE_HOLE_MATRIX_ROW(
    int RowNo,
    IReadOnlyList<ST_MONITOR_COORDINATE_HOLE_BUTTON> Holes);

public sealed record ST_MONITOR_COORDINATE_HOLE_BUTTON(
    string HoleKey,
    string HoleName,
    string Head,
    string Detail,
    bool IsSelected,
    CButtonCommand SelectCommand)
{
    public Brush BorderBrush
    {
        get
        {
            return IsSelected
        ? CStatusBrush.Active
        : CStatusBrush.Frozen(0x3B, 0x4A, 0x5B);
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            return IsSelected
        ? CStatusBrush.Frozen(0x32, 0x47, 0x5A)
        : CStatusBrush.Frozen(0x18, 0x20, 0x29);
        }
    }

    public Brush DetailBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_MONITOR_PRODUCT_ITEM(
    string Name,
    string Value,
    string Tone = "Normal")
{
    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateToneSwitch46()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    case "Error":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateToneSwitch46();
        }
    }
}

public sealed record ST_MONITOR_PRODUCT_HEAD_ROW(
    string Head,
    string State,
    string TotalPoints,
    string CompletedPoints,
    string Result,
    string ErrorCode,
    string Message)
{
    public Brush ResultBrush
    {
        get
        {
            Brush EvaluateValueSwitch47()
            {
                var switchValue = Result.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "OK":
                        return CStatusBrush.Online;
                    case "NG":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateValueSwitch47();
        }
    }

    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch48()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "COMPLETED" or "READY":
                        return CStatusBrush.Online;
                    case "RUNNING":
                        return CStatusBrush.Simul;
                    case "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateValueSwitch48();
        }
    }
}

public sealed record ST_MONITOR_PRODUCT_HISTORY_ROW(
    string Time,
    string ProductId,
    string RecipeId,
    string Action,
    string State,
    string Result,
    string Detail)
{
    public Brush ResultBrush
    {
        get
        {
            Brush EvaluateValueSwitch49()
            {
                var switchValue = Result.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "OK":
                        return CStatusBrush.Online;
                    case "NG":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateValueSwitch49();
        }
    }
}

public sealed record ST_MONITOR_MELSEC_GROUP(
    string Name,
    bool IsSelected,
    int Count);

public sealed class ST_MONITOR_MELSEC_ROW
{
    public ST_MONITOR_MELSEC_ROW(
        string id,
        string group,
        string name,
        string deviceNo,
        string address,
        string dataType,
        string direction,
        string access,
        string scale,
        string length,
        string pollMs,
        string readValue,
        string writeValue,
        string state,
        string description)
    {
        Id = id;
        Group = group;
        Name = name;
        DeviceNo = deviceNo;
        Address = address;
        DataType = dataType;
        Direction = direction;
        Access = access;
        Scale = scale;
        Length = length;
        PollMs = pollMs;
        ReadValue = readValue;
        WriteValue = writeValue;
        State = state;
        Description = description;
    }

    public string Id { get; }

    public string Group { get; }

    public string Name { get; }

    public string DeviceNo { get; }

    public string Address { get; }

    public string DataType { get; }

    public string Direction { get; }

    public string Access { get; }

    public string Scale { get; }

    public string Length { get; }

    public string PollMs { get; }

    public string ReadValue { get; set; }

    public string WriteValue { get; set; }

    public IReadOnlyList<string> WriteValueOptions
    {
        get
        {
            return DataType == "BIT" ? ["ON", "OFF"] : [];
        }
    }

    public bool UsesWriteSelection
    {
        get
        {
            return WriteValueOptions.Count > 0;
        }
    }

    public EN_RECIPE_DATA_TYPE WriteInputType
    {
        get
        {
            EN_RECIPE_DATA_TYPE EvaluateDataTypeSwitch50()
            {
                var switchValue = DataType;
                switch (switchValue)
                {
                    case "WORD" or "DWORD":
                        return EN_RECIPE_DATA_TYPE.Int;
                    case "DOUBLE" or "FLOAT":
                        return EN_RECIPE_DATA_TYPE.Double;
                    default:
                        return EN_RECIPE_DATA_TYPE.String;
                }
            }

            return EvaluateDataTypeSwitch50();
        }
    }

    public string State { get; }

    public string Description { get; }

    public bool CanRead
    {
        get
        {
            return Access is "R" or "RW";
        }
    }

    public bool CanWrite
    {
        get
        {
            return Access is "W" or "RW";
        }
    }

    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch51()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "READY" or "READ":
                        return CStatusBrush.Online;
                    case "WRITE":
                        return CStatusBrush.Wait;
                    case "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Muted;
                }
            }

            return EvaluateValueSwitch51();
        }
    }

    public Brush ValueBrush
    {
        get
        {
            return State.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
        ? CStatusBrush.Offline
        : string.IsNullOrWhiteSpace(ReadValue) || ReadValue == "-"
            ? CStatusBrush.Muted
            : CStatusBrush.Simul;
        }
    }
}

public sealed record ST_MONITOR_IO_ROW(
    string Id,
    string Address,
    string Name,
    string State,
    string OnDelay,
    string OffDelay,
    string Description,
    bool IsSelected = false)
{
    public Brush StateBrush
    {
        get
        {
            return MonitorStatusBrush(State);
        }
    }

    public Brush RowBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }

    private static Brush MonitorStatusBrush(string state)
    {
        Brush EvaluateValueSwitch52()
        {
            var switchValue = state.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "ON" or "ONLINE" or "OK" or "READY" or "RUN" or "NORMAL" or "SAFE" or "YES" or "DONE" or "IDLE":
                    return CStatusBrush.Online;
                case "OFF" or "CLOSE" or "ERROR" or "ALARM":
                    return CStatusBrush.Offline;
                case "WARN" or "WARNING" or "SET" or "WAIT":
                    return CStatusBrush.Wait;
                default:
                    return CStatusBrush.Muted;
            }
        }

        return EvaluateValueSwitch52();
    }
}

public sealed record ST_MONITOR_AXIS_ROW(
    string Axis,
    string Name,
    string CurrentPosition,
    string TargetPosition,
    string CommandPosition,
    string Servo,
    string Home,
    string LimitPlus,
    string LimitMinus,
    string Alarm,
    string State,
    bool IsSelected = false)
{
    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch53()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "READY" or "OK":
                        return CStatusBrush.Online;
                    case "ALARM" or "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateValueSwitch53();
        }
    }

    public Brush ServoBrush
    {
        get
        {
            return Servo.Trim().ToUpperInvariant() == "ON" ? CStatusBrush.Online : CStatusBrush.Offline;
        }
    }

    public Brush RowBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_MONITOR_STATUS_ROW(
    string Item,
    string Value,
    string State,
    string Unit,
    string Description)
{
    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch54()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "ON" or "ONLINE" or "OK" or "READY" or "RUN" or "NORMAL" or "SAFE" or "YES" or "DONE" or "IDLE":
                        return CStatusBrush.Online;
                    case "OFF" or "CLOSE" or "ERROR" or "ALARM":
                        return CStatusBrush.Offline;
                    case "WARN" or "WARNING" or "SET" or "WAIT" or "SIMULATION" or "SIM":
                        return CStatusBrush.Wait;
                    default:
                        return CStatusBrush.Muted;
                }
            }

            return EvaluateValueSwitch54();
        }
    }

    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateValueSwitch55()
            {
                var switchValue = Value.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "22.4" or "55.000" or "1.200" or "20.000" or "900" or "50.000" or "30.000":
                        return CStatusBrush.Wait;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueSwitch55();
        }
    }
}

public sealed record ST_MONITOR_OPERATION_BUTTON(
    string Label,
    string Icon,
    string Tone,
    string CommandKey = "")
{
    public Brush BackgroundBrush
    {
        get
        {
            Brush EvaluateToneSwitch56()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Green":
                        return CStatusBrush.CommandGreen;
                    case "Red":
                        return CStatusBrush.CommandRed;
                    case "Blue":
                        return CStatusBrush.CommandBlue;
                    default:
                        return CStatusBrush.CommandDark;
                }
            }

            return EvaluateToneSwitch56();
        }
    }

    public Brush BorderBrush
    {
        get
        {
            Brush EvaluateToneSwitch57()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Green":
                        return CStatusBrush.CommandGreenBorder;
                    case "Red":
                        return CStatusBrush.CommandRedBorder;
                    case "Blue":
                        return CStatusBrush.CommandBlueBorder;
                    default:
                        return CStatusBrush.CommandDarkBorder;
                }
            }

            return EvaluateToneSwitch57();
        }
    }

    public Geometry IconGeometry
    {
        get
        {
            return CMonitorIcon.Get(Icon);
        }
    }
}

public sealed record ST_MONITOR_HEAD_SELECT_ROW(
    int Number,
    string HeadName,
    string NickName,
    string State,
    bool IsRegistered,
    bool IsSelected)
{
    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch58()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "ONLINE":
                        return CStatusBrush.Online;
                    case "SIMULATION" or "SIMUL" or "SIM":
                        return CStatusBrush.Wait;
                    default:
                        return CStatusBrush.Offline;
                }
            }

            return EvaluateValueSwitch58();
        }
    }

    public Brush BorderBrush
    {
        get
        {
            return IsSelected
        ? CStatusBrush.Active
        : CStatusBrush.CommandDarkBorder;
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            return IsSelected
        ? CStatusBrush.CommandBlue
        : CStatusBrush.CommandDark;
        }
    }

    public Brush TextBrush
    {
        get
        {
            return IsRegistered
        ? CStatusBrush.PrimaryText
        : CStatusBrush.Muted;
        }
    }
}

public sealed record ST_MONITOR_LASER_CONTROL_ROW(
    string CurrentLabel,
    string CurrentValue,
    string CurrentUnit,
    ST_MONITOR_PARAMETER_ROW Setting,
    string SetUnit,
    string CommandLabel,
    string Tone = "Blue",
    bool CanCommand = true)
{
    public double CommandOpacity
    {
        get
        {
            return CanCommand ? 1.0 : 0.5;
        }
    }

    public Brush CurrentValueBrush
    {
        get
        {
            Brush EvaluateToneSwitch59()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateToneSwitch59();
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            Brush EvaluateToneSwitch60()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Green":
                        return CStatusBrush.CommandGreen;
                    case "Red":
                        return CStatusBrush.CommandRed;
                    case "Blue" or "Warn":
                        return CStatusBrush.CommandBlue;
                    default:
                        return CStatusBrush.CommandDark;
                }
            }

            return EvaluateToneSwitch60();
        }
    }

    public Brush BorderBrush
    {
        get
        {
            Brush EvaluateToneSwitch61()
            {
                var switchValue = Tone;
                switch (switchValue)
                {
                    case "Green":
                        return CStatusBrush.CommandGreenBorder;
                    case "Red":
                        return CStatusBrush.CommandRedBorder;
                    case "Blue" or "Warn":
                        return CStatusBrush.CommandBlueBorder;
                    default:
                        return CStatusBrush.CommandDarkBorder;
                }
            }

            return EvaluateToneSwitch61();
        }
    }

    public Geometry IconGeometry
    {
        get
        {
            return CMonitorIcon.Get("Move");
        }
    }
}

public sealed class ST_MONITOR_PARAMETER_ROW
{
    public ST_MONITOR_PARAMETER_ROW(
        string parameter,
        string value,
        string unit,
        string state = "Normal")
    {
        Parameter = parameter;
        Value = value;
        Unit = unit;
        State = state;
    }

    public string Parameter { get; }

    public string Value { get; set; }

    public string Unit { get; }

    public string State { get; }

    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateStateSwitch62()
            {
                var switchValue = State;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateStateSwitch62();
        }
    }
}

public sealed record ST_MONITOR_COMMAND_HISTORY_ROW(
    string Time,
    string User,
    string Name,
    string Command,
    string Target,
    string Result)
{
    public Brush ResultBrush
    {
        get
        {
            Brush EvaluateValueSwitch63()
            {
                var switchValue = Result.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "OK":
                        return CStatusBrush.Online;
                    case "WARN":
                        return CStatusBrush.Wait;
                    case "NG" or "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueSwitch63();
        }
    }
}

public sealed class ST_MONITOR_BET_TABLE_ROW : CBindingBase
{
    private const double DefaultRowBeamSize = 32.64;
    private string _mag;
    private string _div;

    public ST_MONITOR_BET_TABLE_ROW(
        string no,
        string description,
        string mag,
        string div,
        string state,
        bool isSelected = false)
    {
        No = no;
        Description = description;
        _mag = mag;
        _div = div;
        State = state;
        IsSelected = isSelected;
    }

    public string No { get; set; }

    public string Description { get; set; }

    public string Mag
    {
        get
        {
            return _mag;
        }

        set
        {
            if (_mag == value)
            {
                return;
            }

            _mag = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpotSize));
        }
    }

    public string Div
    {
        get
        {
            return _div;
        }

        set
        {
            if (_div == value)
            {
                return;
            }

            _div = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpotSize));
        }
    }

    public string SpotSize
    {
        get
        {
            return CalculateSpotSizeText(Mag);
        }
    }

    public string State { get; set; }

    public bool IsSelected { get; set; }

    public string MoveCommandLabel
    {
        get
        {
            return $"MOVE TABLE {No}";
        }
    }

    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch64()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "ACTIVE" or "SELECTED" or "OK":
                        return CStatusBrush.Online;
                    case "WARN":
                        return CStatusBrush.Wait;
                    case "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueSwitch64();
        }
    }

    public Brush RowBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }

    private static string CalculateSpotSizeText(string mag)
    {
        if (!double.TryParse(mag, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value == 0.0)
        {
            return "0.001000";
        }

        return (DefaultRowBeamSize / value / 1000.0).ToString("F6", CultureInfo.InvariantCulture);
    }

}

public sealed record ST_PWM_PROCESS_ROW(
    string No,
    string ProcessName,
    string Use,
    string State,
    string AveragePower,
    bool IsSelected = false)
{
    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch65()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "LOADED" or "READY":
                        return CStatusBrush.Online;
                    case "WAIT":
                        return CStatusBrush.Wait;
                    case "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueSwitch65();
        }
    }

    public Brush UseBrush
    {
        get
        {
            return Use.Trim().ToUpperInvariant() == "ON"
        ? CStatusBrush.Online
        : CStatusBrush.Muted;
        }
    }

    public Brush RowBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_PWM_STEP_ROW(
    string Step,
    string OptionName,
    string PowerOut,
    string PowerUnit,
    string SettingAtt,
    string SettingPower,
    string SettingFreq,
    string MeasureCycle,
    string MeasureTime,
    string MeasureInterval,
    string StartDelay,
    string CycleDelay,
    string Rotator,
    string MeasurePower,
    string State,
    bool IsSelected = false)
{
    public Brush PowerBrush
    {
        get
        {
            return PowerOut.Trim().ToUpperInvariant() == "ON"
        ? CStatusBrush.Online
        : CStatusBrush.Muted;
        }
    }

    public Brush StateBrush
    {
        get
        {
            Brush EvaluateValueSwitch66()
            {
                var switchValue = State.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "READY" or "OK":
                        return CStatusBrush.Online;
                    case "RUN" or "RUNNING":
                        return CStatusBrush.Simul;
                    case "SKIP":
                        return CStatusBrush.Muted;
                    case "ERROR":
                        return CStatusBrush.Offline;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateValueSwitch66();
        }
    }

    public Brush RowBrush
    {
        get
        {
            return IsSelected ? CStatusBrush.Active : CStatusBrush.PrimaryText;
        }
    }
}

public sealed class ST_PWM_SETTING_ROW(
    string parameter,
    string value,
    string unit) : CBindingBase
{
    private string _value = value;

    public string Parameter { get; } = parameter;

    public string Value
    {
        get
        {
            return _value;
        }

        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public string Unit { get; } = unit;

    public IReadOnlyList<string> ValueOptions
    {
        get
        {
            IReadOnlyList<string> EvaluateValueSwitch67()
            {
                var switchValue = Parameter.ToUpperInvariant();
                switch (switchValue)
                {
                    case "POWER OUT":
                        return ["ON", "OFF"];
                    case "POWER UNIT":
                        return ["W", "mW"];
                    default:
                        return [];
                }
            }

            return EvaluateValueSwitch67();
        }
    }

    public bool UsesSelectionEditor
    {
        get
        {
            return ValueOptions.Count > 0;
        }
    }

    public EN_RECIPE_DATA_TYPE DataType
    {
        get
        {
            EN_RECIPE_DATA_TYPE EvaluateValueSwitch68()
            {
                var switchValue = Parameter.ToUpperInvariant();
                switch (switchValue)
                {
                    case "OPTION NAME":
                        return EN_RECIPE_DATA_TYPE.String;
                    case "MEASURE CYCLE" or "MEASURE TIME" or "MEASURE INTERVAL" or "START DELAY" or "COOLING TIME":
                        return EN_RECIPE_DATA_TYPE.Int;
                    default:
                        return EN_RECIPE_DATA_TYPE.Double;
                }
            }

            return EvaluateValueSwitch68();
        }
    }

    public Brush ValueBrush
    {
        get
        {
            return Parameter.Contains("POWER", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Contains("ATT", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Contains("FREQ", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Contains("WAVELENGTH", StringComparison.OrdinalIgnoreCase)
        ? CStatusBrush.Wait
        : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_PWM_DEVICE_ROW(
    string Item,
    string Value,
    string Unit,
    string Command)
{
    public Brush ValueBrush
    {
        get
        {
            return Item.Contains("POWER", StringComparison.OrdinalIgnoreCase) ||
        Item.Contains("WAVELENGTH", StringComparison.OrdinalIgnoreCase)
        ? CStatusBrush.Wait
        : CStatusBrush.PrimaryText;
        }
    }
}

public sealed record ST_MONITOR_TREND_POINT(
    string Time,
    double PrimaryY,
    double SecondaryY,
    double TertiaryY);

public sealed record ST_MONITOR_SUMMARY_ITEM(
    string Name,
    string Value,
    string Unit,
    string State = "Normal")
{
    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateStateSwitch69()
            {
                var switchValue = State;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateStateSwitch69();
        }
    }
}

public sealed record ST_MONITOR_POSITION_ROW(
    string Name,
    string Value,
    string Unit,
    string State = "Normal")
{
    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateStateSwitch70()
            {
                var switchValue = State;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateStateSwitch70();
        }
    }
}

internal static class CMonitorIcon
{
    private static readonly IReadOnlyDictionary<string, Geometry> Icons =
        new Dictionary<string, Geometry>
        {
            ["Laser"] = Icon("M12,2 V8 M12,16 V22 M2,12 H8 M16,12 H22 M5,5 L9,9 M15,15 L19,19 M19,5 L15,9 M9,15 L5,19"),
            ["Gate"] = Icon("M4,8 H20 M4,16 H20 M7,8 V16 M17,8 V16"),
            ["Shutter"] = Icon("M8,4 H16 V20 H8 Z M10,7 H14 M10,17 H14"),
            ["Reset"] = Icon("M18,9 C17,6 15,4 12,4 C8,4 5,7 5,11 C5,15 8,18 12,18 C15,18 17,16 18,14 M18,5 V9 H14"),
            ["Refresh"] = Icon("M18,9 C17,6 15,4 12,4 C8,4 5,7 5,11 C5,15 8,18 12,18 C15,18 17,16 18,14 M18,5 V9 H14"),
            ["Run"] = Icon("M8,5 L20,12 L8,19 Z"),
            ["Stop"] = Icon("M7,7 H17 V17 H7 Z"),
            ["Pump"] = Icon("M7,12 C7,8 10,6 13,7 C17,8 19,12 17,15 C14,19 8,17 7,12 M13,7 V3 M10,20 H16"),
            ["Temp"] = Icon("M10,14 V5 C10,3.9 10.9,3 12,3 C13.1,3 14,3.9 14,5 V14 C15.2,14.7 16,16 16,17.5 C16,19.7 14.2,21 12,21 C9.8,21 8,19.7 8,17.5 C8,16 8.8,14.7 10,14 M12,8 V17"),
            ["Move"] = Icon("M12,3 V21 M3,12 H21 M12,3 L9,6 M12,3 L15,6 M21,12 L18,9 M21,12 L18,15 M12,21 L9,18 M12,21 L15,18 M3,12 L6,9 M3,12 L6,15"),
            ["Home"] = Icon("M4,11 L12,4 L20,11 M6,10 V20 H10 V14 H14 V20 H18 V10"),
            ["Servo"] = Icon("M12,4 A8,8 0 1 1 12,20 A8,8 0 1 1 12,4 M12,8 V12 L16,14"),
            ["Abs"] = Icon("M5,12 H19 M15,8 L19,12 L15,16 M9,8 L5,12 L9,16"),
            ["Rel"] = Icon("M6,6 H12 V12 H18 M18,12 L14,8 M18,12 L14,16"),
            ["Alarm"] = Icon("M12,3 L22,20 H2 Z M12,8 V13 M12,17 V18"),
            ["Measure"] = Icon("M4,18 L9,8 L13,14 L17,5 L20,10 M4,20 H20"),
            ["Wave"] = Icon("M3,12 C5,6 8,6 10,12 C12,18 15,18 17,12 C18,9 20,8 21,8"),
            ["Position"] = Icon("M12,3 A7,7 0 0 1 19,10 C19,15 12,21 12,21 C12,21 5,15 5,10 A7,7 0 0 1 12,3 M12,8 A2,2 0 1 1 12,12 A2,2 0 1 1 12,8"),
            ["Add"] = Icon("M12,5 V19 M5,12 H19"),
            ["Delete"] = Icon("M5,7 H19 M9,7 V5 H15 V7 M8,10 V19 M12,10 V19 M16,10 V19 M7,7 L8,21 H16 L17,7"),
            ["Save"] = Icon("M5,4 H17 L20,7 V20 H4 V4 H5 M8,4 V10 H16 V4 M8,20 V15 H16 V20"),
            ["Edit"] = Icon("M4,17 V20 H7 L18,9 L15,6 L4,17 M14,7 L17,10")
        };

    public static Geometry Get(string icon)
    {
        return Icons.TryGetValue(icon, out var geometry)
            ? geometry
            : Geometry.Empty;
    }

    private static Geometry Icon(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}





