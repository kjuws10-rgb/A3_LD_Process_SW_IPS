using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.InterLock;

[Flags]
public enum EN_INTERLOCK_TARGET
{
    None = 0,
    AutoRun = 1,
    ManualMove = 2,
    LaserOn = 4
}

public enum EN_INTERLOCK_LEVEL
{
    Ok,
    Warn,
    Error
}

public sealed record ST_INTERLOCK_ITEM(
    string Signal,
    EN_INTERLOCK_LEVEL Level,
    string State,
    string Detail);

public sealed record ST_INTERLOCK_RULE(
    string Signal,
    string IoId,
    bool ExpectedOn,
    string OkState,
    string NgDetail,
    EN_INTERLOCK_LEVEL NgLevel,
    EN_INTERLOCK_TARGET Targets);

public sealed record ST_INTERLOCK_SUMMARY(
    bool CanAutoRun,
    bool CanManualMove,
    bool CanLaserOn,
    bool HasError,
    IReadOnlyList<ST_INTERLOCK_ITEM> Items);

public sealed class CInterLockManager
{
    private static readonly IReadOnlyList<ST_INTERLOCK_RULE> Rules =
    [
        Rule("Door", "DOOR_LOCK_SENSOR", true, "Closed", "Door is open", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.ManualMove | EN_INTERLOCK_TARGET.LaserOn),
        Rule("EMS", "EMS", true, "Normal", "Emergency stop is active", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.ManualMove | EN_INTERLOCK_TARGET.LaserOn),
        Rule("Key Switch", "KEY_SWITCH_AUTO", true, "AUTO", "Key switch is not AUTO", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.AutoRun),
        Rule("Laser Shutter", "LASER_SHUTTER_CLOSED", true, "SAFE", "Laser shutter is not closed", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.LaserOn),
        Rule("Chiller Alarm", "CHILLER_ALARM", false, "OK", "Chiller alarm input is active", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.LaserOn),
        Rule("Leak Sensor", "LEAK_SENSOR", true, "OK", "Leak sensor is active", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.LaserOn),
        Rule("Smoke Temp", "SMOKE_TEMP", true, "OK", "Smoke or over temperature input is active", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.LaserOn),
        Rule("PM Lock", "PM_LOCK", true, "READY", "PM lock is active", EN_INTERLOCK_LEVEL.Error, EN_INTERLOCK_TARGET.AutoRun | EN_INTERLOCK_TARGET.ManualMove | EN_INTERLOCK_TARGET.LaserOn),
        Rule("Scanner Ready", "SCANNER_READY", true, "READY", "Scanner ready signal is off", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.AutoRun),
        Rule("Vision Ready", "VISION_READY", true, "READY", "Vision ready signal is off", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.AutoRun),
        Rule("Panel In Position", "PANEL_IN_POSITION", true, "OK", "Panel is not in position", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.AutoRun),
        Rule("Vacuum Check", "VACUUM_CHECK", true, "OK", "Vacuum check is off", EN_INTERLOCK_LEVEL.Warn, EN_INTERLOCK_TARGET.AutoRun)
    ];

    public ST_INTERLOCK_SUMMARY Evaluate(ST_DEVICE_STATUS status)
    {
        var checks = Rules
            .Select(rule => EvaluateRule(rule, status))
            .ToArray();

        var items = checks
            .Select(check => check.Item)
            .ToList();

        var motionOk = !status.Motors.Any(axis => axis.AlarmOn);
        items.Add(new ST_INTERLOCK_ITEM(
            "Motion",
            motionOk ? EN_INTERLOCK_LEVEL.Ok : EN_INTERLOCK_LEVEL.Error,
            motionOk ? "READY" : "ALARM",
            motionOk ? "All axes normal" : "One or more axes are in alarm"));

        var chillerOk = !status.Chiller.AlarmOn;
        items.Add(new ST_INTERLOCK_ITEM(
            "Chiller",
            chillerOk ? EN_INTERLOCK_LEVEL.Ok : EN_INTERLOCK_LEVEL.Error,
            chillerOk ? "OK" : "ALARM",
            chillerOk ? "Chiller normal" : "Chiller alarm is active"));

        var betOk = !status.Bet.AlarmOn;
        items.Add(new ST_INTERLOCK_ITEM(
            "BET",
            betOk ? EN_INTERLOCK_LEVEL.Ok : EN_INTERLOCK_LEVEL.Warn,
            betOk ? "READY" : "ALARM",
            betOk ? "Beam expander normal" : "Beam expander alarm is active"));

        var hasError = items.Any(item => item.Level == EN_INTERLOCK_LEVEL.Error);
        var canAutoRun = IsTargetOk(checks, EN_INTERLOCK_TARGET.AutoRun) && motionOk && chillerOk && betOk;
        var canManualMove = IsTargetOk(checks, EN_INTERLOCK_TARGET.ManualMove) && motionOk;
        var canLaserOn = IsTargetOk(checks, EN_INTERLOCK_TARGET.LaserOn) && chillerOk;

        return new ST_INTERLOCK_SUMMARY(
            canAutoRun,
            canManualMove,
            canLaserOn,
            hasError,
            items);
    }

    private static ST_INTERLOCK_CHECK EvaluateRule(
        ST_INTERLOCK_RULE rule,
        ST_DEVICE_STATUS status)
    {
        var io = status.Io.FirstOrDefault(channel =>
            channel.Id.Equals(rule.IoId, StringComparison.OrdinalIgnoreCase) ||
            channel.Address.Equals(rule.IoId, StringComparison.OrdinalIgnoreCase));

        if (io is null)
        {
            return new ST_INTERLOCK_CHECK(
                rule,
                false,
                new ST_INTERLOCK_ITEM(
                    rule.Signal,
                    EN_INTERLOCK_LEVEL.Warn,
                    "UNKNOWN",
                    $"{rule.IoId} is not registered"));
        }

        var isOk = io.IsOn == rule.ExpectedOn;

        return new ST_INTERLOCK_CHECK(
            rule,
            isOk,
            new ST_INTERLOCK_ITEM(
                rule.Signal,
                isOk ? EN_INTERLOCK_LEVEL.Ok : rule.NgLevel,
                isOk ? rule.OkState : (io.IsOn ? "ON" : "OFF"),
                isOk ? io.Name : rule.NgDetail));
    }

    private static bool IsTargetOk(
        IEnumerable<ST_INTERLOCK_CHECK> checks,
        EN_INTERLOCK_TARGET target)
    {
        return checks
            .Where(check => check.Rule.Targets.HasFlag(target))
            .All(check => check.IsOk);
    }

    private static ST_INTERLOCK_RULE Rule(
        string signal,
        string ioId,
        bool expectedOn,
        string okState,
        string ngDetail,
        EN_INTERLOCK_LEVEL ngLevel,
        EN_INTERLOCK_TARGET targets)
    {
        return new ST_INTERLOCK_RULE(signal, ioId, expectedOn, okState, ngDetail, ngLevel, targets);
    }

    private sealed record ST_INTERLOCK_CHECK(
        ST_INTERLOCK_RULE Rule,
        bool IsOk,
        ST_INTERLOCK_ITEM Item);
}

