using Drilling.Common.Log;
using System.Globalization;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CInterfaceFile(string configRoot) : IInterfaceFile
{
    private readonly CLogManager _logManager = new(configRoot);

    private static readonly IReadOnlyList<string> FieldNames =
    [
        "TYPE",
        "DEVICE",
        "NUMBER",
        "NICKNAME",
        "SYSTEM_SECTION",
        "AUTOCONNECTION",
        "SIMUL",
        "ARG1",
        "ARG2",
        "ARG3",
        "ARG4",
        "ARG5"
    ];

    private static readonly IReadOnlyList<string> Headers =
    [
        "TYPE",
        "DEVICE",
        "NUMBER",
        "NICKNAME",
        "SYSTEM_SECTION",
        "AUTOCONNECTION",
        "SIMUL",
        "ARG1",
        "ARG2",
        "ARG3",
        "ARG4",
        "ARG5"
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> RequiredHeaderGroups =
    [
        ["TYPE"],
        ["DEVICE"],
        ["NUMBER", "NO"],
        ["NICKNAME"],
        ["SYSTEM_SECTION", "SYSTEM SECTION", "SECTION"],
        ["AUTOCONNECTION", "AUTO_CONNECTION", "AUTO CONNECTION"],
        ["SIMUL", "SIMULATION", "SIM_MODE", "SIM MODE"],
        ["ARG1"],
        ["ARG2"],
        ["ARG3"],
        ["ARG4"],
        ["ARG5"]
    ];

    public Task<IReadOnlyList<ST_INTERFACE_DATA>> LoadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loadedRows = LoadInterfaceRows();
        Validate(loadedRows);

        var rows = loadedRows
            .OrderBy(data => data.Device)
            .ThenBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ST_INTERFACE_DATA>>(rows);
    }

    public Task SaveAll(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Validate(interfaces);

        var oldRows = LoadInterfaceRows()
            .ToDictionary(CreateInterfaceKey, StringComparer.OrdinalIgnoreCase);
        var rows = interfaces
            .OrderBy(data => data.Device)
            .ThenBy(data => data.Number)
            .ThenBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .Select(ToRow)
            .ToArray();

        CCsvParser.Write(GetInterfacePath(), Headers, rows);
        ValidateSavedRows(interfaces);
        WriteModifyLog(oldRows, interfaces);
        _logManager.WriteSettingSave(EN_SETTING_TAB.Interface);

        return Task.CompletedTask;
    }

    private IReadOnlyList<ST_INTERFACE_DATA> LoadInterfaceRows()
    {
        CCsvParser.ValidateRequiredHeaders(GetInterfacePath(), "JHMI_INTERFACE", RequiredHeaderGroups);

        return CCsvParser.Read(GetInterfacePath())
            .Select((row, index) => Parse(row, index + 2))
            .ToArray();
    }

    private ST_INTERFACE_DATA Parse(
        IReadOnlyDictionary<string, string> row,
        int rowNo)
    {
        return new ST_INTERFACE_DATA(
            ParseInterfaceType(RequireText(row, rowNo, "TYPE", "InterfaceType")),
            ParseDevice(RequireText(row, rowNo, "DEVICE", "Device")),
            ReadRequiredInt(RequireText(row, rowNo, "NUMBER", "NO"), rowNo, "NUMBER"),
            RequireText(row, rowNo, "NICKNAME", "NickName"),
            RequireText(row, rowNo, "SYSTEM_SECTION", "SYSTEM SECTION", "SECTION"),
            ReadRequiredBool(
                RequireText(row, rowNo, "AUTOCONNECTION", "AUTO_CONNECTION", "AUTO CONNECTION"),
                rowNo,
                "AUTOCONNECTION"),
            ReadRequiredBool(
                RequireText(row, rowNo, "SIMUL", "SIMULATION", "SIM_MODE", "SIM MODE"),
                rowNo,
                "SIMUL"),
            ReadArguments(row),
            CCsvParser.GetExtra(row, Headers));
    }

    private string GetInterfacePath()
    {
        return Path.Combine(configRoot, "JHMI_INTERFACE.csv");
    }

    private static IReadOnlyList<string> ReadArguments(IReadOnlyDictionary<string, string> row)
    {
        return Enumerable.Range(1, 5)
            .Select(index => CCsvParser.Get(row, $"ARG{index}").Trim())
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ToRow(ST_INTERFACE_DATA data)
    {
        var arguments = data.Arguments
            .Concat(Enumerable.Repeat("", 5))
            .Take(5)
            .ToArray();

        var row = data.Extra is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(data.Extra, StringComparer.OrdinalIgnoreCase);

        row["TYPE"] = InterfaceTypeText(data.InterfaceType);
        row["DEVICE"] = DeviceText(data.Device);
        row["NUMBER"] = data.Number.ToString(CultureInfo.InvariantCulture);
        row["NICKNAME"] = data.NickName;
        row["SYSTEM_SECTION"] = data.SystemSection;
        row["AUTOCONNECTION"] = data.AutoConnection ? "1" : "0";
        row["SIMUL"] = data.IsSimulation ? "1" : "0";
        row["ARG1"] = arguments[0];
        row["ARG2"] = arguments[1];
        row["ARG3"] = arguments[2];
        row["ARG4"] = arguments[3];
        row["ARG5"] = arguments[4];

        return row;
    }

    private static void Validate(IReadOnlyList<ST_INTERFACE_DATA> interfaces)
    {
        var deviceNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var data in interfaces)
        {
            if (string.IsNullOrWhiteSpace(data.NickName))
            {
                throw new InvalidDataException("JHMI_INTERFACE validation failed. NICKNAME cannot be empty.");
            }

            if (!deviceNumbers.Add(CreateInterfaceKey(data)))
            {
                throw new InvalidDataException($"JHMI_INTERFACE validation failed. Duplicated DEVICE/NUMBER: {FormatInterfaceLabel(data)}");
            }

            if (data.Number < 0)
            {
                throw new InvalidDataException($"JHMI_INTERFACE validation failed. NUMBER cannot be negative: {FormatInterfaceLabel(data)}");
            }

            if (data.Arguments.Count > 5)
            {
                throw new InvalidDataException($"JHMI_INTERFACE validation failed. ARG count must be 5 or less: {FormatInterfaceLabel(data)}");
            }

            ValidateConnectionArguments(data);
        }
    }

    private static void ValidateConnectionArguments(ST_INTERFACE_DATA data)
    {
        var args = data.Arguments
            .Concat(Enumerable.Repeat("", 5))
            .Take(5)
            .Select(argument => argument.Trim())
            .ToArray();

        if (data.IsSimulation)
        {
            return;
        }

        switch (data.InterfaceType)
        {
            case EN_INTERFACE_TYPE.Serial:
            case EN_INTERFACE_TYPE.ModbusSerial:
                RequireArgument(data, args[0], "ARG1/COM_PORT");
                RequirePositiveInt(data, args[1], "ARG2/BAUD");
                ValidateParity(data, args[2]);
                RequirePositiveInt(data, args[3], "ARG4/DATA_BITS");
                ValidateStopBits(data, args[4]);
                break;
            case EN_INTERFACE_TYPE.SocketClient:
            case EN_INTERFACE_TYPE.SocketClientUdp:
            case EN_INTERFACE_TYPE.ModbusTcp:
            case EN_INTERFACE_TYPE.AcsNet:
            case EN_INTERFACE_TYPE.XpsNet:
            case EN_INTERFACE_TYPE.Automation1Net:
                RequireArgument(data, args[1], "ARG2/REMOTE_IP");
                RequirePositiveInt(data, args[2], "ARG3/PORT");
                RequirePositiveInt(data, args[3], "ARG4/TIMEOUT_MS");
                RequirePositiveInt(data, args[4], "ARG5/RETRY_COUNT");
                break;
            case EN_INTERFACE_TYPE.SocketServer:
            case EN_INTERFACE_TYPE.SocketServerUdp:
                RequirePositiveInt(data, args[2], "ARG3/PORT");
                RequirePositiveInt(data, args[3], "ARG4/TIMEOUT_MS");
                RequirePositiveInt(data, args[4], "ARG5/RETRY_COUNT");
                break;
            case EN_INTERFACE_TYPE.PicoMotor:
                break;
            case EN_INTERFACE_TYPE.OpcUa:
                RequireArgument(data, args[0], "ARG1/ENDPOINT");
                RequirePositiveInt(data, args[3], "ARG4/TIMEOUT_MS");
                RequirePositiveInt(data, args[4], "ARG5/RETRY_COUNT");
                break;
        }
    }

    private void ValidateSavedRows(IReadOnlyList<ST_INTERFACE_DATA> expectedRows)
    {
        var actualRows = LoadInterfaceRows()
            .ToDictionary(CreateInterfaceKey, StringComparer.OrdinalIgnoreCase);

        foreach (var expected in expectedRows)
        {
            if (!actualRows.TryGetValue(CreateInterfaceKey(expected), out var actual))
            {
                throw new InvalidDataException($"JHMI_INTERFACE validation failed. Missing row: {FormatInterfaceLabel(expected)}");
            }

            if (!BuildComparisonText(actual).Equals(BuildComparisonText(expected), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"JHMI_INTERFACE validation failed. Value mismatch: {FormatInterfaceLabel(expected)}");
            }
        }
    }

    private void WriteModifyLog(
        IReadOnlyDictionary<string, ST_INTERFACE_DATA> oldRows,
        IReadOnlyList<ST_INTERFACE_DATA> newRows)
    {
        var newMap = newRows.ToDictionary(CreateInterfaceKey, StringComparer.OrdinalIgnoreCase);

        foreach (var row in newRows)
        {
            var key = CreateInterfaceKey(row);
            var label = FormatInterfaceLabel(row);

            if (!oldRows.TryGetValue(key, out var oldRow))
            {
                _logManager.WriteSettingModify(EN_SETTING_TAB.Interface, $"{label}.ROW", "-", "CREATED");
                WriteFieldModifyLog(label, CreateEmptyFieldMap(), CreateFieldMap(row));
                continue;
            }

            WriteFieldModifyLog(label, CreateFieldMap(oldRow), CreateFieldMap(row));
        }

        foreach (var oldRow in oldRows.Values.Where(row => !newMap.ContainsKey(CreateInterfaceKey(row))))
        {
            _logManager.WriteSettingModify(EN_SETTING_TAB.Interface, $"{FormatInterfaceLabel(oldRow)}.ROW", "EXIST", "DELETED");
        }
    }

    private void WriteFieldModifyLog(
        string interfaceLabel,
        IReadOnlyDictionary<string, string> oldFields,
        IReadOnlyDictionary<string, string> newFields)
    {
        foreach (var fieldName in FieldNames)
        {
            var oldValue = oldFields.TryGetValue(fieldName, out var oldFieldValue) ? oldFieldValue : "";
            var newValue = newFields.TryGetValue(fieldName, out var newFieldValue) ? newFieldValue : "";

            if (oldValue.Equals(newValue, StringComparison.Ordinal))
            {
                continue;
            }

            _logManager.WriteSettingModify(EN_SETTING_TAB.Interface, $"{interfaceLabel}.{fieldName}", oldValue, newValue);
        }
    }

    private static IReadOnlyDictionary<string, string> CreateEmptyFieldMap()
    {
        return FieldNames.ToDictionary(fieldName => fieldName, _ => "", StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> CreateFieldMap(ST_INTERFACE_DATA data)
    {
        var arguments = data.Arguments
            .Concat(Enumerable.Repeat("", 5))
            .Take(5)
            .ToArray();

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TYPE"] = InterfaceTypeText(data.InterfaceType),
            ["DEVICE"] = DeviceText(data.Device),
            ["NUMBER"] = data.Number.ToString(CultureInfo.InvariantCulture),
            ["NICKNAME"] = data.NickName,
            ["SYSTEM_SECTION"] = data.SystemSection,
            ["AUTOCONNECTION"] = data.AutoConnection ? "1" : "0",
            ["SIMUL"] = data.IsSimulation ? "1" : "0",
            ["ARG1"] = arguments[0],
            ["ARG2"] = arguments[1],
            ["ARG3"] = arguments[2],
            ["ARG4"] = arguments[3],
            ["ARG5"] = arguments[4]
        };
    }

    private static string BuildComparisonText(ST_INTERFACE_DATA data)
    {
        var args = data.Arguments
            .Concat(Enumerable.Repeat("", 5))
            .Take(5);

        return string.Join("|",
            InterfaceTypeText(data.InterfaceType),
            DeviceText(data.Device),
            data.Number.ToString(CultureInfo.InvariantCulture),
            data.NickName,
            data.SystemSection,
            data.AutoConnection ? "1" : "0",
            data.IsSimulation ? "1" : "0",
            string.Join("|", args));
    }

    private static string CreateInterfaceKey(ST_INTERFACE_DATA data)
    {
        return $"{data.Device}:{data.Number}";
    }

    private static string FormatInterfaceLabel(ST_INTERFACE_DATA data)
    {
        return $"{DeviceText(data.Device)}[{data.Number}]/{data.NickName}";
    }

    private static EN_INTERFACE_TYPE ParseInterfaceType(string value)
    {
        return Normalize(value) switch
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
            _ => throw new InvalidDataException($"JHMI_INTERFACE validation failed. Unknown TYPE: {value}")
        };
    }

    private static string InterfaceTypeText(EN_INTERFACE_TYPE type)
    {
        return type switch
        {
            EN_INTERFACE_TYPE.OpcUa => "OPCUA",
            EN_INTERFACE_TYPE.ModbusSerial => "MODBUS_SERIAL",
            EN_INTERFACE_TYPE.ModbusTcp => "MODBUS_TCP",
            EN_INTERFACE_TYPE.Serial => "SERIAL",
            EN_INTERFACE_TYPE.SocketClient => "SOCKET_C",
            EN_INTERFACE_TYPE.SocketServer => "SOCKET_S",
            EN_INTERFACE_TYPE.SocketClientUdp => "SOCKET_C_UDP",
            EN_INTERFACE_TYPE.SocketServerUdp => "SOCKET_S_UDP",
            EN_INTERFACE_TYPE.AcsNet => "ACS_NET",
            EN_INTERFACE_TYPE.XpsNet => "XPS_NET",
            EN_INTERFACE_TYPE.Automation1Net => "AUTOMATION1_NET",
            EN_INTERFACE_TYPE.PicoMotor => "PICOMOTOR",
            _ => "SOCKET_C"
        };
    }

    private static EN_EQP_MODULE ParseDevice(string value)
    {
        return Normalize(value) switch
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
            "PICO_MOTOR" or "PICOMOTOR" or "PICO" => EN_EQP_MODULE.PicoMotor,
            "MELSEC" or "PLC" => EN_EQP_MODULE.Melsec,
            _ => throw new InvalidDataException($"Unknown interface device: {value}")
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

    private static string RequireText(
        IReadOnlyDictionary<string, string> row,
        int rowNo,
        params string[] names)
    {
        return CCsvParser.RequireText(row, "JHMI_INTERFACE", rowNo, names);
    }

    private static bool ReadRequiredBool(
        string value,
        int rowNo,
        string fieldName)
    {
        return CCsvParser.ReadRequiredBool(value, "JHMI_INTERFACE", rowNo, fieldName);
    }

    private static int ReadRequiredInt(
        string value,
        int rowNo,
        string fieldName)
    {
        return CCsvParser.ReadRequiredInt(value, "JHMI_INTERFACE", rowNo, fieldName);
    }

    private static void RequireArgument(
        ST_INTERFACE_DATA data,
        string value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"JHMI_INTERFACE validation failed. {data.NickName}/{fieldName} cannot be empty in ONLINE mode.");
        }
    }

    private static void RequirePositiveInt(
        ST_INTERFACE_DATA data,
        string value,
        string fieldName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ||
            result <= 0)
        {
            throw new InvalidDataException(
                $"JHMI_INTERFACE validation failed. {data.NickName}/{fieldName} must be a positive integer in ONLINE mode.");
        }
    }

    private static void ValidateParity(
        ST_INTERFACE_DATA data,
        string value)
    {
        var normalized = Normalize(value);

        if (normalized is "" or "NONE" or "ODD" or "EVEN" or "MARK" or "SPACE")
        {
            return;
        }

        throw new InvalidDataException(
            $"JHMI_INTERFACE validation failed. {data.NickName}/ARG3/PARITY is invalid: {value}");
    }

    private static void ValidateStopBits(
        ST_INTERFACE_DATA data,
        string value)
    {
        var normalized = Normalize(value).Replace("_", "", StringComparison.OrdinalIgnoreCase);

        if (normalized is "ONE" or "TWO" or "ONEPOINTFIVE" or "1" or "2" or "1.5")
        {
            return;
        }

        throw new InvalidDataException(
            $"JHMI_INTERFACE validation failed. {data.NickName}/ARG5/STOP_BITS is invalid: {value}");
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}







