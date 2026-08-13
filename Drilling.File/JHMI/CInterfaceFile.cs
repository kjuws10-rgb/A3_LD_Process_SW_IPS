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

public sealed class CInterfaceFile(string configRoot) : CInterfaceFileBase
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

    public override IReadOnlyList<ST_INTERFACE_DATA> LoadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loadedRows = LoadInterfaceRows();
        Validate(loadedRows);
        EN_EQP_MODULE GetDataSortKey1(ST_INTERFACE_DATA data)
        {
            return data.Device;
        }

        int GetDataSortKey2(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey3(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        var rows = loadedRows
            .OrderBy(GetDataSortKey1)
            .ThenBy(GetDataSortKey2)
            .ThenBy(GetDataSortKey3, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return rows;
    }

    public override void SaveAll(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Validate(interfaces);

        var oldRows = LoadInterfaceRows()
            .ToDictionary(CreateInterfaceKey, StringComparer.OrdinalIgnoreCase);
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

        var rows = interfaces
            .OrderBy(GetDataSortKey4)
            .ThenBy(GetDataSortKey5)
            .ThenBy(GetDataSortKey6, StringComparer.OrdinalIgnoreCase)
            .Select(ToRow)
            .ToArray();

        CCsvParser.Write(GetInterfacePath(), Headers, rows);
        ValidateSavedRows(interfaces);
        WriteModifyLog(oldRows, interfaces);
        _logManager.WriteSettingSave(EN_SETTING_TAB.Interface);

        return;
    }

    private IReadOnlyList<ST_INTERFACE_DATA> LoadInterfaceRows()
    {
        CCsvParser.ValidateRequiredHeaders(GetInterfacePath(), "JHMI_INTERFACE", RequiredHeaderGroups);
        ST_INTERFACE_DATA SelectRow7(IReadOnlyDictionary<string, string> row, int index)
        {
            return Parse(row, index + 2);
        }

        return CCsvParser.Read(GetInterfacePath())
            .Select(SelectRow7)
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
        string SelectIndex8(int index)
        {
            return CCsvParser.Get(row, $"ARG{index}").Trim();
        }

        return Enumerable.Range(1, 5)
            .Select(SelectIndex8)
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
        string SelectArgument9(string argument)
        {
            return argument.Trim();
        }

        var args = data.Arguments
            .Concat(Enumerable.Repeat("", 5))
            .Take(5)
            .Select(SelectArgument9)
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
        bool FilterRow10(ST_INTERFACE_DATA row)
        {
            return !newMap.ContainsKey(CreateInterfaceKey(row));
        }

        foreach (var oldRow in oldRows.Values.Where(FilterRow10))
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
        string ToDictionaryFieldNameCallback11(string fieldName)
        {
            return fieldName;
        }

        string ToDictionaryValueCallback12(string _)
        {
            return "";
        }

        return FieldNames.ToDictionary(ToDictionaryFieldNameCallback11, ToDictionaryValueCallback12, StringComparer.OrdinalIgnoreCase);
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
        EN_INTERFACE_TYPE EvaluateValueSwitch1()
        {
            var switchValue = Normalize(value);
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
                    throw new InvalidDataException($"JHMI_INTERFACE validation failed. Unknown TYPE: {value}");
            }
        }

        return EvaluateValueSwitch1();
    }

    private static string InterfaceTypeText(EN_INTERFACE_TYPE type)
    {
        string EvaluateTypeSwitch2()
        {
            var switchValue = type;
            switch (switchValue)
            {
                case EN_INTERFACE_TYPE.OpcUa:
                    return "OPCUA";
                case EN_INTERFACE_TYPE.ModbusSerial:
                    return "MODBUS_SERIAL";
                case EN_INTERFACE_TYPE.ModbusTcp:
                    return "MODBUS_TCP";
                case EN_INTERFACE_TYPE.Serial:
                    return "SERIAL";
                case EN_INTERFACE_TYPE.SocketClient:
                    return "SOCKET_C";
                case EN_INTERFACE_TYPE.SocketServer:
                    return "SOCKET_S";
                case EN_INTERFACE_TYPE.SocketClientUdp:
                    return "SOCKET_C_UDP";
                case EN_INTERFACE_TYPE.SocketServerUdp:
                    return "SOCKET_S_UDP";
                case EN_INTERFACE_TYPE.AcsNet:
                    return "ACS_NET";
                case EN_INTERFACE_TYPE.XpsNet:
                    return "XPS_NET";
                case EN_INTERFACE_TYPE.Automation1Net:
                    return "AUTOMATION1_NET";
                case EN_INTERFACE_TYPE.PicoMotor:
                    return "PICOMOTOR";
                default:
                    return "SOCKET_C";
            }
        }

        return EvaluateTypeSwitch2();
    }

    private static EN_EQP_MODULE ParseDevice(string value)
    {
        EN_EQP_MODULE EvaluateValueSwitch3()
        {
            var switchValue = Normalize(value);
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
                case "PICO_MOTOR" or "PICOMOTOR" or "PICO":
                    return EN_EQP_MODULE.PicoMotor;
                case "MELSEC" or "PLC":
                    return EN_EQP_MODULE.Melsec;
                default:
                    throw new InvalidDataException($"Unknown interface device: {value}");
            }
        }

        return EvaluateValueSwitch3();
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
