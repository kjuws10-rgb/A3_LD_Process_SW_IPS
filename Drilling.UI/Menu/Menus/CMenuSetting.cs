using System.Globalization;
using System.IO;
using System.ComponentModel;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuSetting : CBindingBase, IMenu
{
    private readonly ISettingManager _settingManager;
    private readonly Func<string> _selectedTabProvider;
    private readonly Action<string> _selectedTabSetter;
    private readonly Func<string> _selectedGroupProvider;
    private readonly Action<string> _selectedGroupSetter;
    private readonly Func<CMenuSetting?> _editScreenProvider;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<EN_MENU, string> _showLoadingScreen;
    private readonly Action _refreshShellStatus;
    private readonly Func<Task> _refreshCurrentScreen;
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
        ISettingManager settingManager,
        Func<string> selectedTabProvider,
        Action<string> selectedTabSetter,
        Func<string> selectedGroupProvider,
        Action<string> selectedGroupSetter,
        Func<CMenuSetting?> editScreenProvider,
        Action<string> setStatusMessage,
        Action<EN_MENU, string> showLoadingScreen,
        Action refreshShellStatus,
        Func<Task> refreshCurrentScreen)
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

        SelectTabCommand = new CButtonCommand(async parameter => await SelectTab(parameter));
        SelectGroupCommand = new CButtonCommand(async parameter => await SelectGroup(parameter));
        ConnectInterfaceCommand = new CButtonCommand(async _ => await ConnectInterface(), _ => CanOperateSelectedInterface);
        DisconnectInterfaceCommand = new CButtonCommand(async _ => await DisconnectInterface(), _ => CanOperateSelectedInterface);
        SaveCommand = new CButtonCommand(async _ => await Save());
        CancelCommand = new CButtonCommand(async _ => await Cancel());
        ReloadCommand = new CButtonCommand(async _ => await Reload());
    }

    public EN_MENU Menu
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
                _selectedInterfaceRow.PropertyChanged -= SelectedInterfaceRowChanged;
            }

            if (!SetProperty(ref _selectedInterfaceRow, value))
            {
                if (_selectedInterfaceRow is not null)
                {
                    _selectedInterfaceRow.PropertyChanged += SelectedInterfaceRowChanged;
                }

                return;
            }

            if (_selectedInterfaceRow is not null)
            {
                _selectedInterfaceRow.PropertyChanged += SelectedInterfaceRowChanged;
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

    public async Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        var displaySections = new List<ST_SCREEN_SECTION>();

        foreach (var section in Sections)
        {
            var sectionParameters = await _settingManager.LoadSection(section, cancellationToken);
            displaySections.Add(new ST_SCREEN_SECTION(
                ToTabText(section),
                sectionParameters.Select(item => new ST_DISPLAY_ITEM(
                    item.Name,
                    $"{item.Value} {item.Unit}".Trim(),
                    item.Description)).ToArray()));
        }

        var selectedTab = NormalizeTab(_selectedTabProvider());
        var selectedSection = ToSection(selectedTab);
        IReadOnlyList<ST_SYSTEM_PARAMETER> loadedParameters = selectedTab == "INTERFACE"
            ? []
            : await _settingManager.LoadSection(selectedSection, cancellationToken);
        var loadedRows = BuildParameterRows(loadedParameters);
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> loadedInterfaceRows = selectedTab == "INTERFACE"
            ? BuildInterfaceRows(await _settingManager.LoadInterfaceList(cancellationToken))
            : [];
        var editScreen = _editScreenProvider();
        var allRows = GetEditRows(loadedRows, editScreen, selectedTab);
        var allInterfaceRows = GetEditInterfaceRows(loadedInterfaceRows, editScreen, selectedTab);
        var groups = selectedTab == "INTERFACE"
            ? BuildInterfaceGroups(allInterfaceRows)
            : BuildGroups(allRows);
        var selectedGroup = NormalizeGroup(_selectedGroupProvider(), groups);
        var filteredRows = selectedGroup == "ALL"
            ? allRows
            : allRows.Where(row => row.Group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase)).ToArray();
        var filteredInterfaceRows = selectedGroup == "ALL"
            ? allInterfaceRows
            : allInterfaceRows.Where(row => row.Type.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase)).ToArray();
        var history = await _settingManager.LoadHistory(selectedSection, cancellationToken);

        Apply(
            displaySections,
            BuildHistoryItems(history),
            selectedTab,
            selectedGroup,
            BuildTabs(selectedTab),
            groups.Select(group => new ST_SETTING_GROUP(group, group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase))).ToArray(),
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

    private async Task SelectTab(object? parameter)
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
        await _refreshCurrentScreen();
    }

    private async Task SelectGroup(object? parameter)
    {
        if (parameter is not string group || string.IsNullOrWhiteSpace(group))
        {
            return;
        }

        var selectedGroup = group.Trim().ToUpperInvariant();
        _selectedGroupSetter(selectedGroup);
        _setStatusMessage($"Setting group {SelectedTab} / {selectedGroup} selected.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task ConnectInterface()
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
            await _settingManager.ConnectInterface(
                ParseDevice(row.Device),
                ReadInt(row.Number, row.NickName, "NUMBER"));
            _setStatusMessage($"{InterfaceRowLabel(row)} connect command sent.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or KeyNotFoundException or IOException)
        {
            _setStatusMessage($"Connect blocked. {exception.Message}");
            return;
        }

        await _refreshCurrentScreen();
    }

    private async Task DisconnectInterface()
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
            await _settingManager.DisconnectInterface(
                ParseDevice(row.Device),
                ReadInt(row.Number, row.NickName, "NUMBER"));
            _setStatusMessage($"{InterfaceRowLabel(row)} disconnected.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or KeyNotFoundException or IOException)
        {
            _setStatusMessage($"Disconnect blocked. {exception.Message}");
            return;
        }

        await _refreshCurrentScreen();
    }

    private async Task Save()
    {
        if (SelectedTab == "INTERFACE")
        {
            try
            {
                await _settingManager.SaveInterfaceList(ToInterfaceData(AllInterfaceRows));
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
            await _refreshCurrentScreen();
            return;
        }

        var section = ToSection(SelectedTab);
        var parameters = AllParameterRows
            .Select(row => new ST_SYSTEM_PARAMETER(
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
                row.Max))
            .ToArray();

        try
        {
            await _settingManager.SaveSection(section, parameters);
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
        await _refreshCurrentScreen();
    }

    private async Task Cancel()
    {
        _showLoadingScreen(EN_MENU.Setting, "SETTING");
        _setStatusMessage($"Setting edits canceled. Reloaded {SelectedTab} / {SelectedGroup} from CSV.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task Reload()
    {
        _showLoadingScreen(EN_MENU.Setting, "SETTING");
        _setStatusMessage($"Setting {SelectedTab} / {SelectedGroup} reloaded from CSV.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
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

        return rows.FirstOrDefault(row => IsSameInterfaceKey(row, current))
            ?? rows.FirstOrDefault(row => row.NickName.Equals(current.NickName, StringComparison.OrdinalIgnoreCase))
            ?? rows[0];
    }

    private static bool IsSameInterfaceKey(
        ST_SETTING_INTERFACE_ROW left,
        ST_SETTING_INTERFACE_ROW right)
    {
        return NormalizeSettingText(left.Device, "").Equals(NormalizeSettingText(right.Device, ""), StringComparison.OrdinalIgnoreCase) &&
            left.Number.Trim().Equals(right.Number.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void SelectedInterfaceRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ST_SETTING_INTERFACE_ROW.Simul) or
            nameof(ST_SETTING_INTERFACE_ROW.IsSimulation) or
            nameof(ST_SETTING_INTERFACE_ROW.IsModified))
        {
            RefreshInterfaceCommandState();
        }
    }

    private void RefreshInterfaceCommandState()
    {
        OnPropertyChanged(nameof(CanOperateSelectedInterface));
        ConnectInterfaceCommand.NotifyCanExecuteChanged();
        DisconnectInterfaceCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<ST_SETTING_TAB> BuildTabs(string selectedTab)
    {
        return Sections
            .Select(section => ToTabText(section))
            .Select(tab => new ST_SETTING_TAB(tab, tab.Equals(selectedTab, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> BuildParameterRows(
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters)
    {
        return parameters
            .Where(parameter => parameter.Show && parameter.Use)
            .Select(parameter => new ST_SYSTEM_PARAMETER_ROW(
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
                parameter.Max))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildGroups(IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> rows)
    {
        var groups = rows
            .Select(row => row.Group)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { "ALL" }.Concat(groups).ToArray();
    }

    private static IReadOnlyList<string> BuildInterfaceGroups(IReadOnlyList<ST_SETTING_INTERFACE_ROW> rows)
    {
        var groups = rows
            .Select(row => row.Type)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { "ALL" }.Concat(groups).ToArray();
    }

    private static IReadOnlyList<ST_DISPLAY_ITEM> BuildHistoryItems(
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        return history
            .Select(item => new ST_DISPLAY_ITEM(
                item.ChangedAt.ToString("HH:mm:ss"),
                $"{item.Action} / {item.ParameterName}",
                $"{item.OldValue} -> {item.NewValue}"))
            .ToArray();
    }

    private static IReadOnlyList<ST_SETTING_HISTORY_ROW> BuildHistoryRows(
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        return history
            .Where(item => !item.Action.Equals("SAVE", StringComparison.OrdinalIgnoreCase))
            .Select(item => new ST_SETTING_HISTORY_ROW(
                item.ChangedAt.ToString("HH:mm:ss"),
                item.OperatorId,
                ToTabText(item.Section),
                item.ParameterName,
                item.OldValue,
                item.NewValue,
                "Warn"))
            .ToArray();
    }

    private static IReadOnlyList<ST_SETTING_SUMMARY_ROW> BuildSummaryRows(
        string selectedTab,
        IReadOnlyList<ST_SYSTEM_PARAMETER_ROW> rows,
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> interfaceRows,
        IReadOnlyList<ST_SETTING_HISTORY> history)
    {
        var modifiedCount = selectedTab == "INTERFACE"
            ? interfaceRows.Count(row => row.IsModified)
            : rows.Count(row => row.IsModified);
        var lastSavedTime = history.FirstOrDefault(item => item.Action.Equals("SAVE", StringComparison.OrdinalIgnoreCase))?.ChangedAt;

        return
        [
            new("Modified Items", modifiedCount.ToString(), modifiedCount > 0 ? "Warn" : "Ok"),
            new("Last Saved Time", lastSavedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-")
        ];
    }

    private static IReadOnlyList<ST_SETTING_INTERFACE_ROW> BuildInterfaceRows(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces)
    {
        return interfaces
            .OrderBy(item => item.Device)
            .ThenBy(item => item.Number)
            .ThenBy(item => item.NickName, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) =>
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
            })
            .ToArray();
    }

    private static IReadOnlyList<ST_INTERFACE_DATA> ToInterfaceData(
        IReadOnlyList<ST_SETTING_INTERFACE_ROW> rows)
    {
        return rows
            .Select(row => new ST_INTERFACE_DATA(
                ParseInterfaceType(row.Type),
                ParseDevice(row.Device),
                ReadInt(row.Number, row.NickName, "NUMBER"),
                RequireText(row.NickName, "NICKNAME"),
                RequireText(row.SystemSection, "SYSTEM_SECTION"),
                ReadBool(row.AutoConnection, row.NickName, "AUTOCONNECTION"),
                ReadBool(row.Simul, row.NickName, "SIMUL"),
                [row.Arg1.Trim(), row.Arg2.Trim(), row.Arg3.Trim(), row.Arg4.Trim(), row.Arg5.Trim()],
                row.Extra))
            .ToArray();
    }

    private static EN_INTERFACE_TYPE ParseInterfaceType(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "OPCUA" => EN_INTERFACE_TYPE.OpcUa,
            "MODBUS_SERIAL" => EN_INTERFACE_TYPE.ModbusSerial,
            "MODBUS_TCP" => EN_INTERFACE_TYPE.ModbusTcp,
            "SERIAL" => EN_INTERFACE_TYPE.Serial,
            "SOCKET_C" => EN_INTERFACE_TYPE.SocketClient,
            "SOCKET_S" => EN_INTERFACE_TYPE.SocketServer,
            "SOCKET_C_UDP" => EN_INTERFACE_TYPE.SocketClientUdp,
            "SOCKET_S_UDP" => EN_INTERFACE_TYPE.SocketServerUdp,
            "ACS_NET" or "ACS" => EN_INTERFACE_TYPE.AcsNet,
            "XPS_NET" or "XPS" or "NEWPORT_XPS" => EN_INTERFACE_TYPE.XpsNet,
            "AUTOMATION1_NET" or "AUTOMATION1" or "A1_NET" or "AEROTECH_AUTOMATION1" => EN_INTERFACE_TYPE.Automation1Net,
            "PICOMOTOR" or "PICO_MOTOR" or "PICO" => EN_INTERFACE_TYPE.PicoMotor,
            _ => throw new InvalidDataException($"JHMI_INTERFACE save blocked. Unknown TYPE: {value}")
        };
    }

    private static EN_EQP_MODULE ParseDevice(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "WONIK_CONTROL" or "WONIK_CTRL" or "CONTROL" => EN_EQP_MODULE.WonikCtrl,
            "WONIK_VISION" or "VISION" => EN_EQP_MODULE.Vision,
            "AUTOMATION1" or "AUTOMATION_ONE" or "A1" => EN_EQP_MODULE.Automation1,
            "MOTION" or "SCANNER" => EN_EQP_MODULE.Motion,
            "TALON" or "TALON_LASER" or "LASER" => EN_EQP_MODULE.TalonLaser,
            "CHILLER" or "ORION_CHILLER" or "SMCCHILLER" => EN_EQP_MODULE.Chiller,
            "CONEX_AGP" or "ATTENUATOR" => EN_EQP_MODULE.Attenuator,
            "BEAM_EXPANDER" or "BET" => EN_EQP_MODULE.Bet,
            "POWER_METER" or "POWERMETER" or "POWERMAX" => EN_EQP_MODULE.PowerMeter,
            "MELSEC" or "PLC" => EN_EQP_MODULE.Melsec,
            "PICO_MOTOR" or "PICOMOTOR" or "PICO" => EN_EQP_MODULE.PicoMotor,
            _ => throw new InvalidDataException($"JHMI_INTERFACE save blocked. Unknown DEVICE: {value}")
        };
    }

    private static string InterfaceTypeText(EN_INTERFACE_TYPE type)
    {
        return type switch
        {
            EN_INTERFACE_TYPE.SocketClient => "SOCKET_C",
            EN_INTERFACE_TYPE.SocketServer => "SOCKET_S",
            EN_INTERFACE_TYPE.SocketClientUdp => "SOCKET_C_UDP",
            EN_INTERFACE_TYPE.SocketServerUdp => "SOCKET_S_UDP",
            EN_INTERFACE_TYPE.ModbusSerial => "MODBUS_SERIAL",
            EN_INTERFACE_TYPE.ModbusTcp => "MODBUS_TCP",
            EN_INTERFACE_TYPE.OpcUa => "OPCUA",
            EN_INTERFACE_TYPE.Serial => "SERIAL",
            EN_INTERFACE_TYPE.AcsNet => "ACS_NET",
            EN_INTERFACE_TYPE.XpsNet => "XPS_NET",
            EN_INTERFACE_TYPE.Automation1Net => "AUTOMATION1_NET",
            EN_INTERFACE_TYPE.PicoMotor => "PICOMOTOR",
            _ => type.ToString().ToUpperInvariant()
        };
    }

    private static string DeviceText(EN_EQP_MODULE module)
    {
        return module switch
        {
            EN_EQP_MODULE.WonikCtrl => "WONIK_CONTROL",
            EN_EQP_MODULE.Vision => "WONIK_VISION",
            EN_EQP_MODULE.Automation1 => "AUTOMATION1",
            EN_EQP_MODULE.Motion => "MOTION",
            EN_EQP_MODULE.TalonLaser => "TALON",
            EN_EQP_MODULE.Chiller => "CHILLER",
            EN_EQP_MODULE.Attenuator => "CONEX_AGP",
            EN_EQP_MODULE.Bet => "BEAM_EXPANDER",
            EN_EQP_MODULE.PowerMeter => "POWER_METER",
            EN_EQP_MODULE.PicoMotor => "PICO_MOTOR",
            EN_EQP_MODULE.Melsec => "MELSEC",
            _ => module.ToString().ToUpperInvariant()
        };
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
        return value.Trim().ToUpperInvariant() switch
        {
            "1" or "TRUE" or "ON" or "YES" or "USE" or "SIMUL" or "SIMULATION" or "SIM" => true,
            "0" or "FALSE" or "OFF" or "NO" or "ONLINE" or "LIVE" or "REAL" => false,
            _ => throw new InvalidDataException($"JHMI_INTERFACE save blocked. {nickName}/{fieldName} must be 1/0 or ON/OFF.")
        };
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
        return groups.Any(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase))
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
        return tab.Trim().ToUpperInvariant() switch
        {
            "INTERFACE" => EN_SETTING_TAB.Interface,
            "IO" => EN_SETTING_TAB.Io,
            "MOTOR" => EN_SETTING_TAB.Motor,
            "ALARM" => EN_SETTING_TAB.Alarm,
            _ => EN_SETTING_TAB.Option
        };
    }

    private static string ToTabText(EN_SETTING_TAB section)
    {
        return section switch
        {
            EN_SETTING_TAB.Io => "IO",
            _ => section.ToString().ToUpperInvariant()
        };
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
            return ValueState switch
            {
                "Accent" => CStatusBrush.Simul,
                "Warn" => CStatusBrush.Wait,
                _ => CStatusBrush.PrimaryText
            };
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
            return AfterState switch
            {
                "Accent" => CStatusBrush.Simul,
                _ => CStatusBrush.Wait
            };
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
            return State switch
            {
                "Accent" => CStatusBrush.Simul,
                "Warn" => CStatusBrush.Wait,
                "Ok" => CStatusBrush.Online,
                _ => CStatusBrush.PrimaryText
            };
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
            return Simul.Trim().ToUpperInvariant() switch
            {
                "0" or "FALSE" or "OFF" or "NO" or "ONLINE" or "LIVE" or "REAL" => false,
                _ => true
            };
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
            return Simul.Trim().ToUpperInvariant() switch
            {
                "SIMULATION" or "SIMUL" or "SIM" or "1" => CStatusBrush.Simul,
                _ => CStatusBrush.Online
            };
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




