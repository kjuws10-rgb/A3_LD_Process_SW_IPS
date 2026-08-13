using System.Globalization;
using System.IO;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuSetting : CMenuBase
{
    private readonly CSettingManager _settingManager;
    private readonly Func<string> _selectedTabProvider;
    private readonly Action<string> _selectedTabSetter;
    private readonly Func<string> _selectedGroupProvider;
    private readonly Action<string> _selectedGroupSetter;
    private readonly Func<CMenuSetting?> _editScreenProvider;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<EN_MENU, string> _showLoadingScreen;
    private readonly Action _refreshShellStatus;
    private readonly Action _refreshCurrentScreen;
    private ST_SETTING_INTERFACE_ROW? _selectedInterfaceRow;

    private static readonly EN_SETTING_TAB[] Sections =
    [
        EN_SETTING_TAB.Option,
        EN_SETTING_TAB.Interface,
        EN_SETTING_TAB.Io,
        EN_SETTING_TAB.Motor,
        EN_SETTING_TAB.Alarm
    ];

    public CMenuSetting(
        CSettingManager settingManager,
        Func<string> selectedTabProvider,
        Action<string> selectedTabSetter,
        Func<string> selectedGroupProvider,
        Action<string> selectedGroupSetter,
        Func<CMenuSetting?> editScreenProvider,
        Action<string> setStatusMessage,
        Action<EN_MENU, string> showLoadingScreen,
        Action refreshShellStatus,
        Action refreshCurrentScreen)
    {
        _settingManager = settingManager;
        _selectedTabProvider = selectedTabProvider;
        _selectedTabSetter = selectedTabSetter;
        _selectedGroupProvider = selectedGroupProvider;
        _selectedGroupSetter = selectedGroupSetter;
        _editScreenProvider = editScreenProvider;
        _setStatusMessage = setStatusMessage;
        _showLoadingScreen = showLoadingScreen;
        _refreshShellStatus = refreshShellStatus;
        _refreshCurrentScreen = refreshCurrentScreen;

        void HandleSelectTabCommand1(object? parameter)
        {
            SelectTab(parameter);
        }

        SelectTabCommand = new CButtonCommand(HandleSelectTabCommand1);

        void HandleSelectGroupCommand2(object? parameter)
        {
            SelectGroup(parameter);
        }

        SelectGroupCommand = new CButtonCommand(HandleSelectGroupCommand2);

        void HandleConnectInterfaceCommand3(object? _)
        {
            ConnectInterface();
        }

        bool HandleConnectInterfaceCommand4(object? _)
        {
            return CanOperateSelectedInterface;
        }

        ConnectInterfaceCommand = new CButtonCommand(HandleConnectInterfaceCommand3, HandleConnectInterfaceCommand4);

        void HandleDisconnectInterfaceCommand5(object? _)
        {
            DisconnectInterface();
        }

        bool HandleDisconnectInterfaceCommand6(object? _)
        {
            return CanOperateSelectedInterface;
        }

        DisconnectInterfaceCommand = new CButtonCommand(HandleDisconnectInterfaceCommand5, HandleDisconnectInterfaceCommand6);

        void HandleSaveCommand7(object? _)
        {
            Save();
        }

        SaveCommand = new CButtonCommand(HandleSaveCommand7);

        void HandleCancelCommand8(object? _)
        {
            Cancel();
        }

        CancelCommand = new CButtonCommand(HandleCancelCommand8);

        void HandleReloadCommand9(object? _)
        {
            Reload();
        }

        ReloadCommand = new CButtonCommand(HandleReloadCommand9);
    }

    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Setting;
        }
    }

    public IReadOnlyList<ST_SCREEN_SECTION> Tabs { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> History { get; private set; } = [];

    public string SelectedTab { get; private set; } = "";

    public string SelectedGroup { get; private set; } = "";

    public IReadOnlyList<ST_SETTING_TAB> TabItems { get; private set; } = [];

    public IReadOnlyList<ST_SETTING_GROUP> GroupItems { get; private set; } = [];

    public IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> AllParameterRows { get; private set; } = [];

    public IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> ParameterRows { get; private set; } = [];

    public IReadOnlyList<ST_SETTING_INTERFACE_ROW> AllInterfaceRows { get; private set; } = [];

    public IReadOnlyList<ST_SETTING_INTERFACE_ROW> InterfaceRows { get; private set; } = [];

    public IReadOnlyList<ST_SETTING_HISTORY_ROW> ChangeHistory { get; private set; } = [];

    public IReadOnlyList<ST_SETTING_SUMMARY_ROW> SummaryRows { get; private set; } = [];

    public CButtonCommand SelectTabCommand { get; }

    public CButtonCommand SelectGroupCommand { get; }

    public CButtonCommand ConnectInterfaceCommand { get; }

    public CButtonCommand DisconnectInterfaceCommand { get; }

    public CButtonCommand SaveCommand { get; }

    public CButtonCommand CancelCommand { get; }

    public CButtonCommand ReloadCommand { get; }

    public bool IsInterfaceTab
    {
        get
        {
            return SelectedTab == "INTERFACE";
        }
    }

    public bool IsParameterTab
    {
        get
        {
            return !IsInterfaceTab;
        }
    }

    public ST_SETTING_INTERFACE_ROW? SelectedInterfaceRow
    {
        get
        {
            return _selectedInterfaceRow;
        }

        set
        {
            if (_selectedInterfaceRow is not null)
            {
                UnsubscribeSelectedInterfaceRow(_selectedInterfaceRow);
            }

            if (!SetProperty(ref _selectedInterfaceRow, value))
            {
                if (_selectedInterfaceRow is not null)
                {
                    SubscribeSelectedInterfaceRow(_selectedInterfaceRow);
                }

                return;
            }

            if (_selectedInterfaceRow is not null)
            {
                SubscribeSelectedInterfaceRow(_selectedInterfaceRow);
            }

            RefreshInterfaceCommandState();
        }
    }

    public bool CanOperateSelectedInterface
    {
        get
        {
            return IsInterfaceTab &&
        SelectedInterfaceRow is not null &&
        !SelectedInterfaceRow.IsSimulation;
        }
    }

    public override CScreenViewModel Build(CancellationToken cancellationToken = default)
    {
        var displaySections = new List<ST_SCREEN_SECTION>();

        foreach (var section in Sections)
        {
            var sectionParameters = _settingManager.LoadSection(section, cancellationToken);
            ST_DISPLAY_ITEM SelectItem10(ST_SYSTEM_PARAMETER item)
            {
                return new ST_DISPLAY_ITEM(
                                    item.Name,
                                    $"{item.Value} {item.Unit}".Trim(),
                                    item.Description);
            }

            displaySections.Add(new ST_SCREEN_SECTION(
                ToTabText(section),
                sectionParameters.Select(SelectItem10).ToArray()));
        }

        var selectedTab = NormalizeTab(_selectedTabProvider());
        var selectedSection = ToSection(selectedTab);
        IReadOnlyList<ST_SYSTEM_PARAMETER> loadedParameters = selectedTab == "INTERFACE"
            ? []
            : _settingManager.LoadSection(selectedSection, cancellationToken);
        var loadedRows = BuildParameterRows(loadedParameters);
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> loadedInterfaceRows = selectedTab == "INTERFACE"
            ? BuildInterfaceRows(_settingManager.LoadInterfaceList(cancellationToken))
            : [];
        var editScreen = _editScreenProvider();
        var allRows = GetEditRows(loadedRows, editScreen, selectedTab);
        var allInterfaceRows = GetEditInterfaceRows(loadedInterfaceRows, editScreen, selectedTab);
        var groups = selectedTab == "INTERFACE"
            ? BuildInterfaceGroups(allInterfaceRows)
            : BuildGroups(allRows);
        var selectedGroup = NormalizeGroup(_selectedGroupProvider(), groups);
        bool FilterRow11(ST_SYSTEM_PARAMETER_ROW row)
        {
            return row.Group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase);
        }

        var filteredRows = selectedGroup == "ALL"
            ? allRows
            : allRows.Where(FilterRow11).ToArray();
        bool FilterRow12(ST_SETTING_INTERFACE_ROW row)
        {
            return row.Type.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase);
        }

        var filteredInterfaceRows = selectedGroup == "ALL"
            ? allInterfaceRows
            : allInterfaceRows.Where(FilterRow12).ToArray();
        var history = _settingManager.LoadHistory(selectedSection, cancellationToken);
        ST_SETTING_GROUP SelectGroup13(string group)
        {
            return new ST_SETTING_GROUP(group, group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        Apply(
            displaySections,
            BuildHistoryItems(history),
            selectedTab,
            selectedGroup,
            BuildTabs(selectedTab),
            groups.Select(SelectGroup13).ToArray(),
            allRows,
            filteredRows,
            allInterfaceRows,
            filteredInterfaceRows,
            BuildHistoryRows(history),
            BuildSummaryRows(selectedTab, allRows, allInterfaceRows, history));

        return new CScreenViewModel(
            EN_MENU.Setting,
            "SETTING / PARAMETER CONFIG",
            "Direct grid edit for option, interface, IO and motor parameters.",
            [
            new("Source", "CSV"),
            new("History", "Setting trace log")
            ],
            displaySections,
            setting: this);
    }

    private void SelectTab(object? parameter)
    {
        if (parameter is not string tab || string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        var selectedTab = NormalizeTab(tab);
        _selectedTabSetter(selectedTab);
        _selectedGroupSetter(GetDefaultGroup());
        _setStatusMessage($"Setting tab {selectedTab} selected.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void SelectGroup(object? parameter)
    {
        if (parameter is not string group || string.IsNullOrWhiteSpace(group))
        {
            return;
        }

        var selectedGroup = group.Trim().ToUpperInvariant();
        _selectedGroupSetter(selectedGroup);
        _setStatusMessage($"Setting group {SelectedTab} / {selectedGroup} selected.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void ConnectInterface()
    {
        var row = SelectedInterfaceRow;

        if (row is null)
        {
            _setStatusMessage("Select an interface row before connect.");
            return;
        }

        if (row.IsSimulation)
        {
            _setStatusMessage($"{InterfaceRowLabel(row)} is SIMUL mode. Connect is disabled.");
            return;
        }

        if (row.IsModified)
        {
            _setStatusMessage("Save JHMI_INTERFACE.csv before connect.");
            return;
        }

        try
        {
            _settingManager.ConnectInterface(
                ParseDevice(row.Device),
                ReadInt(row.Number, row.NickName, "NUMBER"));
            _setStatusMessage($"{InterfaceRowLabel(row)} connect command sent.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or KeyNotFoundException or IOException)
        {
            _setStatusMessage($"Connect blocked. {exception.Message}");
            return;
        }

        _refreshCurrentScreen();
    }

    private void DisconnectInterface()
    {
        var row = SelectedInterfaceRow;

        if (row is null)
        {
            _setStatusMessage("Select an interface row before disconnect.");
            return;
        }

        if (row.IsSimulation)
        {
            _setStatusMessage($"{InterfaceRowLabel(row)} is SIMUL mode. Disconnect is disabled.");
            return;
        }

        if (row.IsModified)
        {
            _setStatusMessage("Save JHMI_INTERFACE.csv before disconnect.");
            return;
        }

        try
        {
            _settingManager.DisconnectInterface(
                ParseDevice(row.Device),
                ReadInt(row.Number, row.NickName, "NUMBER"));
            _setStatusMessage($"{InterfaceRowLabel(row)} disconnected.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or KeyNotFoundException or IOException)
        {
            _setStatusMessage($"Disconnect blocked. {exception.Message}");
            return;
        }

        _refreshCurrentScreen();
    }

    private void Save()
    {
        if (SelectedTab == "INTERFACE")
        {
            try
            {
                _settingManager.SaveInterfaceList(ToInterfaceData(AllInterfaceRows));
            }
            catch (InvalidDataException exception)
            {
                _setStatusMessage(exception.Message);
                return;
            }
            catch (IOException exception)
            {
                _setStatusMessage($"JHMI_INTERFACE save blocked. {exception.Message}");
                return;
            }

            _setStatusMessage("JHMI_INTERFACE.csv saved, verified, and reloaded to InterfaceManager.");
            _showLoadingScreen(EN_MENU.Setting, "SETTING");
            _refreshShellStatus();
            _refreshCurrentScreen();
            return;
        }

        var section = ToSection(SelectedTab);
        ST_SYSTEM_PARAMETER SelectRow14(ST_SYSTEM_PARAMETER_ROW row)
        {
            return new ST_SYSTEM_PARAMETER(
                            section,
                            row.Parameter,
                            row.Value,
                            NormalizeSettingUnit(row.Unit),
                            row.Description,
                            row.Group,
                            row.Key,
                            row.DefaultValue,
                            row.DataType,
                            row.Min,
                            row.Max);
        }

        var parameters = AllParameterRows
            .Select(SelectRow14)
            .ToArray();

        try
        {
            _settingManager.SaveSection(section, parameters);
        }
        catch (InvalidDataException exception)
        {
            _setStatusMessage(exception.Message);
            return;
        }
        catch (IOException exception)
        {
            _setStatusMessage($"Setting save blocked. {exception.Message}");
            return;
        }

        _setStatusMessage($"Setting.csv saved for {SelectedTab} and CSV verified.");
        _showLoadingScreen(EN_MENU.Setting, "SETTING");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Cancel()
    {
        _showLoadingScreen(EN_MENU.Setting, "SETTING");
        _setStatusMessage($"Setting edits canceled. Reloaded {SelectedTab} / {SelectedGroup} from CSV.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Reload()
    {
        _showLoadingScreen(EN_MENU.Setting, "SETTING");
        _setStatusMessage($"Setting {SelectedTab} / {SelectedGroup} reloaded from CSV.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Apply(
        IReadOnlyList<ST_SCREEN_SECTION> tabs,
        IReadOnlyList<ST_DISPLAY_ITEM> history,
        string selectedTab,
        string selectedGroup,
        IReadOnlyList<ST_SETTING_TAB> tabItems,
        IReadOnlyList<ST_SETTING_GROUP> groupItems,
        IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> allParameterRows,
        IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> parameterRows,
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> allInterfaceRows,
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> interfaceRows,
        IReadOnlyList<ST_SETTING_HISTORY_ROW> changeHistory,
        IReadOnlyList<ST_SETTING_SUMMARY_ROW> summaryRows)
    {
        Tabs = tabs;
        History = history;
        SelectedTab = selectedTab;
        SelectedGroup = selectedGroup;
        TabItems = tabItems;
        GroupItems = groupItems;
        AllParameterRows = allParameterRows;
        ParameterRows = parameterRows;
        AllInterfaceRows = allInterfaceRows;
        InterfaceRows = interfaceRows;
        SelectedInterfaceRow = GetSelectedInterfaceRow(interfaceRows, SelectedInterfaceRow);
        ChangeHistory = changeHistory;
        SummaryRows = summaryRows;
    }

    private static IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> GetEditRows(
        IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> loadedRows,
        CMenuSetting? editScreen,
        string selectedTab)
    {
        return editScreen is not null &&
            editScreen.SelectedTab.Equals(selectedTab, StringComparison.OrdinalIgnoreCase) &&
            editScreen.AllParameterRows.Count > 0
                ? editScreen.AllParameterRows
                : loadedRows;
    }

    private static IReadOnlyList<ST_SETTING_INTERFACE_ROW> GetEditInterfaceRows(
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> loadedRows,
        CMenuSetting? editScreen,
        string selectedTab)
    {
        return editScreen is not null &&
            editScreen.SelectedTab.Equals(selectedTab, StringComparison.OrdinalIgnoreCase) &&
            editScreen.AllInterfaceRows.Count > 0
                ? editScreen.AllInterfaceRows
                : loadedRows;
    }

    private static ST_SETTING_INTERFACE_ROW? GetSelectedInterfaceRow(
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> rows,
        ST_SETTING_INTERFACE_ROW? current)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        if (current is null)
        {
            return rows[0];
        }
        bool MatchRow15(ST_SETTING_INTERFACE_ROW row)
        {
            return IsSameInterfaceKey(row, current);
        }

        bool MatchRow16(ST_SETTING_INTERFACE_ROW row)
        {
            return row.NickName.Equals(current.NickName, StringComparison.OrdinalIgnoreCase);
        }

        return rows.FirstOrDefault(MatchRow15)
            ?? rows.FirstOrDefault(MatchRow16)
            ?? rows[0];
    }

    private static bool IsSameInterfaceKey(
        ST_SETTING_INTERFACE_ROW left,
        ST_SETTING_INTERFACE_ROW right)
    {
        return NormalizeSettingText(left.Device, "").Equals(NormalizeSettingText(right.Device, ""), StringComparison.OrdinalIgnoreCase) &&
            left.Number.Trim().Equals(right.Number.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void SubscribeSelectedInterfaceRow(ST_SETTING_INTERFACE_ROW row)
    {
        row.SimulChanged += SelectedInterfaceRowValueChanged;
        row.IsSimulationChanged += SelectedInterfaceRowValueChanged;
        row.IsModifiedChanged += SelectedInterfaceRowValueChanged;
    }

    private void UnsubscribeSelectedInterfaceRow(ST_SETTING_INTERFACE_ROW row)
    {
        row.SimulChanged -= SelectedInterfaceRowValueChanged;
        row.IsSimulationChanged -= SelectedInterfaceRowValueChanged;
        row.IsModifiedChanged -= SelectedInterfaceRowValueChanged;
    }

    private void SelectedInterfaceRowValueChanged(object? sender, EventArgs eventArgs)
    {
        RefreshInterfaceCommandState();
    }

    private void RefreshInterfaceCommandState()
    {
        OnPropertyChanged(nameof(CanOperateSelectedInterface));
        ConnectInterfaceCommand.NotifyCanExecuteChanged();
        DisconnectInterfaceCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<ST_SETTING_TAB> BuildTabs(string selectedTab)
    {
        string SelectSection17(EN_SETTING_TAB section)
        {
            return ToTabText(section);
        }

        ST_SETTING_TAB SelectTab18(string tab)
        {
            return new ST_SETTING_TAB(tab, tab.Equals(selectedTab, StringComparison.OrdinalIgnoreCase));
        }

        return Sections
            .Select(SelectSection17)
            .Select(SelectTab18)
            .ToArray();
    }

    private static IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> BuildParameterRows(
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters)
    {
        bool FilterParameter19(ST_SYSTEM_PARAMETER parameter)
        {
            return parameter.Show && parameter.Use;
        }

        ST_SYSTEM_PARAMETER_ROW SelectParameter20(ST_SYSTEM_PARAMETER parameter)
        {
            return new ST_SYSTEM_PARAMETER_ROW(
                            NormalizeSettingText(parameter.Group, "COMMON"),
                            parameter.Name,
                            parameter.Value,
                            NormalizeUnit(parameter.Unit),
                            parameter.Description,
                            false,
                            GetValueState(parameter.Value),
                            parameter.Key,
                            parameter.DefaultValue,
                            parameter.DataType,
                            parameter.Min,
                            parameter.Max);
        }

        return parameters
            .Where(FilterParameter19)
            .Select(SelectParameter20)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildGroups(IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> rows)
    {
        string SelectRow21(ST_SYSTEM_PARAMETER_ROW row)
        {
            return row.Group;
        }

        bool FilterGroup22(string group)
        {
            return !string.IsNullOrWhiteSpace(group);
        }

        var groups = rows
            .Select(SelectRow21)
            .Where(FilterGroup22)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { "ALL" }.Concat(groups).ToArray();
    }

    private static IReadOnlyList<string> BuildInterfaceGroups(IReadOnlyList<ST_SETTING_INTERFACE_ROW> rows)
    {
        string SelectRow23(ST_SETTING_INTERFACE_ROW row)
        {
            return row.Type;
        }

        bool FilterGroup24(string group)
        {
            return !string.IsNullOrWhiteSpace(group);
        }

        var groups = rows
            .Select(SelectRow23)
            .Where(FilterGroup24)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { "ALL" }.Concat(groups).ToArray();
    }

    private static IReadOnlyList<ST_DISPLAY_ITEM> BuildHistoryItems(
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        ST_DISPLAY_ITEM SelectItem25(ST_SETTING_HISTORY item)
        {
            return new ST_DISPLAY_ITEM(
                            item.ChangedAt.ToString("HH:mm:ss"),
                            $"{item.Action} / {item.ParameterName}",
                            $"{item.OldValue} -> {item.NewValue}");
        }

        return history
            .Select(SelectItem25)
            .ToArray();
    }

    private static IReadOnlyList<ST_SETTING_HISTORY_ROW> BuildHistoryRows(
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        bool FilterItem26(ST_SETTING_HISTORY item)
        {
            return !item.Action.Equals("SAVE", StringComparison.OrdinalIgnoreCase);
        }

        ST_SETTING_HISTORY_ROW SelectItem27(ST_SETTING_HISTORY item)
        {
            return new ST_SETTING_HISTORY_ROW(
                            item.ChangedAt.ToString("HH:mm:ss"),
                            item.OperatorId,
                            ToTabText(item.Section),
                            item.ParameterName,
                            item.OldValue,
                            item.NewValue,
                            "Warn");
        }

        return history
            .Where(FilterItem26)
            .Select(SelectItem27)
            .ToArray();
    }

    private static IReadOnlyList<ST_SETTING_SUMMARY_ROW> BuildSummaryRows(
        string selectedTab,
        IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> rows,
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> interfaceRows,
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        bool HandleModifiedCount28(ST_SETTING_INTERFACE_ROW row)
        {
            return row.IsModified;
        }

        bool HandleModifiedCount29(ST_SYSTEM_PARAMETER_ROW row)
        {
            return row.IsModified;
        }

        var modifiedCount = selectedTab == "INTERFACE"
            ? interfaceRows.Count(HandleModifiedCount28)
            : rows.Count(HandleModifiedCount29);
        bool MatchItem30(ST_SETTING_HISTORY item)
        {
            return item.Action.Equals("SAVE", StringComparison.OrdinalIgnoreCase);
        }

        var lastSavedTime = history.FirstOrDefault(MatchItem30)?.ChangedAt;

        return
        [
            new("Modified Items", modifiedCount.ToString(), modifiedCount > 0 ? "Warn" : "Ok"),
            new("Last Saved Time", lastSavedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-")
        ];
    }

    private static IReadOnlyList<ST_SETTING_INTERFACE_ROW> BuildInterfaceRows(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces)
    {
        EN_EQP_MODULE GetItemSortKey31(ST_INTERFACE_DATA item)
        {
            return item.Device;
        }

        int GetItemSortKey32(ST_INTERFACE_DATA item)
        {
            return item.Number;
        }

        string GetItemSortKey33(ST_INTERFACE_DATA item)
        {
            return item.NickName;
        }

        ST_SETTING_INTERFACE_ROW SelectItem34(ST_INTERFACE_DATA item, int index)
        {
            var arguments = item.Arguments
                .Concat(Enumerable.Repeat("", 5))
                .Take(5)
                .ToArray();

            return new ST_SETTING_INTERFACE_ROW(
                (index + 1).ToString("D2", CultureInfo.InvariantCulture),
                InterfaceTypeText(item.InterfaceType),
                DeviceText(item.Device),
                item.Number.ToString(CultureInfo.InvariantCulture),
                item.NickName,
                item.SystemSection,
                item.AutoConnection ? "ON" : "OFF",
                item.IsSimulation ? "ON" : "OFF",
                arguments[0],
                arguments[1],
                arguments[2],
                arguments[3],
                arguments[4],
                item.Extra);
        }
        return interfaces
            .OrderBy(GetItemSortKey31)
            .ThenBy(GetItemSortKey32)
            .ThenBy(GetItemSortKey33, StringComparer.OrdinalIgnoreCase)
            .Select(SelectItem34)
            .ToArray();
    }

    private static IReadOnlyList<ST_INTERFACE_DATA> ToInterfaceData(
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> rows)
    {
        ST_INTERFACE_DATA SelectRow35(ST_SETTING_INTERFACE_ROW row)
        {
            return new ST_INTERFACE_DATA(
                            ParseInterfaceType(row.Type),
                            ParseDevice(row.Device),
                            ReadInt(row.Number, row.NickName, "NUMBER"),
                            RequireText(row.NickName, "NICKNAME"),
                            RequireText(row.SystemSection, "SYSTEM_SECTION"),
                            ReadBool(row.AutoConnection, row.NickName, "AUTOCONNECTION"),
                            ReadBool(row.Simul, row.NickName, "SIMUL"),
                            [row.Arg1.Trim(), row.Arg2.Trim(), row.Arg3.Trim(), row.Arg4.Trim(), row.Arg5.Trim()],
                            row.Extra);
        }

        return rows
            .Select(SelectRow35)
            .ToArray();
    }

    private static EN_INTERFACE_TYPE ParseInterfaceType(string value)
    {
        EN_INTERFACE_TYPE EvaluateValueSwitch1()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "OPCUA":
                    return EN_INTERFACE_TYPE.OpcUa;
                case "MODBUS_SERIAL":
                    return EN_INTERFACE_TYPE.ModbusSerial;
                case "MODBUS_TCP":
                    return EN_INTERFACE_TYPE.ModbusTcp;
                case "SERIAL":
                    return EN_INTERFACE_TYPE.Serial;
                case "SOCKET_C":
                    return EN_INTERFACE_TYPE.SocketClient;
                case "SOCKET_S":
                    return EN_INTERFACE_TYPE.SocketServer;
                case "SOCKET_C_UDP":
                    return EN_INTERFACE_TYPE.SocketClientUdp;
                case "SOCKET_S_UDP":
                    return EN_INTERFACE_TYPE.SocketServerUdp;
                case "ACS_NET" or "ACS":
                    return EN_INTERFACE_TYPE.AcsNet;
                case "XPS_NET" or "XPS" or "NEWPORT_XPS":
                    return EN_INTERFACE_TYPE.XpsNet;
                case "AUTOMATION1_NET" or "AUTOMATION1" or "A1_NET" or "AEROTECH_AUTOMATION1":
                    return EN_INTERFACE_TYPE.Automation1Net;
                case "PICOMOTOR" or "PICO_MOTOR" or "PICO":
                    return EN_INTERFACE_TYPE.PicoMotor;
                default:
                    throw new InvalidDataException($"JHMI_INTERFACE save blocked. Unknown TYPE: {value}");
            }
        }

        return EvaluateValueSwitch1();
    }

    private static EN_EQP_MODULE ParseDevice(string value)
    {
        EN_EQP_MODULE EvaluateValueSwitch2()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "WONIK_CONTROL" or "WONIK_CTRL" or "CONTROL":
                    return EN_EQP_MODULE.WonikCtrl;
                case "WONIK_VISION" or "VISION":
                    return EN_EQP_MODULE.Vision;
                case "AUTOMATION1" or "AUTOMATION_ONE" or "A1":
                    return EN_EQP_MODULE.Automation1;
                case "MOTION" or "SCANNER":
                    return EN_EQP_MODULE.Motion;
                case "TALON" or "TALON_LASER" or "LASER":
                    return EN_EQP_MODULE.TalonLaser;
                case "CHILLER" or "ORION_CHILLER" or "SMCCHILLER":
                    return EN_EQP_MODULE.Chiller;
                case "CONEX_AGP" or "ATTENUATOR":
                    return EN_EQP_MODULE.Attenuator;
                case "BEAM_EXPANDER" or "BET":
                    return EN_EQP_MODULE.Bet;
                case "POWER_METER" or "POWERMETER" or "POWERMAX":
                    return EN_EQP_MODULE.PowerMeter;
                case "MELSEC" or "PLC":
                    return EN_EQP_MODULE.Melsec;
                case "PICO_MOTOR" or "PICOMOTOR" or "PICO":
                    return EN_EQP_MODULE.PicoMotor;
                default:
                    throw new InvalidDataException($"JHMI_INTERFACE save blocked. Unknown DEVICE: {value}");
            }
        }

        return EvaluateValueSwitch2();
    }

    private static string InterfaceTypeText(EN_INTERFACE_TYPE type)
    {
        string EvaluateTypeSwitch3()
        {
            var switchValue = type;
            switch (switchValue)
            {
                case EN_INTERFACE_TYPE.SocketClient:
                    return "SOCKET_C";
                case EN_INTERFACE_TYPE.SocketServer:
                    return "SOCKET_S";
                case EN_INTERFACE_TYPE.SocketClientUdp:
                    return "SOCKET_C_UDP";
                case EN_INTERFACE_TYPE.SocketServerUdp:
                    return "SOCKET_S_UDP";
                case EN_INTERFACE_TYPE.ModbusSerial:
                    return "MODBUS_SERIAL";
                case EN_INTERFACE_TYPE.ModbusTcp:
                    return "MODBUS_TCP";
                case EN_INTERFACE_TYPE.OpcUa:
                    return "OPCUA";
                case EN_INTERFACE_TYPE.Serial:
                    return "SERIAL";
                case EN_INTERFACE_TYPE.AcsNet:
                    return "ACS_NET";
                case EN_INTERFACE_TYPE.XpsNet:
                    return "XPS_NET";
                case EN_INTERFACE_TYPE.Automation1Net:
                    return "AUTOMATION1_NET";
                case EN_INTERFACE_TYPE.PicoMotor:
                    return "PICOMOTOR";
                default:
                    return type.ToString().ToUpperInvariant();
            }
        }

        return EvaluateTypeSwitch3();
    }

    private static string DeviceText(EN_EQP_MODULE module)
    {
        string EvaluateModuleSwitch4()
        {
            var switchValue = module;
            switch (switchValue)
            {
                case EN_EQP_MODULE.WonikCtrl:
                    return "WONIK_CONTROL";
                case EN_EQP_MODULE.Vision:
                    return "WONIK_VISION";
                case EN_EQP_MODULE.Automation1:
                    return "AUTOMATION1";
                case EN_EQP_MODULE.Motion:
                    return "MOTION";
                case EN_EQP_MODULE.TalonLaser:
                    return "TALON";
                case EN_EQP_MODULE.Chiller:
                    return "CHILLER";
                case EN_EQP_MODULE.Attenuator:
                    return "CONEX_AGP";
                case EN_EQP_MODULE.Bet:
                    return "BEAM_EXPANDER";
                case EN_EQP_MODULE.PowerMeter:
                    return "POWER_METER";
                case EN_EQP_MODULE.PicoMotor:
                    return "PICO_MOTOR";
                case EN_EQP_MODULE.Melsec:
                    return "MELSEC";
                default:
                    return module.ToString().ToUpperInvariant();
            }
        }

        return EvaluateModuleSwitch4();
    }

    private static string InterfaceRowLabel(ST_SETTING_INTERFACE_ROW row)
    {
        return $"{row.Device.Trim()}[{row.Number.Trim()}]/{row.NickName.Trim()}";
    }

    private static string RequireText(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"JHMI_INTERFACE save blocked. {fieldName} cannot be empty.");
        }

        return value.Trim();
    }

    private static bool ReadBool(string value, string nickName, string fieldName)
    {
        bool EvaluateValueSwitch5()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "1" or "TRUE" or "ON" or "YES" or "USE" or "SIMUL" or "SIMULATION" or "SIM":
                    return true;
                case "0" or "FALSE" or "OFF" or "NO" or "ONLINE" or "LIVE" or "REAL":
                    return false;
                default:
                    throw new InvalidDataException($"JHMI_INTERFACE save blocked. {nickName}/{fieldName} must be 1/0 or ON/OFF.");
            }
        }

        return EvaluateValueSwitch5();
    }

    private static int ReadInt(string value, string nickName, string fieldName)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < 0)
        {
            throw new InvalidDataException($"JHMI_INTERFACE save blocked. {nickName}/{fieldName} must be a non-negative integer.");
        }

        return result;
    }

    private static string NormalizeTab(string tab)
    {
        var normalized = tab.Trim().ToUpperInvariant();
        if (normalized == "POSITION")
        {
            return "OPTION";
        }

        return normalized is "OPTION" or "INTERFACE" or "IO" or "MOTOR" or "ALARM"
            ? normalized
            : "OPTION";
    }

    private static string NormalizeGroup(
        string group,
        IReadOnlyList<string> groups)
    {
        var normalized = NormalizeSettingText(group, "ALL");
        bool CheckItem36(string item)
        {
            return item.Equals(normalized, StringComparison.OrdinalIgnoreCase);
        }

        return groups.Any(CheckItem36)
            ? normalized
            : "ALL";
    }

    private static string NormalizeSettingText(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeUnit(string unit)
    {
        return string.IsNullOrWhiteSpace(unit) ? "-" : unit;
    }

    private static string NormalizeSettingUnit(string unit)
    {
        return unit == "-" ? "" : unit;
    }

    private static string GetValueState(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        return normalized is "ON" or "OFF" or "TRUE" or "FALSE"
            ? "Accent"
            : "Normal";
    }

    private static EN_SETTING_TAB ToSection(string tab)
    {
        EN_SETTING_TAB EvaluateValueSwitch6()
        {
            var switchValue = tab.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "INTERFACE":
                    return EN_SETTING_TAB.Interface;
                case "IO":
                    return EN_SETTING_TAB.Io;
                case "MOTOR":
                    return EN_SETTING_TAB.Motor;
                case "ALARM":
                    return EN_SETTING_TAB.Alarm;
                default:
                    return EN_SETTING_TAB.Option;
            }
        }

        return EvaluateValueSwitch6();
    }

    private static string ToTabText(EN_SETTING_TAB section)
    {
        string EvaluateSectionSwitch7()
        {
            var switchValue = section;
            switch (switchValue)
            {
                case EN_SETTING_TAB.Io:
                    return "IO";
                default:
                    return section.ToString().ToUpperInvariant();
            }
        }

        return EvaluateSectionSwitch7();
    }

    private static string GetDefaultGroup()
    {
        return "ALL";
    }
}

public sealed record ST_SETTING_TAB(
    string Name,
    bool IsSelected);

public sealed record ST_SETTING_GROUP(
    string Name,
    bool IsSelected);

public sealed class ST_SYSTEM_PARAMETER_ROW : CBindingBase
{
    private static readonly IReadOnlyList<string> VisionFlipOptions =
    [
        "OFF",
        "ON"
    ];

    private readonly string _originalValue;
    private readonly string _originalValueState;
    private string _value;
    private string _valueState;

    public ST_SYSTEM_PARAMETER_ROW(
        string group,
        string parameter,
        string value,
        string unit,
        string description,
        bool isModified = false,
        string valueState = "Normal",
        string key = "",
        string defaultValue = "",
        EN_RECIPE_DATA_TYPE dataType = EN_RECIPE_DATA_TYPE.String,
        double min = 0.0,
        double max = 0.0)
    {
        Group = group;
        Parameter = parameter;
        Unit = unit;
        Description = description;
        Key = key;
        DefaultValue = defaultValue;
        DataType = dataType;
        Min = min;
        Max = max;
        var displayValue = IsVisionFlipOption
            ? NormalizeVisionFlipDisplay(value)
            : value;
        _value = displayValue;
        _originalValue = displayValue;
        _valueState = isModified ? "Warn" : valueState;
        _originalValueState = valueState;
    }

    public string Group { get; }

    public string Parameter { get; }

    public string Value
    {
        get
        {
            return _value;
        }

        set
        {
            if (!SetProperty(ref _value, value))
            {
                return;
            }

            ValueState = IsModified ? "Warn" : _originalValueState;
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(ModifiedText));
            OnPropertyChanged(nameof(ModifiedBrush));
        }
    }

    public string Unit { get; }

    public string Description { get; }

    public string Key { get; }

    public string DefaultValue { get; }

    public EN_RECIPE_DATA_TYPE DataType { get; }

    public bool IsVisionFlipOption
    {
        get
        {
            return Key.Equals("VisionXFlip", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("VisionYFlip", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("VisionXyFlip", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsBoolean
    {
        get
        {
            return DataType == EN_RECIPE_DATA_TYPE.Bool;
        }
    }

    public bool UsesSelectionEditor
    {
        get
        {
            return IsVisionFlipOption || IsBoolean;
        }
    }

    public IReadOnlyList<string> ValueOptions
    {
        get
        {
            return UsesSelectionEditor ? VisionFlipOptions : [];
        }
    }

    public double Min { get; }

    public double Max { get; }

    public string OriginalValue
    {
        get
        {
            return _originalValue;
        }
    }

    public bool IsModified
    {
        get
        {
            return !NormalizeValue(Value).Equals(NormalizeValue(_originalValue), StringComparison.OrdinalIgnoreCase);
        }
    }

    public string ModifiedText
    {
        get
        {
            return IsModified ? "Yes" : "No";
        }
    }

    public string ValueState
    {
        get
        {
            return _valueState;
        }

        private set
        {
            if (SetProperty(ref _valueState, value))
            {
                OnPropertyChanged(nameof(ValueBrush));
            }
        }
    }

    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateValueStateSwitch8()
            {
                var switchValue = ValueState;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueStateSwitch8();
        }
    }

    public Brush ModifiedBrush
    {
        get
        {
            return IsModified ? CStatusBrush.Wait : CStatusBrush.PrimaryText;
        }
    }

    private static string NormalizeValue(string value)
    {
        return value.Trim();
    }

    private static string NormalizeVisionFlipDisplay(string value)
    {
        return value.Trim().ToUpperInvariant() is
            "ON" or "TRUE" or "1" or "YES"
            ? "ON"
            : "OFF";
    }
}

public sealed record ST_SETTING_HISTORY_ROW(
    string Time,
    string User,
    string Tab,
    string Parameter,
    string Before,
    string After,
    string AfterState = "Warn")
{
    public Brush AfterBrush
    {
        get
        {
            Brush EvaluateAfterStateSwitch9()
            {
                var switchValue = AfterState;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    default:
                        return CStatusBrush.Wait;
                }
            }

            return EvaluateAfterStateSwitch9();
        }
    }
}

public sealed record ST_SETTING_SUMMARY_ROW(
    string Name,
    string Value,
    string State = "Normal")
{
    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateStateSwitch10()
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

            return EvaluateStateSwitch10();
        }
    }
}

public sealed class ST_SETTING_INTERFACE_ROW : CBindingBase
{
    public IReadOnlyList<string> TypeOptions { get; } =
    [
        "OPCUA", "MODBUS_SERIAL", "MODBUS_TCP", "SERIAL", "SOCKET_C", "SOCKET_S",
        "SOCKET_C_UDP", "SOCKET_S_UDP", "ACS_NET", "XPS_NET", "AUTOMATION1_NET", "PICOMOTOR"
    ];

    public IReadOnlyList<string> AutoConnectionOptions { get; } = ["OFF", "ON"];

    private readonly string _originalType;
    private readonly string _originalDevice;
    private readonly string _originalNumber;
    private readonly string _originalNickName;
    private readonly string _originalSystemSection;
    private readonly string _originalAutoConnection;
    private readonly string _originalSimul;
    private readonly string _originalArg1;
    private readonly string _originalArg2;
    private readonly string _originalArg3;
    private readonly string _originalArg4;
    private readonly string _originalArg5;
    private string _type;
    private string _device;
    private string _number;
    private string _nickName;
    private string _systemSection;
    private string _autoConnection;
    private string _simul;
    private string _arg1;
    private string _arg2;
    private string _arg3;
    private string _arg4;
    private string _arg5;

    public ST_SETTING_INTERFACE_ROW(
        string no,
        string type,
        string device,
        string number,
        string nickName,
        string systemSection,
        string autoConnection,
        string simul,
        string arg1,
        string arg2,
        string arg3,
        string arg4,
        string arg5,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        No = no;
        Extra = extra;
        _type = type;
        _device = device;
        _number = number;
        _nickName = nickName;
        _systemSection = systemSection;
        _autoConnection = autoConnection;
        _simul = simul;
        _arg1 = arg1;
        _arg2 = arg2;
        _arg3 = arg3;
        _arg4 = arg4;
        _arg5 = arg5;
        _originalType = type;
        _originalDevice = device;
        _originalNumber = number;
        _originalNickName = nickName;
        _originalSystemSection = systemSection;
        _originalAutoConnection = autoConnection;
        _originalSimul = simul;
        _originalArg1 = arg1;
        _originalArg2 = arg2;
        _originalArg3 = arg3;
        _originalArg4 = arg4;
        _originalArg5 = arg5;
    }

    public string No { get; }

    public IReadOnlyDictionary<string, string>? Extra { get; }

    public string Type
    {
        get
        {
            return _type;
        }

        set
        {
            SetEditable(ref _type, value);
        }
    }

    public string Device
    {
        get
        {
            return _device;
        }

        set
        {
            SetEditable(ref _device, value);
        }
    }

    public string Number
    {
        get
        {
            return _number;
        }

        set
        {
            SetEditable(ref _number, value);
        }
    }

    public string NickName
    {
        get
        {
            return _nickName;
        }

        set
        {
            SetEditable(ref _nickName, value);
        }
    }

    public string SystemSection
    {
        get
        {
            return _systemSection;
        }

        set
        {
            SetEditable(ref _systemSection, value);
        }
    }

    public string AutoConnection
    {
        get
        {
            return _autoConnection;
        }

        set
        {
            SetEditable(ref _autoConnection, value);
        }
    }

    public string Simul
    {
        get
        {
            return _simul;
        }

        set
        {
            SetEditable(ref _simul, value);
        }
    }

    public string Arg1
    {
        get
        {
            return _arg1;
        }

        set
        {
            SetEditable(ref _arg1, value);
        }
    }

    public string Arg2
    {
        get
        {
            return _arg2;
        }

        set
        {
            SetEditable(ref _arg2, value);
        }
    }

    public string Arg3
    {
        get
        {
            return _arg3;
        }

        set
        {
            SetEditable(ref _arg3, value);
        }
    }

    public string Arg4
    {
        get
        {
            return _arg4;
        }

        set
        {
            SetEditable(ref _arg4, value);
        }
    }

    public string Arg5
    {
        get
        {
            return _arg5;
        }

        set
        {
            SetEditable(ref _arg5, value);
        }
    }

    public bool IsModified
    {
        get
        {
            return IsChanged(Type, _originalType) ||
        IsChanged(Device, _originalDevice) ||
        IsChanged(Number, _originalNumber) ||
        IsChanged(NickName, _originalNickName) ||
        IsChanged(SystemSection, _originalSystemSection) ||
        IsChanged(AutoConnection, _originalAutoConnection) ||
        IsChanged(Simul, _originalSimul) ||
        IsChanged(Arg1, _originalArg1) ||
        IsChanged(Arg2, _originalArg2) ||
        IsChanged(Arg3, _originalArg3) ||
        IsChanged(Arg4, _originalArg4) ||
        IsChanged(Arg5, _originalArg5);
        }
    }

    public bool IsSimulation
    {
        get
        {
            bool EvaluateValueSwitch11()
            {
                var switchValue = Simul.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "0" or "FALSE" or "OFF" or "NO" or "ONLINE" or "LIVE" or "REAL":
                        return false;
                    default:
                        return true;
                }
            }

            return EvaluateValueSwitch11();
        }
    }

    public string ModifiedText
    {
        get
        {
            return IsModified ? "Yes" : "No";
        }
    }

    public Brush ModifiedBrush
    {
        get
        {
            return IsModified ? CStatusBrush.Wait : CStatusBrush.Muted;
        }
    }

    public Brush SimulBrush
    {
        get
        {
            Brush EvaluateValueSwitch12()
            {
                var switchValue = Simul.Trim().ToUpperInvariant();
                switch (switchValue)
                {
                    case "SIMULATION" or "SIMUL" or "SIM" or "1":
                        return CStatusBrush.Simul;
                    default:
                        return CStatusBrush.Online;
                }
            }

            return EvaluateValueSwitch12();
        }
    }

    private void SetEditable(ref string field, string value)
    {
        if (!SetProperty(ref field, value))
        {
            return;
        }

        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(ModifiedBrush));
        OnPropertyChanged(nameof(SimulBrush));
        OnPropertyChanged(nameof(IsSimulation));
    }

    private static bool IsChanged(string current, string original)
    {
        return !Normalize(current).Equals(Normalize(original), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim();
    }
}
