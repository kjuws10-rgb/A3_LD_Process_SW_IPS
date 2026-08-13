using Drilling.Common.Log;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.Common.Product;
using Drilling.Common.Recipe;
using Drilling.Common.Automation;

namespace Drilling.Common.Station;

public enum EN_SCRIPT_STATUS
{
    NotCreated,
    Created,
    Running,
    Completed,
    Error
}

public enum EN_PROCESS_STEP
{
    Idle,
    PreCheck,
    OpticReady,
    PowerCheck,
    Align,
    Process,
    Inspection,
    Completed,
    Stopped,
    Error
}

public enum EN_HEAD_PROCESS_STATUS
{
    Ready,
    Running,
    Completed,
    Error,
    Disabled
}

public enum EN_STATION_ID
{
    Process = 0
}

public enum EN_STATION_STATE
{
    Idle,
    Check,
    Process,
    Complete,
    Alarm,
    Stopped
}

public sealed record ST_PROCESS_PLAN(
    string ProcessId,
    string RecipeId,
    string ProductId,
    string PanelId,
    string LotId,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ST_PATH_POINT(double X, double Y, bool LaserOn = true);

public sealed record ST_HEAD_PROCESS_DATA(
    int HeadNo,
    double LaserPower,
    double FrequencyKhz,
    int ShotCount,
    double ShotTimeDelayMs,
    double MarkSpeed,
    double JumpSpeed,
    double DoeZPosition,
    IReadOnlyList<ST_PATH_POINT> Path)
{
    public int AutomationNo { get; init; } = 0;

    public int TaskNo { get; init; } = HeadNo;

    public string ScriptFileName { get; init; } = $"PROCESS_H{HeadNo:00}.ascript";

    public IReadOnlyList<ST_RECIPE_HOLE_POINT> ProcessPoints { get; init; } = [];
}

public sealed record ST_PROCESS_MODEL(
    ST_PROCESS_PLAN Plan,
    ST_PRODUCT_DATA? Product,
    IReadOnlyList<ST_HEAD_PROCESS_DATA> Heads,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset CreatedAt);

public sealed record ST_HEAD_PATH_DATA(
    int HeadNo,
    EN_HEAD_PROCESS_STATUS Status,
    IReadOnlyList<ST_PATH_POINT> Points);

public sealed record ST_PROCESS_RESULT(
    bool IsSuccess,
    string Message,
    DateTimeOffset CompletedAt);

public sealed record ST_PROCESS_DISPLAY_ITEM(
    string Name,
    string Value,
    string Detail = "");

public sealed record ST_PROCESS_LOG_ITEM(
    DateTimeOffset OccurredAt,
    string Level,
    string Source,
    string Message);

public sealed record ST_PROCESS_STATISTICS(
    int TotalPoints,
    int MoveCount,
    int LaserOnSegments,
    TimeSpan EstimatedTime,
    TimeSpan ElapsedTime,
    double ProgressPercent);

public sealed record ST_AUTOMATION1_SCRIPT(
    string FileName,
    string FilePath,
    IReadOnlyList<string> Lines,
    int TotalPoints,
    int HeadCount,
    DateTimeOffset CreatedAt)
{
    public IReadOnlyList<ST_AUTOMATION1_HEAD_SCRIPT> HeadScripts { get; init; } = [];
}

public sealed record ST_AUTOMATION1_HEAD_SCRIPT(
    int HeadNo,
    int AutomationNo,
    int TaskNo,
    string FileName,
    string FilePath,
    IReadOnlyList<string> Lines,
    int TotalPoints,
    DateTimeOffset CreatedAt);

public enum EN_AEROTECH_PSO_MODE
{
    Unused = 0,
    WindowMask = 1,
    ExtSync = 2,
    LaserMask = 3,
    ExtSyncGalvo = 4
}

public enum EN_AEROTECH_MODE
{
    Mof = 0,
    Ifov = 1,
    Scanner = 2
}

public interface IAutomation1Script
{
    string FileName { get; }

    string FilePath { get; }

    IReadOnlyList<string> Lines { get; }

    void Clear();

    void AddLine(string line);

    void Start(string title = "");

    void SetDeviceNo(int deviceNo);

    void SetNMarkDriveLaserControl(bool use);

    void SetScanPlannerStageEncoderMode(bool use);

    void DefaultSetting(
        double scannerAcc = 500000.0,
        int motionUpdateRate = 0,
        int executeLineCount = 110,
        bool resetPso = true);

    void DefaultFigureScanSetting(
        int motionUpdateRate = 100,
        int executeLineCount = 110);

    void SetAxis(
        string xAxis,
        string yAxis,
        string? laserAxis = null);

    void SetStageAxis(
        string xAxis,
        string yAxis);

    void SetFrequency(double frequencyKhz);

    void SetLaserPower(
        double powerPercent,
        double outputRate = 100.0,
        bool analogOutputUse = false);

    void SetLaserPowerNoDelay(
        double powerPercent,
        double outputRate = 100.0);

    void SetPulseOnTimeLaserPower(
        double powerPercent,
        double dutyPercent,
        double outputRate = 100.0);

    void SetLaserMode(int mode);

    void SetLaserDelay(
        double onDelay,
        double offDelay);

    void SetJumpSpeed(double speedMeterPerSec);

    void SetJumpSpeedRate(
        double speedMeterPerSec,
        double rate = 1.0);

    void SetMarkSpeed(double speedMeterPerSec);

    void SetStageSpeed(
        double speedX,
        double speedY);

    void SetScannerAcc(double acc);

    void SetMarkAcc(double acc);

    void SetIFOV(bool use);

    void SetIFOVEmulatedQuadratureDivider();

    void SetIFOVIO(bool use = true);

    void SetIFOVScaleXY();

    void SetIFOVTime(long time);

    void SetIFOVSize(double size);

    void SetIFOVTrackingSpeed(long speed);

    void SetIFOVTrackingAccel(long acc);

    void SetIFOVPair(
        string xStageAxis,
        string yStageAxis,
        bool xDirection,
        bool yDirection);

    void SetIFOVSyncAxis();

    void SetMoveBlending(bool use);

    void SetAbsoluteMode();

    void SetIncrementalMode();

    void SetWaitModeAuto();

    void SetMoveDelay(
        double delaySeconds,
        bool addTactTime = true);

    void SetExecuteLineCount(int lineCount);

    void SetScannerRotate(double angle);

    void SetScannerRotate(
        string laserAxis,
        double angle);

    void SetMoveUpdateRate(int rate);

    void SetCoordinatedAccelLimit(
        long acc,
        long arcAcc);

    void SetTaskAccelLimit(
        long acc,
        long arcAcc);

    void SetScanTrajectoryFIRFilterX(long delay);

    void SetScanTrajectoryFIRFilterY(long delay);

    void SetStageTrajectoryFIRFilterX(long delay);

    void SetStageTrajectoryFIRFilterY(long delay);

    void SetProjection(
        string axis,
        double offsetX,
        double offsetY,
        double offsetT);

    void SetProjectionOff(string axis);

    void SetGearing(
        string masterAxis,
        string slaveAxis);

    void SetGearingOff(string slaveAxis = "AUTO");

    void SetSoftwareLimitSetup(bool use = true);

    void SetAerotechEncoderReset(
        string axisX,
        string axisY);

    void SetScanPlannerStageEncoder(string stageAxis);

    void SetEmulatedQuadratureDividerX(int value);

    void SetEmulatedQuadratureDividerY(int value);

    void SetStageEmulatedQuadratureDivider(
        int xValue,
        int yValue);

    void SetPSO(
        double pulseDistance,
        double totalTime,
        double laserOnTime,
        double delay,
        EN_AEROTECH_MODE mode,
        EN_AEROTECH_PSO_MODE psoMode,
        double frequencyKhz,
        double powerPercent,
        int windowMaskDirection,
        double markSpeed,
        bool manual = false);

    void SetPSODistance(double pulseDistance);

    void SetPSOOnOff(bool on);

    void SetPSOChangePower(
        double frequencyKhz,
        double powerPercent);

    void SetPSOFire(
        double totalTime,
        double laserOnTime,
        int count,
        double delay,
        EN_AEROTECH_MODE mode);

    void SetPSOLaserWindowMask(
        bool on,
        double windowStartRange = 0,
        double windowEndRange = 0);

    void DeclareEncoderVariable(
        string axis = "",
        bool useFeedback = false);

    void InitDeclareVariable();

    void InitDeclareVariableIFOV();

    void SetWaitForEncoder(
        string axis,
        double position,
        bool directionPlus = true);

    void SetWaitForEncoder(
        string axis,
        bool directionPlus,
        double position,
        double limit,
        double encoderScale = 1.0);

    void SetWaitForEncoder2Axis(
        string axisX,
        string axisY,
        bool inToOut,
        double posX,
        double posY,
        double limitX,
        double limitY);

    void SetWaitForStartAxis2(
        string axisX,
        string axisY,
        bool inToOut,
        double posX,
        double posY,
        double limitX,
        double limitY);

    void SetEncoderScaleFactor(
        string galvoAxis,
        string encoderAxis,
        int scale);

    void SetEncoderScaleFactor(
        string galvoAxis,
        string encoderAxis,
        bool directionPlus);

    void SetEncoderScaleFactor(
        string galvoAxis,
        string encoderAxis,
        double encoderX,
        double encoderY,
        bool directionPlus);

    void SetEncoderScaleFactorByPrimaryDivider(
        string galvoAxis,
        string encoderAxis,
        bool directionPlus);

    void InitEncoderCount(string galvoAxis);

    void EncoderNotFeedback(string axis);

    void ReleaseEncoderScaleFactor(string galvoAxis);

    void LaserAuto();

    void LaserOn();

    void LaserOff();

    void PsoLaserControl(
        bool on,
        bool manual = false);

    void LaserFire(bool on);

    void Jump(double x, double y);

    void Mark(double x, double y);

    void GCodeMove(
        double x,
        double y);

    void JumpRel(double x, double y);

    void MarkRel(double x, double y);

    void Arc(
        double startX,
        double startY,
        double endX,
        double endY,
        double centerX,
        double centerY,
        double angle);

    void JumpLinear(
        double x,
        double y);

    void WaitMoveDone();

    void Dwell(double delay);

    void EnableAxisPair();

    void DisableAxisPair();

    void FaultAckAxisPair();

    void HomeAxisPair();

    void OffsetClearAxisPair();

    void OffsetSetAxisPair(
        double x,
        double y);

    void SetSignalLogTrigger(bool use);

    void ProgramStart();

    void ProgramEnd();

    void BufferedEnd();

    void WaitInpos();

    void SetHomePos();

    void SetGalvoPosZero();

    void End(bool bufferedRun = false);

    Task<ST_AUTOMATION1_SCRIPT> Save(CancellationToken cancellationToken = default);
}

public interface IAutomationScriptFile
{
    string ScriptFileName { get; }

    IAutomation1Script Create(string? fileName = null);

    Task<ST_AUTOMATION1_SCRIPT> Build(
        ST_PROCESS_MODEL processModel,
        CancellationToken cancellationToken = default);

    Task<ST_AUTOMATION1_SCRIPT> Build(
        ST_PROCESS_MODEL processModel,
        string subDirectoryName,
        CancellationToken cancellationToken = default);
}

public sealed record ST_STATION_PROCESS_STATUS(
    ST_PROCESS_PLAN? ProcessPlan,
    ST_PROCESS_MODEL? ProcessModel,
    IReadOnlyList<ST_HEAD_PATH_DATA> HeadPreviews,
    EN_SCRIPT_STATUS ScriptStatus,
    EN_PROCESS_STEP ProcessStep,
    ST_PROCESS_RESULT? Result,
    IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> ProcessSequence,
    IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> CurrentStepDetails,
    IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> ProcessSummary,
    IReadOnlyList<ST_PROCESS_LOG_ITEM> ProcessLogs,
    IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> ScriptStatusItems,
    IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> ScriptLifecycleItems,
    IReadOnlyList<ST_INTERLOCK_ITEM> InterlockItems,
    ST_PROCESS_STATISTICS Statistics);

public sealed record ST_STATION_STATUS(
    EN_STATION_ID StationId,
    string StationName,
    EN_STATION_STATE State,
    EN_PROCESS_STEP ProcessStep,
    EN_SCRIPT_STATUS ScriptStatus,
    string LastMessage,
    DateTimeOffset ChangedAt);

public sealed record ST_STATION_PROCESS_FLOW_ITEM(
    int Order,
    string StepKey,
    string StepName,
    EN_STATION_STATE RunningState,
    EN_PROCESS_STEP RunningStep,
    EN_SCRIPT_STATUS ScriptStatus,
    string OnSuccess,
    string OnFail);

public sealed class CStationManager {
    private readonly CStationProcess _processStation;

    public CStationManager(
        CInterfaceManager interfaceManager,
        CMotionManager motionManager,
        CInterLockManager interLockManager,
        CSettingManager settingManager,
        IAutomationScriptFile automationScriptFile,
        CAutomationManager automationManager,
        CProductManager? productManager = null,
        CLogManager? logManager = null,
        string? scriptDirectory = null)
    {
        _processStation = new CStationProcess(
            interfaceManager,
            motionManager,
            interLockManager,
            settingManager,
            automationScriptFile,
            automationManager,
            productManager,
            logManager,
            scriptDirectory: scriptDirectory);
    }

    public Task<ST_STATION_PROCESS_STATUS> GetStatus(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_processStation.Current);
    }

    public Task<IReadOnlyList<ST_STATION_STATUS>> GetStationStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ST_STATION_STATUS>>([_processStation.Status]);
    }

    public IReadOnlyList<ST_STATION_PROCESS_FLOW_ITEM> GetProcessFlow()
    {
        return CStationProcess.GetProcessFlow();
    }

    public Task<ST_STATION_PROCESS_STATUS> PrepareProcessPlan(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken = default)
    {
        return _processStation.PrepareProcessPlan(processPlan, cancellationToken);
    }

    public Task<ST_STATION_PROCESS_STATUS> Start(CancellationToken cancellationToken = default)
    {
        return _processStation.Start(cancellationToken);
    }

    public Task<ST_STATION_PROCESS_STATUS> Stop(CancellationToken cancellationToken = default)
    {
        return _processStation.Stop(cancellationToken);
    }

    public Task<ST_STATION_PROCESS_STATUS> Reset(CancellationToken cancellationToken = default)
    {
        return _processStation.Reset(cancellationToken);
    }
}




