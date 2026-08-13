using System.Globalization;
using Drilling.Common.Interface;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Threading;

namespace Drilling.UI.Menu.Menus;

internal sealed class CMonitorStatusPollingService(
    CInterfaceManager interfaceManager,
    CMotionManager motionManager) : CtrlThread
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
    private readonly Dictionary<string, DateTimeOffset> _lastMelsecPolls = new(StringComparer.OrdinalIgnoreCase);
    private ST_MONITOR_POLLING_CONTEXT _context = ST_MONITOR_POLLING_CONTEXT.Default;
    private ST_MONITOR_STATUS_SNAPSHOT _snapshot = ST_MONITOR_STATUS_SNAPSHOT.Empty;
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
        base.Start((int)LoopInterval.TotalMilliseconds, "MonitorStatusPolling");
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

    public override void Run()
    {
        PollDue(CancellationToken.None);
    }

    private void PollDue(CancellationToken cancellationToken)
    {
        var context = GetContext();
        var now = DateTimeOffset.Now;

            if (IsDue(ref _lastCommunicationPoll, now, CommunicationInterval))
            {
                TryPoll(PollCommunication, cancellationToken);
            }

            switch (context.SelectedTab)
            {
                case "IO" when IsDue(ref _lastIoPoll, now, ActiveInterval):
                    TryPoll(PollIo, cancellationToken);
                    break;
                case "MOTOR" when IsDue(ref _lastMotorPoll, now, ActiveInterval):
                    TryPoll(PollMotor, cancellationToken);
                    break;
                case "LASER" when IsDue(ref _lastLaserPoll, now, ActiveInterval):
                    void TryPollTokenCallback2(CancellationToken token)
                    {
                        PollLaser(context.LaserNumber, token);
                    }

                    TryPoll(TryPollTokenCallback2, cancellationToken);
                    break;
                case "CHILLER" when IsDue(ref _lastChillerPoll, now, ActiveInterval):
                    TryPoll(PollChiller, cancellationToken);
                    break;
                case "ATTENUATOR" when IsDue(ref _lastAttenuatorPoll, now, ActiveInterval):
                    void TryPollTokenCallback3(CancellationToken token)
                    {
                        PollAttenuator(context.AttenuatorNumber, token);
                    }

                    TryPoll(TryPollTokenCallback3, cancellationToken);
                    break;
                case "BET" when IsDue(ref _lastBetPoll, now, ActiveInterval):
                    void TryPollTokenCallback4(CancellationToken token)
                    {
                        PollBet(context.BetNumber, token);
                    }

                    TryPoll(TryPollTokenCallback4, cancellationToken);
                    break;
                case "POWER METER" when IsDue(ref _lastPowerMeterPoll, now, GetPowerMeterInterval()):
                    TryPoll(PollPowerMeter, cancellationToken);
                    break;
                case "PICO MOTOR" when IsDue(ref _lastPicoMotorPoll, now, ActiveInterval):
                    TryPoll(PollPicoMotor, cancellationToken);
                    break;
                case "MELSEC":
                    void TryPollTokenCallback5(CancellationToken token)
                    {
                        PollMelsec(context.MelsecGroup, token);
                    }

                    TryPoll(TryPollTokenCallback5, cancellationToken);
                    break;
            }

            if (context.SelectedTab != "CHILLER" &&
                IsDue(ref _lastChillerPoll, now, SlowSafetyInterval))
            {
                TryPoll(PollChiller, cancellationToken);
            }

            if (context.SelectedTab != "POWER METER" &&
                IsDue(ref _lastPowerMeterPoll, now, SlowSafetyInterval))
            {
                TryPoll(PollPowerMeter, cancellationToken);
            }
    }

    private void TryPoll(
        Action<CancellationToken> poll,
        CancellationToken cancellationToken)
    {
        try
        {
            poll(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("Monitor polling failed: " + exception);
        }
    }

    private void PollCommunication(CancellationToken cancellationToken)
    {
        var communication = interfaceManager.GetCommunicationStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback6(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with { Communication = communication };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback6);
    }

    private void PollIo(CancellationToken cancellationToken)
    {
        var io = motionManager.GetIoStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback7(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Io = io }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback7);
    }

    private void PollMotor(CancellationToken cancellationToken)
    {
        var motors = motionManager.GetAxisStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback8(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Motors = motors }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback8);
    }

    private void PollLaser(
        int laserNumber,
        CancellationToken cancellationToken)
    {
        var talonStatus = interfaceManager.RefreshTalonLaserStatus(laserNumber, cancellationToken)
            ;
        var laserStatus = new ST_LASER_STATUS(
            talonStatus.LaserOn,
            talonStatus.ShutterOpen,
            talonStatus.GateOpen,
            talonStatus.OutputPower);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback9(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Laser = laserStatus },
                TalonStatus = talonStatus
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback9);
    }

    private void PollChiller(CancellationToken cancellationToken)
    {
        var chiller = interfaceManager.GetChillerStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback10(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Chiller = chiller }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback10);
    }

    private void PollAttenuator(
        int attenuatorNumber,
        CancellationToken cancellationToken)
    {
        var attenuator = interfaceManager.GetAttenuatorStatus(attenuatorNumber, cancellationToken)
            ;
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback11(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Attenuator = attenuator }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback11);
    }

    private void PollBet(
        int betNumber,
        CancellationToken cancellationToken)
    {
        var bet = interfaceManager.GetBETStatus(betNumber, cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback12(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { Bet = bet }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback12);
    }

    private void PollPowerMeter(CancellationToken cancellationToken)
    {
        var powerMeter = interfaceManager.GetPowerMeterStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback13(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with
            {
                DeviceStatus = snapshot.DeviceStatus with { PowerMeter = powerMeter }
            };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback13);
    }

    private void PollPicoMotor(CancellationToken cancellationToken)
    {
        var picoMotor = interfaceManager.GetPicoMotorStatus(cancellationToken);
        ST_MONITOR_STATUS_SNAPSHOT UpdateSnapshotSnapshotCallback14(ST_MONITOR_STATUS_SNAPSHOT snapshot)
        {
            return snapshot with { PicoMotorStatus = picoMotor };
        }

        UpdateSnapshot(UpdateSnapshotSnapshotCallback14);
    }

    private void PollMelsec(
        string selectedGroup,
        CancellationToken cancellationToken)
    {
        var mapRows = selectedGroup.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? interfaceManager.Melsec.Map
            : interfaceManager.Melsec.GetMapList(selectedGroup);
        bool FilterRow15(ST_MELSEC_MAP_DATA row)
        {
            return row.Access != EN_MELSEC_ACCESS.Write;
        }

        var readRows = mapRows
            .Where(FilterRow15)
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
        bool FilterRow16(ST_MELSEC_MAP_DATA row)
        {
            return row.Access != EN_MELSEC_ACCESS.Write;
        }

        string GetRowSortKey17(ST_MELSEC_MAP_DATA row)
        {
            return row.Group;
        }

        string GetRowSortKey18(ST_MELSEC_MAP_DATA row)
        {
            return row.Id;
        }

        bool FilterRow19(ST_MELSEC_MAP_DATA row)
        {
            return IsMelsecDue(row, now);
        }

        var dueRows = mapRows
            .Where(FilterRow16)
            .OrderBy(GetRowSortKey17, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetRowSortKey18, StringComparer.OrdinalIgnoreCase)
            .Where(FilterRow19)
            .Take(MaxMelsecReadPerCycle)
            .ToArray();

        foreach (var row in dueRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var value = ReadMelsecValue(row, cancellationToken);
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
                bool FilterReadRow20(ST_MELSEC_MAP_DATA readRow)
                {
                    return !readRow.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase);
                }

                UpdateMelsecValues(
                    readRows.Where(FilterReadRow20),
                    "ERR",
                    "ERROR",
                    exception.Message,
                    failureTime);
                break;
            }
        }
    }

    private string ReadMelsecValue(
        ST_MELSEC_MAP_DATA row,
        CancellationToken cancellationToken)
    {
        switch (row.DataType)
        {
            case EN_MELSEC_DATA_TYPE.Bit:
                return (interfaceManager.Melsec.ReadBit(row.Id, cancellationToken))
                    .ToString()
                    .ToUpperInvariant();
            case EN_MELSEC_DATA_TYPE.Word:
            case EN_MELSEC_DATA_TYPE.DWord:
                return (interfaceManager.Melsec.ReadWord(row.Id, cancellationToken))
                    .ToString(CultureInfo.InvariantCulture);
            case EN_MELSEC_DATA_TYPE.Double:
            case EN_MELSEC_DATA_TYPE.Float:
                return (interfaceManager.Melsec.ReadDouble(row.Id, cancellationToken))
                    .ToString("F3", CultureInfo.InvariantCulture);
            case EN_MELSEC_DATA_TYPE.String:
                return interfaceManager.Melsec.ReadString(row.Id, cancellationToken);
            default:
                throw new InvalidOperationException($"Unsupported MELSEC read type: {row.DataType}");
        }
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
