using System.Globalization;
using Drilling.Common.Interface;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CMelsecMapFile(string configRoot) : CMelsecMapFileBase
{
    private const string TableName = "JHMI_MELSEC_MAP";

    private static readonly IReadOnlyList<string> Headers =
    [
        "ID",
        "USE",
        "GROUP",
        "NAME",
        "DEVICE NO",
        "ADDRESS",
        "DATA TYPE",
        "DIRECTION",
        "ACCESS",
        "SCALE",
        "LENGTH",
        "POLL_MS",
        "DESCRIPTION"
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> RequiredHeaderGroups =
    [
        ["ID"],
        ["USE"],
        ["GROUP"],
        ["NAME"],
        ["DEVICE NO", "DEV NO", "NUMBER"],
        ["ADDRESS"],
        ["DATA TYPE", "DATATYPE", "TYPE"],
        ["DIRECTION", "DIR"],
        ["ACCESS"],
        ["SCALE"],
        ["LENGTH", "SIZE"],
        ["POLL_MS", "POLL MS", "POLL"]
    ];

    public override Task<IReadOnlyList<ST_MELSEC_MAP_DATA>> LoadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFile();
        CCsvParser.ValidateRequiredHeaders(GetMapPath(), TableName, RequiredHeaderGroups);
        ST_MELSEC_MAP_DATA SelectRow1(IReadOnlyDictionary<string, string> row, int index)
        {
            return Parse(row, index + 2);
        }

        bool FilterData2(ST_MELSEC_MAP_DATA data)
        {
            return !string.IsNullOrWhiteSpace(data.Id);
        }

        string GetDataSortKey3(ST_MELSEC_MAP_DATA data)
        {
            return data.Group;
        }

        int GetDataSortKey4(ST_MELSEC_MAP_DATA data)
        {
            return data.DeviceNo;
        }

        string GetDataSortKey5(ST_MELSEC_MAP_DATA data)
        {
            return data.Id;
        }

        var rows = CCsvParser.Read(GetMapPath())
            .Select(SelectRow1)
            .Where(FilterData2)
            .OrderBy(GetDataSortKey3, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetDataSortKey4)
            .ThenBy(GetDataSortKey5, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Validate(rows);
        return Task.FromResult<IReadOnlyList<ST_MELSEC_MAP_DATA>>(rows);
    }

    private ST_MELSEC_MAP_DATA Parse(
        IReadOnlyDictionary<string, string> row,
        int rowNo)
    {
        return new ST_MELSEC_MAP_DATA(
            NormalizeId(RequireText(row, rowNo, "ID")),
            ReadBool(ReadFirst(row, "USE"), true),
            NormalizeGroup(RequireText(row, rowNo, "GROUP")),
            ReadFirst(row, "NAME"),
            ReadInt(ReadFirst(row, "DEVICE NO"), rowNo, "DEVICE NO", 0),
            NormalizeAddress(RequireText(row, rowNo, "ADDRESS")),
            ReadDataType(RequireText(row, rowNo, "DATA TYPE", "DATATYPE", "TYPE"), rowNo),
            ReadDirection(RequireText(row, rowNo, "DIRECTION", "DIR"), rowNo),
            ReadAccess(RequireText(row, rowNo, "ACCESS"), rowNo),
            ReadDouble(ReadFirst(row, "SCALE"), rowNo, "SCALE", 1.0),
            ReadInt(ReadFirst(row, "LENGTH"), rowNo, "LENGTH", 1),
            ReadInt(ReadFirst(row, "POLL_MS"), rowNo, "POLL_MS", 0),
            ReadFirst(row, "DESCRIPTION"));
    }

    private void EnsureFile()
    {
        var path = GetMapPath();

        if (System.IO.File.Exists(path))
        {
            return;
        }

        CCsvParser.Write(path, Headers, CreateDefaultRows());
    }

    private string GetMapPath()
    {
        return Path.Combine(configRoot, "JHMI_MELSEC_MAP.csv");
    }

    private static void Validate(IReadOnlyList<ST_MELSEC_MAP_DATA> rows)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool FilterRow6(ST_MELSEC_MAP_DATA row)
        {
            return row.Use;
        }

        foreach (var row in rows.Where(FilterRow6))
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                throw new InvalidDataException($"{TableName} validation failed. ID cannot be empty.");
            }

            if (!usedIds.Add(row.Id))
            {
                throw new InvalidDataException($"{TableName} validation failed. Duplicated ID: {row.Id}");
            }

            if (row.DeviceNo < 0)
            {
                throw new InvalidDataException($"{TableName} validation failed. DEVICE NO cannot be negative: {row.Id}");
            }

            if (string.IsNullOrWhiteSpace(row.Address))
            {
                throw new InvalidDataException($"{TableName} validation failed. ADDRESS cannot be empty: {row.Id}");
            }

            if (row.Length <= 0)
            {
                throw new InvalidDataException($"{TableName} validation failed. LENGTH must be positive: {row.Id}");
            }

            if (row.PollMs < 0)
            {
                throw new InvalidDataException($"{TableName} validation failed. POLL_MS cannot be negative: {row.Id}");
            }

            if (row.DataType == EN_MELSEC_DATA_TYPE.Bit && row.Length != 1)
            {
                throw new InvalidDataException($"{TableName} validation failed. BIT LENGTH must be 1: {row.Id}");
            }
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> CreateDefaultRows()
    {
        return
        [
            Row("FUNCTION_PAUSE_POSSIBLE_READ", "FUNCTION", "PAUSE Possible Read", 0, "W23458.0", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("FUNCTION_PAUSE_ACK_READ", "FUNCTION", "PAUSE ACK Read", 0, "W23458.1", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("FUNCTION_PAUSE_ACK_WRITE", "FUNCTION", "PAUSE ACK Write", 0, "W33458.1", "BIT", "OUT", "W", "1", "1", "0", "250703 function write"),
            Row("FUNCTION_PM_POSSIBLE_READ", "FUNCTION", "PM Possible Read", 0, "W23459.0", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("FUNCTION_NORMAL_POSSIBLE_READ", "FUNCTION", "NORMAL Possible Read", 0, "W2345A.0", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("FUNCTION_RESUME_POSSIBLE_READ", "FUNCTION", "RESUME Possible Read", 0, "W2345B.0", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("FUNCTION_COMMUNICATION_CHECK_COMMCHECK_READ", "FUNCTION", "Communication Check Read", 0, "W23660", "WORD", "IN", "R", "1", "1", "100", "250703 communication check read"),
            Row("FUNCTION_COMMUNICATION_CHECK_COMMCHECK_WRITE", "FUNCTION", "Communication Check Write", 0, "W33660", "WORD", "OUT", "W", "1", "1", "0", "250703 communication check write"),
            Row("FUNCTION_RECIPE_SET_POSSIBLE_READ", "FUNCTION", "Recipe Set Possible Read", 0, "W283BB.0", "BIT", "IN", "R", "1", "1", "100", "250703 function read"),
            Row("MNT_LD01_EQUIPMENT_STATUS_LD", "MNT", "LD01 Equipment Status", 0, "W29000", "DWORD", "IN", "R", "1", "2", "500", "MNT equipment status"),
            Row("MNT_LD01_PROCESS_STATUS_LD", "MNT", "LD01 Process Status", 0, "W29002", "DWORD", "IN", "R", "1", "2", "500", "MNT process status"),
            Row("MNT_LD01_CHAMBER_STATUS_LD", "MNT", "LD01 Chamber Status", 0, "W29004", "DWORD", "IN", "R", "1", "2", "500", "MNT chamber status"),
            Row("MNT_LD01_CURRENT_STEP", "MNT", "LD01 Current Step", 0, "W2900A", "DWORD", "IN", "R", "1", "2", "500", "MNT current step"),
            Row("MNT_LD01_AUTO_MODE_CHNG_STATUS", "MNT", "LD01 Auto Mode Change Status", 0, "W29016", "DWORD", "IN", "R", "1", "2", "500", "MNT auto mode status"),
            Row("MNT_LD01_ALARM_WARNING_STATUS", "MNT", "LD01 Alarm Warning Status", 0, "W29018", "DWORD", "IN", "R", "1", "2", "500", "MNT alarm warning status"),
            Row("MNT_LD01_PPID", "MNT", "LD01 PPID", 0, "W290D3", "STRING", "IN", "R", "1", "10", "0", "MNT PPID ASCII 20"),
            Row("SV_LD01_LD_CH_STATUS", "SV", "LD01 LD Channel Status", 0, "W23864", "DWORD", "IN", "R", "1", "2", "500", "SV channel status"),
            Row("SV_LD01_LD_CURRENT_STEP", "SV", "LD01 Current Step", 0, "W23868", "DWORD", "IN", "R", "1", "2", "500", "SV current step"),
            Row("SV_LD01_AUTO_MODE_CHNG_STATUS", "SV", "LD01 Auto Mode Change Status", 0, "W2386E", "DWORD", "IN", "R", "1", "2", "500", "SV auto mode status"),
            Row("SV_LD01_ALARM_STATUS", "SV", "LD01 Alarm Status", 0, "W23870", "DWORD", "IN", "R", "1", "2", "500", "SV alarm status"),
            Row("SV_SDC_ALIVE_ON_DEVICE1", "SV", "SDC Alive On Device1", 0, "W23C80", "DWORD", "IN", "R", "1", "2", "0", "SV SDC alive device1"),
            Row("SV_SDC_ALIVE_ON_DEVICE2", "SV", "SDC Alive On Device2", 0, "W23CBE", "DWORD", "IN", "R", "1", "2", "0", "SV SDC alive device2"),
            Row("PPID_ONLINE_PPID_NAME_READ", "PPID", "Online PPID Name Read", 0, "W26286", "STRING", "IN", "R", "1", "8", "0", "PPID online name read"),
            Row("PPID_ONLINE_PPID_NAME_WRITE", "PPID", "Online PPID Name Write", 0, "W36286", "STRING", "OUT", "W", "1", "8", "0", "PPID online name write"),
            Row("PPID_STAGE_SPEED_READ", "PPID", "Stage Speed Read", 0, "W2628E", "DWORD", "IN", "R", "1", "2", "0", "PPID stage speed read"),
            Row("PPID_STAGE_SPEED_WRITE", "PPID", "Stage Speed Write", 0, "W3628E", "DWORD", "OUT", "W", "1", "2", "0", "PPID stage speed write"),
            Row("PPID_LASER_POWER_READ", "PPID", "Laser Power Read", 0, "W26290", "DWORD", "IN", "R", "1", "2", "0", "PPID laser power read"),
            Row("PPID_LASER_POWER_WRITE", "PPID", "Laser Power Write", 0, "W36290", "DWORD", "OUT", "W", "1", "2", "0", "PPID laser power write"),
            Row("PPID_LASER_FREQUENCY_READ", "PPID", "Laser Frequency Read", 0, "W26292", "DWORD", "IN", "R", "1", "2", "0", "PPID laser frequency read"),
            Row("PPID_LASER_FREQUENCY_WRITE", "PPID", "Laser Frequency Write", 0, "W36292", "DWORD", "OUT", "W", "1", "2", "0", "PPID laser frequency write"),
            Row("EC_SET_ALIGN_STAGE_X1_POS_READ", "EC", "Set Align Stage X1 Position Read", 0, "W249F0", "DOUBLE", "IN", "R", "0.0001", "2", "0", "EC align stage X1 read"),
            Row("EC_SET_ALIGN_STAGE_X1_POS_WRITE", "EC", "Set Align Stage X1 Position Write", 0, "W349F0", "DOUBLE", "OUT", "W", "0.0001", "2", "0", "EC align stage X1 write"),
            Row("EC_SET_ALIGN_STAGE_Y1_POS_READ", "EC", "Set Align Stage Y1 Position Read", 0, "W249F2", "DOUBLE", "IN", "R", "0.0001", "2", "0", "EC align stage Y1 read"),
            Row("EC_SET_ALIGN_STAGE_Y1_POS_WRITE", "EC", "Set Align Stage Y1 Position Write", 0, "W349F2", "DOUBLE", "OUT", "W", "0.0001", "2", "0", "EC align stage Y1 write"),
            Row("DC_TOP_POWERMETER_1_MEASURE_VALUE", "DC", "Top Powermeter 1 Measure Value", 0, "W25DEE", "DOUBLE", "IN", "R", "0.001", "2", "0", "DC top powermeter 1"),
            Row("ALARM_F0000", "ALARM", "F0000 PLC Alarm", 0, "W271C2.0", "BIT", "IN", "R", "1", "1", "0", "Alarm F0000"),
            Row("WARNING_F2000", "WARNING", "F2000 PLC Battery Low Warning", 0, "W27320.0", "BIT", "IN", "R", "1", "1", "0", "Warning F2000")
        ];
    }

    private static IReadOnlyDictionary<string, string> Row(
        string id,
        string group,
        string name,
        int deviceNo,
        string address,
        string dataType,
        string direction,
        string access,
        string scale,
        string length,
        string pollMs,
        string description)
    {
        return new Dictionary<string, string>
        {
            ["ID"] = id,
            ["USE"] = "1",
            ["GROUP"] = group,
            ["NAME"] = name,
            ["DEVICE NO"] = deviceNo.ToString(CultureInfo.InvariantCulture),
            ["ADDRESS"] = address,
            ["DATA TYPE"] = dataType,
            ["DIRECTION"] = direction,
            ["ACCESS"] = access,
            ["SCALE"] = scale,
            ["LENGTH"] = length,
            ["POLL_MS"] = pollMs,
            ["DESCRIPTION"] = description
        };
    }

    private static EN_MELSEC_DATA_TYPE ReadDataType(string value, int rowNo)
    {
        EN_MELSEC_DATA_TYPE EvaluateValueSwitch1()
        {
            var switchValue = NormalizeText(value);
            switch (switchValue)
            {
                case "BIT":
                    return EN_MELSEC_DATA_TYPE.Bit;
                case "WORD":
                    return EN_MELSEC_DATA_TYPE.Word;
                case "DWORD" or "D_WORD" or "DOUBLEWORD":
                    return EN_MELSEC_DATA_TYPE.DWord;
                case "DOUBLE" or "REAL64":
                    return EN_MELSEC_DATA_TYPE.Double;
                case "FLOAT" or "REAL32":
                    return EN_MELSEC_DATA_TYPE.Float;
                case "STRING" or "TEXT":
                    return EN_MELSEC_DATA_TYPE.String;
                default:
                    throw new InvalidDataException($"{TableName} validation failed. Row {rowNo} / DATA TYPE is invalid: {value}");
            }
        }

        return EvaluateValueSwitch1();
    }

    private static EN_MELSEC_DIRECTION ReadDirection(string value, int rowNo)
    {
        EN_MELSEC_DIRECTION EvaluateValueSwitch2()
        {
            var switchValue = NormalizeText(value);
            switch (switchValue)
            {
                case "IN" or "INPUT":
                    return EN_MELSEC_DIRECTION.In;
                case "OUT" or "OUTPUT":
                    return EN_MELSEC_DIRECTION.Out;
                case "INOUT" or "IN_OUT" or "BOTH":
                    return EN_MELSEC_DIRECTION.InOut;
                default:
                    throw new InvalidDataException($"{TableName} validation failed. Row {rowNo} / DIRECTION is invalid: {value}");
            }
        }

        return EvaluateValueSwitch2();
    }

    private static EN_MELSEC_ACCESS ReadAccess(string value, int rowNo)
    {
        EN_MELSEC_ACCESS EvaluateValueSwitch3()
        {
            var switchValue = NormalizeText(value);
            switch (switchValue)
            {
                case "R" or "READ":
                    return EN_MELSEC_ACCESS.Read;
                case "W" or "WRITE":
                    return EN_MELSEC_ACCESS.Write;
                case "RW" or "R/W" or "READWRITE" or "READ_WRITE":
                    return EN_MELSEC_ACCESS.ReadWrite;
                default:
                    throw new InvalidDataException($"{TableName} validation failed. Row {rowNo} / ACCESS is invalid: {value}");
            }
        }

        return EvaluateValueSwitch3();
    }

    private static string RequireText(
        IReadOnlyDictionary<string, string> row,
        int rowNo,
        params string[] names)
    {
        return CCsvParser.RequireText(row, TableName, rowNo, names);
    }

    private static string ReadFirst(
        IReadOnlyDictionary<string, string> row,
        params string[] names)
    {
        return CCsvParser.GetFirst(row, names);
    }

    private static bool ReadBool(string value, bool defaultValue)
    {
        return CCsvParser.ReadBool(value, defaultValue);
    }

    private static int ReadInt(
        string value,
        int rowNo,
        string fieldName,
        int defaultValue)
    {
        return CCsvParser.ReadInt(value, TableName, rowNo, fieldName, defaultValue);
    }

    private static double ReadDouble(
        string value,
        int rowNo,
        string fieldName,
        double defaultValue)
    {
        return CCsvParser.ReadDouble(value, TableName, rowNo, fieldName, defaultValue);
    }

    private static string NormalizeId(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeGroup(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeAddress(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeText(string value)
    {
        return value.Trim()
            .ToUpperInvariant()
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "_", StringComparison.OrdinalIgnoreCase);
    }
}
