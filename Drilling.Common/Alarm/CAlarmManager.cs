using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Alarm;

public enum EN_ALARM_LEVEL
{
    Info,
    Warning,
    Critical
}

public enum EN_ALARM_STATE
{
    Clear,
    Occur
}

public sealed record ST_ALARM_DATA(
    int Code,
    EN_ALARM_LEVEL Severity,
    string Message,
    string RecoveryAction,
    DateTimeOffset OccurredAt,
    string Device = "SYSTEM",
    string StationName = "COMMON");

public sealed class CAlarmManager
{
    private static readonly IReadOnlyDictionary<string, ST_ALARM_RULE> InterLockAlarmRules =
        new Dictionary<string, ST_ALARM_RULE>(StringComparer.OrdinalIgnoreCase)
        {
            ["EMS"] = new(21001, EN_ALARM_LEVEL.Critical, "INTERLOCK", "COMMON", "Release EMS and check emergency circuit"),
            ["Door"] = new(21002, EN_ALARM_LEVEL.Warning, "INTERLOCK", "COMMON", "Close door and check door lock sensor"),
            ["Leak Sensor"] = new(26001, EN_ALARM_LEVEL.Critical, "INTERLOCK", "COMMON", "Check leak sensor and fluid line"),
            ["Smoke Temp"] = new(26002, EN_ALARM_LEVEL.Critical, "INTERLOCK", "COMMON", "Check smoke and temperature sensor"),
            ["PM Lock"] = new(27001, EN_ALARM_LEVEL.Critical, "INTERLOCK", "COMMON", "Release PM lock before operation")
        };

    private readonly Dictionary<int, DateTimeOffset> _activeSince = [];

    public void Reset()
    {
        _activeSince.Clear();
    }

    public IReadOnlyList<ST_ALARM_DATA> Build(
        ST_DEVICE_STATUS status,
        ST_INTERLOCK_SUMMARY interLock)
    {
        var now = DateTimeOffset.Now;
        var candidates = new List<ST_ALARM_CANDIDATE>();

        AddInterLockAlarms(candidates, interLock);

        if (status.Chiller.AlarmOn)
        {
            candidates.Add(new ST_ALARM_CANDIDATE(
                23001,
                EN_ALARM_LEVEL.Warning,
                "Chiller alarm is active",
                "Check chiller alarm code and reset chiller",
                "CHILLER",
                "COMMON"));
        }

        foreach (var axis in status.Motors.Where(axis => axis.AlarmOn))
        {
            candidates.Add(new ST_ALARM_CANDIDATE(
                24001,
                EN_ALARM_LEVEL.Warning,
                $"{axis.AxisId} axis alarm",
                "Reset axis alarm and confirm servo state",
                "MOTION",
                GetStationNameFromAxis(axis.AxisId)));
        }

        if (status.Bet.AlarmOn)
        {
            candidates.Add(new ST_ALARM_CANDIDATE(
                25001,
                EN_ALARM_LEVEL.Warning,
                "BET alarm is active",
                "Check beam expander controller and reset alarm",
                "BET",
                "COMMON"));
        }

        var activeCodes = candidates
            .Select(alarm => alarm.Code)
            .ToHashSet();

        foreach (var code in _activeSince.Keys.Where(code => !activeCodes.Contains(code)).ToArray())
        {
            _activeSince.Remove(code);
        }

        return candidates
            .Select(alarm => ToAlarmData(alarm, now))
            .OrderByDescending(alarm => alarm.Severity)
            .ThenBy(alarm => alarm.Code)
            .ToArray();
    }

    private static void AddInterLockAlarms(
        ICollection<ST_ALARM_CANDIDATE> alarms,
        ST_INTERLOCK_SUMMARY interLock)
    {
        foreach (var item in interLock.Items.Where(item => item.Level != EN_INTERLOCK_LEVEL.Ok))
        {
            if (!InterLockAlarmRules.TryGetValue(item.Signal, out var rule))
            {
                continue;
            }

            alarms.Add(new ST_ALARM_CANDIDATE(
                rule.Code,
                rule.Severity,
                item.Detail,
                rule.RecoveryAction,
                rule.Device,
                rule.StationName));
        }
    }

    private ST_ALARM_DATA ToAlarmData(
        ST_ALARM_CANDIDATE candidate,
        DateTimeOffset now)
    {
        if (!_activeSince.TryGetValue(candidate.Code, out var occurredAt))
        {
            occurredAt = now;
            _activeSince[candidate.Code] = occurredAt;
        }

        return new ST_ALARM_DATA(
            candidate.Code,
            candidate.Severity,
            candidate.Message,
            candidate.RecoveryAction,
            occurredAt,
            candidate.Device,
            candidate.StationName);
    }

    private static string GetStationNameFromAxis(string axisId)
    {
        var normalized = axisId.Trim().ToUpperInvariant();

        if (normalized.StartsWith("SCANNER_", StringComparison.OrdinalIgnoreCase))
        {
            var parts = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"SCANNER_{parts[1]}" : "DRILLING";
        }

        return "DRILLING";
    }

    private sealed record ST_ALARM_RULE(
        int Code,
        EN_ALARM_LEVEL Severity,
        string Device,
        string StationName,
        string RecoveryAction);

    private sealed record ST_ALARM_CANDIDATE(
        int Code,
        EN_ALARM_LEVEL Severity,
        string Message,
        string RecoveryAction,
        string Device,
        string StationName);
}

