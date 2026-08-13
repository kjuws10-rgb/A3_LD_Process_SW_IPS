using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Drilling.UI.Menu;
using Drilling.UI.Popup;
using Drilling.UI.Menu.Menus;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Product;
using Drilling.Common.Review;
using Drilling.Common.Station;

namespace Drilling.UI;

public sealed class CRootView : CBindingBase
{
    private static readonly TimeSpan MainLiveStatusRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MainLiveStatusTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MonitorChillerRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MonitorDefaultRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorReportInterval = TimeSpan.FromSeconds(10);

    private static readonly IReadOnlyList<EN_MENU> OperatorMenus =
    [
        EN_MENU.Main,
        EN_MENU.Manual,
        EN_MENU.Recipe,
        EN_MENU.Setting,
        EN_MENU.Alarm,
        EN_MENU.Monitor,
        EN_MENU.Review,
        EN_MENU.Correction,
        EN_MENU.Pm,
        EN_MENU.Exit
    ];

    private static readonly IReadOnlyList<EN_EQP_MODULE> HeaderModules =
    [
        EN_EQP_MODULE.WonikCtrl,
        EN_EQP_MODULE.Vision,
        EN_EQP_MODULE.Automation1,
        EN_EQP_MODULE.Motion,
        EN_EQP_MODULE.TalonLaser,
        EN_EQP_MODULE.Chiller,
        EN_EQP_MODULE.Attenuator,
        EN_EQP_MODULE.Bet
    ];

    private readonly IStationManager _stationManager;
    private readonly CManager _manager;
    private readonly IInterfaceManager _interfaceManager;
    private readonly IMotionManager _motionManager;
    private readonly CAlarmManager _alarmManager;
    private readonly CInterLockManager _interLockManager;
    private readonly IRecipeManager _recipeManager;
    private readonly IReadOnlyDictionary<EN_MENU, IMenu> _menus;
    private readonly Dictionary<EN_EQP_MODULE, int> _selectedHeaderModuleIndexes = new();

    private CMenuItem _selectedMenu;
    private CScreenViewModel _currentScreen;
    private ST_SYSTEM_STATUS _systemStatus = CreateFallbackST_SYSTEM_STATUS();
    private IReadOnlyList<ST_INTERFACE_COMM_STATUS> _interfaceCommStatuses = [];
    private string _statusMessage = "Simulation mode ready.";
    private string _currentDateText = DateTime.Now.ToString("yyyy-MM-dd");
    private string _currentTimeText = DateTime.Now.ToString("HH:mm:ss");
    private int _selectedHeadNo = 4;
    private readonly HashSet<int> _selectedPreviewHeadNos = [];
    private string _selectedManualSettingName = "CIRCLE_TEST.scan";
    private string _selectedRecipeId = "";
    private string _selectedRecipeCategory = "ALL";
    private string _selectedSettingTab = "OPTION";
    private string _selectedSettingGroup = "ALL";
    private string _selectedMonitorTab = "IO";
    private EN_UI_THEME _currentTheme = EN_UI_THEME.Dark;
    private bool _statusRefreshRunning;
    private bool _screenRefreshRunning;
    private bool _monitorLiveRefreshRunning;
    private bool _mainLiveRefreshRunning;
    private bool _mainLiveStatusErrorActive;
    private CancellationTokenSource? _mainLiveStatusRefreshCts;
    private long _lastMonitorScreenRefreshTimestamp;
    private long _lastMainLiveStatusRefreshTimestamp;
    private long _lastMainLiveStatusErrorReportTimestamp;
    private long _lastClockTickErrorReportTimestamp;
    private string _lastMainLiveStatusError = "";
    private string _lastClockTickError = "";
    private ST_PM_LOCK_STATUS _pmLockStatus = new(false, null);

    public CRootView(
        CManager manager,
        IStationManager stationManager,
        IInterfaceManager interfaceManager,
        IMotionManager motionManager,
        CAlarmManager alarmManager,
        CInterLockManager interLockManager,
        IManualScanFile manualScanFile,
        IRecipeManager recipeManager,
        ISettingManager settingManager,
        IProductManager productManager,
        IReviewManager reviewManager,
        IReviewRuleFile reviewRuleFile,
        IAutomationScriptFile automationScriptFile)
    {
        _manager = manager;
        _stationManager = stationManager;
        _interfaceManager = interfaceManager;
        _motionManager = motionManager;
        _alarmManager = alarmManager;
        _interLockManager = interLockManager;
        _recipeManager = recipeManager;
        CThemeManager.Apply(_currentTheme);
        CMenuItem SelectMenu1(EN_MENU menu)
        {
            return new CMenuItem(menu, GetMenuDisplayName(menu));
        }

        Menus = new ObservableCollection<CMenuItem>(
            OperatorMenus.Select(SelectMenu1));
        _selectedMenu = Menus[0];
        HeaderStatusItems = new ObservableCollection<ST_HEADER_STATUS_ITEM>();
        FooterStatusItems = new ObservableCollection<ST_HEADER_STATUS_ITEM>();
        RefreshShellStatusItems();

        _currentScreen = CreateLoadingScreen(EN_MENU.Main, "MAIN");

        async void HandleStartCommand2(object? _)
        {
            await StartCycle();
        }

        bool HandleStartCommand3(object? _)
        {
            return SelectedMenu.Menu == EN_MENU.Main;
        }

        StartCommand = new CButtonCommand(HandleStartCommand2, HandleStartCommand3);

        async void HandleStopCommand4(object? _)
        {
            await StopCycle();
        }

        bool HandleStopCommand5(object? _)
        {
            return SelectedMenu.Menu == EN_MENU.Main;
        }

        StopCommand = new CButtonCommand(HandleStopCommand4, HandleStopCommand5);

        async void HandleSelectHeadCommand6(object? parameter)
        {
            await SelectHead(parameter);
        }

        bool HandleSelectHeadCommand7(object? _)
        {
            return CanSelectHead;
        }

        SelectHeadCommand = new CButtonCommand(HandleSelectHeadCommand6, HandleSelectHeadCommand7);

        async void HandleTogglePreviewHeadCommand8(object? parameter)
        {
            await TogglePreviewHead(parameter);
        }

        bool HandleTogglePreviewHeadCommand9(object? _)
        {
            return SelectedMenu.Menu == EN_MENU.Main;
        }

        TogglePreviewHeadCommand = new CButtonCommand(
HandleTogglePreviewHeadCommand8,
HandleTogglePreviewHeadCommand9);

        async void HandleToggleThemeCommand10(object? _)
        {
            await ToggleTheme();
        }

        ToggleThemeCommand = new CButtonCommand(HandleToggleThemeCommand10);

        _menus = CreateMenus(
            stationManager,
            interfaceManager,
            motionManager,
            alarmManager,
            interLockManager,
            manualScanFile,
            recipeManager,
            settingManager,
            productManager,
            reviewManager,
            reviewRuleFile,
            automationScriptFile);

        StartClock();
        _ = PrepareInitialProcessPlan();
    }

    public ObservableCollection<CMenuItem> Menus { get; }

    public ObservableCollection<ST_HEADER_STATUS_ITEM> HeaderStatusItems { get; }

    public ObservableCollection<ST_HEADER_STATUS_ITEM> FooterStatusItems { get; }

    public CMenuItem SelectedMenu
    {
        get
        {
            return _selectedMenu;
        }

        set
        {
            var previousMenu = _selectedMenu.Menu;
            if (!SetProperty(ref _selectedMenu, value))
            {
                return;
            }

            if (previousMenu == EN_MENU.Main && value.Menu != EN_MENU.Main)
            {
                CancelMainLiveStatusRefresh();
            }

            ResetMenuStateOnOpen(previousMenu, value.Menu);
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            SelectHeadCommand.NotifyCanExecuteChanged();
            TogglePreviewHeadCommand.NotifyCanExecuteChanged();
            RefreshShellStatusItems();
            _ = RefreshCurrentScreen();
        }
    }

    public CScreenViewModel CurrentScreen
    {
        get
        {
            return _currentScreen;
        }

        private set
        {
            SetProperty(ref _currentScreen, value);
        }
    }

    public string StatusMessage
    {
        get
        {
            return _statusMessage;
        }

        private set
        {
            SetProperty(ref _statusMessage, value);
        }
    }

    public string CurrentDateText
    {
        get
        {
            return _currentDateText;
        }

        private set
        {
            SetProperty(ref _currentDateText, value);
        }
    }

    public string CurrentTimeText
    {
        get
        {
            return _currentTimeText;
        }

        private set
        {
            SetProperty(ref _currentTimeText, value);
        }
    }

    public string CurrentUserText
    {
        get
        {
            return _systemStatus.OperationMode == EN_SYSTEM_MODE.Simulation
        ? "Engineer / Simulation"
        : "Engineer / Live";
        }
    }

    public string ThemeToggleText
    {
        get
        {
            return _currentTheme == EN_UI_THEME.Light
        ? "DARK"
        : "LIGHT";
        }
    }

    public string ThemeModeText
    {
        get
        {
            return _currentTheme == EN_UI_THEME.Light
        ? "Light Theme"
        : "Dark Theme";
        }
    }

    public CButtonCommand StartCommand { get; }

    public CButtonCommand StopCommand { get; }

    public CButtonCommand SelectHeadCommand { get; }

    public CButtonCommand TogglePreviewHeadCommand { get; }

    public CButtonCommand ToggleThemeCommand { get; }

    private bool CanSelectHead
    {
        get
        {
            return SelectedMenu.Menu is EN_MENU.Main or EN_MENU.Manual;
        }
    }

    private async Task RefreshCurrentScreen()
    {
        if (_screenRefreshRunning)
        {
            return;
        }

        _screenRefreshRunning = true;

        try
        {
            if (!_menus.TryGetValue(SelectedMenu.Menu, out var menu))
            {
                CurrentScreen = CreateLoadingScreen(SelectedMenu.Menu, SelectedMenu.Name);
                return;
            }

            CurrentScreen = await menu.Build();

            if (SelectedMenu.Menu == EN_MENU.Recipe)
            {
                SyncSelectedRecipeIdFromScreen();
            }

            if (SelectedMenu.Menu == EN_MENU.Manual)
            {
                SyncSelectedManualSettingFromScreen();
            }

            if (SelectedMenu.Menu == EN_MENU.Setting)
            {
                SyncSelectedSettingSelectionFromScreen();
            }

            _systemStatus = await GetSystemStatus();
            RefreshShellStatusItems();
            if (SelectedMenu.Menu == EN_MENU.Pm)
            {
                StatusMessage = "PM lock entered. Operation commands are blocked.";
            }
        }
        finally
        {
            _screenRefreshRunning = false;
        }
    }

    private void ResetMenuStateOnOpen(
        EN_MENU previousMenu,
        EN_MENU selectedMenu)
    {
        if (selectedMenu != EN_MENU.Review || previousMenu == EN_MENU.Review)
        {
            return;
        }

        if (_menus.TryGetValue(EN_MENU.Review, out var menu) && menu is CMenuReview reviewMenu)
        {
            reviewMenu.ResetForMenuOpen();
        }
    }

    private async Task ToggleTheme()
    {
        _currentTheme = CThemeManager.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(ThemeModeText));
        RefreshShellStatusItems();
        await RefreshCurrentScreen();
        StatusMessage = $"{ThemeModeText} applied.";
    }

    private void RefreshShellStatusItems()
    {
        Replace(HeaderStatusItems, CreateHeaderStatusItems());
        Replace(FooterStatusItems, CreateFooterStatusItems(SelectedMenu.Menu));
        OnPropertyChanged(nameof(CurrentUserText));
    }

    private async Task RefreshSystemStatus()
    {
        if (_statusRefreshRunning)
        {
            return;
        }

        _statusRefreshRunning = true;

        try
        {
            _systemStatus = await GetSystemStatus();
            RefreshShellStatusItems();
        }
        catch
        {
            // Communication polling must not stop the operator UI.
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    private async Task RefreshMonitorLiveData()
    {
        if (_monitorLiveRefreshRunning)
        {
            return;
        }

        _monitorLiveRefreshRunning = true;

        try
        {
            if (_menus.TryGetValue(EN_MENU.Monitor, out var menu) && menu is CMenuMonitor monitor)
            {
                await monitor.RefreshLiveData();
            }

            await RefreshShellCommunicationStatus();
        }
        catch
        {
            // Live monitor polling must not block the operator UI.
        }
        finally
        {
            _monitorLiveRefreshRunning = false;
        }
    }

    private async Task RefreshMainLiveStatus()
    {
        if (_mainLiveRefreshRunning)
        {
            return;
        }

        var startedScreen = CurrentScreen;
        var startedMainOperating = startedScreen.MainOperating;
        if (SelectedMenu.Menu != EN_MENU.Main ||
            startedMainOperating is null)
        {
            return;
        }

        _mainLiveRefreshRunning = true;
        CancellationTokenSource? refreshCts = null;

        try
        {
            refreshCts = new CancellationTokenSource(MainLiveStatusTimeout);
            _mainLiveStatusRefreshCts = refreshCts;

            await startedMainOperating.RefreshLiveStatus(refreshCts.Token);
            if (IsMainLiveStatusTargetCurrent(startedScreen, startedMainOperating))
            {
                ReportMainLiveStatusRestored();
            }
        }
        catch (OperationCanceledException) when (!IsMainLiveStatusTargetCurrent(startedScreen, startedMainOperating))
        {
            // Screen transition cancels stale Main polling without changing the active UI.
        }
        catch (Exception exception) when (IsMainLiveStatusTargetCurrent(startedScreen, startedMainOperating))
        {
            ReportMainLiveStatusError(exception);
        }
        catch (Exception exception)
        {
            ReportStaleMainLiveStatusError(exception);
        }
        finally
        {
            if (refreshCts is not null)
            {
                if (ReferenceEquals(_mainLiveStatusRefreshCts, refreshCts))
                {
                    _mainLiveStatusRefreshCts = null;
                }

                refreshCts.Dispose();
            }

            _mainLiveRefreshRunning = false;
        }
    }

    private void CancelMainLiveStatusRefresh()
    {
        try
        {
            _mainLiveStatusRefreshCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed polling request may dispose at the same time as menu navigation.
        }
    }

    private bool IsMainLiveStatusTargetCurrent(
        CScreenViewModel startedScreen,
        CMenuMain? startedMainOperating)
    {
        return SelectedMenu.Menu == EN_MENU.Main &&
            ReferenceEquals(CurrentScreen, startedScreen) &&
            ReferenceEquals(CurrentScreen.MainOperating, startedMainOperating);
    }

    private void ReportMainLiveStatusError(Exception exception)
    {
        var nowTimestamp = Stopwatch.GetTimestamp();
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        var shouldLog =
            !detail.Equals(_lastMainLiveStatusError, StringComparison.Ordinal) ||
            HasElapsed(_lastMainLiveStatusErrorReportTimestamp, nowTimestamp, ErrorReportInterval);

        _mainLiveStatusErrorActive = true;
        StatusMessage = $"Main live status refresh failed. {detail}";

        if (!shouldLog)
        {
            return;
        }

        _lastMainLiveStatusError = detail;
        _lastMainLiveStatusErrorReportTimestamp = nowTimestamp;
        QueueStationStateLog("MAIN_LIVE_STATUS", "ERROR", detail);
    }

    private void ReportStaleMainLiveStatusError(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        var detail = $"{exception.GetType().Name}: {exception.Message}";
        QueueStationStateLog(
            "MAIN_LIVE_STATUS",
            "STALE_ERROR",
            $"Stale Main live status failure ignored for UI. {detail}");
    }

    private void ReportMainLiveStatusRestored()
    {
        if (!_mainLiveStatusErrorActive)
        {
            return;
        }

        _mainLiveStatusErrorActive = false;
        _lastMainLiveStatusError = "";
        StatusMessage = "Main live status refresh restored.";
        QueueStationStateLog("MAIN_LIVE_STATUS", "INFO", "Main live status refresh restored.");
    }

    private void ReportClockTickError(Exception exception)
    {
        var nowTimestamp = Stopwatch.GetTimestamp();
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        var shouldLog =
            !detail.Equals(_lastClockTickError, StringComparison.Ordinal) ||
            HasElapsed(_lastClockTickErrorReportTimestamp, nowTimestamp, ErrorReportInterval);

        StatusMessage = $"UI timer refresh failed. {detail}";

        if (!shouldLog)
        {
            return;
        }

        _lastClockTickError = detail;
        _lastClockTickErrorReportTimestamp = nowTimestamp;
        QueueStationStateLog("UI_CLOCK", "ERROR", detail);
    }

    private void QueueStationStateLog(
        string stateName,
        string action,
        string detail)
    {
        var logManager = _manager.Log();
        void RunTask11()
        {
            try
            {
                logManager.WriteStationState("UI", stateName, action, detail);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Station state log failed. {exception}");
            }
        }
        _ = Task.Run(RunTask11);
    }

    private static bool HasElapsed(
        long lastTimestamp,
        long currentTimestamp,
        TimeSpan interval)
    {
        if (lastTimestamp <= 0)
        {
            return true;
        }

        var elapsedTicks = currentTimestamp - lastTimestamp;
        var requiredTicks = interval.TotalSeconds * Stopwatch.Frequency;

        return elapsedTicks >= requiredTicks;
    }

    private async Task RefreshShellCommunicationStatus(CancellationToken cancellationToken = default)
    {
        var modules = await _interfaceManager.GetCommunicationStatus(cancellationToken);
        _interfaceCommStatuses = _interfaceManager.GetInterfaceCommunicationList();
        _systemStatus = _systemStatus with
        {
            CurrentRecipeId = string.IsNullOrWhiteSpace(_selectedRecipeId) ? "DRILL_A01" : _selectedRecipeId,
            OperationMode = _interfaceManager.IsSimulation && _motionManager.IsSimulation
                ? EN_SYSTEM_MODE.Simulation
                : EN_SYSTEM_MODE.Auto,
            Modules = NormalizeCommunicationStatuses(modules)
        };
        RefreshShellStatusItems();
    }

    private async Task<ST_SYSTEM_STATUS> GetSystemStatus(CancellationToken cancellationToken = default)
    {
        var modules = await _interfaceManager.GetCommunicationStatus(cancellationToken);
        _interfaceCommStatuses = _interfaceManager.GetInterfaceCommunicationList();
        var alarms = await GetCurrentAlarms(cancellationToken);

        return new ST_SYSTEM_STATUS(
            string.IsNullOrWhiteSpace(_selectedRecipeId) ? "DRILL_A01" : _selectedRecipeId,
            _interfaceManager.IsSimulation && _motionManager.IsSimulation ? EN_SYSTEM_MODE.Simulation : EN_SYSTEM_MODE.Auto,
            alarms.Count > 0 ? EN_ALARM_STATE.Occur : EN_ALARM_STATE.Clear,
            _pmLockStatus.IsLocked ? EN_PM_LOCK_STATE.Locked : EN_PM_LOCK_STATE.Released,
            NormalizeCommunicationStatuses(modules));
    }

    private async Task<IReadOnlyList<ST_ALARM_DATA>> GetCurrentAlarms(CancellationToken cancellationToken)
    {
        var snapshot = await GetDeviceStatus(cancellationToken);
        var interLock = _interLockManager.Evaluate(snapshot);
        return _alarmManager.Build(snapshot, interLock);
    }

    private async Task<ST_DEVICE_STATUS> GetDeviceStatus(CancellationToken cancellationToken)
    {
        var io = await _motionManager.GetIoStatus(cancellationToken);
        var motors = await _motionManager.GetAxisStatus(cancellationToken);
        var laserStatus = await _interfaceManager.GetLaserStatus(cancellationToken);
        var chillerStatus = await _interfaceManager.GetChillerStatus(cancellationToken);
        var attenuatorStatus = await _interfaceManager.GetAttenuatorStatus(cancellationToken);
        var betStatus = await _interfaceManager.GetBETStatus(cancellationToken);
        var powerMeterStatus = await _interfaceManager.GetPowerMeterStatus(cancellationToken);

        return new ST_DEVICE_STATUS(
            io,
            motors,
            laserStatus,
            chillerStatus,
            attenuatorStatus,
            betStatus,
            powerMeterStatus);
    }

    private ST_PM_LOCK_STATUS GetPMLockStatus()
    {
        return _pmLockStatus;
    }

    private void EnterPMLock()
    {
        if (_pmLockStatus.IsLocked)
        {
            return;
        }

        _pmLockStatus = new ST_PM_LOCK_STATUS(true, DateTimeOffset.Now);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private IReadOnlyList<ST_HEADER_STATUS_ITEM> CreateHeaderStatusItems()
    {
        var status = _systemStatus;
        var alarmState = AlarmStateValue(status.AlarmState);

        var items = new List<ST_HEADER_STATUS_ITEM>
        {
            new("RECIPE", GetHeaderRecipeId(status), "RECIPE"),
            new("MODE", OperationModeValue(status.OperationMode), OperationModeState(status.OperationMode))
        };
        ST_HEADER_STATUS_ITEM SelectModule12(EN_EQP_MODULE module)
        {
            return ModuleHeader(status, module);
        }

        items.AddRange(HeaderModules.Select(SelectModule12));
        items.Add(new ST_HEADER_STATUS_ITEM("ALARM", alarmState, alarmState));

        return items;
    }

    private IReadOnlyList<ST_HEADER_STATUS_ITEM> CreateFooterStatusItems(EN_MENU menu)
    {
        if (menu == EN_MENU.Manual)
        {
            return
            [
                new("STATE", "Manual Setting Loaded", "SIM"),
                new("HEAD", $"H{_selectedHeadNo:00} Selected", "SIM"),
                new("SCRIPT", "Script Created", "ONLINE"),
                new("TASK", "Task Running", "WAIT"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Recipe)
        {
            bool CheckItem13(ST_RECIPE_MANAGED_ITEM item)
            {
                return item.IsEdited;
            }

            var hasRecipeChanges = CurrentScreen.Recipe?.AllManagedItems.Any(CheckItem13) == true;

            return
            [
                new("STATE", hasRecipeChanges ? "Recipe Modified" : "Recipe Loaded", hasRecipeChanges ? "WAIT" : "ONLINE"),
                new("RECIPE", GetSelectedRecipeFileText(), "SIM"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Setting)
        {
            bool HandleModifiedCount14(ST_SYSTEM_PARAMETER_ROW item)
            {
                return item.IsModified;
            }

            var modifiedCount = CurrentScreen.Setting?.AllParameterRows.Count(HandleModifiedCount14) ?? 0;

            return
            [
                new("STATE", modifiedCount > 0 ? "SETTING Modified" : "SETTING Loaded", modifiedCount > 0 ? "WAIT" : "ONLINE"),
                new("GROUP", $"{_selectedSettingTab} / {_selectedSettingGroup}", "SIM"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Alarm)
        {
            var alarmState = AlarmStateValue(_systemStatus.AlarmState);

            return
            [
                new("STATE", $"Alarm {alarmState}", alarmState),
                new("BUZZER", "Buzzer OFF", "SIM"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Monitor)
        {
            return CreateMonitorFooterStatusItems();
        }

        if (menu == EN_MENU.Review)
        {
            return
            [
                new("SCREEN", "REVIEW / INSPECTION", "SIM"),
                new("POINT", "Review Standby", "WAIT"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Correction)
        {
            return
            [
                new("SCREEN", "CORRECTION / OFFSET", "SIM"),
                new("TABLE", "Correction Standby", "WAIT"),
                new("SIM", "Simulation PASS", "ONLINE")
            ];
        }

        if (menu == EN_MENU.Pm)
        {
            var isLocked = _systemStatus.PMLockState == EN_PM_LOCK_STATE.Locked;

            return
            [
                new("PM", isLocked ? "PM Lock Active" : "PM Lock Ready", isLocked ? "WARN" : "ONLINE"),
                new("OPERATION", isLocked ? "Operation Disabled" : "Operation Enabled", isLocked ? "OCCUR" : "ONLINE")
            ];
        }

        return
        [
            new("SCRIPT", "Script Created", "ONLINE"),
            new("TASK", "Task Running", "SIM"),
            new("SIM", "Simulation PASS", "ONLINE"),
            new("MODE", "Auto Mode", "READY")
        ];
    }

    private async Task PrepareInitialProcessPlan()
    {
        var recipes = await _recipeManager.LoadRecipes();
        var selectedRecipe = FindSelectedRecipe(recipes);
        var recipeId = selectedRecipe?.Id ??
            (string.IsNullOrWhiteSpace(_selectedRecipeId) ? "DRILL_A01" : _selectedRecipeId);
        var parameters = CreateProcessParameters(selectedRecipe);
        var processPlan = new ST_PROCESS_PLAN(
            $"SCAN-{DateTime.Now:yyyyMMddHHmmss}",
            recipeId,
            "SIM-PRODUCT-001",
            "SIM-PANEL-001",
            "SIM-LOT-001",
            DateTimeOffset.Now,
            parameters);

        await _stationManager.PrepareProcessPlan(processPlan);
        StatusMessage = selectedRecipe is null
            ? "Process plan prepared with fallback data."
            : $"Process plan prepared from {recipeId}.csv.";
        await RefreshCurrentScreen();
    }

    private ST_RECIPE_DATA? FindSelectedRecipe(IReadOnlyList<ST_RECIPE_DATA> recipes)
    {
        if (recipes.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_selectedRecipeId))
        {
            bool MatchRecipe15(ST_RECIPE_DATA recipe)
            {
                return recipe.Id.Equals(_selectedRecipeId, StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyDictionary<string, string> CreateProcessParameters(ST_RECIPE_DATA? recipe)
    {
        bool FilterParameter17(ST_RECIPE_PARAM parameter)
        {
            return !string.IsNullOrWhiteSpace(parameter.Key);
        }

        string GroupByParameterCallback18(ST_RECIPE_PARAM parameter)
        {
            return parameter.Key;
        }

        string HandleParameters19(IGrouping<string, ST_RECIPE_PARAM> group)
        {
            return group.Key;
        }

        string HandleParameters20(IGrouping<string, ST_RECIPE_PARAM> group)
        {
            return group.Last().Value;
        }

        var parameters = recipe?.Parameters
            .Where(FilterParameter17)
            .GroupBy(GroupByParameterCallback18, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
HandleParameters19,
HandleParameters20,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        parameters["ProductId"] = "SIM-PRODUCT-001";
        parameters["PanelId"] = "SIM-PANEL-001";
        parameters["LotId"] = "SIM-LOT-001";
        parameters.TryAdd("DIAMETER", "0.350");
        parameters.TryAdd("HEAD_COUNT", "8");

        return parameters;
    }

    private async Task StartCycle()
    {
        await PrepareInitialProcessPlan();
        await _stationManager.Start();
        StatusMessage = "Cycle completed in simulation mode.";
        await RefreshCurrentScreen();
    }

    private async Task StopCycle()
    {
        await _stationManager.Stop();
        StatusMessage = "Cycle stopped.";
        await RefreshCurrentScreen();
    }

    private async Task SelectHead(object? parameter)
    {
        int EvaluateParameterSwitch1()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case int value:
                    return value;
                case string text when int.TryParse(text, out var parsed):
                    return parsed;
                case ST_HEAD_PREVIEW head:
                    return head.HeadNo;
                default:
                    return _selectedHeadNo;
            }
        }

        var headNo = EvaluateParameterSwitch1();

        if (headNo <= 0)
        {
            return;
        }

        _selectedHeadNo = headNo;
        StatusMessage = $"HEAD {headNo:00} selected. Preview updated.";
        RefreshShellStatusItems();
        await RefreshCurrentScreen();
    }

    private async Task TogglePreviewHead(object? parameter)
    {
        if (parameter is string action)
        {
            if (action.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewHeadNos.Clear();
                foreach (var head in CurrentScreen.MainOperating?.HeadPreviews ?? [])
                {
                    _selectedPreviewHeadNos.Add(head.HeadNo);
                }

                UpdatePreviewHeadStatusMessage();
                await RefreshCurrentScreen();
                return;
            }

            if (action.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewHeadNos.Clear();
                UpdatePreviewHeadStatusMessage();
                await RefreshCurrentScreen();
                return;
            }
        }
        int EvaluateParameterSwitch2()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case int value:
                    return value;
                case string text when int.TryParse(text, out var parsed):
                    return parsed;
                case ST_HEAD_PREVIEW head:
                    return head.HeadNo;
                default:
                    return 0;
            }
        }

        var headNo = EvaluateParameterSwitch2();

        if (headNo <= 0)
        {
            return;
        }

        if (!_selectedPreviewHeadNos.Add(headNo))
        {
            _selectedPreviewHeadNos.Remove(headNo);
        }

        UpdatePreviewHeadStatusMessage();
        await RefreshCurrentScreen();
    }

    private void UpdatePreviewHeadStatusMessage()
    {
        int GetValueSortKey21(int value)
        {
            return value;
        }

        string SelectValue22(int value)
        {
            return $"H{value:00}";
        }

        StatusMessage = _selectedPreviewHeadNos.Count == 0
            ? "Head preview selection cleared."
            : $"Head preview: {string.Join(", ", _selectedPreviewHeadNos.OrderBy(GetValueSortKey21).Select(SelectValue22))}";
    }

    private void StartClock()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        async void TickHandler23(object? unusedParameter1, EventArgs unusedParameter2)
        {
            try
            {
                CurrentDateText = DateTime.Now.ToString("yyyy-MM-dd");
                CurrentTimeText = DateTime.Now.ToString("HH:mm:ss");

                var nowTimestamp = Stopwatch.GetTimestamp();
                var monitorRefreshInterval = _selectedMonitorTab == "CHILLER"
                    ? MonitorChillerRefreshInterval
                    : MonitorDefaultRefreshInterval;
                var shouldRefreshMonitorScreen =
                    SelectedMenu.Menu == EN_MENU.Monitor &&
                    HasElapsed(_lastMonitorScreenRefreshTimestamp, nowTimestamp, monitorRefreshInterval);
                var shouldRefreshMainLiveStatus =
                    SelectedMenu.Menu == EN_MENU.Main &&
                    HasElapsed(_lastMainLiveStatusRefreshTimestamp, nowTimestamp, MainLiveStatusRefreshInterval);

                if (shouldRefreshMonitorScreen)
                {
                    _lastMonitorScreenRefreshTimestamp = nowTimestamp;
                    await RefreshMonitorLiveData();
                    return;
                }

                if (shouldRefreshMainLiveStatus)
                {
                    _lastMainLiveStatusRefreshTimestamp = nowTimestamp;
                    await RefreshMainLiveStatus();
                    await RefreshSystemStatus();
                    return;
                }

                await RefreshSystemStatus();
            }
            catch (Exception exception)
            {
                ReportClockTickError(exception);
            }
        }
        timer.Tick += TickHandler23;

        timer.Start();
    }

    private IReadOnlyDictionary<EN_MENU, IMenu> CreateMenus(
        IStationManager stationManager,
        IInterfaceManager interfaceManager,
        IMotionManager motionManager,
        CAlarmManager alarmManager,
        CInterLockManager interLockManager,
        IManualScanFile manualScanFile,
        IRecipeManager recipeManager,
        ISettingManager settingManager,
        IProductManager productManager,
        IReviewManager reviewManager,
        IReviewRuleFile reviewRuleFile,
        IAutomationScriptFile automationScriptFile)
    {
        string HandleMenus24()
        {
            return _selectedRecipeId;
        }

        IReadOnlySet<int> HandleMenus25()
        {
            return _selectedPreviewHeadNos;
        }

        void HandleMenus26(string message)
        {
            StatusMessage = message;
        }

        int HandleMenus27()
        {
            return _selectedHeadNo;
        }

        string HandleMenus28()
        {
            return _selectedManualSettingName;
        }

        void HandleMenus29(string value)
        {
            _selectedManualSettingName = value;
        }

        void HandleMenus30(string message)
        {
            StatusMessage = message;
        }

        string HandleMenus31()
        {
            return _selectedRecipeId;
        }

        void HandleMenus32(string value)
        {
            _selectedRecipeId = value;
        }

        string HandleMenus33()
        {
            return _selectedRecipeCategory;
        }

        void HandleMenus34(string value)
        {
            _selectedRecipeCategory = value;
        }

        CMenuRecipe? HandleMenus35()
        {
            return CurrentScreen.Recipe;
        }

        void HandleMenus36(string message)
        {
            StatusMessage = message;
        }

        void HandleMenus37(EN_MENU menu, string title)
        {
            CurrentScreen = CreateLoadingScreen(menu, title);
        }

        string HandleMenus38()
        {
            return _selectedSettingTab;
        }

        void HandleMenus39(string value)
        {
            _selectedSettingTab = value;
        }

        string HandleMenus40()
        {
            return _selectedSettingGroup;
        }

        void HandleMenus41(string value)
        {
            _selectedSettingGroup = value;
        }

        CMenuSetting? HandleMenus42()
        {
            return CurrentScreen.Setting;
        }

        void HandleMenus43(string message)
        {
            StatusMessage = message;
        }

        void HandleMenus44(EN_MENU menu, string title)
        {
            CurrentScreen = CreateLoadingScreen(menu, title);
        }

        void HandleMenus45(string message)
        {
            StatusMessage = message;
        }

        string HandleMenus46()
        {
            return _selectedRecipeId;
        }

        string HandleMenus47()
        {
            return _selectedMonitorTab;
        }

        void HandleMenus48(string value)
        {
            _selectedMonitorTab = value;
        }

        void HandleMenus49(string message)
        {
            StatusMessage = message;
        }

        string HandleMenus50()
        {
            return _selectedRecipeId;
        }

        void HandleMenus51(string message)
        {
            StatusMessage = message;
        }

        void HandleMenus52()
        {
            _ = RefreshCurrentScreen();
        }

        string HandleMenus53()
        {
            return _selectedRecipeId;
        }

        void HandleMenus54(string message)
        {
            StatusMessage = message;
        }

        IMenu[] menus =
        [
            new CMenuMain(
                stationManager,
                interfaceManager,
                _manager.Automation(),
                recipeManager,
                settingManager,
                reviewManager,
HandleMenus24,
HandleMenus25,
                TogglePreviewHeadCommand,
HandleMenus26,
                RefreshCurrentScreen),
            new CMenuManual(
                _manager,
                manualScanFile,
                automationScriptFile,
HandleMenus27,
HandleMenus28,
HandleMenus29,
                SelectHeadCommand,
HandleMenus30,
                RefreshShellStatusItems,
                RefreshCurrentScreen),
            new CMenuRecipe(
                recipeManager,
                settingManager,
HandleMenus31,
HandleMenus32,
HandleMenus33,
HandleMenus34,
HandleMenus35,
HandleMenus36,
HandleMenus37,
                RefreshShellStatusItems,
                RefreshCurrentScreen),
            new CMenuSetting(
                settingManager,
HandleMenus38,
HandleMenus39,
HandleMenus40,
HandleMenus41,
HandleMenus42,
HandleMenus43,
HandleMenus44,
                RefreshShellStatusItems,
                RefreshCurrentScreen),
            new CMenuAlarm(
                interfaceManager,
                motionManager,
                alarmManager,
                interLockManager,
                stationManager,
HandleMenus45,
                RefreshShellStatusItems,
                RefreshCurrentScreen),
            new CMenuMonitor(
                interfaceManager,
                motionManager,
                interLockManager,
                productManager,
                recipeManager,
                settingManager,
HandleMenus46,
HandleMenus47,
HandleMenus48,
HandleMenus49,
                RefreshShellStatusItems,
                RefreshCurrentScreen),
            new CMenuReview(
                reviewManager,
                reviewRuleFile,
                recipeManager,
HandleMenus50,
HandleMenus51,
HandleMenus52),
            new CMenuCorrection(
                _manager.ReviewResultFile(),
                recipeManager,
                settingManager,
HandleMenus53,
HandleMenus54,
                RefreshCurrentScreen),
            new CMenuPm(GetPMLockStatus, EnterPMLock),
            new CMenuExit()
        ];
        EN_MENU ToDictionaryMenuCallback55(IMenu menu)
        {
            return menu.Menu;
        }

        return menus.ToDictionary(ToDictionaryMenuCallback55);
    }

    private static CScreenViewModel CreateLoadingScreen(EN_MENU menu, string title)
    {
        return new CScreenViewModel(
            menu,
            title,
            "Loading screen state.",
            [],
            []);
    }

    private string GetHeaderRecipeId(ST_SYSTEM_STATUS status)
    {
        return string.IsNullOrWhiteSpace(_selectedRecipeId)
            ? status.CurrentRecipeId
            : _selectedRecipeId;
    }

    private void SyncSelectedRecipeIdFromScreen()
    {
        var recipeFile = CurrentScreen.Recipe?.SelectedRecipeFile ?? "";
        var recipeId = Path.GetFileNameWithoutExtension(recipeFile.Trim());

        if (!string.IsNullOrWhiteSpace(recipeId))
        {
            _selectedRecipeId = recipeId;
        }
    }

    private void SyncSelectedManualSettingFromScreen()
    {
        var settingName = CurrentScreen.Manual?.LoadedSettingName?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(settingName))
        {
            _selectedManualSettingName = settingName;
        }
    }

    private void SyncSelectedSettingSelectionFromScreen()
    {
        if (CurrentScreen.Setting is null)
        {
            return;
        }

        _selectedSettingTab = CurrentScreen.Setting.SelectedTab;
        _selectedSettingGroup = CurrentScreen.Setting.SelectedGroup;
    }

    private string GetSelectedRecipeFileText()
    {
        if (CurrentScreen.Recipe is not null)
        {
            return CurrentScreen.Recipe.SelectedRecipeFile;
        }

        return string.IsNullOrWhiteSpace(_selectedRecipeId)
            ? "-"
            : $"{_selectedRecipeId}.csv";
    }

    private IReadOnlyList<ST_HEADER_STATUS_ITEM> CreateMonitorFooterStatusItems()
    {
        IReadOnlyList<ST_HEADER_STATUS_ITEM> Evaluate_selectedMonitorTabSwitch3()
        {
            var switchValue = _selectedMonitorTab;
            switch (switchValue)
            {
                case "MOTOR":
                    return [
                        new("SCREEN", "MONITOR / MOTOR", "SIM"),
                new("AXIS", "Selected Axis GX", "SIM"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "LASER":
                    return [
                        new("SCREEN", "MONITOR / LASER", "SIM"),
                new("LASER", "Laser SAFE", "ONLINE"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "CHILLER":
                    return [
                        new("SCREEN", "MONITOR / CHILLER", "SIM"),
                new("IO", "Selected IO -", "SIM"),
                new("CHILLER", "Chiller RUN", "ONLINE"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "ATTENUATOR":
                    return [
                        new("SCREEN", "MONITOR / ATTENUATOR", "SIM"),
                new("POSITION", "Position 55.000", "WARN"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "BET":
                    return [
                        new("SCREEN", "MONITOR / BET", "SIM"),
                new("BET", "MAG 1.000 / DIV 1.000", "ONLINE"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "PRODUCT":
                    return [
                        new("SCREEN", "MONITOR / PRODUCT", "SIM"),
                new("PRODUCT", "Product Tracking", "ONLINE"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "MELSEC":
                    return [
                        new("SCREEN", "MONITOR / MELSEC", "SIM"),
                new("PLC", "Read / Write Map", "WARN"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                case "COORDINATE VIEWER":
                    return [
                        new("SCREEN", "MONITOR / COORDINATE VIEWER", "SIM"),
                new("VIEWER", "Coordinate Viewer", "SIM"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
                default:
                    return [
                        new("SCREEN", $"MONITOR / {_selectedMonitorTab}", "SIM"),
                new("CONTROL", _selectedMonitorTab == "IO" ? "Direct ON/OFF Control" : "Status Monitor", "WARN"),
                new("SIM", "Simulation PASS", "ONLINE")
                    ];
            }
        }

        return Evaluate_selectedMonitorTabSwitch3();
    }

    private static string NormalizeMonitorTab(string tab)
    {
        var normalized = tab.Trim().ToUpperInvariant();
        string EvaluateNormalizedSwitch4()
        {
            var switchValue = normalized;
            switch (switchValue)
            {
                case "ATT":
                    return "ATTENUATOR";
                case "POWER" or "POWERMETER" or "POWER_METER":
                    return "POWER METER";
                case "COORDINATE" or "COORDINATE_VIEWER":
                    return "COORDINATE VIEWER";
                case "IO" or "MOTOR" or "LASER" or "CHILLER" or "ATTENUATOR" or "BET" or "POWER METER" or "PRODUCT" or "MELSEC" or "COORDINATE VIEWER":
                    return normalized;
                default:
                    return "IO";
            }
        }

        return EvaluateNormalizedSwitch4();
    }

    private static ST_SYSTEM_STATUS CreateFallbackST_SYSTEM_STATUS()
    {
        return new ST_SYSTEM_STATUS(
            "DRILL_A01",
            EN_SYSTEM_MODE.Simulation,
            EN_ALARM_STATE.Clear,
            EN_PM_LOCK_STATE.Released,
            [
                new(EN_EQP_MODULE.WonikCtrl, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Vision, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Automation1, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Motion, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.TalonLaser, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Chiller, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Attenuator, EN_COMM_STATE.Simulation),
                new(EN_EQP_MODULE.Bet, EN_COMM_STATE.Simulation)
            ]);
    }

    private static IReadOnlyList<ST_DEVICE_COMM_STATUS> NormalizeCommunicationStatuses(
        IReadOnlyList<ST_DEVICE_COMM_STATUS> modules)
    {
        ST_DEVICE_COMM_STATUS SelectModule56(EN_EQP_MODULE module)
        {
            bool MatchStatus1(ST_DEVICE_COMM_STATUS status)
            {
                return status.Module == module;
            }

            return modules.FirstOrDefault(MatchStatus1)
                            ?? new ST_DEVICE_COMM_STATUS(module, EN_COMM_STATE.Offline);
        }

        return Enum.GetValues<EN_EQP_MODULE>()
            .Select(SelectModule56)
            .ToArray();
    }

    private ST_HEADER_STATUS_ITEM ModuleHeader(ST_SYSTEM_STATUS status, EN_EQP_MODULE module)
    {
        var moduleStatuses = GetModuleCommunicationStatuses(module);
        if (moduleStatuses.Count == 0)
        {
            var fallbackConnection = status.GetModule(module).ConnectionState;
            var fallbackValue = ConnectionStateValue(fallbackConnection);

            return new ST_HEADER_STATUS_ITEM(ModuleDisplayName(module), fallbackValue, fallbackValue);
        }

        var selectedIndex = GetSelectedModuleIndex(module, moduleStatuses.Count);
        var selectedStatus = moduleStatuses[selectedIndex];
        var connection = selectedStatus.ConnectionState;
        var value = ConnectionStateValue(connection);
        var displayNumber = selectedStatus.Number + 1;
        var displayValue = moduleStatuses.Count > 1
            ? $"{displayNumber}: {value}"
            : value;
        var pageText = moduleStatuses.Count > 1
            ? $"{displayNumber}/{moduleStatuses.Count}"
            : "";
        void HandleValueCallback57(object? _)
        {
            SelectPreviousModuleStatus(module);
        }

        void HandleValueCallback58(object? _)
        {
            SelectNextModuleStatus(module);
        }

        void HandleValueCallback59(object? _)
        {
            ShowModuleStatusPopup(module);
        }

        return new ST_HEADER_STATUS_ITEM(
            ModuleDisplayName(module),
            displayValue,
            value,
            moduleStatuses.Count > 1,
            pageText,
            new CButtonCommand(HandleValueCallback57),
            new CButtonCommand(HandleValueCallback58),
            new CButtonCommand(HandleValueCallback59));
    }

    private IReadOnlyList<ST_INTERFACE_COMM_STATUS> GetModuleCommunicationStatuses(EN_EQP_MODULE module)
    {
        bool FilterStatus60(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Module == module;
        }

        int GetStatusSortKey61(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Number;
        }

        string GetStatusSortKey62(ST_INTERFACE_COMM_STATUS status)
        {
            return status.NickName;
        }

        return _interfaceCommStatuses
            .Where(FilterStatus60)
            .OrderBy(GetStatusSortKey61)
            .ThenBy(GetStatusSortKey62, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int GetSelectedModuleIndex(EN_EQP_MODULE module, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (!_selectedHeaderModuleIndexes.TryGetValue(module, out var selectedIndex))
        {
            selectedIndex = 0;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, count - 1);
        _selectedHeaderModuleIndexes[module] = selectedIndex;
        return selectedIndex;
    }

    private void SelectPreviousModuleStatus(EN_EQP_MODULE module)
    {
        SelectModuleStatus(module, -1);
    }

    private void SelectNextModuleStatus(EN_EQP_MODULE module)
    {
        SelectModuleStatus(module, 1);
    }

    private void SelectModuleStatus(EN_EQP_MODULE module, int offset)
    {
        var moduleStatuses = GetModuleCommunicationStatuses(module);
        if (moduleStatuses.Count <= 1)
        {
            return;
        }

        var selectedIndex = GetSelectedModuleIndex(module, moduleStatuses.Count);
        _selectedHeaderModuleIndexes[module] = (selectedIndex + offset + moduleStatuses.Count) % moduleStatuses.Count;
        RefreshShellStatusItems();
    }

    private void ShowModuleStatusPopup(EN_EQP_MODULE module)
    {
        var moduleStatuses = GetModuleCommunicationStatuses(module);
        if (moduleStatuses.Count == 0)
        {
            return;
        }

        var dialog = new CInterfaceStatusDialog(ModuleDisplayName(module), moduleStatuses);
        bool MatchWindow63(Window window)
        {
            return window.IsActive;
        }

        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(MatchWindow63)
            ?? Application.Current.MainWindow;

        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    private static string ModuleDisplayName(EN_EQP_MODULE module)
    {
        string EvaluateModuleSwitch5()
        {
            var switchValue = module;
            switch (switchValue)
            {
                case EN_EQP_MODULE.WonikCtrl:
                    return "WONIK CTRL";
                case EN_EQP_MODULE.Vision:
                    return "VISION";
                case EN_EQP_MODULE.Automation1:
                    return "AUTOMATION";
                case EN_EQP_MODULE.Motion:
                    return "MOTION";
                case EN_EQP_MODULE.TalonLaser:
                    return "TALON LASER";
                case EN_EQP_MODULE.Chiller:
                    return "CHILLER";
                case EN_EQP_MODULE.Attenuator:
                    return "ATTENUATOR";
                case EN_EQP_MODULE.Bet:
                    return "BET";
                case EN_EQP_MODULE.PowerMeter:
                    return "POWER METER";
                case EN_EQP_MODULE.Melsec:
                    return "MELSEC";
                default:
                    return module.ToString().ToUpperInvariant();
            }
        }

        return EvaluateModuleSwitch5();
    }

    private static string ConnectionStateValue(EN_COMM_STATE state)
    {
        string EvaluateStateSwitch6()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_COMM_STATE.Online:
                    return "ONLINE";
                case EN_COMM_STATE.Offline:
                    return "OFFLINE";
                default:
                    return "SIMULATION";
            }
        }

        return EvaluateStateSwitch6();
    }

    private static string OperationModeValue(EN_SYSTEM_MODE mode)
    {
        string EvaluateModeSwitch7()
        {
            var switchValue = mode;
            switch (switchValue)
            {
                case EN_SYSTEM_MODE.Auto:
                    return "AUTO";
                case EN_SYSTEM_MODE.Manual:
                    return "MANUAL";
                default:
                    return "SIMULATION";
            }
        }

        return EvaluateModeSwitch7();
    }

    private static string OperationModeState(EN_SYSTEM_MODE mode)
    {
        return mode == EN_SYSTEM_MODE.Simulation ? "SIMULATION" : "ONLINE";
    }

    private static string AlarmStateValue(EN_ALARM_STATE state)
    {
        return state == EN_ALARM_STATE.Occur ? "OCCUR" : "CLEAR";
    }

    private static string GetMenuDisplayName(EN_MENU menu)
    {
        string EvaluateMenuSwitch8()
        {
            var switchValue = menu;
            switch (switchValue)
            {
                case EN_MENU.Main:
                    return "MAIN";
                case EN_MENU.Manual:
                    return "MANUAL";
                case EN_MENU.Recipe:
                    return "RECIPE";
                case EN_MENU.Setting:
                    return "SETTING";
                case EN_MENU.Alarm:
                    return "ALARM";
                case EN_MENU.Monitor:
                    return "MONITOR";
                case EN_MENU.Review:
                    return "REVIEW";
                case EN_MENU.Correction:
                    return "CORRECTION";
                case EN_MENU.Pm:
                    return "PM";
                case EN_MENU.Exit:
                    return "EXIT";
                default:
                    return menu.ToString();
            }
        }

        return EvaluateMenuSwitch8();
    }
}




