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

        var headers = ParseLine(lines[0], path, 1)
            .Select(header => header.Trim())
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
        var outputHeaders = headers
            .Concat(rowList.SelectMany(row => row.Keys)
                .Where(key => !headers.Contains(key, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string> { string.Join(",", outputHeaders.Select(Escape)) };
        lines.AddRange(rowList.Select(row =>
            string.Join(",", outputHeaders.Select(header => Escape(Get(row, header))))));

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

        return NormalizeHeader(value) switch
        {
            "1" or "TRUE" or "ON" or "YES" or "USE" or "Y" => true,
            "0" or "FALSE" or "OFF" or "NO" or "NOTUSE" or "N" => false,
            _ => defaultValue
        };
    }

    public static bool ReadRequiredBool(
        string value,
        string tableName,
        int rowNo,
        string fieldName)
    {
        return NormalizeHeader(value) switch
        {
            "1" or "TRUE" or "ON" or "YES" or "USE" or "Y" or "SIMUL" or "SIMULATION" or "SIM" => true,
            "0" or "FALSE" or "OFF" or "NO" or "NOTUSE" or "N" or "ONLINE" or "LIVE" or "REAL" => false,
            _ => throw new InvalidDataException(
                $"{tableName} validation failed. Row {rowNo} / {fieldName} must be 1/0 or ON/OFF.")
        };
    }

    public static IReadOnlyList<string> ReadHeaders(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        var firstLine = System.IO.File.ReadLines(path).FirstOrDefault();
        var headers = string.IsNullOrWhiteSpace(firstLine)
            ? []
            : ParseLine(firstLine, path, 1)
                .Select(header => header.Trim())
                .Where(header => !string.IsNullOrWhiteSpace(header))
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
        var missing = requiredHeaderGroups
            .Select(group => group
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .Select(header => header.Trim())
                .ToArray())
            .Where(group => group.Length > 0 &&
                !group.Any(header => headerSet.Contains(NormalizeHeader(header))))
            .Select(group => string.Join(" or ", group))
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

        return row
            .Where(item => !known.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
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
        var emptyIndexes = headers
            .Select((header, index) => new { Header = header, Index = index + 1 })
            .Where(item => string.IsNullOrWhiteSpace(item.Header))
            .Select(item => item.Index)
            .ToArray();

        if (emptyIndexes.Length > 0)
        {
            throw new InvalidDataException(
                $"CSV validation failed. {Path.GetFileName(path)} header column cannot be empty. Column(s): {string.Join(", ", emptyIndexes)}");
        }

        var duplicatedHeaders = headers
            .GroupBy(NormalizeHeader, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
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


