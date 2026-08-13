using System.Globalization;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CManualScanFile(string configRoot) : IManualScanFile
{
    private const string DefaultSettingName = "CIRCLE_TEST.scan";

    private static readonly IReadOnlyList<string> ValueHeaders =
    [
        "NAME",
        "VALUE"
    ];

    private readonly string _manualDirectory = Path.Combine(configRoot, "Manual");

    public Task<IReadOnlyList<string>> List(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_manualDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        bool FilterName1(string? name)
        {
            return !string.IsNullOrWhiteSpace(name);
        }

        string GetNameSortKey2(string name)
        {
            return name;
        }

        var settingNames = Directory
            .EnumerateFiles(_manualDirectory, "*.scan")
            .Select(Path.GetFileName)
            .Where(FilterName1)
            .Cast<string>()
            .OrderBy(GetNameSortKey2, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(settingNames);
    }

    public Task<IReadOnlyList<ST_MANUAL_SCAN_FORM>> LoadForm(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(LoadFormItems());
    }

    public Task<ST_MANUAL_SCAN_PARAM> Load(CancellationToken cancellationToken = default)
    {
        return Load(GetDefaultSettingName(), cancellationToken);
    }

    public Task<ST_MANUAL_SCAN_PARAM> Load(
        string settingName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var formItems = LoadFormItems();
        bool FilterRow3(IReadOnlyDictionary<string, string> row)
        {
            return !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "NAME"));
        }

        string HandleValues4(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "NAME");
        }

        string HandleValues5(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "VALUE");
        }

        var values = CCsvParser.Read(GetSettingPath(settingName))
            .Where(FilterRow3)
            .ToDictionary(
HandleValues4,
HandleValues5,
                StringComparer.OrdinalIgnoreCase);

        var settings = new ST_MANUAL_SCAN_PARAM(
            ReadDouble(values, formItems, "ShapeSize", 0.350),
            ReadDouble(values, formItems, "OffsetX", 0.000),
            ReadDouble(values, formItems, "OffsetY", 0.000),
            ReadString(values, formItems, "Direction", "CW"),
            ReadString(values, formItems, "ShapeName", "Circle"),
            ReadDouble(values, formItems, "LaserPower", 1.0),
            ReadDouble(values, formItems, "JumpSpeed", 1.5),
            ReadDouble(values, formItems, "MarkSpeed", 0.9),
            0.0,
            ReadDouble(values, formItems, "LaserFrequency", 20.0),
            ReadDouble(values, formItems, "LaserOnDelay", 8.0),
            ReadDouble(values, formItems, "LaserOffDelay", 12.0),
            ReadDouble(values, formItems, "Time", 10.0),
            ReadInt(values, formItems, "Count", 48000),
            ReadInt(values, formItems, "GridRowLines", 5),
            ReadInt(values, formItems, "GridColLines", 5));

        return Task.FromResult(settings);
    }

    public Task Save(ST_MANUAL_SCAN_PARAM settings, CancellationToken cancellationToken = default)
    {
        return Save(GetDefaultSettingName(), settings, cancellationToken);
    }

    public Task Save(
        string settingName,
        ST_MANUAL_SCAN_PARAM settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = NormalizeSettingName(settingName);
        var formItems = LoadFormItems();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ShapeSize"] = settings.ShapeSize.ToString("F3", CultureInfo.InvariantCulture),
            ["OffsetX"] = settings.OffsetX.ToString("F3", CultureInfo.InvariantCulture),
            ["OffsetY"] = settings.OffsetY.ToString("F3", CultureInfo.InvariantCulture),
            ["Direction"] = settings.Direction,
            ["ShapeName"] = settings.ShapeName,
            ["LaserPower"] = settings.LaserPower.ToString("F2", CultureInfo.InvariantCulture),
            ["JumpSpeed"] = settings.JumpSpeed.ToString("F3", CultureInfo.InvariantCulture),
            ["MarkSpeed"] = settings.MarkSpeed.ToString("F3", CultureInfo.InvariantCulture),
            ["LaserFrequency"] = settings.LaserFrequency.ToString("F3", CultureInfo.InvariantCulture),
            ["LaserOnDelay"] = settings.LaserOnDelay.ToString("F3", CultureInfo.InvariantCulture),
            ["LaserOffDelay"] = settings.LaserOffDelay.ToString("F3", CultureInfo.InvariantCulture),
            ["GridRowLines"] = settings.GridRowLines.ToString(CultureInfo.InvariantCulture),
            ["GridColLines"] = settings.GridColLines.ToString(CultureInfo.InvariantCulture)
        };

        ValidateValues(formItems, values);
        bool FilterItem6(ST_MANUAL_SCAN_FORM item)
        {
            return item.Use;
        }

        int GetItemSortKey7(ST_MANUAL_SCAN_FORM item)
        {
            return item.DisplayOrder;
        }

        Dictionary<string, string> SelectItem8(ST_MANUAL_SCAN_FORM item)
        {
            return new Dictionary<string, string>
            {
                ["NAME"] = item.Name,
                ["VALUE"] = values.TryGetValue(item.Name, out var value) ? value : item.DefaultValue
            };
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            formItems
                .Where(FilterItem6)
                .OrderBy(GetItemSortKey7)
                .Select(SelectItem8)
                .ToArray();

        CCsvParser.Write(GetSettingPath(normalizedName), ValueHeaders, rows);
        ValidateSavedSetting(normalizedName, values);
        return Task.CompletedTask;
    }

    public Task Rename(
        string oldSettingName,
        string newSettingName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var oldPath = GetSettingPath(oldSettingName);
        var newPath = GetSettingPath(newSettingName);

        if (!System.IO.File.Exists(oldPath))
        {
            throw new FileNotFoundException($"Manual setting file was not found: {NormalizeSettingName(oldSettingName)}");
        }

        if (System.IO.File.Exists(newPath))
        {
            throw new IOException($"Manual setting file already exists: {NormalizeSettingName(newSettingName)}");
        }

        Directory.CreateDirectory(_manualDirectory);
        System.IO.File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public Task Delete(string settingName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetSettingPath(settingName);

        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetDefaultSettingName()
    {
        if (System.IO.File.Exists(GetSettingPath(DefaultSettingName)))
        {
            return DefaultSettingName;
        }
        bool MatchName9(string? name)
        {
            return !string.IsNullOrWhiteSpace(name);
        }

        return Directory.Exists(_manualDirectory)
            ? Directory
                .EnumerateFiles(_manualDirectory, "*.scan")
                .Select(Path.GetFileName)
                .FirstOrDefault(MatchName9)
                ?? DefaultSettingName
            : DefaultSettingName;
    }

    private IReadOnlyList<ST_MANUAL_SCAN_FORM> LoadFormItems()
    {
        ST_MANUAL_SCAN_FORM SelectRow10(IReadOnlyDictionary<string, string> row, int index)
        {
            return new ST_MANUAL_SCAN_FORM(
                            CCsvParser.Get(row, "NAME"),
                            GetOrDefault(CCsvParser.Get(row, "DISPLAY NAME"), CCsvParser.Get(row, "NAME")),
                            ReadDataType(CCsvParser.Get(row, "DATA TYPE")),
                            CCsvParser.Get(row, "UNIT"),
                            ReadBool(CCsvParser.Get(row, "SHOW"), true),
                            ReadBool(CCsvParser.Get(row, "USE"), true),
                            CCsvParser.Get(row, "VALUE"),
                            ReadDoubleValue(CCsvParser.Get(row, "MIN"), 0.0),
                            ReadDoubleValue(CCsvParser.Get(row, "MAX"), 0.0),
                            CCsvParser.Get(row, "DESCRIPTION"),
                            ReadInt(CCsvParser.Get(row, "ORDER"), index + 1));
        }

        bool FilterItem11(ST_MANUAL_SCAN_FORM item)
        {
            return !string.IsNullOrWhiteSpace(item.Name);
        }

        var formItems = CCsvParser.Read(GetFormPath())
            .Select(SelectRow10)
            .Where(FilterItem11)
            .ToArray();

        return formItems.Length > 0
            ? formItems
            : CreateFallbackFormItems();
    }

    private void ValidateSavedSetting(
        string settingName,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool FilterRow12(IReadOnlyDictionary<string, string> row)
        {
            return !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "NAME"));
        }

        string HandleActualValues13(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "NAME");
        }

        string HandleActualValues14(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "VALUE");
        }

        var actualValues = CCsvParser.Read(GetSettingPath(settingName))
            .Where(FilterRow12)
            .ToDictionary(
HandleActualValues13,
HandleActualValues14,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (name, expectedValue) in values)
        {
            if (!actualValues.TryGetValue(name, out var actualValue))
            {
                throw new InvalidDataException($"Manual setting CSV validation failed. Missing parameter: {name}");
            }

            if (!actualValue.Equals(expectedValue, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Manual setting CSV validation failed. {name}: expected '{expectedValue}', actual '{actualValue}'");
            }
        }
    }

    private static void ValidateValues(
        IReadOnlyList<ST_MANUAL_SCAN_FORM> formItems,
        IReadOnlyDictionary<string, string> values)
    {
        bool FilterItem15(ST_MANUAL_SCAN_FORM item)
        {
            return item.Use;
        }

        string HandleFormItemsByName16(ST_MANUAL_SCAN_FORM item)
        {
            return item.Name;
        }

        var formItemsByName = formItems
            .Where(FilterItem15)
            .ToDictionary(HandleFormItemsByName16, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in values)
        {
            if (!formItemsByName.TryGetValue(name, out var formItem))
            {
                throw new InvalidDataException($"Manual setting save blocked. Unknown parameter: {name}");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Manual setting save blocked. {formItem.DisplayName} cannot be empty.");
            }
            string EvaluateDataTypeSwitch1()
            {
                var switchValue = formItem.DataType;
                switch (switchValue)
                {
                    case EN_RECIPE_DATA_TYPE.Int:
                        return ValidateIntParameter(formItem, value);
                    case EN_RECIPE_DATA_TYPE.Double:
                        return ValidateDoubleParameter(formItem, value);
                    case EN_RECIPE_DATA_TYPE.Bool:
                        return ValidateBoolParameter(formItem, value);
                    default:
                        return "";
                }
            }

            var validationMessage = EvaluateDataTypeSwitch1();

            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                throw new InvalidDataException(validationMessage);
            }
        }
    }

    private static string ValidateIntParameter(
        ST_MANUAL_SCAN_FORM formItem,
        string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Manual setting save blocked. {formItem.DisplayName} must be an integer.";
        }

        return ValidateNumericRange(formItem, parsed);
    }

    private static string ValidateDoubleParameter(
        ST_MANUAL_SCAN_FORM formItem,
        string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Manual setting save blocked. {formItem.DisplayName} must be numeric.";
        }

        return ValidateNumericRange(formItem, parsed);
    }

    private static string ValidateBoolParameter(
        ST_MANUAL_SCAN_FORM formItem,
        string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        return normalized is "ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0" or "YES" or "NO"
            ? ""
            : $"Manual setting save blocked. {formItem.DisplayName} must be ON/OFF or TRUE/FALSE.";
    }

    private static string ValidateNumericRange(
        ST_MANUAL_SCAN_FORM formItem,
        double value)
    {
        if (formItem.Min.Equals(formItem.Max))
        {
            return "";
        }

        return value < formItem.Min || value > formItem.Max
            ? $"Manual setting save blocked. {formItem.DisplayName} must be between {formItem.Min:0.###} and {formItem.Max:0.###} {formItem.Unit}."
            : "";
    }

    private double ReadDouble(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<ST_MANUAL_SCAN_FORM> formItems,
        string key,
        double defaultValue)
    {
        var value = ReadString(values, formItems, key, defaultValue.ToString(CultureInfo.InvariantCulture));

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private int ReadInt(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<ST_MANUAL_SCAN_FORM> formItems,
        string key,
        int defaultValue)
    {
        var value = ReadString(values, formItems, key, defaultValue.ToString(CultureInfo.InvariantCulture));

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static double ReadDoubleValue(string value, double defaultValue)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private string ReadString(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<ST_MANUAL_SCAN_FORM> formItems,
        string key,
        string defaultValue)
    {
        bool MatchItem17(ST_MANUAL_SCAN_FORM item)
        {
            return item.Name.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        return values.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value)
            ? value
            : formItems.FirstOrDefault(MatchItem17)?.DefaultValue
                ?? defaultValue;
    }

    private string GetFormPath()
    {
        return Path.Combine(configRoot, "JHMI_MANUAL_SCAN.csv");
    }

    private string GetSettingPath(string settingName)
    {
        return Path.Combine(_manualDirectory, NormalizeSettingName(settingName));
    }

    private static string NormalizeSettingName(string settingName)
    {
        var normalized = Path.GetFileName(settingName.Trim());

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = DefaultSettingName;
        }

        if (!normalized.EndsWith(".scan", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{normalized}.scan";
        }

        return normalized;
    }

    private static EN_RECIPE_DATA_TYPE ReadDataType(string value)
    {
        EN_RECIPE_DATA_TYPE EvaluateValueSwitch2()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "INT":
                    return EN_RECIPE_DATA_TYPE.Int;
                case "DOUBLE":
                    return EN_RECIPE_DATA_TYPE.Double;
                case "BOOL":
                    return EN_RECIPE_DATA_TYPE.Bool;
                default:
                    return EN_RECIPE_DATA_TYPE.String;
            }
        }

        return EvaluateValueSwitch2();
    }

    private static bool ReadBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("USE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(string value, int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static string GetOrDefault(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static IReadOnlyList<ST_MANUAL_SCAN_FORM> CreateFallbackFormItems()
    {
        return
        [
            new("ShapeSize", "Shape Size", EN_RECIPE_DATA_TYPE.Double, "mm", false, true, "0.350", 0.001, 100.0, "Manual shape size", 1),
            new("OffsetX", "Offset X", EN_RECIPE_DATA_TYPE.Double, "mm", false, true, "0.000", -100.0, 100.0, "Manual shape center offset X", 2),
            new("OffsetY", "Offset Y", EN_RECIPE_DATA_TYPE.Double, "mm", false, true, "0.000", -100.0, 100.0, "Manual shape center offset Y", 3),
            new("Direction", "Direction", EN_RECIPE_DATA_TYPE.String, "", false, true, "CW", 0.0, 0.0, "Manual shape direction", 4),
            new("ShapeName", "Shape Name", EN_RECIPE_DATA_TYPE.String, "", false, true, "Circle", 0.0, 0.0, "Manual shape name", 5),
            new("GridRowLines", "Grid Row Lines", EN_RECIPE_DATA_TYPE.Int, "ea", false, true, "5", 2.0, 200.0, "Manual grid row line count", 6),
            new("GridColLines", "Grid Col Lines", EN_RECIPE_DATA_TYPE.Int, "ea", false, true, "5", 2.0, 200.0, "Manual grid column line count", 7),
            new("LaserPower", "Laser Power", EN_RECIPE_DATA_TYPE.Double, "W", true, true, "1.00", 0.0, 100.0, "Manual laser power", 10),
            new("JumpSpeed", "Jump Speed", EN_RECIPE_DATA_TYPE.Double, "m/sec", true, true, "1.500", 0.001, 100.0, "Manual jump speed", 20),
            new("MarkSpeed", "Mark Speed", EN_RECIPE_DATA_TYPE.Double, "m/sec", true, true, "0.900", 0.001, 100.0, "Manual mark speed", 30),
            new("LaserFrequency", "Laser Frequency", EN_RECIPE_DATA_TYPE.Double, "kHz", true, true, "20.0", 0.001, 1000.0, "Manual laser frequency", 40),
            new("LaserOnDelay", "Laser On Delay", EN_RECIPE_DATA_TYPE.Double, "usec", true, true, "8", 0.0, 10000.0, "Manual laser on delay", 50),
            new("LaserOffDelay", "Laser Off Delay", EN_RECIPE_DATA_TYPE.Double, "usec", true, true, "12", 0.0, 10000.0, "Manual laser off delay", 60)
        ];
    }
}





