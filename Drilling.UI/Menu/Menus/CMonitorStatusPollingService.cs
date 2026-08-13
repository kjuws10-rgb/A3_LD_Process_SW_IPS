using System.Globalization;
using Drilling.Common.Interface;
using Drilling.Common.Managers;
using Drilling.Common.Motion;

namespace Drilling.UI.Menu.Menus;

internal sealed class CMonitorStatusPollingService(
    IInterfaceManager interfaceManager,
    IMotionManager motionManager)
{
    private const int HeadCount = 8;
    private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ActivePowerMeterMeasureInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SlowSafetyInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommunicationInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMelsecInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MelsecFailureBackoff = TimeSpan.FromSeconds(2);
    private const int MaxMelsecReadPerCycle = 4;

    private readonly object _contextLock = new();
    private readonly object _snapshotLock = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastMelsecPolls = new(StringComparer.OrdinalIgnoreCase);
    private ST_MONITOR_POLLING_CONTEXT _context = ST_MONITOR_POLLING_CONTEXT.Default;
    private ST_MONITOR_STATUS_SNAPSHOT _snapshot = ST_MONITOR_STATUS_SNAPSHOT.Empty;
    private CancellationTokenSource? _stopSource;
    private Task? _pollTask;
    private DateTimeOffset _lastMelsecFailure = DateTimeOffset.MinValue;
    private string _lastMelsecFailureMessage = "";
    private DateTimeOffset _lastCommunicationPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastIoPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMotorPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLaserPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastChillerPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttenuatorPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBetPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPowerMeterPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPicoMotorPoll = DateTimeOffset.MinValue;

    public void Start()
    {
        if (_pollTask is { IsCompleted: false })
        {
            return;
        }

        _stopSource?.Dispose();
        _stopSource = new CancellationTokenSource();
        _pollTask = Task.Run(
            () => PollLoop(_stopSource.Token),
            CancellationToken.None);
    }

    public void UpdateContext(
        string selectedTab,
        int laserNumber,
        int attenuatorNumber,
        int betNumber,
        string melsecGroup)
    {
        var nextContext = new ST_MONITOR_POLLING_CONTEXT(
            NormalizeTab(selectedTab),
            Math.Clamp(laserNumber, 0, HeadCount - 1),
            Math.Clamp(attenuatorNumber, 0, HeadCount - 1),
            Math.Clamp(betNumber, 0, HeadCount - 1),
            NormalizeMelsecGroup(melsecGroup));

        lock (_contextLock)
        {
            _context = nextContext;
        }
    }

    public ST_MONITOR_STATUS_SNAPSHOT GetSnapshot()
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
                await PollDue(cancellationToken).ConfigureAwait(false);
                await Task.Delay(LoopInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollDue(CancellationToken cancellationToken)
    {
        if (!await _pollLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var context = GetContext();
            var now = DateTimeOffset.Now;

            if (IsDue(ref _lastCommunicationPoll, now, CommunicationInterval))
            {
                await TryPoll(PollCommunication, cancellationToken).ConfigureAwait(false);
            }

            switch (context.SelectedTab)
            {
                case "IO" when IsDue(ref _lastIoPoll, now, ActiveInterval):
                    await TryPoll(PollIo, cancellationToken).ConfigureAwait(false);
                    break;
                case "MOTOR" when IsDue(ref _lastMotorPoll, now, ActiveInterval):
                    await TryPoll(PollMotor, cancellationToken).ConfigureAwait(false);
                    break;
                case "LASER" when IsDue(ref _lastLaserPoll, now, ActiveInterval):
                    await TryPoll(token => PollLaser(context.LaserNumber, token), cancellationToken).ConfigureAwait(false);
                    break;
                case "CHILLER" when IsDue(ref _lastChillerPoll, now, ActiveInterval):
                    await TryPoll(PollChiller, cancellationToken).ConfigureAwait(false);
                    break;
                case "ATTENUATOR" when IsDue(ref _lastAttenuatorPoll, now, ActiveInterval):
                    await TryPoll(token => PollAttenuator(context.AttenuatorNumber, token), cancellationToken).ConfigureAwait(false);
                    break;
                case "BET" when IsDue(ref _lastBetPoll, now, ActiveInterval):
                    await TryPoll(token => PollBet(context.BetNumber, token), cancellationToken).ConfigureAwait(false);
                    break;
                case "POWER METER" when IsDue(ref _lastPowerMeterPoll, now, GetPowerMeterInterval()):
                    await TryPoll(PollPowerMeter, cancellationToken).ConfigureAwait(false);
                    break;
                case "PICO MOTOR" when IsDue(ref _lastPicoMotorPoll, now, ActiveInterval):
                    await TryPoll(PollPicoMotor, cancellationToken).ConfigureAwait(false);
                    break;
                case "MELSEC":
                    await TryPoll(token => PollMelsec(context.MelsecGroup, token), cancellationToken).ConfigureAwait(false);
                    break;
            }

            if (context.SelectedTab != "CHILLER" &&
                IsDue(ref _lastChillerPoll, now, SlowSafetyInterval))
            {
                await TryPoll(PollChiller, cancellationToken).ConfigureAwait(false);
            }

            if (context.SelectedTab != "POWER METER" &&
                IsDue(ref _lastPowerMeterPoll, now, SlowSafetyInterval))
            {
                await TryPoll(PollPowerMeter, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async Task TryPoll(
        Func<CancellationToken, Task> poll,
        CancellationToken cancellationToken)
    {
        try
        {
            await poll(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async Task PollCommunication(CancellationToken cancellationToken)
    {
        var communication = await interfaceManager.GetCommunicationStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with { Communication = communication });
    }

    private async Task PollIo(CancellationToken cancellationToken)
    {
        var io = await motionManager.GetIoStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Io = io }
        });
    }

    private async Task PollMotor(CancellationToken cancellationToken)
    {
        var motors = await motionManager.GetAxisStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Motors = motors }
        });
    }

    private async Task PollLaser(
        int laserNumber,
        CancellationToken cancellationToken)
    {
        var talonStatus = await interfaceManager.RefreshTalonLaserStatus(laserNumber, cancellationToken)
            .ConfigureAwait(false);
        var laserStatus = new ST_LASER_STATUS(
            talonStatus.LaserOn,
            talonStatus.ShutterOpen,
            talonStatus.GateOpen,
            talonStatus.OutputPower);

        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Laser = laserStatus },
            TalonStatus = talonStatus
        });
    }

    private async Task PollChiller(CancellationToken cancellationToken)
    {
        var chiller = await interfaceManager.GetChillerStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Chiller = chiller }
        });
    }

    private async Task PollAttenuator(
        int attenuatorNumber,
        CancellationToken cancellationToken)
    {
        var attenuator = await interfaceManager.GetAttenuatorStatus(attenuatorNumber, cancellationToken)
            .ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Attenuator = attenuator }
        });
    }

    private async Task PollBet(
        int betNumber,
        CancellationToken cancellationToken)
    {
        var bet = await interfaceManager.GetBETStatus(betNumber, cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { Bet = bet }
        });
    }

    private async Task PollPowerMeter(CancellationToken cancellationToken)
    {
        var powerMeter = await interfaceManager.GetPowerMeterStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with
        {
            DeviceStatus = snapshot.DeviceStatus with { PowerMeter = powerMeter }
        });
    }

    private async Task PollPicoMotor(CancellationToken cancellationToken)
    {
        var picoMotor = await interfaceManager.GetPicoMotorStatus(cancellationToken).ConfigureAwait(false);
        UpdateSnapshot(snapshot => snapshot with { PicoMotorStatus = picoMotor });
    }

    private async Task PollMelsec(
        string selectedGroup,
        CancellationToken cancellationToken)
    {
        var mapRows = selectedGroup.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? interfaceManager.Melsec.Map
            : interfaceManager.Melsec.GetMapList(selectedGroup);
        var readRows = mapRows
            .Where(row => row.Access != EN_MELSEC_ACCESS.Write)
            .ToArray();
        var now = DateTimeOffset.Now;

        if (now - _lastMelsecFailure < MelsecFailureBackoff)
        {
            UpdateMelsecValues(
                readRows,
                "ERR",
                "ERROR",
                _lastMelsecFailureMessage,
                now);
            return;
        }

        var dueRows = mapRows
            .Where(row => row.Access != EN_MELSEC_ACCESS.Write)
            .OrderBy(row => row.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .Where(row => IsMelsecDue(row, now))
            .Take(MaxMelsecReadPerCycle)
            .ToArray();

        foreach (var row in dueRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var value = await ReadMelsecValue(row, cancellationToken).ConfigureAwait(false);
                UpdateMelsecValue(new ST_MONITOR_MELSEC_VALUE(
                    row.Id,
                    value,
                    "OK",
                    "",
                    DateTimeOffset.Now));
                _lastMelsecFailure = DateTimeOffset.MinValue;
                _lastMelsecFailureMessage = "";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var failureTime = DateTimeOffset.Now;
                _lastMelsecFailure = failureTime;
                _lastMelsecFailureMessage = exception.Message;
                UpdateMelsecValue(new ST_MONITOR_MELSEC_VALUE(
                    row.Id,
                    "ERR",
                    "ERROR",
                    exception.Message,
                    failureTime));
                UpdateMelsecValues(
                    readRows.Where(readRow => !readRow.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase)),
                    "ERR",
                    "ERROR",
                    exception.Message,
                    failureTime);
                break;
            }
        }
    }

    private async Task<string> ReadMelsecValue(
        ST_MELSEC_MAP_DATA row,
        CancellationToken cancellationToken)
    {
        return row.DataType switch
        {
            EN_MELSEC_DATA_TYPE.Bit => (await interfaceManager.Melsec.ReadBit(row.Id, cancellationToken).ConfigureAwait(false)).ToString().ToUpperInvariant(),
            EN_MELSEC_DATA_TYPE.Word or EN_MELSEC_DATA_TYPE.DWord => (await interfaceManager.Melsec.ReadWord(row.Id, cancellationToken).ConfigureAwait(false)).ToString(CultureInfo.InvariantCulture),
            EN_MELSEC_DATA_TYPE.Double or EN_MELSEC_DATA_TYPE.Float => (await interfaceManager.Melsec.ReadDouble(row.Id, cancellationToken).ConfigureAwait(false)).ToString("F3", CultureInfo.InvariantCulture),
            EN_MELSEC_DATA_TYPE.String => await interfaceManager.Melsec.ReadString(row.Id, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported MELSEC read type: {row.DataType}")
        };
    }

    private ST_MONITOR_POLLING_CONTEXT GetContext()
    {
        lock (_contextLock)
        {
            return _context;
        }
    }

    private TimeSpan GetPowerMeterInterval()
    {
        var snapshot = GetSnapshot();
        return snapshot.DeviceStatus.PowerMeter.IsMeasuring
            ? ActivePowerMeterMeasureInterval
            : ActiveInterval;
    }

    private void UpdateSnapshot(Func<ST_MONITOR_STATUS_SNAPSHOT, ST_MONITOR_STATUS_SNAPSHOT> update)
    {
        lock (_snapshotLock)
        {
            _snapshot = update(_snapshot);
        }
    }

    private void UpdateMelsecValue(ST_MONITOR_MELSEC_VALUE value)
    {
        lock (_snapshotLock)
        {
            var values = new Dictionary<string, ST_MONITOR_MELSEC_VALUE>(
                _snapshot.MelsecValues,
                StringComparer.OrdinalIgnoreCase)
            {
                [value.Id] = value
            };

            _snapshot = _snapshot with { MelsecValues = values };
        }
    }

    private void UpdateMelsecValues(
        IEnumerable<ST_MELSEC_MAP_DATA> rows,
        string value,
        string state,
        string message,
        DateTimeOffset updatedAt)
    {
        lock (_snapshotLock)
        {
            var values = new Dictionary<string, ST_MONITOR_MELSEC_VALUE>(
                _snapshot.MelsecValues,
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                values[row.Id] = new ST_MONITOR_MELSEC_VALUE(
                    row.Id,
                    value,
                    state,
                    message,
                    updatedAt);
            }

            _snapshot = _snapshot with { MelsecValues = values };
        }
    }

    private bool IsMelsecDue(
        ST_MELSEC_MAP_DATA row,
        DateTimeOffset now)
    {
        var interval = row.PollMs > 0
            ? TimeSpan.FromMilliseconds(row.PollMs)
            : DefaultMelsecInterval;

        if (_lastMelsecPolls.TryGetValue(row.Id, out var lastPoll) &&
            now - lastPoll < interval)
        {
            return false;
        }

        _lastMelsecPolls[row.Id] = now;
        return true;
    }

    private static bool IsDue(
        ref DateTimeOffset lastPoll,
        DateTimeOffset now,
        TimeSpan interval)
    {
        if (now - lastPoll < interval)
        {
            return false;
        }

        lastPoll = now;
        return true;
    }

    private static string NormalizeTab(string tab)
    {
        return string.IsNullOrWhiteSpace(tab) ? "IO" : tab.Trim().ToUpperInvariant();
    }

    private static string NormalizeMelsecGroup(string group)
    {
        return string.IsNullOrWhiteSpace(group) ? "ALL" : group.Trim().ToUpperInvariant();
    }
}

internal sealed record ST_MONITOR_POLLING_CONTEXT(
    string SelectedTab,
    int LaserNumber,
    int AttenuatorNumber,
    int BetNumber,
    string MelsecGroup)
{
    public static ST_MONITOR_POLLING_CONTEXT Default { get; } = new("IO", 0, 0, 0, "ALL");
}

internal sealed record ST_MONITOR_STATUS_SNAPSHOT(
    ST_DEVICE_STATUS DeviceStatus,
    IReadOnlyList<ST_DEVICE_COMM_STATUS> Communication,
    ST_TALON_STATUS TalonStatus,
    ST_PICO_MOTOR_STATUS PicoMotorStatus,
    IReadOnlyDictionary<string, ST_MONITOR_MELSEC_VALUE> MelsecValues)
{
    public static ST_MONITOR_STATUS_SNAPSHOT Empty { get; } = new(
        new ST_DEVICE_STATUS(
            [],
            [],
            new ST_LASER_STATUS(false, false, false, 0.0),
            new ST_CHILLER_STATUS(false, 0.0, 0.0, 0.0, false),
            new ST_ATTENUATOR_STATUS(0.0, 0.0, "IDLE", false),
            new ST_BET_STATUS(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, false, false, false, false, false),
            ST_POWER_METER_STATUS.Empty),
        [],
        ST_TALON_STATUS.Empty,
        ST_PICO_MOTOR_STATUS.Empty,
        new Dictionary<string, ST_MONITOR_MELSEC_VALUE>(StringComparer.OrdinalIgnoreCase));
}

internal sealed record ST_MONITOR_MELSEC_VALUE(
    string Id,
    string Value,
    string State,
    string Message,
    DateTimeOffset UpdatedAt);
