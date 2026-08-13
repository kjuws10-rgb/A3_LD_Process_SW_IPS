using Drilling.Common.Log;

namespace Drilling.Common.Product;

public enum EN_PRODUCT_STATE
{
    Created,
    Running,
    Completed,
    Stopped,
    Error,
    Scrapped
}

public enum EN_PRODUCT_RESULT
{
    Pending,
    OK,
    NG
}

public enum EN_PRODUCT_HEAD_STATE
{
    Ready,
    Running,
    Completed,
    Error,
    Disabled
}

public sealed record ST_PRODUCT_HEAD_RESULT(
    int HeadNo,
    EN_PRODUCT_HEAD_STATE State,
    int TotalPoints,
    int CompletedPoints,
    EN_PRODUCT_RESULT Result,
    string ErrorCode,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ST_PRODUCT_DATA(
    string ProductId,
    string PanelId,
    string LotId,
    string ProcessId,
    string RecipeId,
    EN_PRODUCT_STATE State,
    EN_PRODUCT_RESULT Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<ST_PRODUCT_HEAD_RESULT> Heads);

public sealed record ST_PRODUCT_HISTORY(
    DateTimeOffset OccurredAt,
    string ProductId,
    string ProcessId,
    string RecipeId,
    string Action,
    string State,
    string Result,
    string Detail);

public interface IProductFile
{
    Task<ST_PRODUCT_DATA?> LoadActive(CancellationToken cancellationToken = default);

    Task SaveActive(
        ST_PRODUCT_DATA product,
        CancellationToken cancellationToken = default);

    Task ClearActive(CancellationToken cancellationToken = default);

    Task AppendHistory(
        ST_PRODUCT_HISTORY history,
        CancellationToken cancellationToken = default);

    Task AppendHeadResults(
        ST_PRODUCT_DATA product,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_PRODUCT_HISTORY>> LoadHistory(
        int maxRows = 100,
        int days = 14,
        CancellationToken cancellationToken = default);
}

public interface IProductManager
{
    Task<ST_PRODUCT_DATA?> LoadActive(CancellationToken cancellationToken = default);

    ST_PRODUCT_DATA? Current { get; }

    Task<ST_PRODUCT_DATA> CreateProduct(
        string processId,
        string productId,
        string panelId,
        string lotId,
        string recipeId,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<int, int> headPointCounts,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> StartProduct(
        string productId,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> SetHeadRunning(
        string productId,
        int headNo,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> SetHeadResult(
        string productId,
        int headNo,
        bool isOk,
        string errorCode = "",
        string message = "",
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> CompleteProduct(
        string productId,
        bool isOk,
        string message,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> StopProduct(
        string productId,
        string message,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> SetError(
        string productId,
        string message,
        CancellationToken cancellationToken = default);

    Task<ST_PRODUCT_DATA> ScrapProduct(
        string productId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_PRODUCT_HISTORY>> LoadHistory(
        int maxRows = 100,
        int days = 14,
        CancellationToken cancellationToken = default);
}

public sealed class CProductManager(
    IProductFile productFile,
    ILogManager? logManager = null) : IProductManager
{
    private ST_PRODUCT_DATA? _current;

    public ST_PRODUCT_DATA? Current => _current;

    public async Task<ST_PRODUCT_DATA?> LoadActive(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _current = await productFile.LoadActive(cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> CreateProduct(
        string processId,
        string productId,
        string panelId,
        string lotId,
        string recipeId,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<int, int> headPointCounts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedProductId = ChooseProductId(processId, productId, panelId, parameters);
        var now = DateTimeOffset.Now;
        var heads = headPointCounts
            .OrderBy(item => item.Key)
            .Select(item => new ST_PRODUCT_HEAD_RESULT(
                item.Key,
                EN_PRODUCT_HEAD_STATE.Ready,
                Math.Max(0, item.Value),
                0,
                EN_PRODUCT_RESULT.Pending,
                "",
                "",
                null,
                null))
            .ToArray();

        _current = new ST_PRODUCT_DATA(
            resolvedProductId,
            panelId,
            lotId,
            processId,
            recipeId,
            EN_PRODUCT_STATE.Created,
            EN_PRODUCT_RESULT.Pending,
            now,
            null,
            null,
            new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
            heads);

        await SaveAndLog("CREATE", "Product created.", cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> StartProduct(
        string productId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        var now = DateTimeOffset.Now;
        _current = product with
        {
            State = EN_PRODUCT_STATE.Running,
            StartedAt = product.StartedAt ?? now,
            Result = EN_PRODUCT_RESULT.Pending
        };

        await SaveAndLog("START", "Product started.", cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> SetHeadRunning(
        string productId,
        int headNo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        var now = DateTimeOffset.Now;
        _current = product with
        {
            Heads = product.Heads
                .Select(head => head.HeadNo == headNo
                    ? head with
                    {
                        State = EN_PRODUCT_HEAD_STATE.Running,
                        StartedAt = head.StartedAt ?? now
                    }
                    : head)
                .ToArray()
        };

        await SaveAndLog("HEAD START", $"Head {headNo:00} started.", cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> SetHeadResult(
        string productId,
        int headNo,
        bool isOk,
        string errorCode = "",
        string message = "",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        var now = DateTimeOffset.Now;
        _current = product with
        {
            Heads = product.Heads
                .Select(head => head.HeadNo == headNo
                    ? head with
                    {
                        State = isOk ? EN_PRODUCT_HEAD_STATE.Completed : EN_PRODUCT_HEAD_STATE.Error,
                        CompletedPoints = isOk ? head.TotalPoints : head.CompletedPoints,
                        Result = isOk ? EN_PRODUCT_RESULT.OK : EN_PRODUCT_RESULT.NG,
                        ErrorCode = errorCode,
                        Message = message,
                        StartedAt = head.StartedAt ?? now,
                        CompletedAt = now
                    }
                    : head)
                .ToArray()
        };

        await SaveAndLog(
            isOk ? "HEAD COMPLETE" : "HEAD ERROR",
            $"Head {headNo:00}: {(isOk ? "OK" : "NG")} {message}".Trim(),
            cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> CompleteProduct(
        string productId,
        bool isOk,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        _current = product with
        {
            State = EN_PRODUCT_STATE.Completed,
            Result = isOk ? EN_PRODUCT_RESULT.OK : EN_PRODUCT_RESULT.NG,
            CompletedAt = DateTimeOffset.Now
        };

        await SaveAndLog("COMPLETE", message, cancellationToken);
        await productFile.AppendHeadResults(_current, cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> StopProduct(
        string productId,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        _current = product with
        {
            State = EN_PRODUCT_STATE.Stopped,
            Result = EN_PRODUCT_RESULT.NG,
            CompletedAt = DateTimeOffset.Now
        };

        await SaveAndLog("STOP", message, cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> SetError(
        string productId,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        _current = product with
        {
            State = EN_PRODUCT_STATE.Error,
            Result = EN_PRODUCT_RESULT.NG,
            CompletedAt = DateTimeOffset.Now
        };

        await SaveAndLog("ERROR", message, cancellationToken);
        return _current;
    }

    public async Task<ST_PRODUCT_DATA> ScrapProduct(
        string productId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = GetCurrent(productId);
        _current = product with
        {
            State = EN_PRODUCT_STATE.Scrapped,
            Result = EN_PRODUCT_RESULT.NG,
            CompletedAt = DateTimeOffset.Now
        };

        await SaveAndLog("SCRAP", reason, cancellationToken);
        return _current;
    }

    public Task<IReadOnlyList<ST_PRODUCT_HISTORY>> LoadHistory(
        int maxRows = 100,
        int days = 14,
        CancellationToken cancellationToken = default)
    {
        return productFile.LoadHistory(maxRows, days, cancellationToken);
    }

    private ST_PRODUCT_DATA GetCurrent(string productId)
    {
        if (_current is null)
        {
            throw new InvalidOperationException("Product is not loaded.");
        }

        if (!_current.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Product ID mismatch. Current={_current.ProductId}, Input={productId}");
        }

        return _current;
    }

    private async Task SaveAndLog(
        string action,
        string detail,
        CancellationToken cancellationToken)
    {
        if (_current is null)
        {
            return;
        }

        await productFile.SaveActive(_current, cancellationToken);

        var history = new ST_PRODUCT_HISTORY(
            DateTimeOffset.Now,
            _current.ProductId,
            _current.ProcessId,
            _current.RecipeId,
            action,
            _current.State.ToString().ToUpperInvariant(),
            _current.Result.ToString().ToUpperInvariant(),
            detail);

        await productFile.AppendHistory(history, cancellationToken);
        logManager?.WriteProductEvent(
            _current.ProductId,
            action,
            _current.State.ToString().ToUpperInvariant(),
            _current.Result.ToString().ToUpperInvariant(),
            detail);
    }

    private static string ChooseProductId(
        string processId,
        string productId,
        string panelId,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!string.IsNullOrWhiteSpace(productId))
        {
            return productId.Trim();
        }

        foreach (var key in new[] { "ProductId", "PRODUCT_ID", "PanelId", "PANEL_ID", "PanelID" })
        {
            if (parameters.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(panelId))
        {
            return panelId.Trim();
        }

        return string.IsNullOrWhiteSpace(processId)
            ? $"PRODUCT_{DateTime.Now:yyyyMMddHHmmssfff}"
            : processId.Trim();
    }
}
