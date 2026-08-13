using System.Globalization;
using Drilling.Common.Motion;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CIoFile(string configRoot) : IIoFile
{
    private static readonly IReadOnlyList<string> Headers =
    [
        "ID",
        "USE",
        "ADDRESS",
        "NAME",
        "DIRECTION",
        "DEV TYPE",
        "DEV NO",
        "INITIAL STATE",
        "DISPLAY ORDER",
        "DESCRIPTION"
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> RequiredHeaderGroups =
    [
        ["ID"],
        ["USE"],
        ["ADDRESS"],
        ["NAME"],
        ["DIRECTION", "DIR"],
        ["DEV TYPE", "DEVICE TYPE"],
        ["DEV NO", "DEVICE NO"]
    ];

    public Task<IReadOnlyList<ST_IO_DATA>> LoadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFile();
        CCsvParser.ValidateRequiredHeaders(GetIoPath(), "JHMI_IO", RequiredHeaderGroups);

        var rows = CCsvParser.Read(GetIoPath())
            .Select((row, index) => Parse(row, index + 2))
            .Where(io => !string.IsNullOrWhiteSpace(io.Id))
            .OrderBy(io => io.DisplayOrder)
            .ThenBy(io => io.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Validate(rows);
        return Task.FromResult<IReadOnlyList<ST_IO_DATA>>(rows);
    }

    private ST_IO_DATA Parse(
        IReadOnlyDictionary<string, string> row,
        int rowNo)
    {
        var id = NormalizeId(RequireText(row, rowNo, "ID"));
        var name = ReadFirst(row, "NAME");

        return new ST_IO_DATA(
            id,
            ReadBool(ReadFirst(row, "USE"), true),
            NormalizeAddress(RequireText(row, rowNo, "ADDRESS")),
            string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
            ReadDirection(RequireText(row, rowNo, "DIRECTION", "DIR"), rowNo),
            ReadController(ReadFirst(row, "DEV TYPE")),
            ReadInt(ReadFirst(row, "DEV NO"), rowNo, "DEV NO", 0),
            ReadBool(ReadFirst(row, "INITIAL STATE"), false),
            ReadInt(ReadFirst(row, "DISPLAY ORDER"), rowNo, "DISPLAY ORDER", rowNo - 2),
            ReadFirst(row, "DESCRIPTION"));
    }

    private void EnsureFile()
    {
        var path = GetIoPath();

        if (System.IO.File.Exists(path))
        {
            return;
        }

        CCsvParser.Write(path, Headers, CreateDefaultRows());
    }

    private string GetIoPath()
    {
        return Path.Combine(configRoot, "JHMI_IO.csv");
    }

    private static void Validate(IReadOnlyList<ST_IO_DATA> rows)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(row => row.Use))
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                throw new InvalidDataException("JHMI_IO validation failed. ID cannot be empty.");
            }

            if (!usedIds.Add(row.Id))
            {
                throw new InvalidDataException($"JHMI_IO validation failed. Duplicated ID: {row.Id}");
            }

            if (string.IsNullOrWhiteSpace(row.Address))
            {
                throw new InvalidDataException($"JHMI_IO validation failed. ADDRESS cannot be empty: {row.Id}");
            }

            if (!usedAddresses.Add(row.Address))
            {
                throw new InvalidDataException($"JHMI_IO validation failed. Duplicated ADDRESS: {row.Address}");
            }

            if (string.IsNullOrWhiteSpace(row.DevType))
            {
                throw new InvalidDataException($"JHMI_IO validation failed. DEV TYPE cannot be empty: {row.Id}");
            }

            if (row.DevNo < 0)
            {
                throw new InvalidDataException($"JHMI_IO validation failed. DEV NO cannot be negative: {row.Id}");
            }
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> CreateDefaultRows()
    {
        return
        [
            Row("DOOR_LOCK_SENSOR", "X000", "Door Lock Sensor", false, true, 10, "Door lock closed"),
            Row("EMS", "X001", "EMS", false, true, 20, "Emergency stop"),
            Row("KEY_SWITCH_AUTO", "X002", "Key Switch Auto", false, true, 30, "Key switch in auto"),
            Row("LASER_SHUTTER_CLOSED", "X003", "Laser Shutter Closed", false, true, 40, "Shutter closed"),
            Row("CHILLER_ALARM", "X004", "Chiller Alarm", false, false, 50, "Chiller alarm input"),
            Row("LEAK_SENSOR", "X005", "Leak Sensor", false, true, 60, "Leak detection input"),
            Row("SMOKE_TEMP", "X006", "Smoke Temp", false, true, 70, "Smoke or over temp input"),
            Row("PM_LOCK", "X007", "PM Lock", false, true, 80, "PM lock status"),
            Row("MAIN_KEY_SOL_FEEDBACK", "X008", "Main Key Sol Feedback", false, true, 90, "Main key sol feedback"),
            Row("BUZZER_KEY", "X009", "Buzzer Key", false, false, 100, "Buzzer key input"),
            Row("SCANNER_READY", "X010", "Scanner Ready", false, true, 110, "Scanner ready signal"),
            Row("VISION_READY", "X011", "Vision Ready", false, true, 120, "Vision system ready"),
            Row("PANEL_IN_POSITION", "X012", "Panel In Position", false, true, 130, "Panel in position"),
            Row("VACUUM_CHECK", "X013", "Vacuum Check", false, true, 140, "Vacuum OK"),
            Row("DOOR_LOCK", "Y000", "Door Lock", true, true, 210, "Door lock solenoid"),
            Row("BUZZER", "Y001", "Buzzer", true, false, 220, "Buzzer output"),
            Row("TOWER_GREEN", "Y002", "Tower Green", true, true, 230, "Tower lamp green"),
            Row("TOWER_YELLOW", "Y003", "Tower Yellow", true, false, 240, "Tower lamp yellow"),
            Row("TOWER_RED", "Y004", "Tower Red", true, false, 250, "Tower lamp red"),
            Row("LASER_SHUTTER_COMMAND", "Y005", "Laser Shutter Command", true, true, 260, "Laser shutter control"),
            Row("CHILLER_RUN_COMMAND", "Y006", "Chiller Run Command", true, true, 270, "Chiller run control"),
            Row("MAIN_KEY_SOL", "Y007", "Main Key Sol", true, true, 280, "Main key solenoid"),
            Row("PM_LOCK_RELEASE", "Y008", "PM Lock Release", true, false, 290, "PM lock release"),
            Row("VACUUM_ON", "Y009", "Vacuum On", true, false, 300, "Vacuum pump on"),
            Row("SCANNER_ENABLE", "Y010", "Scanner Enable", true, true, 310, "Scanner enable"),
            Row("VISION_TRIGGER", "Y011", "Vision Trigger", true, false, 320, "Vision trigger pulse"),
            Row("LIGHT_ON", "Y012", "Light On", true, true, 330, "Work light output"),
            Row("SPARE_OUTPUT", "Y013", "Spare Output", true, false, 340, "Spare output")
        ];
    }

    private static IReadOnlyDictionary<string, string> Row(
        string id,
        string address,
        string name,
        bool isOutput,
        bool initialState,
        int displayOrder,
        string description)
    {
        return new Dictionary<string, string>
        {
            ["ID"] = id,
            ["USE"] = "1",
            ["ADDRESS"] = address,
            ["NAME"] = name,
            ["DIRECTION"] = isOutput ? "OUT" : "IN",
            ["DEV TYPE"] = "AUTOMATION1",
            ["DEV NO"] = "0",
            ["INITIAL STATE"] = initialState ? "ON" : "OFF",
            ["DISPLAY ORDER"] = displayOrder.ToString(CultureInfo.InvariantCulture),
            ["DESCRIPTION"] = description
        };
    }

    private static bool ReadDirection(
        string value,
        int rowNo)
    {
        bool EvaluateValueSwitch1()
        {
            var switchValue = NormalizeText(value);
            switch (switchValue)
            {
                case "OUT" or "OUTPUT" or "Y":
                    return true;
                case "IN" or "INPUT" or "X":
                    return false;
                default:
                    throw new InvalidDataException(
                        $"JHMI_IO validation failed. Row {rowNo} / DIRECTION must be IN or OUT: {value}");
            }
        }

        return EvaluateValueSwitch1();
    }

    private static string RequireText(
        IReadOnlyDictionary<string, string> row,
        int rowNo,
        params string[] names)
    {
        return CCsvParser.RequireText(row, "JHMI_IO", rowNo, names);
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
        return CCsvParser.ReadInt(value, "JHMI_IO", rowNo, fieldName, defaultValue);
    }

    private static string ReadController(string value)
    {
        var controller = NormalizeText(value);
        return string.IsNullOrWhiteSpace(controller) ? "AUTOMATION1" : controller;
    }

    private static string NormalizeId(string value)
    {
        var text = NormalizeText(value).Replace(" ", "_", StringComparison.OrdinalIgnoreCase);

        while (text.Contains("__", StringComparison.Ordinal))
        {
            text = text.Replace("__", "_", StringComparison.Ordinal);
        }

        return text.Trim('_');
    }

    private static string NormalizeAddress(string value)
    {
        return NormalizeText(value);
    }

    private static string NormalizeText(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
