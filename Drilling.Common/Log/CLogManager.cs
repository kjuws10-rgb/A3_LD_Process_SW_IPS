using System.Globalization;
using System.Text;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Log;

public interface ILogManager
{
    void WriteStationState(
        string stationName,
        string stateName,
        string action,
        string detail);

    void WriteInterfaceConnection(
        EN_EQP_MODULE module,
        string action,
        string nickName,
        string oldState,
        string newState);

    void WriteInterfaceCommand(
        EN_EQP_MODULE module,
        string nickName,
        string command,
        string response,
        string detail = "");

    void WriteInterfaceError(
        EN_EQP_MODULE module,
        string nickName,
        string command,
        string detail);

    void WriteProductEvent(
        string productId,
        string action,
        string state,
        string result,
        string detail);

    IReadOnlyList<ST_INTERFACE_HISTORY> ReadInterfaceRecent(
        EN_EQP_MODULE? module = null,
        string nickName = "",
        int maxRows = 100,
        int days = 14);
}

public sealed class CLogManager : ILogManager
{
    private const int DefaultReadDays = 14;
    private const int DefaultRecipeReadRows = 10;
    private const int DefaultSettingReadRows = 20;
    private const int DefaultInterfaceReadRows = 100;
    private static readonly object FileLock = new();

    private readonly string _recipeLogRoot;
    private readonly string _settingLogRoot;
    private readonly string _interfaceLogRoot;
    private readonly string _stationLogRoot;
    private readonly string _productLogRoot;

    public CLogManager(string configRoot)
    {
        var root = Directory.GetParent(configRoot)?.FullName ?? configRoot;
        _recipeLogRoot = Path.Combine(root, "Log", "Recipe");
        _settingLogRoot = Path.Combine(root, "Log", "Setting");
        _interfaceLogRoot = Path.Combine(root, "Log", "Interface");
        _stationLogRoot = Path.Combine(root, "Log", "Station");
        _productLogRoot = Path.Combine(root, "Log", "Product");
    }

    public void WriteStationState(
        string stationName,
        string stateName,
        string action,
        string detail)
    {
        var now = DateTime.Now;
        var directory = Path.Combine(_stationLogRoot, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, $"Station_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\STATION\\{EscapeField(stationName)}\\{EscapeField(stateName)}\\{EscapeField(action)}\\{EscapeField(detail)}";

        AppendLine(directory, path, line);
    }

    public IReadOnlyList<ST_RECIPE_HISTORY> ReadRecipeRecent(
        string recipeName,
        string recipeId,
        int maxRows = DefaultRecipeReadRows,
        int days = DefaultReadDays)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            recipeName,
            recipeId
        };
        bool FilterItem1(ST_RECIPE_HISTORY? item)
        {
            return item is not null;
        }

        bool FilterItem2(ST_RECIPE_HISTORY item)
        {
            return names.Contains(item.RecipeName);
        }

        DateTimeOffset GetItemSortKey3(ST_RECIPE_HISTORY item)
        {
            return item.ChangedAt;
        }

        return EnumerateRecentLogFiles(_recipeLogRoot, days)
            .SelectMany(ReadLogFile)
            .Select(ParseRecipeLine)
            .Where(FilterItem1)
            .Cast<ST_RECIPE_HISTORY>()
            .Where(FilterItem2)
            .OrderByDescending(GetItemSortKey3)
            .Take(maxRows)
            .ToArray();
    }

    public void WriteRecipeCreate(string recipeName)
    {
        WriteRecipeAction(recipeName, "CREATE", "Recipe", "-", recipeName);
    }

    public void WriteRecipeDelete(string recipeName)
    {
        WriteRecipeAction(recipeName, "DELETE", "Recipe", recipeName, "-");
    }

    public void WriteRecipeSave(string recipeName)
    {
        WriteRecipeAction(recipeName, "SAVE", "Recipe", "-", recipeName);
    }

    public void WriteRecipeModify(
        string recipeName,
        string itemName,
        string oldValue,
        string newValue)
    {
        WriteRecipeAction(recipeName, "MODIFY", itemName, oldValue, newValue);
    }

    public void WriteRecipeModify(
        string recipeName,
        string tab,
        string group,
        string itemName,
        string oldValue,
        string newValue)
    {
        WriteRecipeAction(recipeName, "MODIFY", tab, group, itemName, oldValue, newValue);
    }

    public void WriteRecipeRename(string oldRecipeName, string newRecipeName)
    {
        WriteRecipeAction(newRecipeName, "RENAME", "Recipe", oldRecipeName, newRecipeName);
    }

    public IReadOnlyList<ST_SETTING_HISTORY> ReadSettingRecent(
        EN_SETTING_TAB section,
        int maxRows = DefaultSettingReadRows,
        int days = DefaultReadDays)
    {
        bool FilterItem4(ST_SETTING_HISTORY? item)
        {
            return item is not null;
        }

        bool FilterItem5(ST_SETTING_HISTORY item)
        {
            return item.Section == section;
        }

        DateTimeOffset GetItemSortKey6(ST_SETTING_HISTORY item)
        {
            return item.ChangedAt;
        }

        return EnumerateRecentLogFiles(_settingLogRoot, days)
            .SelectMany(ReadLogFile)
            .Select(ParseSettingLine)
            .Where(FilterItem4)
            .Cast<ST_SETTING_HISTORY>()
            .Where(FilterItem5)
            .OrderByDescending(GetItemSortKey6)
            .Take(maxRows)
            .ToArray();
    }

    public void WriteSettingModify(
        EN_SETTING_TAB section,
        string parameterName,
        string oldValue,
        string newValue)
    {
        WriteSettingAction(section, "MODIFY", parameterName, oldValue, newValue);
    }

    public void WriteSettingSave(EN_SETTING_TAB section)
    {
        WriteSettingAction(section, "SAVE", "Section", "-", section.ToString().ToUpperInvariant());
    }

    public void WriteInterfaceConnection(
        EN_EQP_MODULE module,
        string action,
        string nickName,
        string oldState,
        string newState)
    {
        WriteInterfaceAction(module, action, nickName, oldState, newState);
    }

    public void WriteInterfaceCommand(
        EN_EQP_MODULE module,
        string nickName,
        string command,
        string response,
        string detail = "")
    {
        var after = string.IsNullOrWhiteSpace(detail)
            ? response
            : $"{response} / {detail}";

        WriteInterfaceAction(module, "COMMAND", nickName, command, after);
    }

    public void WriteInterfaceError(
        EN_EQP_MODULE module,
        string nickName,
        string command,
        string detail)
    {
        WriteInterfaceAction(module, "ERROR", nickName, command, $"ERROR / {detail}");
    }

    public void WriteProductEvent(
        string productId,
        string action,
        string state,
        string result,
        string detail)
    {
        var now = DateTime.Now;
        var directory = Path.Combine(_productLogRoot, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, $"Product_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\PRODUCT\\{EscapeField(productId)}\\{EscapeField(action)}\\{EscapeField(state)}\\{EscapeField(result)}\\{EscapeField(detail)}";

        AppendLine(directory, path, line);
    }

    public IReadOnlyList<ST_INTERFACE_HISTORY> ReadInterfaceRecent(
        EN_EQP_MODULE? module = null,
        string nickName = "",
        int maxRows = DefaultInterfaceReadRows,
        int days = DefaultReadDays)
    {
        var normalizedNickName = nickName.Trim();
        bool FilterItem7(ST_INTERFACE_HISTORY? item)
        {
            return item is not null;
        }

        bool FilterItem8(ST_INTERFACE_HISTORY item)
        {
            return string.IsNullOrWhiteSpace(normalizedNickName) ||
                            item.NickName.Equals(normalizedNickName, StringComparison.OrdinalIgnoreCase);
        }

        DateTimeOffset GetItemSortKey9(ST_INTERFACE_HISTORY item)
        {
            return item.OccurredAt;
        }

        return EnumerateInterfaceLogFiles(module, days)
            .SelectMany(ReadLogFile)
            .Select(ParseInterfaceLine)
            .Where(FilterItem7)
            .Cast<ST_INTERFACE_HISTORY>()
            .Where(FilterItem8)
            .OrderByDescending(GetItemSortKey9)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }

    private void WriteRecipeAction(
        string recipeName,
        string action,
        string itemName,
        string oldValue,
        string newValue)
    {
        var now = DateTime.Now;
        var directory = Path.Combine(_recipeLogRoot, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, $"Recipe_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\{recipeName}\\{action}\\{itemName}\\{oldValue}\\{newValue}";

        AppendLine(directory, path, line);
    }

    private void WriteRecipeAction(
        string recipeName,
        string action,
        string tab,
        string group,
        string itemName,
        string oldValue,
        string newValue)
    {
        var now = DateTime.Now;
        var directory = Path.Combine(_recipeLogRoot, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, $"Recipe_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\{EscapeField(recipeName)}\\{action}\\{EscapeField(tab)}\\{EscapeField(group)}\\{EscapeField(itemName)}\\{EscapeField(oldValue)}\\{EscapeField(newValue)}";

        AppendLine(directory, path, line);
    }

    private void WriteSettingAction(
        EN_SETTING_TAB section,
        string action,
        string parameterName,
        string oldValue,
        string newValue)
    {
        var now = DateTime.Now;
        var directory = Path.Combine(_settingLogRoot, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, $"Setting_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\SETTING\\{section.ToString().ToUpperInvariant()}\\{action}\\{EscapeField(parameterName)}\\{EscapeField(oldValue)}\\{EscapeField(newValue)}";

        AppendLine(directory, path, line);
    }

    private void WriteInterfaceAction(
        EN_EQP_MODULE module,
        string action,
        string nickName,
        string oldState,
        string newState)
    {
        var now = DateTime.Now;
        var moduleName = ModuleLogName(module);
        var directory = Path.Combine(
            _interfaceLogRoot,
            moduleName,
            now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var path = Path.Combine(
            directory,
            $"Interface_{moduleName}_{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_Trace.txt");
        var time = now.ToString("yy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{time} \\INTERFACE\\{moduleName}\\{EscapeField(nickName)}\\{action}\\{EscapeField(oldState)}\\{EscapeField(newState)}";

        AppendLine(directory, path, line);
    }

    private static void AppendLine(string directory, string path, string line)
    {
        lock (FileLock)
        {
            Directory.CreateDirectory(directory);
            System.IO.File.AppendAllText(path, line + Environment.NewLine, Encoding.Unicode);
        }
    }

    private static IEnumerable<string> EnumerateRecentLogFiles(string logRoot, int days)
    {
        var today = DateTime.Today;

        for (var offset = 0; offset < days; offset++)
        {
            var date = today.AddDays(-offset).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var directory = Path.Combine(logRoot, date);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.txt"))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ReadLogFile(string path)
    {
        try
        {
            return System.IO.File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private IEnumerable<string> EnumerateInterfaceLogFiles(
        EN_EQP_MODULE? module,
        int days)
    {
        if (module is not null)
        {
            return EnumerateRecentLogFiles(Path.Combine(_interfaceLogRoot, ModuleLogName(module.Value)), days);
        }

        if (!Directory.Exists(_interfaceLogRoot))
        {
            return [];
        }
        IEnumerable<string> SelectDirectory10(string directory)
        {
            return EnumerateRecentLogFiles(directory, days);
        }

        return Directory
            .EnumerateDirectories(_interfaceLogRoot)
            .SelectMany(SelectDirectory10)
            .ToArray();
    }

    private static ST_RECIPE_HISTORY? ParseRecipeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = SplitFields(line);

        if (parts.Length < 3)
        {
            return null;
        }

        var changedAt = ParseTimestamp(parts[0].Trim());
        var recipeName = UnescapeField(parts[1].Trim());
        var action = parts[2].Trim();

        if (string.IsNullOrWhiteSpace(recipeName) || string.IsNullOrWhiteSpace(action))
        {
            return null;
        }
        ST_RECIPE_HISTORY EvaluateValueSwitch1()
        {
            var switchValue = action.ToUpperInvariant();
            switch (switchValue)
            {
                case "MODIFY" when parts.Length >= 8:
                    return CreateRecipeHistory(
                            changedAt,
                            recipeName,
                            action,
                            UnescapeField(parts[5]),
                            UnescapeField(parts[6]),
                            UnescapeField(parts[7]),
                            UnescapeField(parts[3]),
                            UnescapeField(parts[4]));
                case "MODIFY" when parts.Length >= 6:
                    return CreateRecipeHistory(changedAt, recipeName, action, UnescapeField(parts[3]), UnescapeField(parts[4]), UnescapeField(parts[5]));
                case "PARAM CHANGE" when parts.Length >= 7:
                    return CreateRecipeHistory(changedAt, recipeName, action, $"{parts[3]} / {parts[4]}", parts[5], parts[6]);
                case "PARAM CHANGE OFFSET X" when parts.Length >= 7:
                    return CreateRecipeHistory(changedAt, recipeName, action, $"{parts[3]} / {parts[4]}", parts[5], parts[6]);
                case "PARAM CHANGE OFFSET Y" when parts.Length >= 7:
                    return CreateRecipeHistory(changedAt, recipeName, action, $"{parts[3]} / {parts[4]}", parts[5], parts[6]);
                case "CREATE":
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", "-", recipeName);
                case "HOST RECIPE CREATE":
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", "-", recipeName);
                case "DELETE":
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", recipeName, "-");
                case "RENAME" when parts.Length >= 6:
                    return CreateRecipeHistory(changedAt, recipeName, action, parts[3], parts[4], parts[5]);
                case "CHANGE":
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", "-", recipeName);
                case "SAVE":
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", "-", recipeName);
                case var _ when parts.Length >= 6:
                    return CreateRecipeHistory(changedAt, recipeName, action, parts[3], parts[4], parts[5]);
                default:
                    return CreateRecipeHistory(changedAt, recipeName, action, "Recipe", "-", "-");
            }
        }

        return EvaluateValueSwitch1();
    }

    private static ST_INTERFACE_HISTORY? ParseInterfaceLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = SplitFields(line);

        if (parts.Length < 7 ||
            !parts[1].Trim().Equals("INTERFACE", StringComparison.OrdinalIgnoreCase) ||
            !TryReadModuleLogName(parts[2], out var module))
        {
            return null;
        }

        var afterState = UnescapeField(parts[6].Trim());
        var detail = "";
        var separator = afterState.IndexOf(" / ", StringComparison.Ordinal);

        if (separator >= 0)
        {
            detail = afterState[(separator + 3)..].Trim();
            afterState = afterState[..separator].Trim();
        }

        return new ST_INTERFACE_HISTORY(
            ParseTimestamp(parts[0].Trim()),
            module,
            UnescapeField(parts[3].Trim()),
            parts[4].Trim(),
            UnescapeField(parts[5].Trim()),
            afterState,
            detail);
    }

    private static ST_RECIPE_HISTORY CreateRecipeHistory(
        DateTimeOffset changedAt,
        string recipeName,
        string action,
        string itemName,
        string oldValue,
        string newValue,
        string tab = "",
        string group = "")
    {
        return new ST_RECIPE_HISTORY(
            changedAt,
            itemName.Trim(),
            oldValue.Trim(),
            newValue.Trim(),
            "LOG",
            recipeName,
            action,
            tab.Trim(),
            group.Trim());
    }

    private static ST_SETTING_HISTORY? ParseSettingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = SplitFields(line);

        if (parts.Length < 7 ||
            !parts[1].Trim().Equals("SETTING", StringComparison.OrdinalIgnoreCase) ||
            !TryReadSection(parts[2], out var section))
        {
            return null;
        }

        return new ST_SETTING_HISTORY(
            ParseTimestamp(parts[0].Trim()),
            section,
            UnescapeField(parts[4].Trim()),
            UnescapeField(parts[5].Trim()),
            UnescapeField(parts[6].Trim()),
            "LOG",
            parts[3].Trim());
    }

    private static string[] SplitFields(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];

            if (current != '\\')
            {
                value.Append(current);
                continue;
            }

            if (index + 1 < line.Length && line[index + 1] == '\\')
            {
                value.Append('\\');
                index++;
                continue;
            }

            fields.Add(value.ToString());
            value.Clear();
        }

        fields.Add(value.ToString());
        return fields.ToArray();
    }

    private static string EscapeField(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string ModuleLogName(EN_EQP_MODULE module)
    {
        string EvaluateModuleSwitch2()
        {
            var switchValue = module;
            switch (switchValue)
            {
                case EN_EQP_MODULE.WonikCtrl:
                    return "WONIK_CTRL";
                case EN_EQP_MODULE.TalonLaser:
                    return "TALON_LASER";
                case EN_EQP_MODULE.PowerMeter:
                    return "POWER_METER";
                default:
                    return module.ToString().ToUpperInvariant();
            }
        }

        return EvaluateModuleSwitch2();
    }

    private static bool TryReadModuleLogName(string value, out EN_EQP_MODULE module)
    {
        var normalized = NormalizeModuleName(value);

        foreach (var candidate in Enum.GetValues<EN_EQP_MODULE>())
        {
            if (NormalizeModuleName(ModuleLogName(candidate)).Equals(normalized, StringComparison.Ordinal) ||
                NormalizeModuleName(candidate.ToString()).Equals(normalized, StringComparison.Ordinal))
            {
                module = candidate;
                return true;
            }
        }

        module = EN_EQP_MODULE.WonikCtrl;
        return false;
    }

    private static string NormalizeModuleName(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string UnescapeField(string value)
    {
        return value.Replace("\\\\", "\\");
    }

    private static bool TryReadSection(string value, out EN_SETTING_TAB section)
    {
        EN_SETTING_TAB EvaluateValueSwitch3()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "OPTION":
                    return EN_SETTING_TAB.Option;
                case "INTERFACE":
                    return EN_SETTING_TAB.Interface;
                case "IO":
                    return EN_SETTING_TAB.Io;
                case "MOTOR":
                    return EN_SETTING_TAB.Motor;
                case "POSITION":
                    return EN_SETTING_TAB.Option;
                case "ALARM":
                    return EN_SETTING_TAB.Alarm;
                default:
                    return EN_SETTING_TAB.Option;
            }
        }

        section = EvaluateValueSwitch3();

        return value.Trim().ToUpperInvariant() is "OPTION" or "INTERFACE" or "IO" or "MOTOR" or "POSITION" or "ALARM";
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        var formats = new[]
        {
            "yy/MM/dd HH:mm:ss.fff",
            "yyyy/MM/dd HH:mm:ss.fff",
            "yy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.fff"
        };

        return DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var result)
            ? new DateTimeOffset(result)
            : DateTimeOffset.Now;
    }
}



