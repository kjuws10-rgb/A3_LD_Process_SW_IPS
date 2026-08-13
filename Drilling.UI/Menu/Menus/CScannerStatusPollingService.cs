using System.Globalization;
using Drilling.Common.Automation;
using Drilling.Common.Managers;

namespace Drilling.UI.Menu.Menus;

internal sealed class CScannerStatusPollingService(
    CAutomationManager automationManager,
    CSettingManager settingManager)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private CancellationTokenSource? _stopSource;
    private Task? _pollTask;
    private IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> _snapshot = [];

    public void Start()
    {
        if (_pollTask is { IsCompleted: false })
        {
            return;
        }

        _stopSource?.Dispose();
        _stopSource = new CancellationTokenSource();
        Task? RunTask1()
        {
            return PollLoop(_stopSource.Token);
        }

        _pollTask = Task.Run(
RunTask1,
            CancellationToken.None);
    }

    public IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> GetSnapshot()
    {
        lock (_snapshotLock)
        {
            return _snapshot;
        }
    }

    private async Task PollLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshSnapshot(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshSnapshot(CancellationToken cancellationToken)
    {
        if (!await _pollLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var settings = await settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken)
                .ConfigureAwait(false);
            var axisDefinitions = BuildAxisDefinitions(settings);
            var items = new List<ST_SCANNER_AXIS_STATUS_ITEM>(axisDefinitions.Count);

            foreach (var axis in axisDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await BuildScannerAxisStatusItem(axis, cancellationToken).ConfigureAwait(false));
            }

            SetSnapshot(items);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetSnapshot(CreatePollingErrorSnapshot(exception.Message));
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task<ST_SCANNER_AXIS_STATUS_ITEM> BuildScannerAxisStatusItem(
        ST_SCANNER_AXIS_SETTING axis,
        CancellationToken cancellationToken)
    {
        var axisNoText = axis.AxisNo.ToString(CultureInfo.InvariantCulture);
        var automationLabel = $"#{axis.AutomationNo}";

        try
        {
            if (!automationManager.IsSimul(axis.AutomationNo) &&
                !automationManager.IsConnect(axis.AutomationNo))
            {
                return new ST_SCANNER_AXIS_STATUS_ITEM(
                    $"H{axis.HeadNo:00}",
                    automationLabel,
                    axis.AxisName,
                    axisNoText,
                    "OFFLINE",
                    "-",
                    "-",
                    "-",
                    "OFFLINE",
                    "Automation not connected.");
            }

            var status = await automationManager.ReadAxisStatus(
                    axisNoText,
                    axis.AutomationNo,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ST_SCANNER_AXIS_STATUS_ITEM(
                $"H{axis.HeadNo:00}",
                automationLabel,
                axis.AxisName,
                axisNoText,
                status.Able ? "ABLE" : "DISABLE",
                FormatScannerPosition(status.PositionFeedback),
                FormatScannerPosition(status.AuxiliaryFeedback),
                status.HomeDone ? "HOME" : "WAIT",
                status.HasError ? "ERROR" : "OK",
                string.IsNullOrWhiteSpace(status.RawResponse) ? "-" : status.RawResponse);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ST_SCANNER_AXIS_STATUS_ITEM(
                $"H{axis.HeadNo:00}",
                automationLabel,
                axis.AxisName,
                axisNoText,
                "ERROR",
                "-",
                "-",
                "-",
                "ERROR",
                exception.Message);
        }
    }

    private static IReadOnlyList<ST_SCANNER_AXIS_SETTING> BuildAxisDefinitions(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings)
    {
        IEnumerable<ST_SCANNER_AXIS_SETTING> SelectHeadNo2(int headNo)
        {
            var automationNo = ReadHeadSettingInt(
                settings,
                headNo <= 4 ? 0 : 1,
                headNo,
                "AUTOMATION_NO");
            var defaultGxAxisNo = ((headNo - 1) % 4) * 2;
            var defaultGyAxisNo = defaultGxAxisNo + 1;

            return new[]
            {
                    new ST_SCANNER_AXIS_SETTING(
                        headNo,
                        automationNo,
                        "GX",
                        ReadHeadSettingInt(settings, defaultGxAxisNo, headNo, "GX_AXIS_NO")),
                    new ST_SCANNER_AXIS_SETTING(
                        headNo,
                        automationNo,
                        "GY",
                        ReadHeadSettingInt(settings, defaultGyAxisNo, headNo, "GY_AXIS_NO"))
                };
        }
        return Enumerable.Range(1, 8)
            .SelectMany(SelectHeadNo2)
            .ToArray();
    }

    private static int ReadHeadSettingInt(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        int defaultValue,
        int headNo,
        params string[] suffixes)
    {
        var prefix = $"H{headNo:00}_";
        foreach (var suffix in suffixes)
        {
            var key = prefix + suffix;
            foreach (var setting in settings)
            {
                if (!key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals(setting.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return defaultValue;
    }

    private static IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> CreatePollingErrorSnapshot(string message)
    {
        return
        [
            new ST_SCANNER_AXIS_STATUS_ITEM(
                "-",
                "-",
                "-",
                "-",
                "ERROR",
                "-",
                "-",
                "-",
                "ERROR",
                string.IsNullOrWhiteSpace(message) ? "Scanner status polling failed." : message)
        ];
    }

    private void SetSnapshot(IReadOnlyList<ST_SCANNER_AXIS_STATUS_ITEM> snapshot)
    {
        lock (_snapshotLock)
        {
            _snapshot = snapshot;
        }
    }

    private static string FormatScannerPosition(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
