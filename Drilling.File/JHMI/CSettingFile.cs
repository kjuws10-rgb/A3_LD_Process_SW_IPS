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

public sealed class CSettingFile(string configRoot) : CSettingFileBase
{
    private static readonly IReadOnlyList<string> FormHeaders =
    [
        "TAB",
        "GROUP",
        "NAME",
        "DISPLAY NAME",
        "DATA TYPE",
        "UNIT",
        "SHOW",
        "USE",
        "VALUE",
        "MIN",
        "MAX",
        "DESCRIPTION",
        "ORDER"
    ];

    private static readonly IReadOnlyList<string> ValueHeaders =
    [
        "TAB",
        "NAME",
        "VALUE"
    ];

    private readonly string _settingDirectory = Path.Combine(configRoot, "Setting");
    private readonly CLogManager _logManager = new(configRoot);

    public override Task<IReadOnlyList<ST_SYSTEM_PARAMETER>> Load(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = LoadSettingValues();
        bool FilterItem1(ST_SETTING_FORM_ITEM item)
        {
            return item.Use && item.Tab.Equals(ToTabText(section), StringComparison.OrdinalIgnoreCase);
        }

        int GetItemSortKey2(ST_SETTING_FORM_ITEM item)
        {
            return item.DisplayOrder;
        }

        ST_SYSTEM_PARAMETER SelectItem3(ST_SETTING_FORM_ITEM item)
        {
            return new ST_SYSTEM_PARAMETER(
                            section,
                            item.DisplayName,
                            GetValue(values, item.Tab, item.Name, item.DefaultValue),
                            item.Unit,
                            item.Description,
                            item.Group,
                            item.Name,
                            item.DefaultValue,
                            item.DataType,
                            item.Min,
                            item.Max,
                            item.Show,
                            item.Use,
                            item.DisplayOrder,
                            item.Extra);
        }

        var parameters = LoadFormItems()
            .Where(FilterItem1)
            .OrderBy(GetItemSortKey2)
            .Select(SelectItem3)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ST_SYSTEM_PARAMETER>>(parameters);
    }

    public override Task Save(
        EN_SETTING_TAB section,
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_settingDirectory);

        var formItems = LoadFormItems();
        var values = LoadSettingValues();
        var sectionTab = ToTabText(section);
        bool FilterItem4(ST_SETTING_FORM_ITEM item)
        {
            return item.Use && item.Tab.Equals(sectionTab, StringComparison.OrdinalIgnoreCase);
        }

        string HandleOldValues5(ST_SETTING_FORM_ITEM item)
        {
            return item.Name;
        }

        string HandleOldValues6(ST_SETTING_FORM_ITEM item)
        {
            return GetValue(values, item.Tab, item.Name, item.DefaultValue);
        }

        var oldValues = formItems
            .Where(FilterItem4)
            .ToDictionary(
HandleOldValues5,
HandleOldValues6,
                StringComparer.OrdinalIgnoreCase);
        bool FilterParameter7(ST_SYSTEM_PARAMETER parameter)
        {
            return !string.IsNullOrWhiteSpace(GetParameterKey(parameter));
        }

        string HandleEditedValues8(ST_SYSTEM_PARAMETER parameter)
        {
            return parameter.Value;
        }

        var editedValues = parameters
            .Where(FilterParameter7)
            .ToDictionary(
                GetParameterKey,
HandleEditedValues8,
                StringComparer.OrdinalIgnoreCase);
        bool FilterItem9(ST_SETTING_FORM_ITEM item)
        {
            return item.Use && item.Tab.Equals(sectionTab, StringComparison.OrdinalIgnoreCase);
        }

        int GetItemSortKey10(ST_SETTING_FORM_ITEM item)
        {
            return item.DisplayOrder;
        }

        ST_SYSTEM_PARAMETER SelectItem11(ST_SETTING_FORM_ITEM item)
        {
            return new ST_SYSTEM_PARAMETER(
                            section,
                            item.DisplayName,
                            editedValues.TryGetValue(item.Name, out var editedValue)
                                ? editedValue
                                : GetValue(values, item.Tab, item.Name, item.DefaultValue),
                            item.Unit,
                            item.Description,
                            item.Group,
                            item.Name,
                            item.DefaultValue,
                            item.DataType,
                            item.Min,
                            item.Max,
                            item.Show,
                            item.Use,
                            item.DisplayOrder,
                            item.Extra);
        }

        var normalizedParameters = formItems
            .Where(FilterItem9)
            .OrderBy(GetItemSortKey10)
            .Select(SelectItem11)
            .ToArray();

        ValidateSectionParameters(sectionTab, normalizedParameters, formItems);

        foreach (var parameter in normalizedParameters)
        {
            values[CreateKey(sectionTab, GetParameterKey(parameter))] = parameter.Value;
        }

        WriteSettingValues(formItems, values);
        ValidateSavedSection(section, normalizedParameters);

        foreach (var parameter in normalizedParameters)
        {
            var oldValue = oldValues.TryGetValue(GetParameterKey(parameter), out var value) ? value : "";

            if (!oldValue.Equals(parameter.Value, StringComparison.Ordinal))
            {
                _logManager.WriteSettingModify(section, parameter.Name, oldValue, parameter.Value);
            }
        }

        _logManager.WriteSettingSave(section);

        return Task.CompletedTask;
    }

    public override Task<IReadOnlyList<ST_SETTING_HISTORY>> LoadHistory(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_logManager.ReadSettingRecent(section));
    }

    private IReadOnlyList<ST_SETTING_FORM_ITEM> LoadFormItems()
    {
        string[] SelectHeader12(string header)
        {
            return new[] { header };
        }

        CCsvParser.ValidateRequiredHeaders(
            GetFormPath(),
            "JHMI_SETTING",
            FormHeaders.Select(SelectHeader12));
        ST_SETTING_FORM_ITEM SelectRow13(IReadOnlyDictionary<string, string> row, int index)
        {
            return new ST_SETTING_FORM_ITEM(
                            NormalizeTab(CCsvParser.Get(row, "TAB")),
                            NormalizeSettingText(CCsvParser.Get(row, "GROUP"), "COMMON"),
                            CCsvParser.Get(row, "NAME"),
                            GetOrDefault(CCsvParser.Get(row, "DISPLAY NAME"), CCsvParser.Get(row, "NAME")),
                            ReadDataType(CCsvParser.Get(row, "DATA TYPE")),
                            CCsvParser.Get(row, "UNIT"),
                            ReadBool(CCsvParser.Get(row, "SHOW"), true),
                            ReadBool(CCsvParser.Get(row, "USE"), true),
                            CCsvParser.Get(row, "VALUE"),
                            ReadDouble(CCsvParser.Get(row, "MIN"), 0.0),
                            ReadDouble(CCsvParser.Get(row, "MAX"), 0.0),
                            CCsvParser.Get(row, "DESCRIPTION"),
                            ReadInt(CCsvParser.Get(row, "ORDER"), index + 1),
                            CCsvParser.GetExtra(row, FormHeaders));
        }

        bool FilterItem14(ST_SETTING_FORM_ITEM item)
        {
            return !string.IsNullOrWhiteSpace(item.Tab) && !string.IsNullOrWhiteSpace(item.Name);
        }

        return CCsvParser.Read(GetFormPath())
            .Select(SelectRow13)
            .Where(FilterItem14)
            .ToArray();
    }

    private Dictionary<string, string> LoadSettingValues()
    {
        var valuePath = GetValuePath();
        if (System.IO.File.Exists(valuePath))
        {
            string[] SelectHeader15(string header)
            {
                return new[] { header };
            }

            CCsvParser.ValidateRequiredHeaders(
                valuePath,
                "Setting.csv",
                ValueHeaders.Select(SelectHeader15));
        }
        bool FilterRow16(IReadOnlyDictionary<string, string> row)
        {
            return !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "TAB")) &&
                            !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "NAME"));
        }

        string GroupByRowCallback17(IReadOnlyDictionary<string, string> row)
        {
            return CreateKey(CCsvParser.Get(row, "TAB"), CCsvParser.Get(row, "NAME"));
        }

        string ToDictionaryGroupCallback18(IGrouping<string, IReadOnlyDictionary<string, string>> group)
        {
            return group.Key;
        }

        string ToDictionaryGroupCallback19(IGrouping<string, IReadOnlyDictionary<string, string>> group)
        {
            return CCsvParser.Get(group.Last(), "VALUE");
        }

        return CCsvParser.Read(GetValuePath())
            .Where(FilterRow16)
            .GroupBy(GroupByRowCallback17, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
ToDictionaryGroupCallback18,
ToDictionaryGroupCallback19,
                StringComparer.OrdinalIgnoreCase);
    }

    private void WriteSettingValues(
        IReadOnlyList<ST_SETTING_FORM_ITEM> formItems,
        IReadOnlyDictionary<string, string> values)
    {
        bool FilterItem20(ST_SETTING_FORM_ITEM item)
        {
            return item.Use;
        }

        Dictionary<string, string> SelectItem21(ST_SETTING_FORM_ITEM item)
        {
            return new Dictionary<string, string>
            {
                ["TAB"] = item.Tab,
                ["NAME"] = item.Name,
                ["VALUE"] = GetValue(values, item.Tab, item.Name, item.DefaultValue)
            };
        }

        var rows = formItems
            .Where(FilterItem20)
            .Select(SelectItem21);

        CCsvParser.Write(GetValuePath(), ValueHeaders, rows);
    }

    private void ValidateSavedSection(
        EN_SETTING_TAB section,
        IReadOnlyList<ST_SYSTEM_PARAMETER> expectedParameters)
    {
        var actualValues = LoadSettingValues();
        var sectionTab = ToTabText(section);

        foreach (var expectedParameter in expectedParameters)
        {
            var parameterKey = GetParameterKey(expectedParameter);
            var key = CreateKey(sectionTab, parameterKey);

            if (!actualValues.TryGetValue(key, out var actualValue))
            {
                throw new InvalidDataException($"Setting CSV validation failed. Missing parameter: {sectionTab}/{parameterKey}");
            }

            if (!actualValue.Equals(expectedParameter.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Setting CSV validation failed. {sectionTab}/{parameterKey}: expected '{expectedParameter.Value}', actual '{actualValue}'");
            }
        }
    }

    private static void ValidateSectionParameters(
        string sectionTab,
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
        IReadOnlyList<ST_SETTING_FORM_ITEM> formItems)
    {
        bool FilterItem22(ST_SETTING_FORM_ITEM item)
        {
            return item.Use && item.Tab.Equals(sectionTab, StringComparison.OrdinalIgnoreCase);
        }

        string HandleFormItemsByName23(ST_SETTING_FORM_ITEM item)
        {
            return item.Name;
        }

        var formItemsByName = formItems
            .Where(FilterItem22)
            .ToDictionary(HandleFormItemsByName23, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            var parameterKey = GetParameterKey(parameter);

            if (!formItemsByName.TryGetValue(parameterKey, out var formItem))
            {
                throw new InvalidDataException($"Setting save blocked. Unknown parameter: {sectionTab}/{parameter.Name}");
            }

            var value = parameter.Value.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Setting save blocked. {formItem.DisplayName} cannot be empty.");
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

    private static string GetParameterKey(ST_SYSTEM_PARAMETER parameter)
    {
        return string.IsNullOrWhiteSpace(parameter.Key)
            ? parameter.Name
            : parameter.Key;
    }

    private static string ValidateIntParameter(
        ST_SETTING_FORM_ITEM formItem,
        string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Setting save blocked. {formItem.DisplayName} must be an integer.";
        }

        return ValidateNumericRange(formItem, parsed);
    }

    private static string ValidateDoubleParameter(
        ST_SETTING_FORM_ITEM formItem,
        string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Setting save blocked. {formItem.DisplayName} must be numeric.";
        }

        return ValidateNumericRange(formItem, parsed);
    }

    private static string ValidateBoolParameter(
        ST_SETTING_FORM_ITEM formItem,
        string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        return normalized is "ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0" or "YES" or "NO"
            ? ""
            : $"Setting save blocked. {formItem.DisplayName} must be ON/OFF or TRUE/FALSE.";
    }

    private static string ValidateNumericRange(
        ST_SETTING_FORM_ITEM formItem,
        double value)
    {
        if (!formItem.Min.Equals(formItem.Max) &&
            (value < formItem.Min || value > formItem.Max))
        {
            return $"Setting save blocked. {formItem.DisplayName} must be between {formItem.Min:0.###} and {formItem.Max:0.###}.";
        }

        return "";
    }

    private string GetFormPath()
    {
        return Path.Combine(configRoot, "JHMI_SETTING.csv");
    }

    private string GetValuePath()
    {
        return Path.Combine(_settingDirectory, "Setting.csv");
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        string tab,
        string name,
        string defaultValue)
    {
        return values.TryGetValue(CreateKey(tab, name), out var value)
            ? value
            : defaultValue;
    }

    private static string CreateKey(string tab, string name)
    {
        return $"{NormalizeTab(tab)}|{name.Trim().ToUpperInvariant()}";
    }

    private static string NormalizeTab(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        string EvaluateNormalizedSwitch2()
        {
            var switchValue = normalized;
            switch (switchValue)
            {
                case "IO":
                    return "IO";
                case "POSITION":
                    return "OPTION";
                case "OPTION" or "INTERFACE" or "MOTOR" or "ALARM":
                    return normalized;
                default:
                    return normalized;
            }
        }

        return EvaluateNormalizedSwitch2();
    }

    private static string NormalizeSettingText(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToUpperInvariant();
    }

    private static string GetOrDefault(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static EN_RECIPE_DATA_TYPE ReadDataType(string value)
    {
        EN_RECIPE_DATA_TYPE EvaluateValueSwitch3()
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

        return EvaluateValueSwitch3();
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

    private static double ReadDouble(string value, double defaultValue)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static string ToTabText(EN_SETTING_TAB section)
    {
        string EvaluateSectionSwitch4()
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

        return EvaluateSectionSwitch4();
    }

    private sealed record ST_SETTING_FORM_ITEM(
        string Tab,
        string Group,
        string Name,
        string DisplayName,
        EN_RECIPE_DATA_TYPE DataType,
        string Unit,
        bool Show,
        bool Use,
        string DefaultValue,
        double Min,
        double Max,
        string Description,
        int DisplayOrder,
        IReadOnlyDictionary<string, string>? Extra = null);
}







