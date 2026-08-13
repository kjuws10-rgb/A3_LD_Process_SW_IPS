using Drilling.Common.Managers;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CConfigStructureFile(string configRoot) : CConfigStructureFileBase
{
    private static readonly IReadOnlyList<ST_REQUIRED_CSV> RequiredCsvFiles =
    [
        Csv("JHMI_RCP", "JHMI_RCP.csv",
        [
            ["TAB"],
            ["GROUP"],
            ["NAME"],
            ["DISPLAY NAME"],
            ["CIM NAME"],
            ["DATA TYPE"],
            ["UNIT"],
            ["SHOW"],
            ["USE"],
            ["VALUE"],
            ["SCALE"],
            ["CHANGE LIMIT"],
            ["MIN"],
            ["MAX"],
            ["DESCRIPTION"],
            ["ORDER"]
        ], ValidateTabNameKey),

        Csv("JHMI_SETTING", "JHMI_SETTING.csv",
        [
            ["TAB"],
            ["GROUP"],
            ["NAME"],
            ["DISPLAY NAME"],
            ["DATA TYPE"],
            ["UNIT"],
            ["SHOW"],
            ["USE"],
            ["VALUE"],
            ["MIN"],
            ["MAX"],
            ["DESCRIPTION"],
            ["ORDER"]
        ], ValidateTabNameKey),

        Csv("JHMI_INTERFACE", "JHMI_INTERFACE.csv",
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
        ], ValidateDeviceNumberKey),

        Csv("JHMI_MOTOR", "JHMI_MOTOR.csv",
        [
            ["NAME", "MOTOR NAME", "AXIS NAME"],
            ["USE"],
            ["AXIS"],
            ["DEV TYPE", "DEVICE TYPE"],
            ["DEV NO", "DEVICE NO"],
            ["SCALE"],
            ["MAXVEL"],
            ["MAXACC"],
            ["MIN"],
            ["MAX"]
        ], ValidateNameKey),

        Csv("JHMI_IO", "JHMI_IO.csv",
        [
            ["ID"],
            ["USE"],
            ["ADDRESS"],
            ["NAME"],
            ["DIRECTION", "DIR"],
            ["DEV TYPE", "DEVICE TYPE"],
            ["DEV NO", "DEVICE NO"]
        ], ValidateIdKey),

        Csv("JHMI_MELSEC_MAP", "JHMI_MELSEC_MAP.csv",
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
        ], ValidateIdKey),

        Csv("JHMI_BET", "JHMI_BET.csv",
        [
            ["INDEX"],
            ["DESCRIPTION"],
            ["DIV"],
            ["MAG"],
            ["SPOTSIZE"]
        ], ValidateIndexKey),

        Csv("JHMI_POWERMETER", "JHMI_POWERMETER.csv",
        [
            ["STEP"],
            ["OPTION_NAME"],
            ["POWER_OUT"],
            ["POWER_UNIT"],
            ["SETTING_ATT"],
            ["SETTING_POWER"],
            ["SETTING_FREQ"],
            ["MEASURE_CYCLE"],
            ["MEASURE_TIME"],
            ["MEASURE_INTERVAL"],
            ["START_DELAY"],
            ["COOLING_TIME"],
            ["ROTATOR"],
            ["MEASURE_POWER"],
            ["STATE"]
        ], ValidateStepKey),

        Csv("JHMI_REVIEW_RULE", "JHMI_REVIEW_RULE.csv",
        [
            ["GROUP"],
            ["KEY"],
            ["DISPLAY NAME"],
            ["DATA TYPE"],
            ["VALUE"],
            ["DESCRIPTION"],
            ["ORDER"]
        ], ValidateGroupKey),

        Csv("JHMI_MANUAL_SCAN", "JHMI_MANUAL_SCAN.csv",
        [
            ["NAME"],
            ["DISPLAY NAME"],
            ["DATA TYPE"],
            ["UNIT"],
            ["SHOW"],
            ["USE"],
            ["VALUE"],
            ["MIN"],
            ["MAX"],
            ["DESCRIPTION"],
            ["ORDER"]
        ], ValidateNameKey)
    ];

    private static readonly IReadOnlyList<ST_VALUE_CSV> OptionalValueFiles =
    [
        ValueCsv("Setting Value", "Setting\\Setting.csv", [["TAB"], ["NAME"], ["VALUE"]], ValidateTabNameKey),
        ValueCsv("PowerMeter Default", "PowerMeter\\POWER_CHECK.pwm", [["STEP"], ["OPTION_NAME"], ["POWER_OUT"]], ValidateStepKey)
    ];

    public override Task<IReadOnlyList<ST_CONFIG_FILE_STATUS>> Validate(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statuses = new List<ST_CONFIG_FILE_STATUS>
        {
            CheckRoot()
        };

        statuses.AddRange(
            [
                EnsureDirectory("RECIPE Directory", "RECIPE", cancellationToken),
                EnsureDirectory("Setting Directory", "Setting", cancellationToken),
                EnsureDirectory("Manual Directory", "Manual", cancellationToken),
                EnsureDirectory("PowerMeter Directory", "PowerMeter", cancellationToken),
                EnsureDirectory("ReviewRule Directory", "ReviewRule", cancellationToken)
            ]);

        foreach (var csvFile in RequiredCsvFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(CheckCsv(csvFile));
        }

        foreach (var valueFile in OptionalValueFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(CheckValueCsv(valueFile));
        }

        statuses.Add(CheckRecipeValueFiles(cancellationToken));
        statuses.Add(CheckManualValueFiles(cancellationToken));
        statuses.Add(CheckPowerMeterValueFiles(cancellationToken));
        statuses.Add(CheckReviewRuleValueFiles(cancellationToken));

        return Task.FromResult<IReadOnlyList<ST_CONFIG_FILE_STATUS>>(statuses);
    }

    private ST_CONFIG_FILE_STATUS CheckRoot()
    {
        var path = NormalizePath(configRoot);
        var exists = Directory.Exists(path);

        return new ST_CONFIG_FILE_STATUS(
            "Config Root",
            path,
            true,
            exists,
            exists,
            exists ? "Config root found." : "Config root is missing.");
    }

    private ST_CONFIG_FILE_STATUS EnsureDirectory(
        string itemName,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetPath(relativePath);
        var existed = Directory.Exists(path);

        if (!existed)
        {
            Directory.CreateDirectory(path);
        }

        return new ST_CONFIG_FILE_STATUS(
            itemName,
            path,
            true,
            Directory.Exists(path),
            Directory.Exists(path),
            existed ? "Directory found." : "Directory created.");
    }

    private ST_CONFIG_FILE_STATUS CheckCsv(ST_REQUIRED_CSV csvFile)
    {
        var path = GetPath(csvFile.RelativePath);

        if (!System.IO.File.Exists(path))
        {
            return new ST_CONFIG_FILE_STATUS(
                csvFile.ItemName,
                path,
                true,
                false,
                false,
                "Required JHMI schema file is missing.");
        }

        try
        {
            CCsvParser.ValidateRequiredHeaders(path, csvFile.ItemName, csvFile.RequiredHeaderGroups);
            var rows = CCsvParser.Read(path);
            csvFile.RowValidator(csvFile.ItemName, rows);

            return new ST_CONFIG_FILE_STATUS(
                csvFile.ItemName,
                path,
                true,
                true,
                true,
                $"CSV header and key structure OK. Rows={rows.Count}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ST_CONFIG_FILE_STATUS(
                csvFile.ItemName,
                path,
                true,
                true,
                false,
                ex.Message);
        }
    }

    private ST_CONFIG_FILE_STATUS CheckValueCsv(ST_VALUE_CSV valueFile)
    {
        var path = GetPath(valueFile.RelativePath);

        if (!System.IO.File.Exists(path))
        {
            return new ST_CONFIG_FILE_STATUS(
                valueFile.ItemName,
                path,
                false,
                false,
                true,
                "Optional value file is not created yet.");
        }

        try
        {
            CCsvParser.ValidateRequiredHeaders(path, valueFile.ItemName, valueFile.RequiredHeaderGroups);
            var rows = CCsvParser.Read(path);
            valueFile.RowValidator(valueFile.ItemName, rows);

            return new ST_CONFIG_FILE_STATUS(
                valueFile.ItemName,
                path,
                false,
                true,
                true,
                $"CSV value structure OK. Rows={rows.Count}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ST_CONFIG_FILE_STATUS(
                valueFile.ItemName,
                path,
                false,
                true,
                false,
                ex.Message);
        }
    }

    private ST_CONFIG_FILE_STATUS CheckRecipeValueFiles(CancellationToken cancellationToken)
    {
        bool CheckLineValueFilesLineFieldsCallback1(IReadOnlyList<string> lineFields)
        {
            return lineFields.Count >= 3;
        }

        return CheckLineValueFiles(
            "Recipe Value Files",
            "RECIPE",
            "*.csv",
CheckLineValueFilesLineFieldsCallback1,
            "Each recipe line must be TAB,NAME,VALUE.",
            cancellationToken);
    }

    private ST_CONFIG_FILE_STATUS CheckManualValueFiles(CancellationToken cancellationToken)
    {
        return CheckCsvValueFiles(
            "Manual Scan Files",
            "Manual",
            "*.scan",
            [["NAME"], ["VALUE"]],
            ValidateNameKey,
            cancellationToken);
    }

    private ST_CONFIG_FILE_STATUS CheckPowerMeterValueFiles(CancellationToken cancellationToken)
    {
        return CheckCsvValueFiles(
            "PowerMeter Process Files",
            "PowerMeter",
            "*.pwm",
            [["STEP"], ["OPTION_NAME"], ["POWER_OUT"]],
            ValidateStepKey,
            cancellationToken);
    }

    private ST_CONFIG_FILE_STATUS CheckReviewRuleValueFiles(CancellationToken cancellationToken)
    {
        return CheckCsvValueFiles(
            "Review Rule Files",
            "ReviewRule",
            "*.csv",
            [["GROUP"], ["KEY"], ["VALUE"]],
            ValidateGroupKey,
            cancellationToken);
    }

    private ST_CONFIG_FILE_STATUS CheckCsvValueFiles(
        string itemName,
        string relativeDirectory,
        string pattern,
        IReadOnlyList<IReadOnlyList<string>> requiredHeaderGroups,
        Action<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> rowValidator,
        CancellationToken cancellationToken)
    {
        var directory = GetPath(relativeDirectory);

        if (!Directory.Exists(directory))
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                false,
                true,
                "Optional value directory is not created yet.");
        }
        string GetPathSortKey2(string path)
        {
            return path;
        }

        var files = Directory.EnumerateFiles(directory, pattern)
            .OrderBy(GetPathSortKey2, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                true,
                "No value file yet.");
        }

        try
        {
            var rowCount = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CCsvParser.ValidateRequiredHeaders(file, itemName, requiredHeaderGroups);
                var rows = CCsvParser.Read(file);
                rowValidator(itemName, rows);
                rowCount += rows.Count;
            }

            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                true,
                $"CSV value files OK. Files={files.Length}, Rows={rowCount}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                false,
                ex.Message);
        }
    }

    private ST_CONFIG_FILE_STATUS CheckLineValueFiles(
        string itemName,
        string relativeDirectory,
        string pattern,
        Func<IReadOnlyList<string>, bool> lineValidator,
        string invalidMessage,
        CancellationToken cancellationToken)
    {
        var directory = GetPath(relativeDirectory);

        if (!Directory.Exists(directory))
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                false,
                true,
                "Optional value directory is not created yet.");
        }
        string GetPathSortKey3(string path)
        {
            return path;
        }

        var files = Directory.EnumerateFiles(directory, pattern)
            .OrderBy(GetPathSortKey3, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                true,
                "No value file yet.");
        }

        try
        {
            var lineCount = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var lineNo = 0;
                foreach (var line in System.IO.File.ReadLines(file))
                {
                    lineNo++;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    lineCount++;
                    var fields = SplitCsvLine(line, file, lineNo);
                    if (!lineValidator(fields))
                    {
                        throw new InvalidDataException(
                            $"{itemName} validation failed. {Path.GetFileName(file)} row {lineNo}: {invalidMessage}");
                    }
                }
            }

            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                true,
                $"Line value files OK. Files={files.Length}, Lines={lineCount}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ST_CONFIG_FILE_STATUS(
                itemName,
                directory,
                false,
                true,
                false,
                ex.Message);
        }
    }

    private string GetPath(string relativePath)
    {
        return NormalizePath(Path.Combine(configRoot, relativePath));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static void ValidateTabNameKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback4(IReadOnlyDictionary<string, string> row)
        {
            return $"{CCsvParser.Get(row, "TAB").Trim().ToUpperInvariant()}|{CCsvParser.Get(row, "NAME").Trim().ToUpperInvariant()}";
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["TAB", "NAME"],
ValidateUniqueKeyRowCallback4);
    }

    private static void ValidateDeviceNumberKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback5(IReadOnlyDictionary<string, string> row)
        {
            return $"{CCsvParser.Get(row, "DEVICE").Trim().ToUpperInvariant()}|{GetFirstValue(row, "NUMBER", "NO").Trim().ToUpperInvariant()}";
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["DEVICE", "NUMBER"],
ValidateUniqueKeyRowCallback5);
    }

    private static void ValidateNameKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback6(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "NAME").Trim().ToUpperInvariant();
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["NAME"],
ValidateUniqueKeyRowCallback6);
    }

    private static void ValidateGroupKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback7(IReadOnlyDictionary<string, string> row)
        {
            return $"{CCsvParser.Get(row, "GROUP").Trim().ToUpperInvariant()}|{CCsvParser.Get(row, "KEY").Trim().ToUpperInvariant()}";
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["GROUP", "KEY"],
ValidateUniqueKeyRowCallback7);
    }

    private static void ValidateIdKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback8(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "ID").Trim().ToUpperInvariant();
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["ID"],
ValidateUniqueKeyRowCallback8);
    }

    private static void ValidateIndexKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback9(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "INDEX").Trim().ToUpperInvariant();
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["INDEX"],
ValidateUniqueKeyRowCallback9);
    }

    private static void ValidateStepKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        string ValidateUniqueKeyRowCallback10(IReadOnlyDictionary<string, string> row)
        {
            return CCsvParser.Get(row, "STEP").Trim().ToUpperInvariant();
        }

        ValidateUniqueKey(
            tableName,
            rows,
            ["STEP"],
ValidateUniqueKeyRowCallback10);
    }

    private static void ValidateUniqueKey(
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> displayKeyNames,
        Func<IReadOnlyDictionary<string, string>, string> createKey)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = createKey(row);
            if (string.IsNullOrWhiteSpace(key.Replace("|", "", StringComparison.Ordinal)))
            {
                continue;
            }

            if (!keys.Add(key))
            {
                throw new InvalidDataException(
                    $"{tableName} validation failed. Duplicated key: {string.Join("+", displayKeyNames)}={key}");
            }
        }
    }

    private static string GetFirstValue(
        IReadOnlyDictionary<string, string> row,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = CCsvParser.Get(row, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static IReadOnlyList<string> SplitCsvLine(
        string line,
        string path,
        int lineNo)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];

            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(current);
            }
        }

        fields.Add(value.ToString());

        if (inQuotes)
        {
            throw new InvalidDataException(
                $"CSV validation failed. {Path.GetFileName(path)} row {lineNo} has an unterminated quoted value.");
        }

        return fields;
    }

    private static ST_REQUIRED_CSV Csv(
        string itemName,
        string relativePath,
        IReadOnlyList<IReadOnlyList<string>> requiredHeaderGroups,
        Action<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> rowValidator)
    {
        return new ST_REQUIRED_CSV(itemName, relativePath, requiredHeaderGroups, rowValidator);
    }

    private static ST_VALUE_CSV ValueCsv(
        string itemName,
        string relativePath,
        IReadOnlyList<IReadOnlyList<string>> requiredHeaderGroups,
        Action<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> rowValidator)
    {
        return new ST_VALUE_CSV(itemName, relativePath, requiredHeaderGroups, rowValidator);
    }

    private sealed record ST_REQUIRED_CSV(
        string ItemName,
        string RelativePath,
        IReadOnlyList<IReadOnlyList<string>> RequiredHeaderGroups,
        Action<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> RowValidator);

    private sealed record ST_VALUE_CSV(
        string ItemName,
        string RelativePath,
        IReadOnlyList<IReadOnlyList<string>> RequiredHeaderGroups,
        Action<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> RowValidator);
}
