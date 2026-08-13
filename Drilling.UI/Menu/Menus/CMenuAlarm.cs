using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuAlarm(
    IInterfaceManager interfaceManager,
    IMotionManager motionManager,
    CAlarmManager alarmManager,
    CInterLockManager interLockManager,
    IStationManager stationManager,
    Action<string> setStatusMessage,
    Action refreshShellStatus,
    Func<Task> refreshCurrentScreen) : IMenu
{
    public EN_MENU Menu => EN_MENU.Alarm;

    public CButtonCommand AlarmResetCommand => new(async _ => await ResetAlarm());

    public CButtonCommand BuzzerOffCommand => new(_ => setStatusMessage("Buzzer off command sent."));

    public IReadOnlyList<ST_DISPLAY_ITEM> CurrentAlarms { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> AlarmDetail { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> RecoveryCommands { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> History { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> Trend { get; private set; } = [];

    public IReadOnlyList<ST_ALARM_CURRENT_ROW> CurrentAlarmRows { get; private set; } = [];

    public IReadOnlyList<ST_ALARM_DETAIL_ROW> DetailRows { get; private set; } = [];

    public IReadOnlyList<ST_ALARM_HISTORY_ROW> HistoryRows { get; private set; } = [];

    public IReadOnlyList<ST_ALARM_TREND_BAR> TrendBars { get; private set; } = [];

    public IReadOnlyList<ST_ALARM_SUMMARY_ITEM> SummaryItems { get; private set; } = [];

    public async Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        var alarms = await GetCurrentAlarms(cancellationToken);
        var currentAlarms = alarms.Select(item =>
            new ST_DISPLAY_ITEM(item.Code.ToString(), item.Message, item.Severity.ToString())).DefaultIfEmpty(
            new ST_DISPLAY_ITEM("No Alarm", "Normal")).ToArray();
        var firstAlarm = alarms.FirstOrDefault();
        Apply(
            currentAlarms,
            [
                new("Code", firstAlarm?.Code.ToString() ?? "-"),
                new("Severity", firstAlarm?.Severity.ToString() ?? "-"),
                new("Recovery", firstAlarm?.RecoveryAction ?? "No recovery action required")
            ],
            [
                new("Alarm Reset", "Ready"),
                new("Buzzer Off", "Ready")
            ],
            [
                new(DateTimeOffset.Now.AddMinutes(-12).ToString("HH:mm:ss"), "No active alarm"),
                new(DateTimeOffset.Now.AddHours(-1).ToString("HH:mm:ss"), "Alarm history sample")
            ],
            [
                new("Critical", "0"),
                new("Warning", "0"),
                new("Info", "0")
            ],
            BuildCurrentAlarmRows(alarms),
            BuildDetailRows(firstAlarm),
            BuildHistoryRows(),
            BuildTrendBars(),
            BuildSummaryItems(alarms));

        return new CScreenViewModel(
            EN_MENU.Alarm,
            "ALARM / CURRENT HISTORY",
            "Current alarm, reset, buzzer off, history and trend.",
            [
                new("Current", alarms.Count.ToString()),
                new("Buzzer", "OFF")
            ],
            [
                new("Current Alarm", currentAlarms),
                new("Recovery Command", [
                    new("Alarm Reset", "Ready"),
                    new("Buzzer Off", "Ready")
                ])
            ],
            alarm: this);
    }

    private async Task<IReadOnlyList<ST_ALARM_DATA>> GetCurrentAlarms(CancellationToken cancellationToken)
    {
        var snapshot = await GetDeviceStatus(cancellationToken);
        var interLock = interLockManager.Evaluate(snapshot);
        return alarmManager.Build(snapshot, interLock);
    }

    private async Task ResetAlarm()
    {
        alarmManager.Reset();
        var station = await stationManager.Reset();
        var isResetBlocked = station.ProcessStep == EN_PROCESS_STEP.Error;
        setStatusMessage(isResetBlocked
            ? $"Alarm reset blocked. {station.Result?.Message ?? "Check InterLock state."}"
            : "Alarm reset completed. Station is ready.");
        refreshShellStatus();
        await refreshCurrentScreen();
    }

    private async Task<ST_DEVICE_STATUS> GetDeviceStatus(CancellationToken cancellationToken)
    {
        var io = await motionManager.GetIoStatus(cancellationToken);
        var motors = await motionManager.GetAxisStatus(cancellationToken);
        var laserStatus = await interfaceManager.GetLaserStatus(cancellationToken);
        var chillerStatus = await interfaceManager.GetChillerStatus(cancellationToken);
        var attenuatorStatus = await interfaceManager.GetAttenuatorStatus(cancellationToken);
        var betStatus = await interfaceManager.GetBETStatus(cancellationToken);
        var powerMeterStatus = await interfaceManager.GetPowerMeterStatus(cancellationToken);

        return new ST_DEVICE_STATUS(
            io,
            motors,
            laserStatus,
            chillerStatus,
            attenuatorStatus,
            betStatus,
            powerMeterStatus);
    }

    private void Apply(
        IReadOnlyList<ST_DISPLAY_ITEM> currentAlarms,
        IReadOnlyList<ST_DISPLAY_ITEM> alarmDetail,
        IReadOnlyList<ST_DISPLAY_ITEM> recoveryCommands,
        IReadOnlyList<ST_DISPLAY_ITEM> history,
        IReadOnlyList<ST_DISPLAY_ITEM> trend,
        IReadOnlyList<ST_ALARM_CURRENT_ROW> currentAlarmRows,
        IReadOnlyList<ST_ALARM_DETAIL_ROW> detailRows,
        IReadOnlyList<ST_ALARM_HISTORY_ROW> historyRows,
        IReadOnlyList<ST_ALARM_TREND_BAR> trendBars,
        IReadOnlyList<ST_ALARM_SUMMARY_ITEM> summaryItems)
    {
        CurrentAlarms = currentAlarms;
        AlarmDetail = alarmDetail;
        RecoveryCommands = recoveryCommands;
        History = history;
        Trend = trend;
        CurrentAlarmRows = currentAlarmRows;
        DetailRows = detailRows;
        HistoryRows = historyRows;
        TrendBars = trendBars;
        SummaryItems = summaryItems;
    }

    private static IReadOnlyList<ST_ALARM_CURRENT_ROW> BuildCurrentAlarmRows(IReadOnlyList<ST_ALARM_DATA> alarms)
    {
        return alarms
            .Select(alarm => new ST_ALARM_CURRENT_ROW(
                alarm.Code.ToString(),
                FormatSeverity(alarm.Severity),
                alarm.Device,
                alarm.Message,
                "Check device state",
                alarm.RecoveryAction,
                alarm.OccurredAt.ToString("HH:mm:ss")))
            .ToArray();
    }

    private static IReadOnlyList<ST_ALARM_DETAIL_ROW> BuildDetailRows(ST_ALARM_DATA? alarm)
    {
        if (alarm is null)
        {
            return
            [
                new("Selected Code", "-"),
                new("Device", "-"),
                new("Level", "CLEAR", "Ok"),
                new("Occurred Time", "-"),
                new("Message", "No active alarm")
            ];
        }

        var severity = FormatSeverity(alarm.Severity);

        return
        [
            new("Selected Code", alarm.Code.ToString(), SeverityState(alarm.Severity)),
            new("Device", alarm.Device),
            new("Level", severity, SeverityState(alarm.Severity)),
            new("Occurred Time", alarm.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Message", alarm.Message)
        ];
    }

    private static IReadOnlyList<ST_ALARM_HISTORY_ROW> BuildHistoryRows()
    {
        return
        [
            new("10:22:58", "21010", "INFO", "MOTION", "Home completed", "ENG1"),
            new("10:22:41", "22018", "WARN", "TALON", "Laser shutter not safe", "---"),
            new("10:21:33", "23004", "INFO", "CHILLER", "Temperature high warning clear", "ENG1"),
            new("10:20:57", "24001", "WARN", "SCANNER", "H04 position timeout", "---"),
            new("10:20:12", "24013", "INFO", "SCANNER", "H04 position OK", "ENG2"),
            new("10:19:45", "25002", "INFO", "AUTOMATION1", "Script load complete", "ENG1"),
            new("10:18:32", "26001", "INFO", "VISION", "Vision offline", "ENG1"),
            new("10:17:58", "26002", "INFO", "VISION", "Vision online", "ENG1")
        ];
    }

    private static IReadOnlyList<ST_ALARM_TREND_BAR> BuildTrendBars()
    {
        return
        [
            new("09:00", 5, 2, 3, 1, 11, 116),
            new("11:00", 2, 1, 1, 1, 5, 128),
            new("13:00", 3, 1, 2, 1, 7, 124),
            new("15:00", 18, 8, 7, 5, 42, 54),
            new("17:00", 9, 3, 2, 2, 16, 106),
            new("19:00", 7, 2, 3, 2, 14, 110),
            new("21:00", 13, 4, 3, 2, 22, 82),
            new("23:00", 10, 2, 2, 1, 15, 108),
            new("01:00", 3, 1, 1, 0, 5, 128),
            new("03:00", 8, 3, 1, 2, 14, 110),
            new("05:00", 4, 1, 0, 1, 6, 126),
            new("07:00", 6, 2, 2, 2, 12, 114),
            new("09:00", 3, 1, 1, 1, 6, 126)
        ];
    }

    private static IReadOnlyList<ST_ALARM_SUMMARY_ITEM> BuildSummaryItems(IReadOnlyList<ST_ALARM_DATA> alarms)
    {
        var warningCount = alarms.Count(alarm => alarm.Severity == EN_ALARM_LEVEL.Warning);
        var criticalCount = alarms.Count(alarm => alarm.Severity == EN_ALARM_LEVEL.Critical);

        return
        [
            new("Today Total", alarms.Count.ToString()),
            new("Warning", warningCount.ToString(), warningCount > 0 ? "Warn" : "Ok"),
            new("Critical", criticalCount.ToString(), criticalCount > 0 ? "Critical" : "Ok"),
            new("Reset Count", "37", "Accent")
        ];
    }

    private static string FormatSeverity(EN_ALARM_LEVEL severity)
    {
        return severity switch
        {
            EN_ALARM_LEVEL.Warning => "WARN",
            EN_ALARM_LEVEL.Critical => "CRITICAL",
            _ => "INFO"
        };
    }

    private static string SeverityState(EN_ALARM_LEVEL severity)
    {
        return severity switch
        {
            EN_ALARM_LEVEL.Warning => "Warn",
            EN_ALARM_LEVEL.Critical => "Critical",
            _ => "Accent"
        };
    }
}

public sealed record ST_ALARM_CURRENT_ROW(
    string Code,
    string Level,
    string Device,
    string Message,
    string Cause,
    string Action,
    string Time)
{
    public Brush LevelBrush => Level switch
    {
        "WARN" => CStatusBrush.Wait,
        "CRITICAL" or "ERROR" => CStatusBrush.Offline,
        "INFO" => CStatusBrush.Simul,
        _ => CStatusBrush.PrimaryText
    };
}

public sealed record ST_ALARM_DETAIL_ROW(
    string Name,
    string Value,
    string State = "Normal")
{
    public Brush ValueBrush => State switch
    {
        "Warn" => CStatusBrush.Wait,
        "Critical" => CStatusBrush.Offline,
        "Accent" => CStatusBrush.Simul,
        "Ok" => CStatusBrush.Online,
        _ => CStatusBrush.PrimaryText
    };
}

public sealed record ST_ALARM_HISTORY_ROW(
    string Time,
    string Code,
    string Level,
    string Device,
    string Message,
    string ResetUser)
{
    public Brush LevelBrush => Level switch
    {
        "WARN" => CStatusBrush.Wait,
        "CRITICAL" or "ERROR" => CStatusBrush.Offline,
        "INFO" => CStatusBrush.Simul,
        _ => CStatusBrush.PrimaryText
    };
}

public sealed record ST_ALARM_TREND_BAR(
    string Time,
    double Scanner,
    double Laser,
    double Chiller,
    double Motion,
    double Total,
    double TotalY);

public sealed record ST_ALARM_SUMMARY_ITEM(
    string Name,
    string Value,
    string State = "Normal")
{
    public Brush ValueBrush => State switch
    {
        "Warn" => CStatusBrush.Wait,
        "Critical" => CStatusBrush.Offline,
        "Accent" => CStatusBrush.Simul,
        "Ok" => CStatusBrush.Online,
        _ => CStatusBrush.PrimaryText
    };
}





