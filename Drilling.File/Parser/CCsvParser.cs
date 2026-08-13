using System.Text;
using System.Globalization;

namespace Drilling.File.Parser;

internal static class CCsvParser
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        var lines = System.IO.File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return [];
        }
        string SelectHeader1(string header)
        {
            return header.Trim();
        }

        var headers = ParseLine(lines[0], path, 1)
            .Select(SelectHeader1)
            .ToArray();
        ValidateHeaders(path, headers);

        var rows = new List<IReadOnlyDictionary<string, string>>();

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseLine(line, path, lineIndex + 1);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < headers.Length; index++)
            {
                row[headers[index]] = index < fields.Count ? fields[index] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static void Write(
        string path,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        var rowList = rows.ToArray();
        IEnumerable<string> SelectRow2(IReadOnlyDictionary<string, string> row)
        {
            return row.Keys;
        }

        bool FilterKey3(string key)
        {
            return !headers.Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        var outputHeaders = headers
            .Concat(rowList.SelectMany(SelectRow2)
                .Where(FilterKey3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string> { string.Join(",", outputHeaders.Select(Escape)) };
        string SelectRow4(IReadOnlyDictionary<string, string> row)
        {
            string SelectHeader2(string header)
            {
                return Escape(Get(row, header));
            }

            return string.Join(",", outputHeaders.Select(SelectHeader2));
        }

        lines.AddRange(rowList.Select(SelectRow4));

        System.IO.File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public static string Get(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value : string.Empty;
    }

    public static string GetFirst(
        IReadOnlyDictionary<string, string> row,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = Get(row, name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    public static string RequireText(
        IReadOnlyDictionary<string, string> row,
        string tableName,
        int rowNo,
        params string[] names)
    {
        var value = GetFirst(row, names);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"{tableName} validation failed. Row {rowNo} / {names[0]} cannot be empty.");
        }

        return value;
    }

    public static int ReadInt(
        string value,
        string tableName,
        int rowNo,
        string fieldName,
        int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException(
                $"{tableName} validation failed. Row {rowNo} / {fieldName} must be integer: {value}");
    }

    public static int ReadRequiredInt(
        string value,
        string tableName,
        int rowNo,
        string fieldName,
        bool allowNegative = false)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ||
            (!allowNegative && result < 0))
        {
            var signText = allowNegative ? "" : " non-negative";
            throw new InvalidDataException(
                $"{tableName} validation failed. Row {rowNo} / {fieldName} must be a{signText} integer.");
        }

        return result;
    }

    public static double ReadDouble(
        string value,
        string tableName,
        int rowNo,
        string fieldName,
        double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException(
                $"{tableName} validation failed. Row {rowNo} / {fieldName} must be number: {value}");
    }

    public static bool ReadBool(
        string value,
        bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        bool EvaluateValueSwitch1()
        {
            var switchValue = NormalizeHeader(value);
            switch (switchValue)
            {
                case "1" or "TRUE" or "ON" or "YES" or "USE" or "Y":
                    return true;
                case "0" or "FALSE" or "OFF" or "NO" or "NOTUSE" or "N":
                    return false;
                default:
                    return defaultValue;
            }
        }

        return EvaluateValueSwitch1();
    }

    public static bool ReadRequiredBool(
        string value,
        string tableName,
        int rowNo,
        string fieldName)
    {
        bool EvaluateValueSwitch2()
        {
            var switchValue = NormalizeHeader(value);
            switch (switchValue)
            {
                case "1" or "TRUE" or "ON" or "YES" or "USE" or "Y" or "SIMUL" or "SIMULATION" or "SIM":
                    return true;
                case "0" or "FALSE" or "OFF" or "NO" or "NOTUSE" or "N" or "ONLINE" or "LIVE" or "REAL":
                    return false;
                default:
                    throw new InvalidDataException(
                        $"{tableName} validation failed. Row {rowNo} / {fieldName} must be 1/0 or ON/OFF.");
            }
        }

        return EvaluateValueSwitch2();
    }

    public static IReadOnlyList<string> ReadHeaders(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        var firstLine = System.IO.File.ReadLines(path).FirstOrDefault();
        string SelectHeader5(string header)
        {
            return header.Trim();
        }

        bool FilterHeader6(string header)
        {
            return !string.IsNullOrWhiteSpace(header);
        }

        var headers = string.IsNullOrWhiteSpace(firstLine)
            ? []
            : ParseLine(firstLine, path, 1)
                .Select(SelectHeader5)
                .Where(FilterHeader6)
                .ToArray();

        if (headers.Length > 0)
        {
            ValidateHeaders(path, headers);
        }

        return headers;
    }

    public static void ValidateRequiredHeaders(
        string path,
        string tableName,
        IEnumerable<IEnumerable<string>> requiredHeaderGroups)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"{tableName} file not found.", path);
        }

        var headers = ReadHeaders(path);
        if (headers.Count == 0)
        {
            throw new InvalidDataException($"{tableName} validation failed. Header row is empty.");
        }

        var headerSet = headers
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] SelectGroup7(IEnumerable<string> group)
        {
            bool FilterHeader2(string header)
            {
                return !string.IsNullOrWhiteSpace(header);
            }

            string SelectHeader3(string header)
            {
                return header.Trim();
            }

            return group
                            .Where(FilterHeader2)
                            .Select(SelectHeader3)
                            .ToArray();
        }

        bool FilterGroup8(string[] group)
        {
            bool CheckHeader4(string header)
            {
                return headerSet.Contains(NormalizeHeader(header));
            }

            return group.Length > 0 &&
                            !group.Any(CheckHeader4);
        }

        string SelectGroup9(string[] group)
        {
            return string.Join(" or ", group);
        }

        var missing = requiredHeaderGroups
            .Select(SelectGroup7)
            .Where(FilterGroup8)
            .Select(SelectGroup9)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"{tableName} validation failed. Missing column(s): {string.Join(", ", missing)}. " +
                $"Available: {string.Join(", ", headers)}");
        }
    }

    public static IReadOnlyDictionary<string, string> GetExtra(
        IReadOnlyDictionary<string, string> row,
        IEnumerable<string> knownHeaders)
    {
        var known = new HashSet<string>(knownHeaders, StringComparer.OrdinalIgnoreCase);
        bool FilterItem10(KeyValuePair<string, string> item)
        {
            return !known.Contains(item.Key);
        }

        string ToDictionaryItemCallback11(KeyValuePair<string, string> item)
        {
            return item.Key;
        }

        string ToDictionaryItemCallback12(KeyValuePair<string, string> item)
        {
            return item.Value;
        }

        return row
            .Where(FilterItem10)
            .ToDictionary(ToDictionaryItemCallback11, ToDictionaryItemCallback12, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ParseLine(
        string line,
        string path,
        int lineNo)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
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

    private static void ValidateHeaders(
        string path,
        IReadOnlyList<string> headers)
    {
        List<int> emptyIndexList = new List<int>();
        for (int index = 0; index < headers.Count; index++)
        {
            string header = headers[index];
            if (string.IsNullOrWhiteSpace(header))
            {
                emptyIndexList.Add(index + 1);
            }
        }

        int[] emptyIndexes = emptyIndexList.ToArray();

        if (emptyIndexes.Length > 0)
        {
            throw new InvalidDataException(
                $"CSV validation failed. {Path.GetFileName(path)} header column cannot be empty. Column(s): {string.Join(", ", emptyIndexes)}");
        }
        bool FilterGroup13(IGrouping<string, string> group)
        {
            return group.Count() > 1;
        }

        string SelectGroup14(IGrouping<string, string> group)
        {
            return group.First();
        }

        var duplicatedHeaders = headers
            .GroupBy(NormalizeHeader, StringComparer.OrdinalIgnoreCase)
            .Where(FilterGroup13)
            .Select(SelectGroup14)
            .ToArray();

        if (duplicatedHeaders.Length > 0)
        {
            throw new InvalidDataException(
                $"CSV validation failed. {Path.GetFileName(path)} has duplicated header(s): {string.Join(", ", duplicatedHeaders)}");
        }
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') &&
            !value.Contains('"') &&
            !value.Contains('\r') &&
            !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string NormalizeHeader(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}


