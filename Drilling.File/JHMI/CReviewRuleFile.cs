using System.Globalization;
using Drilling.Common.Review;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CReviewRuleFile(string configRoot) : IReviewRuleFile
{
    private const string DefaultRuleName = "ALL_POINT.csv";

    private static readonly IReadOnlyList<string> Headers =
    [
        "GROUP",
        "KEY",
        "VALUE",
        "DESCRIPTION"
    ];

    private readonly string _ruleDirectory = Path.Combine(configRoot, "ReviewRule");

    public Task<IReadOnlyList<string>> List(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDefaultRuleFile();

        var ruleNames = Directory
            .EnumerateFiles(_ruleDirectory, "*.csv")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(ruleNames);
    }

    public Task<ST_REVIEW_RULE_DATA> Load(
        string ruleFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDefaultRuleFile();

        var normalizedName = NormalizeRuleName(ruleFileName);
        var path = GetRulePath(normalizedName);

        if (!System.IO.File.Exists(path))
        {
            path = GetRulePath(DefaultRuleName);
            normalizedName = DefaultRuleName;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var holeKeys = new List<string>();

        foreach (var row in CCsvParser.Read(path))
        {
            var group = CCsvParser.Get(row, "GROUP").Trim();
            var key = CCsvParser.Get(row, "KEY").Trim();
            var value = CCsvParser.Get(row, "VALUE").Trim();

            if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (group.Equals("HOLE", StringComparison.OrdinalIgnoreCase))
            {
                if (CCsvParser.ReadBool(value, false) && !string.IsNullOrWhiteSpace(key))
                {
                    holeKeys.Add(CReviewManager.NormalizeHoleKey(key));
                }

                continue;
            }

            values[key] = value;
        }

        var ruleType = ReadRuleType(ReadValue(values, "RULE_TYPE", "ALL_POINT"));
        var ruleName = ReadValue(values, "RULE_NAME", Path.GetFileNameWithoutExtension(normalizedName));

        return Task.FromResult(new ST_REVIEW_RULE_DATA(
            normalizedName,
            ruleName,
            ruleType,
            ReadInt(values, "HEAD_NO", 1),
            ReadInt(values, "CELL_NO", 1),
            ReadInt(values, "ZERO_POINT_COUNT", 0),
            holeKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray()));
    }

    public Task Save(
        ST_REVIEW_RULE_DATA rule,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = NormalizeRuleName(rule.FileName);
        var rows = new List<IReadOnlyDictionary<string, string>>
        {
            Row("COMMON", "RULE_NAME", string.IsNullOrWhiteSpace(rule.RuleName) ? Path.GetFileNameWithoutExtension(normalizedName) : rule.RuleName, "Review rule display name"),
            Row("COMMON", "RULE_TYPE", ToRuleTypeText(rule.RuleType), "ALL_POINT / SAMPLE_POINT / EDGE / CENTER / HEAD_POINT / CELL_POINT / ZERO_LINE"),
            Row("COMMON", "HEAD_NO", rule.HeadNo.ToString(CultureInfo.InvariantCulture), "Target head number for HEAD_POINT"),
            Row("COMMON", "CELL_NO", rule.CellNo.ToString(CultureInfo.InvariantCulture), "Target cell number for CELL_POINT"),
            Row("COMMON", "ZERO_POINT_COUNT", rule.ZeroPointCount.ToString(CultureInfo.InvariantCulture), "Zero line review point count")
        };

        foreach (var holeKey in rule.HoleKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(CReviewManager.NormalizeHoleKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(Row("HOLE", holeKey, "1", "Selected review hole"));
        }

        CCsvParser.Write(GetRulePath(normalizedName), Headers, rows);
        ValidateSavedRule(normalizedName, rule);
        return Task.CompletedTask;
    }

    private void EnsureDefaultRuleFile()
    {
        Directory.CreateDirectory(_ruleDirectory);

        if (System.IO.File.Exists(GetRulePath(DefaultRuleName)))
        {
            return;
        }

        Save(new ST_REVIEW_RULE_DATA(
            DefaultRuleName,
            "ALL_POINT",
            EN_REVIEW_RULE_TYPE.AllPoint,
            1,
            1,
            0,
            [])).GetAwaiter().GetResult();
    }

    private void ValidateSavedRule(
        string ruleFileName,
        ST_REVIEW_RULE_DATA expectedRule)
    {
        var loadedRule = Load(ruleFileName).GetAwaiter().GetResult();

        if (loadedRule.RuleType != expectedRule.RuleType)
        {
            throw new InvalidDataException($"Review rule validation failed. RuleType expected '{expectedRule.RuleType}', actual '{loadedRule.RuleType}'.");
        }
    }

    private string GetRulePath(string ruleFileName)
    {
        return Path.Combine(_ruleDirectory, NormalizeRuleName(ruleFileName));
    }

    private static IReadOnlyDictionary<string, string> Row(
        string group,
        string key,
        string value,
        string description)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GROUP"] = group,
            ["KEY"] = key,
            ["VALUE"] = value,
            ["DESCRIPTION"] = description
        };
    }

    private static string NormalizeRuleName(string ruleFileName)
    {
        var normalizedName = Path.GetFileName(ruleFileName.Trim());

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = DefaultRuleName;
        }

        if (!normalizedName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = $"{normalizedName}.csv";
        }

        return normalizedName;
    }

    private static string ReadValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue)
    {
        return int.TryParse(ReadValue(values, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static EN_REVIEW_RULE_TYPE ReadRuleType(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(" ", "_") switch
        {
            "ALL" or "ALL_POINT" => EN_REVIEW_RULE_TYPE.AllPoint,
            "EDGE" => EN_REVIEW_RULE_TYPE.Edge,
            "CENTER" => EN_REVIEW_RULE_TYPE.Center,
            "HEAD" or "HEAD_POINT" => EN_REVIEW_RULE_TYPE.HeadPoint,
            "CELL" or "CELL_POINT" => EN_REVIEW_RULE_TYPE.CellPoint,
            "ZERO" or "ZERO_LINE" or "0_LINE" or "ZERO_DEFENSE" or "ZERO_DEFENCE" => EN_REVIEW_RULE_TYPE.ZeroLine,
            _ => EN_REVIEW_RULE_TYPE.SamplePoint
        };
    }

    private static string ToRuleTypeText(EN_REVIEW_RULE_TYPE ruleType)
    {
        return ruleType switch
        {
            EN_REVIEW_RULE_TYPE.AllPoint => "ALL_POINT",
            EN_REVIEW_RULE_TYPE.Edge => "EDGE",
            EN_REVIEW_RULE_TYPE.Center => "CENTER",
            EN_REVIEW_RULE_TYPE.HeadPoint => "HEAD_POINT",
            EN_REVIEW_RULE_TYPE.CellPoint => "CELL_POINT",
            EN_REVIEW_RULE_TYPE.ZeroLine => "ZERO_LINE",
            _ => "SAMPLE_POINT"
        };
    }
}
