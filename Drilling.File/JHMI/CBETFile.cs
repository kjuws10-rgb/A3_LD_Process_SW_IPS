using System.Globalization;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CBETFile(string configRoot) : IBETFile
{
    private static readonly IReadOnlyList<string> Headers =
    [
        "INDEX",
        "DESCRIPTION",
        "DIV",
        "MAG",
        "SPOTSIZE"
    ];

    public Task<IReadOnlyList<ST_BET_TABLE_DATA>> Load(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();

        return Task.FromResult<IReadOnlyList<ST_BET_TABLE_DATA>>(ReadTable(GetFormPath()));
    }

    public Task Save(
        IReadOnlyList<ST_BET_TABLE_DATA> table,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteTable(GetFormPath(), table);
        return Task.CompletedTask;
    }

    private void EnsureFiles()
    {
        if (!System.IO.File.Exists(GetFormPath()))
        {
            WriteTable(GetFormPath(), CreateDefaultTable());
        }
    }

    private List<ST_BET_TABLE_DATA> ReadTable(string path)
    {
        return CCsvParser.Read(path)
            .Where(row => !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "INDEX")))
            .Select((row, order) => new ST_BET_TABLE_DATA(
                ReadInt(CCsvParser.Get(row, "INDEX"), order),
                ReadDouble(CCsvParser.Get(row, "MAG"), 0.0),
                ReadDouble(CCsvParser.Get(row, "DIV"), 0.0),
                CCsvParser.Get(row, "DESCRIPTION")))
            .OrderBy(row => row.Index)
            .ToList();
    }

    private static void WriteTable(
        string path,
        IReadOnlyList<ST_BET_TABLE_DATA> table)
    {
        var rows = table
            .OrderBy(row => row.Index)
            .Select(row => new Dictionary<string, string>
            {
                ["INDEX"] = row.Index.ToString(CultureInfo.InvariantCulture),
                ["DESCRIPTION"] = row.Description,
                ["DIV"] = row.Divergence.ToString("F3", CultureInfo.InvariantCulture),
                ["MAG"] = row.Magnification.ToString("F3", CultureInfo.InvariantCulture),
                ["SPOTSIZE"] = row.SpotSize.ToString("F6", CultureInfo.InvariantCulture)
            });

        CCsvParser.Write(path, Headers, rows);
    }

    private static IReadOnlyList<ST_BET_TABLE_DATA> CreateDefaultTable()
    {
        return
        [
            new(0, 1020.000, 1626.000, "2times"),
            new(1, 2351.000, 1118.000, "3times"),
            new(2, 3014.000, 1278.000, "4times"),
            new(3, 3410.000, 1706.000, "5times"),
            new(4, 3673.000, 2267.000, "6times")
        ];
    }

    private string GetFormPath()
    {
        return Path.Combine(configRoot, "JHMI_BET.csv");
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
}





