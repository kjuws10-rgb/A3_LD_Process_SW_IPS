using Drilling.Common.Log;
using System.IO;
using System.Globalization;
using Drilling.Common.Managers;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

public enum EN_COMM_STATE
{
    Offline,
    Simulation,
    Online
}

public enum EN_EQP_MODULE
{
    WonikCtrl,
    Vision,
    Automation1,
    Motion,
    TalonLaser,
    Chiller,
    Attenuator,
    Bet,
    PowerMeter,
    PicoMotor,
    Melsec
}

public enum EN_INTERFACE_TYPE
{
    OpcUa,
    ModbusSerial,
    ModbusTcp,
    Serial,
    SocketClient,
    SocketServer,
    SocketClientUdp,
    SocketServerUdp,
    AcsNet,
    XpsNet,
    Automation1Net,
    PicoMotor
}

public sealed record ST_INTERFACE_HISTORY(
    DateTimeOffset OccurredAt,
    EN_EQP_MODULE Module,
    string NickName,
    string Action,
    string BeforeState,
    string AfterState,
    string Detail);

public sealed record ST_DEVICE_COMM_STATUS(
    EN_EQP_MODULE Module,
    EN_COMM_STATE ConnectionState);

public sealed record ST_INTERFACE_CONNECT_OPTION(
    string Endpoint,
    string LocalAddress,
    string RemoteAddress,
    int Port,
    int TimeoutMs,
    int RetryCount,
    string SerialPort,
    int BaudRate,
    string Parity,
    int DataBits,
    string StopBits,
    string Handshake,
    int MaxClientCount = 8);

public sealed record ST_INTERFACE_COMM_STATUS(
    EN_EQP_MODULE Module,
    string NickName,
    EN_INTERFACE_TYPE InterfaceType,
    int Number,
    bool AutoConnection,
    EN_COMM_STATE ConnectionState,
    bool IsSimulation,
    string Endpoint,
    string LastSent,
    string LastReceived,
    string LastError,
    DateTimeOffset? LastChangedAt)
{
    public string InstanceKey
    {
        get
        {
            return $"{Module}[{Number}]";
        }
    }
}

public sealed record ST_INTERFACE_RECEIVED_MESSAGE(
    DateTimeOffset ReceivedAt,
    EN_EQP_MODULE Module,
    int Number,
    string NickName,
    string RemoteEndPoint,
    string Message);

public sealed record ST_INTERFACE_DATA(
    EN_INTERFACE_TYPE InterfaceType,
    EN_EQP_MODULE Device,
    int Number,
    string NickName,
    string SystemSection,
    bool AutoConnection,
    bool IsSimulation,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Extra = null)
{
    public string InstanceKey
    {
        get
        {
            return $"{Device}[{Number}]";
        }
    }
}

public sealed record ST_DEVICE_COMMAND_RESULT(
    bool IsSuccess,
    string Message);

public abstract class CBETFileBase
{
    public abstract IReadOnlyList<ST_BET_TABLE_DATA> Load(CancellationToken cancellationToken = default);
    public abstract void Save(
            IReadOnlyList<ST_BET_TABLE_DATA> table,
            CancellationToken cancellationToken = default);
}

public sealed class CInterfaceManager {
    private readonly Dictionary<string, CInterfaceDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly CLogManager? _logManager;
    private readonly CBETFileBase? _betFile;
    private readonly CPowerMeterFileBase? _powerMeterFile;
    private readonly CMelsec _melsec;
    private readonly CPicoMotorService _picoMotorService = new();
    private bool? _simulationMode;

    public event Func<ST_INTERFACE_RECEIVED_MESSAGE, CancellationToken, string>? MessageReceived;

    public CInterfaceManager(
        bool? simulationMode = null,
        CLogManager? logManager = null,
        CBETFileBase? betFile = null,
        CPowerMeterFileBase? powerMeterFile = null,
        IReadOnlyList<ST_MELSEC_MAP_DATA>? melsecMap = null)
    {
        _simulationMode = simulationMode;
        _logManager = logManager;
        _betFile = betFile;
        _powerMeterFile = powerMeterFile;
        _melsec = new CMelsec(this, _logManager, melsecMap);
    }

    public bool IsSimulation
    {
        get
        {
            bool CheckDevice1(CInterfaceDevice device)
            {
                return device.IsSimulation;
            }

            return _devices.Count == 0
        ? _simulationMode ?? true
        : _devices.Values.All(CheckDevice1);
        }
    }

    public IReadOnlyList<CInterfaceDevice> Devices
    {
        get
        {
            return _devices.Values.ToArray();
        }
    }

    public CMelsec Melsec
    {
        get
        {
            return _melsec;
        }
    }

    public void SetSimulationMode(bool enabled)
    {
        bool restartMelsec = false;
        foreach (CInterfaceDevice device in _devices.Values)
        {
            if (device.Data.Device == EN_EQP_MODULE.Melsec)
            {
                restartMelsec = true;
                break;
            }
        }

        if (restartMelsec)
        {
            _melsec.DeInitialize();
        }

        _simulationMode = enabled;

        foreach (var device in _devices.Values)
        {
            device.SetSimulationMode(enabled);
        }

        if (restartMelsec)
        {
            _melsec.Initialize();
        }
    }

    public void Register(ST_INTERFACE_DATA data)
    {
        var key = CreateDeviceKey(data.Device, data.Number);

        if (_devices.ContainsKey(key))
        {
            throw new InvalidOperationException($"Interface device was already registered: {FormatDeviceName(data)}");
        }

        var device = new CInterfaceDevice(
            data,
            _simulationMode ?? data.IsSimulation);
        if (data.Device == EN_EQP_MODULE.Melsec)
        {
            device.EnableExternalCommunication();
        }
        device.MessageReceived += OnDeviceMessageReceived;
        _devices[key] = device;
    }

    public void Reload(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        bool reconnect = true,
        CancellationToken cancellationToken = default)
    {
        Disconnect(cancellationToken);
        _devices.Clear();

        foreach (var data in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Register(data);
        }

        PruneDeviceStateMaps();

        if (reconnect)
        {
            Connect(cancellationToken: cancellationToken);
        }
    }

    public void Initialize(CancellationToken cancellationToken = default)
    {
        Connect(init: true, cancellationToken);
    }

    public void Destroy(CancellationToken cancellationToken = default)
    {
        Disconnect(cancellationToken);
        _picoMotorService.DisconnectAll();
        _devices.Clear();
        ClearDeviceStateMaps();
    }

    public int Connect(
        bool init = false,
        CancellationToken cancellationToken = default)
    {
        var connectedCount = 0;

        foreach (var device in _devices.Values)
        {
            if (!device.Data.AutoConnection)
            {
                continue;
            }

            var beforeState = device.ConnectionState;
            if (device.Data.Device == EN_EQP_MODULE.Melsec)
            {
                ConnectMelsecDevice(device, cancellationToken);
            }
            else
            {
                device.Connect(cancellationToken);
            }
            WriteConnectionLog(init ? "INIT_CONNECT" : "CONNECT", device, beforeState);

            if (device.ConnectionState is EN_COMM_STATE.Online or EN_COMM_STATE.Simulation)
            {
                connectedCount++;
            }
        }

        return connectedCount;
    }

    public int Disconnect(CancellationToken cancellationToken = default)
    {
        foreach (var device in _devices.Values)
        {
            var beforeState = device.ConnectionState;
            if (device.Data.Device == EN_EQP_MODULE.Melsec)
            {
                DisconnectMelsecDevice(device, cancellationToken);
            }
            else
            {
                device.Disconnect(cancellationToken);
            }
            WriteConnectionLog("DISCONNECT", device, beforeState);
        }

        _melsec.DeInitialize();

        _picoMotorService.DisconnectAll();

        return _devices.Count;
    }

    public void Connect(
        EN_EQP_MODULE module,
        int number,
        bool autoConnection = true,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        ConnectDevice(device, autoConnection, cancellationToken);
    }

    public void Disconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!_devices.TryGetValue(CreateDeviceKey(module, number), out var device))
        {
            return;
        }

        DisconnectDevice(device, cancellationToken);
    }

    public void Reconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        var beforeState = device.ConnectionState;
        if (device.Data.Device == EN_EQP_MODULE.Melsec)
        {
            DisconnectMelsecDevice(device, cancellationToken);
            ConnectMelsecDevice(device, cancellationToken);
        }
        else
        {
            device.Disconnect(cancellationToken);
            device.Connect(cancellationToken);
        }
        WriteConnectionLog("RECONNECT", device, beforeState);
    }

    public string ExecuteFunction(
        EN_EQP_MODULE module,
        int number,
        string function,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        return ExecuteDeviceFunction(device, function, cancellationToken);
    }

    public bool IsConnect(EN_EQP_MODULE module, int number)
    {
        return _devices.TryGetValue(CreateDeviceKey(module, number), out var device) &&
            device.ConnectionState is EN_COMM_STATE.Online or EN_COMM_STATE.Simulation;
    }

    public bool IsSimul(EN_EQP_MODULE module, int number)
    {
        return _devices.TryGetValue(CreateDeviceKey(module, number), out var device) &&
            device.ConnectionState == EN_COMM_STATE.Simulation;
    }

    public ST_INTERFACE_DATA? GetInterfaceData(EN_EQP_MODULE module, int number)
    {
        return _devices.TryGetValue(CreateDeviceKey(module, number), out var device)
            ? device.Data
            : null;
    }

    internal void UpdateMelsecCommunicationState(
        int number,
        bool online,
        string lastSent,
        string lastReceived,
        string lastError)
    {
        if (!_devices.TryGetValue(CreateDeviceKey(EN_EQP_MODULE.Melsec, number), out CInterfaceDevice? device) ||
            device.IsSimulation)
        {
            return;
        }

        device.SetExternalCommunicationState(
            online ? EN_COMM_STATE.Online : EN_COMM_STATE.Offline,
            lastSent,
            lastReceived,
            lastError);
    }

    public void Connect(
        string nickName,
        bool autoConnection = true,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);
        ConnectDevice(device, autoConnection, cancellationToken);
    }

    public void Disconnect(
        string nickName,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) || device is null)
        {
            return;
        }

        DisconnectDevice(device, cancellationToken);
    }

    public void Reconnect(
        string nickName,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);

        var beforeState = device.ConnectionState;
        if (device.Data.Device == EN_EQP_MODULE.Melsec)
        {
            DisconnectMelsecDevice(device, cancellationToken);
            ConnectMelsecDevice(device, cancellationToken);
        }
        else
        {
            device.Disconnect(cancellationToken);
            device.Connect(cancellationToken);
        }
        WriteConnectionLog("RECONNECT", device, beforeState);
    }

    public string ExecuteFunction(
        string nickName,
        string function,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);
        return ExecuteDeviceFunction(device, function, cancellationToken);
    }

    public bool IsConnect(string nickName)
    {
        return TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) &&
            device is not null &&
            device.ConnectionState is EN_COMM_STATE.Online or EN_COMM_STATE.Simulation;
    }

    public bool IsSimul(string nickName)
    {
        return TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) &&
            device is not null &&
            device.ConnectionState == EN_COMM_STATE.Simulation;
    }

    public ST_INTERFACE_DATA? GetInterfaceData(string nickName)
    {
        return TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) && device is not null
            ? device.Data
            : null;
    }

    public IReadOnlyList<ST_INTERFACE_DATA> GetInterfaceList(EN_EQP_MODULE? module = null)
    {
        bool FilterDevice2(CInterfaceDevice device)
        {
            return module is null || device.Data.Device == module;
        }

        ST_INTERFACE_DATA SelectDevice3(CInterfaceDevice device)
        {
            return device.Data;
        }

        EN_EQP_MODULE GetDataSortKey4(ST_INTERFACE_DATA data)
        {
            return data.Device;
        }

        int GetDataSortKey5(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey6(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return _devices.Values
            .Where(FilterDevice2)
            .Select(SelectDevice3)
            .OrderBy(GetDataSortKey4)
            .ThenBy(GetDataSortKey5)
            .ThenBy(GetDataSortKey6, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ST_INTERFACE_COMM_STATUS> GetInterfaceCommunicationList(EN_EQP_MODULE? module = null)
    {
        bool FilterDevice7(CInterfaceDevice device)
        {
            return module is null || device.Data.Device == module;
        }

        ST_INTERFACE_COMM_STATUS SelectDevice8(CInterfaceDevice device)
        {
            return device.GetCommunicationStatus();
        }

        EN_EQP_MODULE GetStatusSortKey9(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Module;
        }

        int GetStatusSortKey10(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Number;
        }

        string GetStatusSortKey11(ST_INTERFACE_COMM_STATUS status)
        {
            return status.NickName;
        }

        return _devices.Values
            .Where(FilterDevice7)
            .Select(SelectDevice8)
            .OrderBy(GetStatusSortKey9)
            .ThenBy(GetStatusSortKey10)
            .ThenBy(GetStatusSortKey11, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ST_INTERFACE_HISTORY> ReadInterfaceHistory(
        EN_EQP_MODULE? module = null,
        string nickName = "",
        int maxRows = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ST_INTERFACE_HISTORY> history = _logManager is null
            ? []
            : _logManager.ReadInterfaceRecent(module, nickName, maxRows);

        return history;
    }

    public IReadOnlyList<ST_DEVICE_COMM_STATUS> GetCommunicationStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EN_EQP_MODULE GroupByDeviceCallback12(CInterfaceDevice device)
        {
            return device.Data.Device;
        }

        ST_DEVICE_COMM_STATUS SelectGroup13(IGrouping<EN_EQP_MODULE, CInterfaceDevice> group)
        {
            EN_COMM_STATE SelectDevice1(CInterfaceDevice device)
            {
                return device.ConnectionState;
            }

            return new ST_DEVICE_COMM_STATUS(
                            group.Key,
                            CollapseConnectionState(group.Select(SelectDevice1)));
        }

        EN_EQP_MODULE GetStatusSortKey14(ST_DEVICE_COMM_STATUS status)
        {
            return status.Module;
        }

        var statuses = _devices.Values
            .GroupBy(GroupByDeviceCallback12)
            .Select(SelectGroup13)
            .OrderBy(GetStatusSortKey14)
            .ToArray();

        return statuses;
    }

    private static EN_COMM_STATE CollapseConnectionState(IEnumerable<EN_COMM_STATE> states)
    {
        var stateArray = states.ToArray();
        bool CheckState15(EN_COMM_STATE state)
        {
            return state == EN_COMM_STATE.Offline;
        }

        if (stateArray.Any(CheckState15))
        {
            return EN_COMM_STATE.Offline;
        }
        bool CheckState16(EN_COMM_STATE state)
        {
            return state == EN_COMM_STATE.Simulation;
        }

        if (stateArray.All(CheckState16))
        {
            return EN_COMM_STATE.Simulation;
        }

        return EN_COMM_STATE.Online;
    }

    private void ConnectDevice(
        CInterfaceDevice device,
        bool autoConnection,
        CancellationToken cancellationToken)
    {
        if (!autoConnection && device.Data.AutoConnection)
        {
            return;
        }

        var beforeState = device.ConnectionState;
        if (device.Data.Device == EN_EQP_MODULE.Melsec)
        {
            ConnectMelsecDevice(device, cancellationToken);
        }
        else
        {
            device.Connect(cancellationToken);
        }
        WriteConnectionLog("CONNECT", device, beforeState);
    }

    private void DisconnectDevice(
        CInterfaceDevice device,
        CancellationToken cancellationToken)
    {
        var beforeState = device.ConnectionState;
        if (device.Data.Device == EN_EQP_MODULE.Melsec)
        {
            DisconnectMelsecDevice(device, cancellationToken);
        }
        else
        {
            device.Disconnect(cancellationToken);
        }
        WriteConnectionLog("DISCONNECT", device, beforeState);
    }

    private void ConnectMelsecDevice(
        CInterfaceDevice device,
        CancellationToken cancellationToken)
    {
        _melsec.Initialize();
        if (device.IsSimulation)
        {
            _melsec.Open(device.Data.Number, cancellationToken);
            device.SetExternalCommunicationState(
                EN_COMM_STATE.Simulation,
                "[SIMULATION] OPEN",
                "[SIMULATION] READY",
                "");
            return;
        }

        try
        {
            _melsec.Open(device.Data.Number, cancellationToken);
            device.SetExternalCommunicationState(
                EN_COMM_STATE.Online,
                "OPEN",
                "CONNECTED",
                "");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            device.SetExternalCommunicationState(
                EN_COMM_STATE.Offline,
                "OPEN",
                "",
                exception.Message);
        }
    }

    private void DisconnectMelsecDevice(
        CInterfaceDevice device,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _melsec.DeInitialize();
        EN_COMM_STATE closedState = device.IsSimulation
            ? EN_COMM_STATE.Simulation
            : EN_COMM_STATE.Offline;
        device.SetExternalCommunicationState(
            closedState,
            device.IsSimulation ? "[SIMULATION] CLOSE" : "CLOSE",
            device.IsSimulation ? "[SIMULATION] CLOSED" : "CLOSED",
            "");
    }

    private string ExecuteDeviceFunction(
        CInterfaceDevice device,
        string function,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = device.ExecuteFunction(function, cancellationToken);
            WriteCommandLog(device, function, response);
            return response;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            WriteErrorLog(device, function, ex.Message);
            throw;
        }
    }

    private CInterfaceDevice GetDeviceOrThrow(EN_EQP_MODULE module, int number)
    {
        return _devices.TryGetValue(CreateDeviceKey(module, number), out var device)
            ? device
            : throw new KeyNotFoundException($"Interface device was not registered: {FormatDeviceName(module, number)}");
    }

    private CInterfaceDevice GetDeviceByNickNameOrThrow(string nickName)
    {
        return TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) && device is not null
            ? device
            : throw new KeyNotFoundException($"Interface device was not registered: {nickName}");
    }

    private bool TryGetDeviceByNickName(
        string nickName,
        out CInterfaceDevice? device,
        bool throwIfAmbiguous = false)
    {
        var normalized = NormalizeNickName(nickName);
        bool FilterItem17(CInterfaceDevice item)
        {
            return NormalizeNickName(item.Data.NickName).Equals(normalized, StringComparison.OrdinalIgnoreCase);
        }

        var matches = _devices.Values
            .Where(FilterItem17)
            .ToArray();

        if (matches.Length == 0)
        {
            device = null;
            return false;
        }

        if (matches.Length > 1 && throwIfAmbiguous)
        {
            string SelectItem18(CInterfaceDevice item)
            {
                return FormatDeviceName(item.Data);
            }

            var candidates = string.Join(", ", matches.Select(SelectItem18));
            throw new InvalidOperationException(
                $"Interface NICKNAME is duplicated: {nickName}. Use DEVICE + NUMBER instead. Candidates: {candidates}");
        }

        device = matches[0];
        return true;
    }

    private void WriteConnectionLog(
        string action,
        CInterfaceDevice device,
        EN_COMM_STATE beforeState)
    {
        if (_logManager is null)
        {
            return;
        }

        var status = device.GetCommunicationStatus();
        var afterState = FormatConnectionState(status.ConnectionState);

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            afterState = $"{afterState} / {status.LastError}";
        }

        _logManager.WriteInterfaceConnection(
            device.Data.Device,
            action,
            device.Data.NickName,
            FormatConnectionState(beforeState),
            afterState);
    }

    private void WriteCommandLog(
        CInterfaceDevice device,
        string command,
        string response)
    {
        if (_logManager is null)
        {
            return;
        }

        var status = device.GetCommunicationStatus();
        var sentCommand = string.IsNullOrWhiteSpace(status.LastSent)
            ? command
            : status.LastSent;

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            _logManager.WriteInterfaceError(
                device.Data.Device,
                device.Data.NickName,
                sentCommand,
                status.LastError);
            return;
        }

        _logManager.WriteInterfaceCommand(
            device.Data.Device,
            device.Data.NickName,
            sentCommand,
            response,
            FormatConnectionState(status.ConnectionState));
    }

    private void WriteErrorLog(
        CInterfaceDevice device,
        string command,
        string detail)
    {
        var status = device.GetCommunicationStatus();
        var sentCommand = string.IsNullOrWhiteSpace(status.LastSent)
            ? command
            : status.LastSent;

        _logManager?.WriteInterfaceError(
            device.Data.Device,
            device.Data.NickName,
            sentCommand,
            detail);
    }

    private string OnDeviceMessageReceived(
        CInterfaceDevice device,
        ST_COMM_RECEIVED_MESSAGE message,
        CancellationToken cancellationToken)
    {
        var receivedMessage = new ST_INTERFACE_RECEIVED_MESSAGE(
            message.ReceivedAt,
            device.Data.Device,
            device.Data.Number,
            device.Data.NickName,
            message.RemoteEndPoint,
            message.Message);

        _logManager?.WriteInterfaceCommand(
            device.Data.Device,
            device.Data.NickName,
            $"RECV:{message.RemoteEndPoint}",
            message.Message,
            "SOCKET_SERVER");

        var handler = MessageReceived;

        if (handler is null)
        {
            return "ACK";
        }

        var response = "ACK";
        foreach (var callback in handler.GetInvocationList()
                     .Cast<Func<ST_INTERFACE_RECEIVED_MESSAGE, CancellationToken, string>>())
        {
            var callbackResponse = callback(receivedMessage, cancellationToken);
            if (!string.IsNullOrWhiteSpace(callbackResponse))
            {
                response = callbackResponse;
            }
        }

        return string.IsNullOrWhiteSpace(response) ? "ACK" : response;
    }

    private void WriteTalonCommandLog(
        ST_INTERFACE_DATA data,
        string command,
        string response)
    {
        WriteInterfaceCommandLog(data, command, response);
    }

    private void WriteTalonErrorLog(
        ST_INTERFACE_DATA data,
        string command,
        string detail)
    {
        WriteInterfaceErrorLog(data, command, detail);
    }

    private void WriteInterfaceCommandLog(
        ST_INTERFACE_DATA data,
        string command,
        string response)
    {
        _logManager?.WriteInterfaceCommand(
            data.Device,
            data.NickName,
            command,
            response,
            data.IsSimulation ? "SIMULATION" : "");
    }

    private void WriteInterfaceErrorLog(
        ST_INTERFACE_DATA data,
        string command,
        string detail)
    {
        _logManager?.WriteInterfaceError(
            data.Device,
            data.NickName,
            command,
            detail);
    }

    private static string FormatConnectionState(EN_COMM_STATE state)
    {
        string EvaluateStateSwitch1()
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

        return EvaluateStateSwitch1();
    }

    private static string NormalizeNickName(string nickName)
    {
        return nickName.Trim().ToUpperInvariant();
    }

    private static string CreateDeviceKey(EN_EQP_MODULE module, int number)
    {
        return $"{module}:{number}";
    }

    private static string FormatDeviceName(ST_INTERFACE_DATA data)
    {
        return $"{data.Device}[{data.Number}]/{data.NickName}";
    }

    private static string FormatDeviceName(EN_EQP_MODULE module, int number)
    {
        return $"{module}[{number}]";
    }

    private readonly Dictionary<int, HashSet<int>> _laserOnHeads = [];
    private readonly Dictionary<int, ST_TALON_STATUS> _talonStatuses = [];
    private readonly Dictionary<int, ST_ORION_CHILLER_STATUS> _chillerStatuses = [];
    private readonly Dictionary<int, ST_ATTENUATOR_STATUS> _attenuatorStatuses = [];
    private readonly Dictionary<int, ST_BET_STATUS> _betStatuses = [];
    private readonly Dictionary<int, ST_POWER_METER_STATUS> _powerMeterStatuses = [];

    public ST_LASER_STATUS GetLaserStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return GetLaserStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public ST_LASER_STATUS GetLaserStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData(number);

        if (!IsInterfaceSimulation(interfaceData))
        {
            var liveStatus = RefreshTalonLaserStatus(number, cancellationToken);

            return new ST_LASER_STATUS(
                liveStatus.LaserOn,
                liveStatus.ShutterOpen,
                liveStatus.GateOpen,
                liveStatus.OutputPower);
        }

        var simulationStatus = GetTalonStatus(number);
        var outputPower = simulationStatus.OutputPower > 0.0
            ? simulationStatus.OutputPower
            : simulationStatus.LaserOn ? 8.5 : 0.0;

        return new ST_LASER_STATUS(
            simulationStatus.LaserOn,
            simulationStatus.ShutterOpen,
            simulationStatus.GateOpen,
            outputPower);
    }

    public void SetLaser(
        int headNo,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        SetLaser(interfaceData?.Number ?? 0, headNo, enabled, cancellationToken);
    }

    public void SetLaser(
        int number,
        int headNo,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData(number);

        if (interfaceData is not null && IsInterfaceSimulation(interfaceData))
        {
            SetSimulLaserHead(number, headNo, enabled);
            var status = CTalonLaser.Apply(
                EN_TALON_COMMAND.SetLaserOnOff,
                enabled ? 1.0 : 0.0,
                "",
                GetTalonStatus(number),
                simulation: true);
            SetTalonStatus(number, status);
            return;
        }

        var result = ExecuteTalonLaserCommand(
            number,
            EN_TALON_COMMAND.SetLaserOnOff,
            enabled ? 1.0 : 0.0,
            cancellationToken);

        if (result.IsSuccess)
        {
            SetSimulLaserHead(number, headNo, enabled);
        }
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteTalonLaserCommand(
        EN_TALON_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "Talon interface is not registered.")
            : ExecuteTalonLaserCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteTalonLaserCommand(
        int number,
        EN_TALON_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var commandText = CTalonLaser.Build(command, parameter);

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Talon command is not defined: {command}");
        }

        var interfaceData = GetTalonInterfaceData(number);

        if (interfaceData is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Talon interface is not registered: {FormatDeviceName(EN_EQP_MODULE.TalonLaser, number)}");
        }

        if (IsInterfaceSimulation(interfaceData))
        {
            var status = CTalonLaser.Apply(
                command,
                parameter,
                "",
                GetTalonStatus(number),
                simulation: true);
            SetTalonStatus(number, status);
            WriteTalonCommandLog(interfaceData, commandText, "SIMULATION OK");

            return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:TALON:{number}:{command}:OK");
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetTalonStatus(number, GetTalonStatus(number) with { LastError = EN_TALON_ERROR.Timeout });
            WriteTalonErrorLog(interfaceData, commandText, "OFFLINE");
            return new ST_DEVICE_COMMAND_RESULT(false, $"Talon interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            commandText,
            cancellationToken);

        if (!CTalonLaser.IsValidResponse(response))
        {
            SetTalonStatus(number, GetTalonStatus(number) with { LastError = EN_TALON_ERROR.InvalidResponse });
            WriteTalonErrorLog(interfaceData, commandText, $"INVALID RESPONSE / {response}");
            return new ST_DEVICE_COMMAND_RESULT(false, $"Talon invalid response. Command={commandText}, Response={response}");
        }

        var updatedStatus = CTalonLaser.Apply(
            command,
            parameter,
            response,
            GetTalonStatus(number),
            simulation: false);
        SetTalonStatus(number, updatedStatus);

        return new ST_DEVICE_COMMAND_RESULT(true, response);
    }

    public ST_TALON_STATUS RefreshTalonLaserStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return interfaceData is null
            ? ST_TALON_STATUS.Empty
            : RefreshTalonLaserStatus(interfaceData.Number, cancellationToken);
    }

    public ST_TALON_STATUS RefreshTalonLaserStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData(number);

        if (IsInterfaceSimulation(interfaceData))
        {
            return GetTalonStatus(number);
        }

        EN_TALON_COMMAND[] commands =
        [
            EN_TALON_COMMAND.GetDiodeCurrent,
            EN_TALON_COMMAND.GetDiodeTemp,
            EN_TALON_COMMAND.GetGateOpenClose,
            EN_TALON_COMMAND.GetShutterOpenClose,
            EN_TALON_COMMAND.GetExtGateEnableDisable,
            EN_TALON_COMMAND.GetOutputPower,
            EN_TALON_COMMAND.GetTowerTemp,
            EN_TALON_COMMAND.GetQsw,
            EN_TALON_COMMAND.GetThgSpot,
            EN_TALON_COMMAND.GetThgHour,
            EN_TALON_COMMAND.RequestStatusString,
            EN_TALON_COMMAND.GetQMode
        ];

        foreach (var command in commands)
        {
            var result = ExecuteTalonLaserCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetTalonStatus(number);
            }
        }

        return GetTalonStatus(number);
    }

    public ST_CHILLER_STATUS GetChillerStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return GetChillerStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public ST_CHILLER_STATUS GetChillerStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetChillerInterfaceData(number)))
        {
            RefreshChillerStatus(number, cancellationToken);
        }

        var status = GetChillerStatusValue(number);

        return new ST_CHILLER_STATUS(
            status.RunState == EN_CHILLER_RUN_STATE.Run,
            status.LiquidTempC,
            12.8,
            0.42,
            !string.IsNullOrWhiteSpace(status.AlarmCode),
            status.SetTempC,
            FormatChillerRunState(status.RunState),
            status.AlarmCode);
    }

    private static string FormatChillerRunState(EN_CHILLER_RUN_STATE state)
    {
        string EvaluateStateSwitch2()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_CHILLER_RUN_STATE.Run:
                    return "RUN";
                case EN_CHILLER_RUN_STATE.PumpOnly:
                    return "PUMP ONLY";
                default:
                    return "STOP";
            }
        }

        return EvaluateStateSwitch2();
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteChillerCommand(
        EN_CHILLER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "Chiller interface is not registered.")
            : ExecuteChillerCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteChillerCommand(
        int number,
        EN_CHILLER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interfaceData = GetChillerInterfaceData(number);

        if (interfaceData is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Chiller interface is not registered: {FormatDeviceName(EN_EQP_MODULE.Chiller, number)}");
        }

        var commandText = COrionChiller.Build(command, parameter);

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"Chiller command is not defined: {command}");
        }

        var logCommandText = COrionChiller.DescribeCommand(command, parameter);

        if (string.IsNullOrWhiteSpace(logCommandText))
        {
            logCommandText = commandText;
        }

        if (command == EN_CHILLER_COMMAND.ResetAlarm)
        {
            var status = COrionChiller.Apply(
                command,
                parameter,
                "",
                GetChillerStatusValue(number),
                simulation: IsInterfaceSimulation(interfaceData));
            SetChillerStatus(number, status);
            WriteInterfaceErrorLog(interfaceData, logCommandText, "NOT SUPPORTED");

            return new ST_DEVICE_COMMAND_RESULT(false, "Orion Chiller alarm reset is not supported by the checked protocol.");
        }

        if (IsInterfaceSimulation(interfaceData))
        {
            var status = COrionChiller.Apply(
                command,
                parameter,
                "",
                GetChillerStatusValue(number),
                simulation: true);
            SetChillerStatus(number, status);
            WriteInterfaceCommandLog(interfaceData, logCommandText, "SIMULATION OK");

            return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:CHILLER:{number}:{command}:OK");
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetChillerStatus(number, GetChillerStatusValue(number) with
            {
                CommOk = false,
                LastError = EN_CHILLER_ERROR.Timeout,
                UpdatedAt = DateTimeOffset.Now
            });
            WriteInterfaceErrorLog(interfaceData, logCommandText, "OFFLINE");

            return new ST_DEVICE_COMMAND_RESULT(false, $"Chiller interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            commandText,
            cancellationToken);

        var updatedStatus = COrionChiller.Apply(
            command,
            parameter,
            response,
            GetChillerStatusValue(number),
            simulation: false);
        SetChillerStatus(number, updatedStatus);

        return COrionChiller.IsSuccessResponse(response)
            ? new ST_DEVICE_COMMAND_RESULT(true, response)
            : new ST_DEVICE_COMMAND_RESULT(false, $"Chiller command failed. Command={commandText}, Response={response}");
    }

    public ST_ORION_CHILLER_STATUS RefreshChillerStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return interfaceData is null
            ? ST_ORION_CHILLER_STATUS.Empty
            : RefreshChillerStatus(interfaceData.Number, cancellationToken);
    }

    public ST_ORION_CHILLER_STATUS RefreshChillerStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData(number);

        if (IsInterfaceSimulation(interfaceData))
        {
            return GetChillerStatusValue(number);
        }

        EN_CHILLER_COMMAND[] commands =
        [
            EN_CHILLER_COMMAND.PollLiquidTemp,
            EN_CHILLER_COMMAND.PollSetTemp,
            EN_CHILLER_COMMAND.PollRunState,
            EN_CHILLER_COMMAND.PollAlarmCode
        ];

        foreach (var command in commands)
        {
            var result = ExecuteChillerCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetChillerStatusValue(number);
            }
        }

        return GetChillerStatusValue(number);
    }

    public ST_ATTENUATOR_STATUS GetAttenuatorStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return GetAttenuatorStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public ST_ATTENUATOR_STATUS GetAttenuatorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetAttenuatorInterfaceData(number)))
        {
            RefreshAttenuatorStatus(number, cancellationToken);
        }

        return GetAttenuatorStatusValue(number);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteAttenuatorCommand(
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "CONEX_AGP interface is not registered.")
            : ExecuteAttenuatorCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteAttenuatorCommand(
        int number,
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_ATTENUATOR_COMMAND.Refresh)
        {
            var status = RefreshAttenuatorStatus(number, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"CONEX_AGP refreshed. Position {status.CurrentPosition:F3} DEG.");
        }

        var commandText = CConex_AGP.Build(command, parameter);

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"CONEX_AGP command is not defined: {command}");
        }

        var interfaceData = GetAttenuatorInterfaceData(number);

        if (interfaceData is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"CONEX_AGP interface is not registered: {FormatDeviceName(EN_EQP_MODULE.Attenuator, number)}");
        }

        if (IsInterfaceSimulation(interfaceData))
        {
            var status = CConex_AGP.Apply(
                command,
                parameter,
                "",
                GetAttenuatorStatusValue(number),
                simulation: true);
            SetAttenuatorStatus(number, status);
            WriteInterfaceCommandLog(interfaceData, commandText, "SIMULATION OK");

            return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:CONEX_AGP:{number}:{command}:OK");
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetAttenuatorStatus(number, GetAttenuatorStatusValue(number) with
            {
                CommOk = false,
                LastError = EN_CONEX_AGP_ERROR.Timeout,
                UpdatedAt = DateTimeOffset.Now
            });
            WriteInterfaceErrorLog(interfaceData, commandText, "OFFLINE");

            return new ST_DEVICE_COMMAND_RESULT(false, $"CONEX_AGP interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            commandText,
            cancellationToken);

        var updatedStatus = CConex_AGP.Apply(
            command,
            parameter,
            response,
            GetAttenuatorStatusValue(number),
            simulation: false);
        SetAttenuatorStatus(number, updatedStatus);

        return CConex_AGP.IsSuccessResponse(response)
            ? new ST_DEVICE_COMMAND_RESULT(true, response)
            : new ST_DEVICE_COMMAND_RESULT(false, $"CONEX_AGP command failed. Command={commandText}, Response={response}");
    }

    public ST_ATTENUATOR_STATUS RefreshAttenuatorStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return interfaceData is null
            ? CreateDefaultAttenuatorStatus()
            : RefreshAttenuatorStatus(interfaceData.Number, cancellationToken);
    }

    public ST_ATTENUATOR_STATUS RefreshAttenuatorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData(number);

        if (IsInterfaceSimulation(interfaceData))
        {
            return GetAttenuatorStatusValue(number);
        }

        EN_ATTENUATOR_COMMAND[] commands =
        [
            EN_ATTENUATOR_COMMAND.PollCurrentPosition,
            EN_ATTENUATOR_COMMAND.PollTargetPosition,
            EN_ATTENUATOR_COMMAND.PollState
        ];

        foreach (var command in commands)
        {
            var result = ExecuteAttenuatorCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetAttenuatorStatusValue(number);
            }
        }

        return GetAttenuatorStatusValue(number);
    }

    public ST_BET_STATUS GetBETStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return GetBETStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public ST_BET_STATUS GetBETStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetBETInterfaceData(number)))
        {
            RefreshBETStatus(number, cancellationToken);
        }

        return GetBETStatusValue(number);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteBETCommand(
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "BeamExpander interface is not registered.")
            : ExecuteBETCommand(interfaceData.Number, command, parameter1, parameter2, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteBETCommand(
        int number,
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_BET_COMMAND.Refresh)
        {
            var status = RefreshBETStatus(number, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"BET refreshed. MAG {status.CurrentMagnification:F3}, DIV {status.CurrentDivergence:F3}.");
        }

        if (command == EN_BET_COMMAND.MoveTable)
        {
            var table = LoadBETData(cancellationToken);
            var index = (int)Math.Round(parameter1);
            bool MatchItem19(ST_BET_TABLE_DATA item)
            {
                return item.Index == index;
            }

            var row = table.FirstOrDefault(MatchItem19);

            if (row is null)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, $"BET table row was not found: {index}");
            }

            return ExecuteBETCommand(
                number,
                EN_BET_COMMAND.MoveManual,
                row.Magnification,
                row.Divergence,
                cancellationToken);
        }

        var commandText = CBeamExpander.Build(command, parameter1, parameter2);
        var logCommandText = CBeamExpander.BuildLogText(command, parameter1, parameter2);

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"BeamExpander command is not defined: {command}");
        }

        var interfaceData = GetBETInterfaceData(number);

        if (interfaceData is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"BeamExpander interface is not registered: {FormatDeviceName(EN_EQP_MODULE.Bet, number)}");
        }

        if (IsInterfaceSimulation(interfaceData))
        {
            var status = CBeamExpander.Apply(
                command,
                parameter1,
                parameter2,
                "",
                GetBETStatusValue(number),
                simulation: true);
            SetBETStatus(number, status);
            WriteInterfaceCommandLog(interfaceData, logCommandText, "SIMULATION OK");

            return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:BEAM_EXPENDER:{number}:{command}:OK");
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetBETStatus(number, GetBETStatusValue(number) with
            {
                CommOk = false,
                LastError = EN_BET_ERROR.Timeout,
                UpdatedAt = DateTimeOffset.Now
            });
            WriteInterfaceErrorLog(interfaceData, logCommandText, "OFFLINE");

            return new ST_DEVICE_COMMAND_RESULT(false, $"BeamExpander interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            commandText,
            cancellationToken);

        var updatedStatus = CBeamExpander.Apply(
            command,
            parameter1,
            parameter2,
            response,
            GetBETStatusValue(number),
            simulation: false);
        SetBETStatus(number, updatedStatus);

        return CBeamExpander.IsSuccessResponse(response)
            ? new ST_DEVICE_COMMAND_RESULT(true, response)
            : new ST_DEVICE_COMMAND_RESULT(false, $"BeamExpander command failed. Command={logCommandText}, Response={response}");
    }

    public ST_BET_STATUS RefreshBETStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return interfaceData is null
            ? CreateDefaultBETStatus()
            : RefreshBETStatus(interfaceData.Number, cancellationToken);
    }

    public ST_BET_STATUS RefreshBETStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData(number);

        if (IsInterfaceSimulation(interfaceData))
        {
            return GetBETStatusValue(number);
        }

        EN_BET_COMMAND[] commands =
        [
            EN_BET_COMMAND.PollMagnificationPosition,
            EN_BET_COMMAND.PollDivergencePosition
        ];

        foreach (var command in commands)
        {
            var result = ExecuteBETCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetBETStatusValue(number);
            }
        }

        return GetBETStatusValue(number);
    }

    public IReadOnlyList<ST_BET_TABLE_DATA> LoadBETData(CancellationToken cancellationToken = default)
    {
        return _betFile is null
            ? CreateDefaultBETData()
            : _betFile.Load(cancellationToken);
    }

    public void SaveBETData(
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        CancellationToken cancellationToken = default)
    {
        if (_betFile != null)
        {
            _betFile.Save(table, cancellationToken);
        }
    }

    public ST_POWER_METER_TABLE_DATA LoadPowerMeterData(
        string processFile = "",
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? CreateDefaultPowerMeterData(processFile)
            : _powerMeterFile.Load(processFile, cancellationToken);
    }

    public void CreatePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        if (_powerMeterFile != null)
        {
            _powerMeterFile.Create(processFile, cancellationToken);
        }
    }

    public void DeletePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        if (_powerMeterFile != null)
        {
            _powerMeterFile.Delete(processFile, cancellationToken);
        }
    }

    public void RenamePowerMeterData(
        string oldProcessFile,
        string newProcessFile,
        CancellationToken cancellationToken = default)
    {
        if (_powerMeterFile != null)
        {
            _powerMeterFile.Rename(oldProcessFile, newProcessFile, cancellationToken);
        }
    }

    public void SavePowerMeterData(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken = default)
    {
        if (_powerMeterFile != null)
        {
            _powerMeterFile.Save(processFile, steps, cancellationToken);
        }
    }

    public ST_POWER_METER_STATUS GetPowerMeterStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return GetPowerMeterStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public ST_POWER_METER_STATUS GetPowerMeterStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetPowerMeterInterfaceData(number)))
        {
            RefreshPowerMeterStatus(number, cancellationToken);
        }

        return GetPowerMeterStatusValue(number);
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePowerMeterCommand(
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter interface is not registered.")
            : ExecutePowerMeterCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePowerMeterCommand(
        int number,
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_POWER_METER_COMMAND.Refresh)
        {
            var status = RefreshPowerMeterStatus(number, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"PowerMeter refreshed. Power {status.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture)} {status.Unit}.");
        }

        var commandText = CPowerMeter.Build(command, parameter);

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter command is not defined: {command}");
        }

        var interfaceData = GetPowerMeterInterfaceData(number);

        if (interfaceData is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter interface is not registered: {FormatDeviceName(EN_EQP_MODULE.PowerMeter, number)}");
        }

        if (IsInterfaceSimulation(interfaceData))
        {
            var status = CPowerMeter.Apply(
                command,
                parameter,
                "",
                GetPowerMeterStatusValue(number),
                simulation: true);
            SetPowerMeterStatus(number, status);
            WriteInterfaceCommandLog(interfaceData, commandText, "SIMULATION OK");

            return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:POWER_METER:{number}:{command}:OK");
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetPowerMeterStatus(number, GetPowerMeterStatusValue(number) with
            {
                LastError = EN_POWER_METER_ERROR.Timeout,
                MeasuredAt = DateTimeOffset.Now
            });
            WriteInterfaceErrorLog(interfaceData, commandText, "OFFLINE");

            return new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            commandText,
            cancellationToken);

        var updatedStatus = CPowerMeter.Apply(
            command,
            parameter,
            response,
            GetPowerMeterStatusValue(number),
            simulation: false);
        SetPowerMeterStatus(number, updatedStatus);

        return CPowerMeter.IsSuccessResponse(response)
            ? new ST_DEVICE_COMMAND_RESULT(true, response)
            : new ST_DEVICE_COMMAND_RESULT(false, $"PowerMeter command failed. Command={commandText}, Response={response}");
    }

    public ST_POWER_METER_STATUS RefreshPowerMeterStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return interfaceData is null
            ? ST_POWER_METER_STATUS.Empty
            : RefreshPowerMeterStatus(interfaceData.Number, cancellationToken);
    }

    public ST_POWER_METER_STATUS RefreshPowerMeterStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData(number);

        if (interfaceData is null)
        {
            return GetPowerMeterStatusValue(number);
        }

        EN_POWER_METER_COMMAND[] commands =
        [
            EN_POWER_METER_COMMAND.QueryHardwareDescription,
            EN_POWER_METER_COMMAND.QuerySerialNumber,
            EN_POWER_METER_COMMAND.QueryWaveLength,
            EN_POWER_METER_COMMAND.QueryBeamPosition,
            EN_POWER_METER_COMMAND.ReadPower
        ];

        foreach (var command in commands)
        {
            var result = ExecutePowerMeterCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetPowerMeterStatusValue(number);
            }
        }

        return GetPowerMeterStatusValue(number);
    }

    public ST_PICO_MOTOR_STATUS GetPicoMotorStatus(
        CancellationToken cancellationToken = default)
    {
        int GetItemSortKey20(ST_INTERFACE_DATA item)
        {
            return item.Number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(GetItemSortKey20)
            .FirstOrDefault();
        if (data is null)
        {
            return ST_PICO_MOTOR_STATUS.Empty with
            {
                CommOk = false,
                LastError = EN_PICO_MOTOR_ERROR.NotSupported,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        return GetPicoMotorStatus(data.Number, cancellationToken);
    }

    public ST_PICO_MOTOR_STATUS GetPicoMotorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        bool MatchItem21(ST_INTERFACE_DATA item)
        {
            return item.Number == number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(MatchItem21);
        if (data is null)
        {
            return ST_PICO_MOTOR_STATUS.Empty with
            {
                CommOk = false,
                LastError = EN_PICO_MOTOR_ERROR.NotSupported,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        try
        {
            return _picoMotorService.Refresh(
                data.Number,
                IsInterfaceSimulation(data),
                cancellationToken);
        }
        catch
        {
            return _picoMotorService.GetStatus(data.Number) with
            {
                CommOk = false,
                LastError = EN_PICO_MOTOR_ERROR.Timeout,
                UpdatedAt = DateTimeOffset.Now
            };
        }
    }

    public ST_PICO_MOTOR_STATUS RefreshPicoMotorStatus(
        CancellationToken cancellationToken = default)
    {
        return GetPicoMotorStatus(cancellationToken);
    }

    public ST_PICO_MOTOR_STATUS RefreshPicoMotorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        return GetPicoMotorStatus(number, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePicoMotorCommand(
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        int GetItemSortKey22(ST_INTERFACE_DATA item)
        {
            return item.Number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(GetItemSortKey22)
            .FirstOrDefault();
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PicoMotor interface is not registered.");
        }

        return ExecutePicoMotorCommand(data.Number, command, motorNo, parameter, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePicoMotorCommand(
        int number,
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        bool MatchItem23(ST_INTERFACE_DATA item)
        {
            return item.Number == number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(MatchItem23);
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PicoMotor interface {number} is not registered.");
        }

        var result = _picoMotorService.Execute(
            data.Number,
            IsInterfaceSimulation(data),
            command,
            motorNo,
            parameter,
            cancellationToken);
        var commandText = $"{command}:MOTOR={motorNo}:VALUE={parameter.ToString("0.######", CultureInfo.InvariantCulture)}";
        if (result.IsSuccess)
        {
            WriteInterfaceCommandLog(data, commandText, result.Message);
        }
        else
        {
            WriteInterfaceErrorLog(data, commandText, result.Message);
        }
        return result;
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePicoMotorAllMove(
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default)
    {
        int GetItemSortKey24(ST_INTERFACE_DATA item)
        {
            return item.Number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(GetItemSortKey24)
            .FirstOrDefault();
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PicoMotor interface is not registered.");
        }

        return ExecutePicoMotorAllMove(data.Number, motorNos, positionMm, count, cancellationToken);
    }

    public ST_DEVICE_COMMAND_RESULT ExecutePicoMotorAllMove(
        int number,
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default)
    {
        bool MatchItem25(ST_INTERFACE_DATA item)
        {
            return item.Number == number;
        }

        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(MatchItem25);
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PicoMotor interface {number} is not registered.");
        }

        var result = _picoMotorService.ExecuteAllMove(
            data.Number,
            IsInterfaceSimulation(data),
            motorNos,
            positionMm,
            count,
            cancellationToken);
        var commandText = $"ALL_MOVE:MOTOR={string.Join(",", motorNos)}:DIST={positionMm.ToString("0.######", CultureInfo.InvariantCulture)}:COUNT={count}";
        if (result.IsSuccess)
        {
            WriteInterfaceCommandLog(data, commandText, result.Message);
        }
        else
        {
            WriteInterfaceErrorLog(data, commandText, result.Message);
        }
        return result;
    }

    private void SetSimulLaserHead(
        int number,
        int headNo,
        bool enabled)
    {
        var laserOnHeads = GetLaserOnHeads(number);

        if (enabled)
        {
            laserOnHeads.Add(headNo);
        }
        else
        {
            laserOnHeads.Remove(headNo);
        }
    }

    private HashSet<int> GetLaserOnHeads(int number)
    {
        if (!_laserOnHeads.TryGetValue(number, out var heads))
        {
            heads = [];
            _laserOnHeads[number] = heads;
        }

        return heads;
    }

    private ST_TALON_STATUS GetTalonStatus(int number)
    {
        if (!_talonStatuses.TryGetValue(number, out var status))
        {
            status = ST_TALON_STATUS.Empty;
            _talonStatuses[number] = status;
        }

        return status;
    }

    private void SetTalonStatus(int number, ST_TALON_STATUS status)
    {
        _talonStatuses[number] = status;
    }

    private ST_ORION_CHILLER_STATUS GetChillerStatusValue(int number)
    {
        if (!_chillerStatuses.TryGetValue(number, out var status))
        {
            status = ST_ORION_CHILLER_STATUS.Empty;
            _chillerStatuses[number] = status;
        }

        return status;
    }

    private void SetChillerStatus(int number, ST_ORION_CHILLER_STATUS status)
    {
        _chillerStatuses[number] = status;
    }

    private ST_ATTENUATOR_STATUS GetAttenuatorStatusValue(int number)
    {
        if (!_attenuatorStatuses.TryGetValue(number, out var status))
        {
            status = CreateDefaultAttenuatorStatus();
            _attenuatorStatuses[number] = status;
        }

        return status;
    }

    private void SetAttenuatorStatus(int number, ST_ATTENUATOR_STATUS status)
    {
        _attenuatorStatuses[number] = status;
    }

    private ST_BET_STATUS GetBETStatusValue(int number)
    {
        if (!_betStatuses.TryGetValue(number, out var status))
        {
            status = CreateDefaultBETStatus();
            _betStatuses[number] = status;
        }

        return status;
    }

    private void SetBETStatus(int number, ST_BET_STATUS status)
    {
        _betStatuses[number] = status;
    }

    private ST_POWER_METER_STATUS GetPowerMeterStatusValue(int number)
    {
        if (!_powerMeterStatuses.TryGetValue(number, out var status))
        {
            status = ST_POWER_METER_STATUS.Empty;
            _powerMeterStatuses[number] = status;
        }

        return status;
    }

    private void SetPowerMeterStatus(int number, ST_POWER_METER_STATUS status)
    {
        _powerMeterStatuses[number] = status;
    }

    private void PruneDeviceStateMaps()
    {
        PruneDeviceStateMap(_laserOnHeads, EN_EQP_MODULE.TalonLaser);
        PruneDeviceStateMap(_talonStatuses, EN_EQP_MODULE.TalonLaser);
        PruneDeviceStateMap(_chillerStatuses, EN_EQP_MODULE.Chiller);
        PruneDeviceStateMap(_attenuatorStatuses, EN_EQP_MODULE.Attenuator);
        PruneDeviceStateMap(_betStatuses, EN_EQP_MODULE.Bet);
        PruneDeviceStateMap(_powerMeterStatuses, EN_EQP_MODULE.PowerMeter);
    }

    private void ClearDeviceStateMaps()
    {
        _laserOnHeads.Clear();
        _talonStatuses.Clear();
        _chillerStatuses.Clear();
        _attenuatorStatuses.Clear();
        _betStatuses.Clear();
        _powerMeterStatuses.Clear();
    }

    private void PruneDeviceStateMap<T>(
        Dictionary<int, T> statusMap,
        EN_EQP_MODULE module)
    {
        int SelectData26(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        var validNumbers = GetInterfaceList(module)
            .Select(SelectData26)
            .ToHashSet();
        bool FilterNumber27(int number)
        {
            return !validNumbers.Contains(number);
        }

        foreach (var number in statusMap.Keys.Where(FilterNumber27).ToArray())
        {
            statusMap.Remove(number);
        }
    }

    private ST_INTERFACE_DATA? GetTalonInterfaceData()
    {
        int GetDataSortKey28(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey29(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return GetInterfaceList(EN_EQP_MODULE.TalonLaser)
            .OrderBy(GetDataSortKey28)
            .ThenBy(GetDataSortKey29, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetTalonInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.TalonLaser, number);
    }

    private ST_INTERFACE_DATA? GetAttenuatorInterfaceData()
    {
        int GetDataSortKey30(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey31(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return GetInterfaceList(EN_EQP_MODULE.Attenuator)
            .OrderBy(GetDataSortKey30)
            .ThenBy(GetDataSortKey31, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetAttenuatorInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.Attenuator, number);
    }

    private ST_INTERFACE_DATA? GetBETInterfaceData()
    {
        int GetDataSortKey32(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey33(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return GetInterfaceList(EN_EQP_MODULE.Bet)
            .OrderBy(GetDataSortKey32)
            .ThenBy(GetDataSortKey33, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetBETInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.Bet, number);
    }

    private ST_INTERFACE_DATA? GetPowerMeterInterfaceData()
    {
        int GetDataSortKey34(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey35(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return GetInterfaceList(EN_EQP_MODULE.PowerMeter)
            .OrderBy(GetDataSortKey34)
            .ThenBy(GetDataSortKey35, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetPowerMeterInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.PowerMeter, number);
    }

    private ST_INTERFACE_DATA? GetChillerInterfaceData()
    {
        int GetDataSortKey36(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey37(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return GetInterfaceList(EN_EQP_MODULE.Chiller)
            .OrderBy(GetDataSortKey36)
            .ThenBy(GetDataSortKey37, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetChillerInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.Chiller, number);
    }

    private bool IsInterfaceSimulation(ST_INTERFACE_DATA? interfaceData)
    {
        return interfaceData is null || IsSimul(interfaceData.Device, interfaceData.Number);
    }

    private static ST_ATTENUATOR_STATUS CreateDefaultAttenuatorStatus()
    {
        return new ST_ATTENUATOR_STATUS(55.0, 55.0, "READY");
    }

    private static ST_BET_STATUS CreateDefaultBETStatus()
    {
        return new ST_BET_STATUS(1020.000, 1020.000, 1626.000, 1626.000, 1020.000, 1626.000, false, true, true, false);
    }

    private static IReadOnlyList<ST_BET_TABLE_DATA> CreateDefaultBETData()
    {
        return
        [
            new(0, 1020.000, 1626.000, "2times"),
            new(1, 2351.000, 1118.000, "3times"),
            new(2, 3014.000, 1278.000, "4times"),
            new(3, 3410.000, 1706.000, "5times"),
            new(4, 3673.000, 2267.000, "6times")
        ];
    }

    private static ST_POWER_METER_TABLE_DATA CreateDefaultPowerMeterData(string processFile)
    {
        var selectedFile = string.IsNullOrWhiteSpace(processFile)
            ? "POWER_CHECK.pwm"
            : Path.GetFileName(processFile.Trim());

        if (!selectedFile.EndsWith(".pwm", StringComparison.OrdinalIgnoreCase))
        {
            selectedFile = $"{selectedFile}.pwm";
        }

        ST_POWER_METER_PROCESS_DATA[] processes =
        [
            new("POWER_CHECK.pwm", selectedFile.Equals("POWER_CHECK.pwm", StringComparison.OrdinalIgnoreCase)),
            new("POWER_CAL.pwm", selectedFile.Equals("POWER_CAL.pwm", StringComparison.OrdinalIgnoreCase)),
            new("DAILY_CHECK.pwm", selectedFile.Equals("DAILY_CHECK.pwm", StringComparison.OrdinalIgnoreCase))
        ];

        ST_POWER_METER_STEP_DATA[] steps =
        [
            new(1, "PWM_CHECK_HEAD01", true, "W", 23.50, 1.200, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.2040, "READY"),
            new(2, "PWM_CHECK_HEAD02", true, "W", 23.50, 1.200, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.2052, "WAIT"),
            new(3, "PWM_CHECK_HEAD03", true, "W", 23.50, 1.000, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.0068, "WAIT"),
            new(4, "PWM_CHECK_HEAD04", true, "W", 23.50, 1.000, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.0034, "WAIT"),
            new(5, "PWM_CHECK_HEAD05", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8020, "WAIT"),
            new(6, "PWM_CHECK_HEAD06", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8015, "WAIT"),
            new(7, "PWM_CHECK_HEAD07", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8008, "WAIT"),
            new(8, "PWM_CHECK_HEAD08", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8024, "WAIT")
        ];

        return new ST_POWER_METER_TABLE_DATA(processes, selectedFile, steps);
    }
}

internal delegate string CInterfaceDeviceMessageHandler(
    CInterfaceDevice device,
    ST_COMM_RECEIVED_MESSAGE message,
    CancellationToken cancellationToken);

public sealed class CInterfaceDevice
{
    private bool _simulationMode;
    private readonly CCommBase _comm;
    private bool _externalCommunication;
    private EN_COMM_STATE _externalConnectionState = EN_COMM_STATE.Offline;
    private string _externalLastSent = "";
    private string _externalLastReceived = "";
    private string _externalLastError = "";
    private DateTimeOffset? _externalLastChangedAt;
    private string _simulationLastSent = "";
    private string _simulationLastReceived = "";
    private string _simulationLastError = "";
    private DateTimeOffset? _simulationLastChangedAt;

    internal event CInterfaceDeviceMessageHandler? MessageReceived;

    public CInterfaceDevice(
        ST_INTERFACE_DATA data,
        bool simulationMode)
    {
        Data = data;
        ConnectOption = CInterfaceConnectOption.Parse(data);
        _simulationMode = simulationMode;
        _comm = CComm.Create(Data, ConnectOption);

        if (_comm is CSocketServerComm messageSource)
        {
            messageSource.MessageReceived += OnCommMessageReceived;
        }

        if (_simulationMode)
        {
            TouchSimulationState();
        }
    }

    public ST_INTERFACE_DATA Data { get; }

    public ST_INTERFACE_CONNECT_OPTION ConnectOption { get; }

    public EN_COMM_STATE ConnectionState
    {
        get
        {
            if (_externalCommunication)
            {
                return _externalConnectionState;
            }

            return _simulationMode
        ? EN_COMM_STATE.Simulation
        : _comm.ConnectionState;
        }
    }

    public bool IsSimulation
    {
        get
        {
            return _simulationMode;
        }
    }

    public ST_INTERFACE_COMM_STATUS GetCommunicationStatus()
    {
        if (_externalCommunication)
        {
            return new ST_INTERFACE_COMM_STATUS(
                Data.Device,
                Data.NickName,
                Data.InterfaceType,
                Data.Number,
                Data.AutoConnection,
                _externalConnectionState,
                _simulationMode,
                ConnectOption.Endpoint,
                _externalLastSent,
                _externalLastReceived,
                _externalLastError,
                _externalLastChangedAt);
        }

        return new ST_INTERFACE_COMM_STATUS(
            Data.Device,
            Data.NickName,
            Data.InterfaceType,
            Data.Number,
            Data.AutoConnection,
            ConnectionState,
            _simulationMode,
            _comm.Endpoint,
            _simulationMode ? _simulationLastSent : _comm.LastSent,
            _simulationMode ? _simulationLastReceived : _comm.LastReceived,
            _simulationMode ? _simulationLastError : _comm.LastError,
            _simulationMode ? _simulationLastChangedAt : _comm.LastChangedAt);
    }

    public void SetSimulationMode(bool enabled)
    {
        if (_simulationMode == enabled)
        {
            return;
        }

        _simulationMode = enabled;

        if (_externalCommunication)
        {
            _externalConnectionState = enabled
                ? EN_COMM_STATE.Simulation
                : EN_COMM_STATE.Offline;
            _externalLastError = "";
            _externalLastChangedAt = DateTimeOffset.Now;
            return;
        }

        if (_simulationMode)
        {
            _comm.Disconnect();
            TouchSimulationState();
        }
    }

    public void Connect(CancellationToken cancellationToken = default)
    {
        if (_externalCommunication)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _externalConnectionState = _simulationMode
                ? EN_COMM_STATE.Simulation
                : EN_COMM_STATE.Offline;
            _externalLastChangedAt = DateTimeOffset.Now;
            return;
        }

        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _simulationLastError = "";
            TouchSimulationState();
            return;
        }

        _comm.Connect(cancellationToken);
    }

    public void Disconnect(CancellationToken cancellationToken = default)
    {
        if (_externalCommunication)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _externalConnectionState = EN_COMM_STATE.Offline;
            _externalLastChangedAt = DateTimeOffset.Now;
            return;
        }

        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TouchSimulationState();
            return;
        }

        _comm.Disconnect(cancellationToken);
    }

    public string ExecuteFunction(
        string function,
        CancellationToken cancellationToken = default)
    {
        if (_externalCommunication)
        {
            throw new InvalidOperationException(
                "MELSEC commands must use CMelsec typed read/write functions.");
        }

        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _simulationLastSent = function;
            _simulationLastReceived = $"SIM:{Data.NickName}:{function}:OK";
            _simulationLastError = "";
            TouchSimulationState();
            return _simulationLastReceived;
        }

        return _comm.Execute(function, cancellationToken);
    }

    internal void EnableExternalCommunication()
    {
        _externalCommunication = true;
        _externalConnectionState = _simulationMode
            ? EN_COMM_STATE.Simulation
            : EN_COMM_STATE.Offline;
        _externalLastChangedAt = DateTimeOffset.Now;
    }

    internal void SetExternalCommunicationState(
        EN_COMM_STATE state,
        string lastSent,
        string lastReceived,
        string lastError)
    {
        _externalConnectionState = state;
        _externalLastSent = lastSent;
        _externalLastReceived = lastReceived;
        _externalLastError = lastError;
        _externalLastChangedAt = DateTimeOffset.Now;
    }

    private void TouchSimulationState()
    {
        _simulationLastChangedAt = DateTimeOffset.Now;
    }

    private string OnCommMessageReceived(
        ST_COMM_RECEIVED_MESSAGE message,
        CancellationToken cancellationToken)
    {
        var handler = MessageReceived;

        if (handler is null)
        {
            return "ACK";
        }

        string response = "ACK";
        foreach (Delegate callbackItem in handler.GetInvocationList())
        {
            CInterfaceDeviceMessageHandler callback =
                (CInterfaceDeviceMessageHandler)callbackItem;
            string callbackResponse = callback(this, message, cancellationToken);
            if (!string.IsNullOrWhiteSpace(callbackResponse))
            {
                response = callbackResponse;
            }
        }

        return string.IsNullOrWhiteSpace(response) ? "ACK" : response;
    }
}

internal static class CInterfaceConnectOption
{
    public static ST_INTERFACE_CONNECT_OPTION Parse(ST_INTERFACE_DATA data)
    {
        string SelectArgument38(string argument)
        {
            return argument.Trim();
        }

        var args = data.Arguments
            .Select(SelectArgument38)
            .Concat(Enumerable.Repeat("", 5))
            .Take(5)
            .ToArray();
        ST_INTERFACE_CONNECT_OPTION EvaluateInterfaceTypeSwitch3()
        {
            var switchValue = data.InterfaceType;
            switch (switchValue)
            {
                case EN_INTERFACE_TYPE.Serial or EN_INTERFACE_TYPE.ModbusSerial:
                    return CreateSerialOption(data, args);
                case EN_INTERFACE_TYPE.SocketClient or EN_INTERFACE_TYPE.SocketServer or
                        EN_INTERFACE_TYPE.SocketClientUdp or EN_INTERFACE_TYPE.SocketServerUdp or
                        EN_INTERFACE_TYPE.ModbusTcp:
                    return CreateSocketOption(data, args);
                case EN_INTERFACE_TYPE.AcsNet:
                    return CreateAcsOption(args);
                case EN_INTERFACE_TYPE.XpsNet:
                    return CreateXpsOption(args);
                case EN_INTERFACE_TYPE.Automation1Net:
                    return CreateAutomation1Option(args);
                case EN_INTERFACE_TYPE.PicoMotor:
                    return CreatePicoMotorOption();
                case EN_INTERFACE_TYPE.OpcUa:
                    return CreateOpcUaOption(args);
                default:
                    return CreateSocketOption(data, args);
            }
        }

        return EvaluateInterfaceTypeSwitch3();
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateSocketOption(
        ST_INTERFACE_DATA data,
        IReadOnlyList<string> args)
    {
        var isServer = data.InterfaceType is EN_INTERFACE_TYPE.SocketServer or EN_INTERFACE_TYPE.SocketServerUdp;
        var localAddress = DefaultIfBlank(args[0], "0.0.0.0");
        var remoteAddress = DefaultIfBlank(args[1], isServer ? "*" : "127.0.0.1");
        var port = ReadInt(args[2], 0);
        var timeoutMs = ReadInt(args[3], 3000);
        var retryCount = ReadInt(args[4], 1);
        var maxClientCount = Math.Max(
            1,
            ReadInt(ReadExtra(data, "MAX_CLIENT_COUNT"), 8));
        var endpointAddress = isServer ? localAddress : remoteAddress;
        var endpoint = port > 0 ? $"{endpointAddress}:{port}" : endpointAddress;

        return new ST_INTERFACE_CONNECT_OPTION(
            endpoint,
            localAddress,
            remoteAddress,
            port,
            timeoutMs,
            retryCount,
            "",
            0,
            "",
            0,
            "",
            "",
            maxClientCount);
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateAcsOption(IReadOnlyList<string> args)
    {
        var localAddress = DefaultIfBlank(args[0], "0.0.0.0");
        var remoteAddress = DefaultIfBlank(args[1], "127.0.0.1");
        var port = ReadInt(args[2], 701);
        var timeoutMs = ReadInt(args[3], 3000);
        var retryCount = ReadInt(args[4], 1);

        return new ST_INTERFACE_CONNECT_OPTION(
            $"{remoteAddress}:{port}",
            localAddress,
            remoteAddress,
            port,
            timeoutMs,
            retryCount,
            "",
            0,
            "",
            0,
            "",
            "");
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateXpsOption(IReadOnlyList<string> args)
    {
        var localAddress = DefaultIfBlank(args[0], "0.0.0.0");
        var remoteAddress = DefaultIfBlank(args[1], "192.168.254.254");
        var port = ReadInt(args[2], 5001);
        var timeoutMs = ReadInt(args[3], 3000);
        var retryCount = ReadInt(args[4], 1);

        return new ST_INTERFACE_CONNECT_OPTION(
            $"{remoteAddress}:{port}",
            localAddress,
            remoteAddress,
            port,
            timeoutMs,
            retryCount,
            "",
            0,
            "",
            0,
            "",
            "");
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateAutomation1Option(IReadOnlyList<string> args)
    {
        var localAddress = DefaultIfBlank(args[0], "0.0.0.0");
        var remoteAddress = DefaultIfBlank(args[1], "127.0.0.1");
        var port = ReadInt(args[2], 12200);
        var timeoutMs = ReadInt(args[3], 3000);
        var retryCount = ReadInt(args[4], 1);

        return new ST_INTERFACE_CONNECT_OPTION(
            $"{remoteAddress}:{port}",
            localAddress,
            remoteAddress,
            port,
            timeoutMs,
            retryCount,
            "",
            0,
            "",
            0,
            "",
            "");
    }

    private static ST_INTERFACE_CONNECT_OPTION CreatePicoMotorOption()
    {
        return new ST_INTERFACE_CONNECT_OPTION(
            "CmdLib (USB/Ethernet)",
            "",
            "",
            0,
            3000,
            1,
            "",
            0,
            "",
            0,
            "",
            "");
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateSerialOption(
        ST_INTERFACE_DATA data,
        IReadOnlyList<string> args)
    {
        var port = DefaultIfBlank(args[0], "COM1");
        var baudRate = ReadInt(args[1], 9600);
        var parity = DefaultIfBlank(args[2], "NONE");
        var dataBits = ReadInt(args[3], 8);
        var stopBits = DefaultIfBlank(args[4], "ONE");
        var handshake = ReadExtra(data, "FLOW_CONTROL");

        return new ST_INTERFACE_CONNECT_OPTION(
            $"{port}:{baudRate}",
            "",
            "",
            0,
            3000,
            1,
            port,
            baudRate,
            parity,
            dataBits,
            stopBits,
            handshake);
    }

    private static ST_INTERFACE_CONNECT_OPTION CreateOpcUaOption(IReadOnlyList<string> args)
    {
        var endpoint = DefaultIfBlank(args[0], "opc.tcp://127.0.0.1:4840");
        var timeoutMs = ReadInt(args[3], 3000);
        var retryCount = ReadInt(args[4], 1);

        return new ST_INTERFACE_CONNECT_OPTION(
            endpoint,
            "",
            endpoint,
            0,
            timeoutMs,
            retryCount,
            "",
            0,
            "",
            0,
            "",
            "");
    }

    private static string ReadExtra(
        ST_INTERFACE_DATA data,
        params string[] names)
    {
        if (data.Extra is null)
        {
            return "";
        }

        foreach (var name in names)
        {
            if (data.Extra.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string DefaultIfBlank(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static int ReadInt(string value, int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }
}
