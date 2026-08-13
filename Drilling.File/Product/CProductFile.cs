using System.Globalization;
using Drilling.Common.Product;
using Drilling.File.Parser;

namespace Drilling.File.Product;

public sealed class CProductFile(string configRoot) : IProductFile
{
    private static readonly IReadOnlyList<string> ActiveHeaders =
    [
        "ROW_TYPE",
        "PRODUCT_ID",
        "PANEL_ID",
        "LOT_ID",
        "PROCESS_ID",
        "RECIPE_ID",
        "STATE",
        "RESULT",
        "CREATED_AT",
        "STARTED_AT",
        "COMPLETED_AT",
        "NAME",
        "VALUE",
        "HEAD_NO",
        "HEAD_STATE",
        "TOTAL_POINTS",
        "COMPLETED_POINTS",
        "HEAD_RESULT",
        "ERROR_CODE",
        "MESSAGE",
        "HEAD_STARTED_AT",
        "HEAD_COMPLETED_AT"
    ];

    private static readonly IReadOnlyList<string> HistoryHeaders =
    [
        "OCCURRED_AT",
        "ROW_TYPE",
        "PRODUCT_ID",
        "PROCESS_ID",
        "RECIPE_ID",
        "ACTION",
        "STATE",
        "RESULT",
        "DETAIL",
        "HEAD_NO",
        "HEAD_STATE",
        "TOTAL_POINTS",
        "COMPLETED_POINTS",
        "HEAD_RESULT",
        "ERROR_CODE",
        "MESSAGE",
        "HEAD_STARTED_AT",
        "HEAD_COMPLETED_AT"
    ];

    private readonly string _productRoot = Path.Combine(
        Directory.GetParent(configRoot)?.FullName ?? configRoot,
        "Data",
        "Product");

    public Task<ST_PRODUCT_DATA?> LoadActive(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = CCsvParser.Read(GetActiveProductPath());
        var productRow = rows.FirstOrDefault(row =>
            CCsvParser.Get(row, "ROW_TYPE").Equals("PRODUCT", StringComparison.OrdinalIgnoreCase));

        if (productRow is null)
        {
            return Task.FromResult<ST_PRODUCT_DATA?>(null);
        }

        var productId = CCsvParser.Get(productRow, "PRODUCT_ID");
        if (string.IsNullOrWhiteSpace(productId))
        {
            return Task.FromResult<ST_PRODUCT_DATA?>(null);
        }

        var parameters = rows
            .Where(row => IsProductRow(row, productId, "PARAM"))
            .GroupBy(row => CCsvParser.Get(row, "NAME"), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => CCsvParser.Get(group.Last(), "VALUE"),
                StringComparer.OrdinalIgnoreCase);
        var heads = rows
            .Where(row => IsProductRow(row, productId, "HEAD"))
            .Select(ParseHead)
            .OrderBy(head => head.HeadNo)
            .ToArray();

        var product = new ST_PRODUCT_DATA(
            productId,
            CCsvParser.Get(productRow, "PANEL_ID"),
            CCsvParser.Get(productRow, "LOT_ID"),
            ReadProcessId(productRow),
            CCsvParser.Get(productRow, "RECIPE_ID"),
            ParseEnum(CCsvParser.Get(productRow, "STATE"), EN_PRODUCT_STATE.Created),
            ParseEnum(CCsvParser.Get(productRow, "RESULT"), EN_PRODUCT_RESULT.Pending),
            ParseDate(CCsvParser.Get(productRow, "CREATED_AT")) ?? DateTimeOffset.Now,
            ParseDate(CCsvParser.Get(productRow, "STARTED_AT")),
            ParseDate(CCsvParser.Get(productRow, "COMPLETED_AT")),
            parameters,
            heads);

        return Task.FromResult<ST_PRODUCT_DATA?>(product);
    }

    public Task SaveActive(
        ST_PRODUCT_DATA product,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = new List<IReadOnlyDictionary<string, string>>
        {
            ToProductRow(product)
        };

        rows.AddRange(product.Parameters
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => ToParameterRow(product.ProductId, item.Key, item.Value)));
        rows.AddRange(product.Heads
            .OrderBy(head => head.HeadNo)
            .Select(head => ToHeadRow(product.ProductId, head)));

        CCsvParser.Write(GetActiveProductPath(), ActiveHeaders, rows);
        return Task.CompletedTask;
    }

    public Task ClearActive(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfExists(GetActiveProductPath());
        return Task.CompletedTask;
    }

    public Task AppendHistory(
        ST_PRODUCT_HISTORY history,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppendRow(GetHistoryPath(history.OccurredAt), HistoryHeaders, ToHistoryRow(history));
        return Task.CompletedTask;
    }

    public Task AppendHeadResults(
        ST_PRODUCT_DATA product,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetHistoryPath(DateTimeOffset.Now);
        foreach (var head in product.Heads.OrderBy(head => head.HeadNo))
        {
            AppendRow(path, HistoryHeaders, ToHeadResultRow(product, head));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ST_PRODUCT_HISTORY>> LoadHistory(
        int maxRows = 100,
        int days = 14,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var today = DateTime.Today;
        var histories = Enumerable.Range(0, Math.Max(1, days))
            .Select(offset => today.AddDays(-offset))
            .Select(date => GetHistoryPath(new DateTimeOffset(date)))
            .Where(System.IO.File.Exists)
            .SelectMany(CCsvParser.Read)
            .Select(ParseHistory)
            .OrderByDescending(history => history.OccurredAt)
            .Take(Math.Max(1, maxRows))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ST_PRODUCT_HISTORY>>(histories);
    }

    private string GetActiveProductPath()
    {
        return Path.Combine(_productRoot, "ActiveProduct.csv");
    }

    private string GetHistoryPath(DateTimeOffset timestamp)
    {
        return Path.Combine(
            _productRoot,
            "History",
            timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            $"ProductHistory_{timestamp:yyyyMMdd}.csv");
    }

    private static IReadOnlyDictionary<string, string> ToProductRow(ST_PRODUCT_DATA product)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ROW_TYPE"] = "PRODUCT",
            ["PRODUCT_ID"] = product.ProductId,
            ["PANEL_ID"] = product.PanelId,
            ["LOT_ID"] = product.LotId,
            ["PROCESS_ID"] = product.ProcessId,
            ["RECIPE_ID"] = product.RecipeId,
            ["STATE"] = product.State.ToString().ToUpperInvariant(),
            ["RESULT"] = product.Result.ToString().ToUpperInvariant(),
            ["CREATED_AT"] = FormatDate(product.CreatedAt),
            ["STARTED_AT"] = FormatDate(product.StartedAt),
            ["COMPLETED_AT"] = FormatDate(product.CompletedAt)
        };
    }

    private static IReadOnlyDictionary<string, string> ToParameterRow(
        string productId,
        string name,
        string value)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ROW_TYPE"] = "PARAM",
            ["PRODUCT_ID"] = productId,
            ["NAME"] = name,
            ["VALUE"] = value
        };
    }

    private static IReadOnlyDictionary<string, string> ToHeadRow(
        string productId,
        ST_PRODUCT_HEAD_RESULT head)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ROW_TYPE"] = "HEAD",
            ["PRODUCT_ID"] = productId,
            ["HEAD_NO"] = head.HeadNo.ToString(CultureInfo.InvariantCulture),
            ["HEAD_STATE"] = head.State.ToString().ToUpperInvariant(),
            ["TOTAL_POINTS"] = head.TotalPoints.ToString(CultureInfo.InvariantCulture),
            ["COMPLETED_POINTS"] = head.CompletedPoints.ToString(CultureInfo.InvariantCulture),
            ["HEAD_RESULT"] = head.Result.ToString().ToUpperInvariant(),
            ["ERROR_CODE"] = head.ErrorCode,
            ["MESSAGE"] = head.Message,
            ["HEAD_STARTED_AT"] = FormatDate(head.StartedAt),
            ["HEAD_COMPLETED_AT"] = FormatDate(head.CompletedAt)
        };
    }

    private static IReadOnlyDictionary<string, string> ToHistoryRow(ST_PRODUCT_HISTORY history)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OCCURRED_AT"] = FormatDate(history.OccurredAt),
            ["ROW_TYPE"] = "EVENT",
            ["PRODUCT_ID"] = history.ProductId,
            ["PROCESS_ID"] = history.ProcessId,
            ["RECIPE_ID"] = history.RecipeId,
            ["ACTION"] = history.Action,
            ["STATE"] = history.State,
            ["RESULT"] = history.Result,
            ["DETAIL"] = history.Detail
        };
    }

    private static IReadOnlyDictionary<string, string> ToHeadResultRow(
        ST_PRODUCT_DATA product,
        ST_PRODUCT_HEAD_RESULT head)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OCCURRED_AT"] = FormatDate(DateTimeOffset.Now),
            ["ROW_TYPE"] = "HEAD_RESULT",
            ["PRODUCT_ID"] = product.ProductId,
            ["PROCESS_ID"] = product.ProcessId,
            ["RECIPE_ID"] = product.RecipeId,
            ["ACTION"] = "HEAD RESULT",
            ["STATE"] = product.State.ToString().ToUpperInvariant(),
            ["RESULT"] = product.Result.ToString().ToUpperInvariant(),
            ["DETAIL"] = $"Head {head.HeadNo:00}: {head.Result}",
            ["HEAD_NO"] = head.HeadNo.ToString(CultureInfo.InvariantCulture),
            ["HEAD_STATE"] = head.State.ToString().ToUpperInvariant(),
            ["TOTAL_POINTS"] = head.TotalPoints.ToString(CultureInfo.InvariantCulture),
            ["COMPLETED_POINTS"] = head.CompletedPoints.ToString(CultureInfo.InvariantCulture),
            ["HEAD_RESULT"] = head.Result.ToString().ToUpperInvariant(),
            ["ERROR_CODE"] = head.ErrorCode,
            ["MESSAGE"] = head.Message,
            ["HEAD_STARTED_AT"] = FormatDate(head.StartedAt),
            ["HEAD_COMPLETED_AT"] = FormatDate(head.CompletedAt)
        };
    }

    private static ST_PRODUCT_HEAD_RESULT ParseHead(IReadOnlyDictionary<string, string> row)
    {
        return new ST_PRODUCT_HEAD_RESULT(
            ReadInt(row, "HEAD_NO"),
            ParseEnum(CCsvParser.Get(row, "HEAD_STATE"), EN_PRODUCT_HEAD_STATE.Ready),
            ReadInt(row, "TOTAL_POINTS"),
            ReadInt(row, "COMPLETED_POINTS"),
            ParseEnum(CCsvParser.Get(row, "HEAD_RESULT"), EN_PRODUCT_RESULT.Pending),
            CCsvParser.Get(row, "ERROR_CODE"),
            CCsvParser.Get(row, "MESSAGE"),
            ParseDate(CCsvParser.Get(row, "HEAD_STARTED_AT")),
            ParseDate(CCsvParser.Get(row, "HEAD_COMPLETED_AT")));
    }

    private static ST_PRODUCT_HISTORY ParseHistory(IReadOnlyDictionary<string, string> row)
    {
        return new ST_PRODUCT_HISTORY(
            ParseDate(CCsvParser.Get(row, "OCCURRED_AT")) ?? DateTimeOffset.Now,
            CCsvParser.Get(row, "PRODUCT_ID"),
            ReadProcessId(row),
            CCsvParser.Get(row, "RECIPE_ID"),
            CCsvParser.Get(row, "ACTION"),
            CCsvParser.Get(row, "STATE"),
            CCsvParser.Get(row, "RESULT"),
            CCsvParser.Get(row, "DETAIL"));
    }

    private static bool IsProductRow(
        IReadOnlyDictionary<string, string> row,
        string productId,
        string rowType)
    {
        return CCsvParser.Get(row, "PRODUCT_ID").Equals(productId, StringComparison.OrdinalIgnoreCase) &&
            CCsvParser.Get(row, "ROW_TYPE").Equals(rowType, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadProcessId(IReadOnlyDictionary<string, string> row)
    {
        var processId = CCsvParser.Get(row, "PROCESS_ID");
        return string.IsNullOrWhiteSpace(processId)
            ? CCsvParser.Get(row, "PRODUCT_ID")
            : processId;
    }

    private static void AppendRow(
        string path,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> row)
    {
        var rows = CCsvParser.Read(path).Append(row).ToArray();
        CCsvParser.Write(path, headers, rows);
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> row,
        string key)
    {
        return int.TryParse(CCsvParser.Get(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static T ParseEnum<T>(
        string value,
        T defaultValue)
        where T : struct, Enum
    {
        return Enum.TryParse<T>(value, true, out var result)
            ? result
            : defaultValue;
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? "";
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var result)
                ? result
                : null;
    }

    private static void DeleteIfExists(string path)
    {
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }
}
