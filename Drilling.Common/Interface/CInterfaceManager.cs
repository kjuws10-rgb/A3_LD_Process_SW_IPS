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

public interface IInterfaceDevice
{
    ST_INTERFACE_DATA Data { get; }

    ST_INTERFACE_CONNECT_OPTION ConnectOption { get; }

    EN_COMM_STATE ConnectionState { get; }

    bool IsSimulation { get; }

    ST_INTERFACE_COMM_STATUS GetCommunicationStatus();

    Task Connect(CancellationToken cancellationToken = default);

    Task Disconnect(CancellationToken cancellationToken = default);

    Task<string> ExecuteFunction(
        string function,
        CancellationToken cancellationToken = default);
}

public interface IBETFile
{
    Task<IReadOnlyList<ST_BET_TABLE_DATA>> Load(CancellationToken cancellationToken = default);

    Task Save(
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        CancellationToken cancellationToken = default);
}

public interface IInterfaceManager
{
    event Func<ST_INTERFACE_RECEIVED_MESSAGE, CancellationToken, Task<string>>? MessageReceived;

    bool IsSimulation { get; }

    IReadOnlyList<IInterfaceDevice> Devices { get; }

    IMelsec Melsec { get; }

    void SetSimulationMode(bool enabled);

    void Register(ST_INTERFACE_DATA data);

    Task Reload(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        bool reconnect = true,
        CancellationToken cancellationToken = default);

    Task Initialize(CancellationToken cancellationToken = default);

    Task Destroy(CancellationToken cancellationToken = default);

    Task<int> Connect(
        bool init = false,
        CancellationToken cancellationToken = default);

    Task<int> Disconnect(CancellationToken cancellationToken = default);

    Task Connect(
        EN_EQP_MODULE module,
        int number,
        bool autoConnection = true,
        CancellationToken cancellationToken = default);

    Task Disconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default);

    Task Reconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default);

    Task<string> ExecuteFunction(
        EN_EQP_MODULE module,
        int number,
        string function,
        CancellationToken cancellationToken = default);

    bool IsConnect(EN_EQP_MODULE module, int number);

    bool IsSimul(EN_EQP_MODULE module, int number);

    ST_INTERFACE_DATA? GetInterfaceData(EN_EQP_MODULE module, int number);

    Task Connect(
        string nickName,
        bool autoConnection = true,
        CancellationToken cancellationToken = default);

    Task Disconnect(
        string nickName,
        CancellationToken cancellationToken = default);

    Task Reconnect(
        string nickName,
        CancellationToken cancellationToken = default);

    Task<string> ExecuteFunction(
        string nickName,
        string function,
        CancellationToken cancellationToken = default);

    bool IsConnect(string nickName);

    bool IsSimul(string nickName);

    ST_INTERFACE_DATA? GetInterfaceData(string nickName);

    IReadOnlyList<ST_INTERFACE_DATA> GetInterfaceList(EN_EQP_MODULE? module = null);

    IReadOnlyList<ST_INTERFACE_COMM_STATUS> GetInterfaceCommunicationList(EN_EQP_MODULE? module = null);

    Task<IReadOnlyList<ST_INTERFACE_HISTORY>> ReadInterfaceHistory(
        EN_EQP_MODULE? module = null,
        string nickName = "",
        int maxRows = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_DEVICE_COMM_STATUS>> GetCommunicationStatus(
        CancellationToken cancellationToken = default);

    Task<ST_LASER_STATUS> GetLaserStatus(CancellationToken cancellationToken = default);

    Task<ST_LASER_STATUS> GetLaserStatus(int number, CancellationToken cancellationToken = default);

    Task SetLaser(int headNo, bool enabled, CancellationToken cancellationToken = default);

    Task SetLaser(int number, int headNo, bool enabled, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteTalonLaserCommand(
        EN_TALON_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteTalonLaserCommand(
        int number,
        EN_TALON_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_TALON_STATUS> RefreshTalonLaserStatus(CancellationToken cancellationToken = default);

    Task<ST_TALON_STATUS> RefreshTalonLaserStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_CHILLER_STATUS> GetChillerStatus(CancellationToken cancellationToken = default);

    Task<ST_CHILLER_STATUS> GetChillerStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteChillerCommand(
        EN_CHILLER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteChillerCommand(
        int number,
        EN_CHILLER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_ORION_CHILLER_STATUS> RefreshChillerStatus(CancellationToken cancellationToken = default);

    Task<ST_ORION_CHILLER_STATUS> RefreshChillerStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_ATTENUATOR_STATUS> GetAttenuatorStatus(CancellationToken cancellationToken = default);

    Task<ST_ATTENUATOR_STATUS> GetAttenuatorStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteAttenuatorCommand(
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteAttenuatorCommand(
        int number,
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_ATTENUATOR_STATUS> RefreshAttenuatorStatus(CancellationToken cancellationToken = default);

    Task<ST_ATTENUATOR_STATUS> RefreshAttenuatorStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_BET_STATUS> GetBETStatus(CancellationToken cancellationToken = default);

    Task<ST_BET_STATUS> GetBETStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteBETCommand(
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecuteBETCommand(
        int number,
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_BET_STATUS> RefreshBETStatus(CancellationToken cancellationToken = default);

    Task<ST_BET_STATUS> RefreshBETStatus(int number, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_BET_TABLE_DATA>> LoadBETData(CancellationToken cancellationToken = default);

    Task SaveBETData(
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_TABLE_DATA> LoadPowerMeterData(
        string processFile = "",
        CancellationToken cancellationToken = default);

    Task CreatePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default);

    Task DeletePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default);

    Task RenamePowerMeterData(
        string oldProcessFile,
        string newProcessFile,
        CancellationToken cancellationToken = default);

    Task SavePowerMeterData(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_STATUS> GetPowerMeterStatus(CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_STATUS> GetPowerMeterStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePowerMeterCommand(
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePowerMeterCommand(
        int number,
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_STATUS> RefreshPowerMeterStatus(CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_STATUS> RefreshPowerMeterStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_PICO_MOTOR_STATUS> GetPicoMotorStatus(CancellationToken cancellationToken = default);

    Task<ST_PICO_MOTOR_STATUS> GetPicoMotorStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_PICO_MOTOR_STATUS> RefreshPicoMotorStatus(CancellationToken cancellationToken = default);

    Task<ST_PICO_MOTOR_STATUS> RefreshPicoMotorStatus(int number, CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorCommand(
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorCommand(
        int number,
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorAllMove(
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default);

    Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorAllMove(
        int number,
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default);
}

public sealed class CInterfaceManager : IInterfaceManager
{
    private readonly Dictionary<string, CInterfaceDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogManager? _logManager;
    private readonly IBETFile? _betFile;
    private readonly IPowerMeterFile? _powerMeterFile;
    private readonly CMelsec _melsec;
    private readonly CPicoMotorService _picoMotorService = new();
    private bool? _simulationMode;

    public event Func<ST_INTERFACE_RECEIVED_MESSAGE, CancellationToken, Task<string>>? MessageReceived;

    public CInterfaceManager(
        bool? simulationMode = null,
        ILogManager? logManager = null,
        IBETFile? betFile = null,
        IPowerMeterFile? powerMeterFile = null,
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
            return _devices.Count == 0
        ? _simulationMode ?? true
        : _devices.Values.All(device => device.IsSimulation);
        }
    }

    public IReadOnlyList<IInterfaceDevice> Devices
    {
        get
        {
            return _devices.Values.ToArray();
        }
    }

    public IMelsec Melsec
    {
        get
        {
            return _melsec;
        }
    }

    public void SetSimulationMode(bool enabled)
    {
        _simulationMode = enabled;

        foreach (var device in _devices.Values)
        {
            device.SetSimulationMode(enabled);
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
        device.MessageReceived += OnDeviceMessageReceived;
        _devices[key] = device;
    }

    public async Task Reload(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        bool reconnect = true,
        CancellationToken cancellationToken = default)
    {
        await Disconnect(cancellationToken);
        _devices.Clear();

        foreach (var data in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Register(data);
        }

        PruneDeviceStateMaps();

        if (reconnect)
        {
            await Connect(cancellationToken: cancellationToken);
        }
    }

    public Task Initialize(CancellationToken cancellationToken = default)
    {
        return Connect(init: true, cancellationToken);
    }

    public async Task Destroy(CancellationToken cancellationToken = default)
    {
        await Disconnect(cancellationToken);
        _picoMotorService.DisconnectAll();
        _devices.Clear();
        ClearDeviceStateMaps();
    }

    public async Task<int> Connect(
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
            await device.Connect(cancellationToken);
            WriteConnectionLog(init ? "INIT_CONNECT" : "CONNECT", device, beforeState);

            if (device.ConnectionState is EN_COMM_STATE.Online or EN_COMM_STATE.Simulation)
            {
                connectedCount++;
            }
        }

        return connectedCount;
    }

    public async Task<int> Disconnect(CancellationToken cancellationToken = default)
    {
        foreach (var device in _devices.Values)
        {
            var beforeState = device.ConnectionState;
            await device.Disconnect(cancellationToken);
            WriteConnectionLog("DISCONNECT", device, beforeState);
        }

        _picoMotorService.DisconnectAll();

        return _devices.Count;
    }

    public async Task Connect(
        EN_EQP_MODULE module,
        int number,
        bool autoConnection = true,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        await ConnectDevice(device, autoConnection, cancellationToken);
    }

    public async Task Disconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!_devices.TryGetValue(CreateDeviceKey(module, number), out var device))
        {
            return;
        }

        await DisconnectDevice(device, cancellationToken);
    }

    public async Task Reconnect(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        var beforeState = device.ConnectionState;
        await device.Disconnect(cancellationToken);
        await device.Connect(cancellationToken);
        WriteConnectionLog("RECONNECT", device, beforeState);
    }

    public async Task<string> ExecuteFunction(
        EN_EQP_MODULE module,
        int number,
        string function,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceOrThrow(module, number);
        return await ExecuteDeviceFunction(device, function, cancellationToken);
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

    public async Task Connect(
        string nickName,
        bool autoConnection = true,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);
        await ConnectDevice(device, autoConnection, cancellationToken);
    }

    public async Task Disconnect(
        string nickName,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDeviceByNickName(nickName, out var device, throwIfAmbiguous: true) || device is null)
        {
            return;
        }

        await DisconnectDevice(device, cancellationToken);
    }

    public async Task Reconnect(
        string nickName,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);

        var beforeState = device.ConnectionState;
        await device.Disconnect(cancellationToken);
        await device.Connect(cancellationToken);
        WriteConnectionLog("RECONNECT", device, beforeState);
    }

    public async Task<string> ExecuteFunction(
        string nickName,
        string function,
        CancellationToken cancellationToken = default)
    {
        var device = GetDeviceByNickNameOrThrow(nickName);
        return await ExecuteDeviceFunction(device, function, cancellationToken);
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
        return _devices.Values
            .Where(device => module is null || device.Data.Device == module)
            .Select(device => device.Data)
            .OrderBy(data => data.Device)
            .ThenBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ST_INTERFACE_COMM_STATUS> GetInterfaceCommunicationList(EN_EQP_MODULE? module = null)
    {
        return _devices.Values
            .Where(device => module is null || device.Data.Device == module)
            .Select(device => device.GetCommunicationStatus())
            .OrderBy(status => status.Module)
            .ThenBy(status => status.Number)
            .ThenBy(status => status.NickName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<IReadOnlyList<ST_INTERFACE_HISTORY>> ReadInterfaceHistory(
        EN_EQP_MODULE? module = null,
        string nickName = "",
        int maxRows = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ST_INTERFACE_HISTORY> history = _logManager is null
            ? []
            : _logManager.ReadInterfaceRecent(module, nickName, maxRows);

        return Task.FromResult(history);
    }

    public Task<IReadOnlyList<ST_DEVICE_COMM_STATUS>> GetCommunicationStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statuses = _devices.Values
            .GroupBy(device => device.Data.Device)
            .Select(group => new ST_DEVICE_COMM_STATUS(
                group.Key,
                CollapseConnectionState(group.Select(device => device.ConnectionState))))
            .OrderBy(status => status.Module)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ST_DEVICE_COMM_STATUS>>(statuses);
    }

    private static EN_COMM_STATE CollapseConnectionState(IEnumerable<EN_COMM_STATE> states)
    {
        var stateArray = states.ToArray();

        if (stateArray.Any(state => state == EN_COMM_STATE.Offline))
        {
            return EN_COMM_STATE.Offline;
        }

        if (stateArray.All(state => state == EN_COMM_STATE.Simulation))
        {
            return EN_COMM_STATE.Simulation;
        }

        return EN_COMM_STATE.Online;
    }

    private async Task ConnectDevice(
        CInterfaceDevice device,
        bool autoConnection,
        CancellationToken cancellationToken)
    {
        if (!autoConnection && device.Data.AutoConnection)
        {
            return;
        }

        var beforeState = device.ConnectionState;
        await device.Connect(cancellationToken);
        WriteConnectionLog("CONNECT", device, beforeState);
    }

    private async Task DisconnectDevice(
        CInterfaceDevice device,
        CancellationToken cancellationToken)
    {
        var beforeState = device.ConnectionState;
        await device.Disconnect(cancellationToken);
        WriteConnectionLog("DISCONNECT", device, beforeState);
    }

    private async Task<string> ExecuteDeviceFunction(
        CInterfaceDevice device,
        string function,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await device.ExecuteFunction(function, cancellationToken);
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
        var matches = _devices.Values
            .Where(item => NormalizeNickName(item.Data.NickName).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            device = null;
            return false;
        }

        if (matches.Length > 1 && throwIfAmbiguous)
        {
            var candidates = string.Join(", ", matches.Select(item => FormatDeviceName(item.Data)));
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

    private async Task<string> OnDeviceMessageReceived(
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
                     .Cast<Func<ST_INTERFACE_RECEIVED_MESSAGE, CancellationToken, Task<string>>>())
        {
            var callbackResponse = await callback(receivedMessage, cancellationToken);
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

    public async Task<ST_LASER_STATUS> GetLaserStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return await GetLaserStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public async Task<ST_LASER_STATUS> GetLaserStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData(number);

        if (!IsInterfaceSimulation(interfaceData))
        {
            var liveStatus = await RefreshTalonLaserStatus(number, cancellationToken);

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

    public async Task SetLaser(
        int headNo,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        await SetLaser(interfaceData?.Number ?? 0, headNo, enabled, cancellationToken);
    }

    public async Task SetLaser(
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

        var result = await ExecuteTalonLaserCommand(
            number,
            EN_TALON_COMMAND.SetLaserOnOff,
            enabled ? 1.0 : 0.0,
            cancellationToken);

        if (result.IsSuccess)
        {
            SetSimulLaserHead(number, headNo, enabled);
        }
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteTalonLaserCommand(
        EN_TALON_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "Talon interface is not registered.")
            : await ExecuteTalonLaserCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteTalonLaserCommand(
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
            await Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
        }

        if (!IsConnect(interfaceData.Device, interfaceData.Number))
        {
            SetTalonStatus(number, GetTalonStatus(number) with { LastError = EN_TALON_ERROR.Timeout });
            WriteTalonErrorLog(interfaceData, commandText, "OFFLINE");
            return new ST_DEVICE_COMMAND_RESULT(false, $"Talon interface is offline: {FormatDeviceName(interfaceData)}");
        }

        var response = await ExecuteFunction(
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

    public async Task<ST_TALON_STATUS> RefreshTalonLaserStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetTalonInterfaceData();
        return interfaceData is null
            ? ST_TALON_STATUS.Empty
            : await RefreshTalonLaserStatus(interfaceData.Number, cancellationToken);
    }

    public async Task<ST_TALON_STATUS> RefreshTalonLaserStatus(
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
            var result = await ExecuteTalonLaserCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetTalonStatus(number);
            }
        }

        return GetTalonStatus(number);
    }

    public async Task<ST_CHILLER_STATUS> GetChillerStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return await GetChillerStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public async Task<ST_CHILLER_STATUS> GetChillerStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetChillerInterfaceData(number)))
        {
            await RefreshChillerStatus(number, cancellationToken);
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

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteChillerCommand(
        EN_CHILLER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "Chiller interface is not registered.")
            : await ExecuteChillerCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteChillerCommand(
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
            await Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
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

        var response = await ExecuteFunction(
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

    public async Task<ST_ORION_CHILLER_STATUS> RefreshChillerStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetChillerInterfaceData();
        return interfaceData is null
            ? ST_ORION_CHILLER_STATUS.Empty
            : await RefreshChillerStatus(interfaceData.Number, cancellationToken);
    }

    public async Task<ST_ORION_CHILLER_STATUS> RefreshChillerStatus(
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
            var result = await ExecuteChillerCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetChillerStatusValue(number);
            }
        }

        return GetChillerStatusValue(number);
    }

    public async Task<ST_ATTENUATOR_STATUS> GetAttenuatorStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return await GetAttenuatorStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public async Task<ST_ATTENUATOR_STATUS> GetAttenuatorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetAttenuatorInterfaceData(number)))
        {
            await RefreshAttenuatorStatus(number, cancellationToken);
        }

        return GetAttenuatorStatusValue(number);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteAttenuatorCommand(
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "CONEX_AGP interface is not registered.")
            : await ExecuteAttenuatorCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteAttenuatorCommand(
        int number,
        EN_ATTENUATOR_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_ATTENUATOR_COMMAND.Refresh)
        {
            var status = await RefreshAttenuatorStatus(number, cancellationToken);
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
            await Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
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

        var response = await ExecuteFunction(
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

    public async Task<ST_ATTENUATOR_STATUS> RefreshAttenuatorStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetAttenuatorInterfaceData();
        return interfaceData is null
            ? CreateDefaultAttenuatorStatus()
            : await RefreshAttenuatorStatus(interfaceData.Number, cancellationToken);
    }

    public async Task<ST_ATTENUATOR_STATUS> RefreshAttenuatorStatus(
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
            var result = await ExecuteAttenuatorCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetAttenuatorStatusValue(number);
            }
        }

        return GetAttenuatorStatusValue(number);
    }

    public async Task<ST_BET_STATUS> GetBETStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return await GetBETStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public async Task<ST_BET_STATUS> GetBETStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetBETInterfaceData(number)))
        {
            await RefreshBETStatus(number, cancellationToken);
        }

        return GetBETStatusValue(number);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteBETCommand(
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "BeamExpander interface is not registered.")
            : await ExecuteBETCommand(interfaceData.Number, command, parameter1, parameter2, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteBETCommand(
        int number,
        EN_BET_COMMAND command,
        double parameter1 = 0.0,
        double parameter2 = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_BET_COMMAND.Refresh)
        {
            var status = await RefreshBETStatus(number, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"BET refreshed. MAG {status.CurrentMagnification:F3}, DIV {status.CurrentDivergence:F3}.");
        }

        if (command == EN_BET_COMMAND.MoveTable)
        {
            var table = await LoadBETData(cancellationToken);
            var index = (int)Math.Round(parameter1);
            var row = table.FirstOrDefault(item => item.Index == index);

            if (row is null)
            {
                return new ST_DEVICE_COMMAND_RESULT(false, $"BET table row was not found: {index}");
            }

            return await ExecuteBETCommand(
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
            await Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
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

        var response = await ExecuteFunction(
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

    public async Task<ST_BET_STATUS> RefreshBETStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetBETInterfaceData();
        return interfaceData is null
            ? CreateDefaultBETStatus()
            : await RefreshBETStatus(interfaceData.Number, cancellationToken);
    }

    public async Task<ST_BET_STATUS> RefreshBETStatus(
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
            var result = await ExecuteBETCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetBETStatusValue(number);
            }
        }

        return GetBETStatusValue(number);
    }

    public Task<IReadOnlyList<ST_BET_TABLE_DATA>> LoadBETData(CancellationToken cancellationToken = default)
    {
        return _betFile is null
            ? Task.FromResult<IReadOnlyList<ST_BET_TABLE_DATA>>(CreateDefaultBETData())
            : _betFile.Load(cancellationToken);
    }

    public Task SaveBETData(
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        CancellationToken cancellationToken = default)
    {
        return _betFile is null
            ? Task.CompletedTask
            : _betFile.Save(table, cancellationToken);
    }

    public Task<ST_POWER_METER_TABLE_DATA> LoadPowerMeterData(
        string processFile = "",
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? Task.FromResult(CreateDefaultPowerMeterData(processFile))
            : _powerMeterFile.Load(processFile, cancellationToken);
    }

    public Task CreatePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? Task.CompletedTask
            : _powerMeterFile.Create(processFile, cancellationToken);
    }

    public Task DeletePowerMeterData(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? Task.CompletedTask
            : _powerMeterFile.Delete(processFile, cancellationToken);
    }

    public Task RenamePowerMeterData(
        string oldProcessFile,
        string newProcessFile,
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? Task.CompletedTask
            : _powerMeterFile.Rename(oldProcessFile, newProcessFile, cancellationToken);
    }

    public Task SavePowerMeterData(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken = default)
    {
        return _powerMeterFile is null
            ? Task.CompletedTask
            : _powerMeterFile.Save(processFile, steps, cancellationToken);
    }

    public async Task<ST_POWER_METER_STATUS> GetPowerMeterStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return await GetPowerMeterStatus(interfaceData?.Number ?? 0, cancellationToken);
    }

    public async Task<ST_POWER_METER_STATUS> GetPowerMeterStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        if (!IsInterfaceSimulation(GetPowerMeterInterfaceData(number)))
        {
            await RefreshPowerMeterStatus(number, cancellationToken);
        }

        return GetPowerMeterStatusValue(number);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePowerMeterCommand(
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return interfaceData is null
            ? new ST_DEVICE_COMMAND_RESULT(false, "PowerMeter interface is not registered.")
            : await ExecutePowerMeterCommand(interfaceData.Number, command, parameter, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePowerMeterCommand(
        int number,
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command == EN_POWER_METER_COMMAND.Refresh)
        {
            var status = await RefreshPowerMeterStatus(number, cancellationToken);
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
            await Connect(interfaceData.Device, interfaceData.Number, cancellationToken: cancellationToken);
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

        var response = await ExecuteFunction(
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

    public async Task<ST_POWER_METER_STATUS> RefreshPowerMeterStatus(CancellationToken cancellationToken = default)
    {
        var interfaceData = GetPowerMeterInterfaceData();
        return interfaceData is null
            ? ST_POWER_METER_STATUS.Empty
            : await RefreshPowerMeterStatus(interfaceData.Number, cancellationToken);
    }

    public async Task<ST_POWER_METER_STATUS> RefreshPowerMeterStatus(
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
            var result = await ExecutePowerMeterCommand(number, command, cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                return GetPowerMeterStatusValue(number);
            }
        }

        return GetPowerMeterStatusValue(number);
    }

    public async Task<ST_PICO_MOTOR_STATUS> GetPicoMotorStatus(
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(item => item.Number)
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

        return await GetPicoMotorStatus(data.Number, cancellationToken);
    }

    public async Task<ST_PICO_MOTOR_STATUS> GetPicoMotorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(item => item.Number == number);
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
            return await _picoMotorService.Refresh(
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

    public Task<ST_PICO_MOTOR_STATUS> RefreshPicoMotorStatus(
        CancellationToken cancellationToken = default)
    {
        return GetPicoMotorStatus(cancellationToken);
    }

    public Task<ST_PICO_MOTOR_STATUS> RefreshPicoMotorStatus(
        int number,
        CancellationToken cancellationToken = default)
    {
        return GetPicoMotorStatus(number, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorCommand(
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(item => item.Number)
            .FirstOrDefault();
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PicoMotor interface is not registered.");
        }

        return await ExecutePicoMotorCommand(data.Number, command, motorNo, parameter, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorCommand(
        int number,
        EN_PICO_MOTOR_COMMAND command,
        int motorNo = 1,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(item => item.Number == number);
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PicoMotor interface {number} is not registered.");
        }

        var result = await _picoMotorService.Execute(
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

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorAllMove(
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .OrderBy(item => item.Number)
            .FirstOrDefault();
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PicoMotor interface is not registered.");
        }

        return await ExecutePicoMotorAllMove(data.Number, motorNos, positionMm, count, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecutePicoMotorAllMove(
        int number,
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default)
    {
        var data = GetInterfaceList(EN_EQP_MODULE.PicoMotor)
            .FirstOrDefault(item => item.Number == number);
        if (data is null)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, $"PicoMotor interface {number} is not registered.");
        }

        var result = await _picoMotorService.ExecuteAllMove(
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
        var validNumbers = GetInterfaceList(module)
            .Select(data => data.Number)
            .ToHashSet();

        foreach (var number in statusMap.Keys.Where(number => !validNumbers.Contains(number)).ToArray())
        {
            statusMap.Remove(number);
        }
    }

    private ST_INTERFACE_DATA? GetTalonInterfaceData()
    {
        return GetInterfaceList(EN_EQP_MODULE.TalonLaser)
            .OrderBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetTalonInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.TalonLaser, number);
    }

    private ST_INTERFACE_DATA? GetAttenuatorInterfaceData()
    {
        return GetInterfaceList(EN_EQP_MODULE.Attenuator)
            .OrderBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetAttenuatorInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.Attenuator, number);
    }

    private ST_INTERFACE_DATA? GetBETInterfaceData()
    {
        return GetInterfaceList(EN_EQP_MODULE.Bet)
            .OrderBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetBETInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.Bet, number);
    }

    private ST_INTERFACE_DATA? GetPowerMeterInterfaceData()
    {
        return GetInterfaceList(EN_EQP_MODULE.PowerMeter)
            .OrderBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ST_INTERFACE_DATA? GetPowerMeterInterfaceData(int number)
    {
        return GetInterfaceData(EN_EQP_MODULE.PowerMeter, number);
    }

    private ST_INTERFACE_DATA? GetChillerInterfaceData()
    {
        return GetInterfaceList(EN_EQP_MODULE.Chiller)
            .OrderBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
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

public sealed class CInterfaceDevice : IInterfaceDevice
{
    private bool _simulationMode;
    private readonly IComm _comm;
    private string _simulationLastSent = "";
    private string _simulationLastReceived = "";
    private string _simulationLastError = "";
    private DateTimeOffset? _simulationLastChangedAt;

    internal event Func<CInterfaceDevice, ST_COMM_RECEIVED_MESSAGE, CancellationToken, Task<string>>? MessageReceived;

    public CInterfaceDevice(
        ST_INTERFACE_DATA data,
        bool simulationMode)
    {
        Data = data;
        ConnectOption = CInterfaceConnectOption.Parse(data);
        _simulationMode = simulationMode;
        _comm = CComm.Create(Data, ConnectOption);

        if (_comm is ICommMessageSource messageSource)
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

        if (_simulationMode)
        {
            _comm.Disconnect().GetAwaiter().GetResult();
            TouchSimulationState();
        }
    }

    public Task Connect(CancellationToken cancellationToken = default)
    {
        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _simulationLastError = "";
            TouchSimulationState();
            return Task.CompletedTask;
        }

        return _comm.Connect(cancellationToken);
    }

    public Task Disconnect(CancellationToken cancellationToken = default)
    {
        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TouchSimulationState();
            return Task.CompletedTask;
        }

        return _comm.Disconnect(cancellationToken);
    }

    public Task<string> ExecuteFunction(
        string function,
        CancellationToken cancellationToken = default)
    {
        if (_simulationMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _simulationLastSent = function;
            _simulationLastReceived = $"SIM:{Data.NickName}:{function}:OK";
            _simulationLastError = "";
            TouchSimulationState();
            return Task.FromResult(_simulationLastReceived);
        }

        return _comm.Execute(function, cancellationToken);
    }

    private void TouchSimulationState()
    {
        _simulationLastChangedAt = DateTimeOffset.Now;
    }

    private async Task<string> OnCommMessageReceived(
        ST_COMM_RECEIVED_MESSAGE message,
        CancellationToken cancellationToken)
    {
        var handler = MessageReceived;

        if (handler is null)
        {
            return "ACK";
        }

        var response = "ACK";
        foreach (var callback in handler.GetInvocationList()
                     .Cast<Func<CInterfaceDevice, ST_COMM_RECEIVED_MESSAGE, CancellationToken, Task<string>>>())
        {
            var callbackResponse = await callback(this, message, cancellationToken);
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
        var args = data.Arguments
            .Select(argument => argument.Trim())
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



