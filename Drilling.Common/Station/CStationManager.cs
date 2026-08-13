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

public abstract class CAutomation1ScriptBase
{
    public abstract string FileName { get; }
    public abstract string FilePath { get; }
    public abstract IReadOnlyList<string> Lines { get; }

    public abstract void Clear();
    public abstract void AddLine(string line);
    public abstract void Start(string title = "");
    public abstract void SetDeviceNo(int deviceNo);
    public abstract void SetNMarkDriveLaserControl(bool use);
    public abstract void SetScanPlannerStageEncoderMode(bool use);
    public abstract void DefaultSetting(
            double scannerAcc = 500000.0,
            int motionUpdateRate = 0,
            int executeLineCount = 110,
            bool resetPso = true);
    public abstract void DefaultFigureScanSetting(
            int motionUpdateRate = 100,
            int executeLineCount = 110);
    public abstract void SetAxis(
            string xAxis,
            string yAxis,
            string? laserAxis = null);
    public abstract void SetStageAxis(
            string xAxis,
            string yAxis);
    public abstract void SetFrequency(double frequencyKhz);
    public abstract void SetLaserPower(
            double powerPercent,
            double outputRate = 100.0,
            bool analogOutputUse = false);
    public abstract void SetLaserPowerNoDelay(
            double powerPercent,
            double outputRate = 100.0);
    public abstract void SetPulseOnTimeLaserPower(
            double powerPercent,
            double dutyPercent,
            double outputRate = 100.0);
    public abstract void SetLaserMode(int mode);
    public abstract void SetLaserDelay(
            double onDelay,
            double offDelay);
    public abstract void SetJumpSpeed(double speedMeterPerSec);
    public abstract void SetJumpSpeedRate(
            double speedMeterPerSec,
            double rate = 1.0);
    public abstract void SetMarkSpeed(double speedMeterPerSec);
    public abstract void SetStageSpeed(
            double speedX,
            double speedY);
    public abstract void SetScannerAcc(double acc);
    public abstract void SetMarkAcc(double acc);
    public abstract void SetIFOV(bool use);
    public abstract void SetIFOVEmulatedQuadratureDivider();
    public abstract void SetIFOVIO(bool use = true);
    public abstract void SetIFOVScaleXY();
    public abstract void SetIFOVTime(long time);
    public abstract void SetIFOVSize(double size);
    public abstract void SetIFOVTrackingSpeed(long speed);
    public abstract void SetIFOVTrackingAccel(long acc);
    public abstract void SetIFOVPair(
            string xStageAxis,
            string yStageAxis,
            bool xDirection,
            bool yDirection);
    public abstract void SetIFOVSyncAxis();
    public abstract void SetMoveBlending(bool use);
    public abstract void SetAbsoluteMode();
    public abstract void SetIncrementalMode();
    public abstract void SetWaitModeAuto();
    public abstract void SetMoveDelay(
            double delaySeconds,
            bool addTactTime = true);
    public abstract void SetExecuteLineCount(int lineCount);
    public abstract void SetScannerRotate(double angle);
    public abstract void SetScannerRotate(
            string laserAxis,
            double angle);
    public abstract void SetMoveUpdateRate(int rate);
    public abstract void SetCoordinatedAccelLimit(
            long acc,
            long arcAcc);
    public abstract void SetTaskAccelLimit(
            long acc,
            long arcAcc);
    public abstract void SetScanTrajectoryFIRFilterX(long delay);
    public abstract void SetScanTrajectoryFIRFilterY(long delay);
    public abstract void SetStageTrajectoryFIRFilterX(long delay);
    public abstract void SetStageTrajectoryFIRFilterY(long delay);
    public abstract void SetProjection(
            string axis,
            double offsetX,
            double offsetY,
            double offsetT);
    public abstract void SetProjectionOff(string axis);
    public abstract void SetGearing(
            string masterAxis,
            string slaveAxis);
    public abstract void SetGearingOff(string slaveAxis = "AUTO");
    public abstract void SetSoftwareLimitSetup(bool use = true);
    public abstract void SetAerotechEncoderReset(
            string axisX,
            string axisY);
    public abstract void SetScanPlannerStageEncoder(string stageAxis);
    public abstract void SetEmulatedQuadratureDividerX(int value);
    public abstract void SetEmulatedQuadratureDividerY(int value);
    public abstract void SetStageEmulatedQuadratureDivider(
            int xValue,
            int yValue);
    public abstract void SetPSO(
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
    public abstract void SetPSODistance(double pulseDistance);
    public abstract void SetPSOOnOff(bool on);
    public abstract void SetPSOChangePower(
            double frequencyKhz,
            double powerPercent);
    public abstract void SetPSOFire(
            double totalTime,
            double laserOnTime,
            int count,
            double delay,
            EN_AEROTECH_MODE mode);
    public abstract void SetPSOLaserWindowMask(
            bool on,
            double windowStartRange = 0,
            double windowEndRange = 0);
    public abstract void DeclareEncoderVariable(
            string axis = "",
            bool useFeedback = false);
    public abstract void InitDeclareVariable();
    public abstract void InitDeclareVariableIFOV();
    public abstract void SetWaitForEncoder(
            string axis,
            double position,
            bool directionPlus = true);
    public abstract void SetWaitForEncoder(
            string axis,
            bool directionPlus,
            double position,
            double limit,
            double encoderScale = 1.0);
    public abstract void SetWaitForEncoder2Axis(
            string axisX,
            string axisY,
            bool inToOut,
            double posX,
            double posY,
            double limitX,
            double limitY);
    public abstract void SetWaitForStartAxis2(
            string axisX,
            string axisY,
            bool inToOut,
            double posX,
            double posY,
            double limitX,
            double limitY);
    public abstract void SetEncoderScaleFactor(
            string galvoAxis,
            string encoderAxis,
            int scale);
    public abstract void SetEncoderScaleFactor(
            string galvoAxis,
            string encoderAxis,
            bool directionPlus);
    public abstract void SetEncoderScaleFactor(
            string galvoAxis,
            string encoderAxis,
            double encoderX,
            double encoderY,
            bool directionPlus);
    public abstract void SetEncoderScaleFactorByPrimaryDivider(
            string galvoAxis,
            string encoderAxis,
            bool directionPlus);
    public abstract void InitEncoderCount(string galvoAxis);
    public abstract void EncoderNotFeedback(string axis);
    public abstract void ReleaseEncoderScaleFactor(string galvoAxis);
    public abstract void LaserAuto();
    public abstract void LaserOn();
    public abstract void LaserOff();
    public abstract void PsoLaserControl(
            bool on,
            bool manual = false);
    public abstract void LaserFire(bool on);
    public abstract void Jump(double x, double y);
    public abstract void Mark(double x, double y);
    public abstract void GCodeMove(
            double x,
            double y);
    public abstract void JumpRel(double x, double y);
    public abstract void MarkRel(double x, double y);
    public abstract void Arc(
            double startX,
            double startY,
            double endX,
            double endY,
            double centerX,
            double centerY,
            double angle);
    public abstract void JumpLinear(
            double x,
            double y);
    public abstract void WaitMoveDone();
    public abstract void Dwell(double delay);
    public abstract void EnableAxisPair();
    public abstract void DisableAxisPair();
    public abstract void FaultAckAxisPair();
    public abstract void HomeAxisPair();
    public abstract void OffsetClearAxisPair();
    public abstract void OffsetSetAxisPair(
            double x,
            double y);
    public abstract void SetSignalLogTrigger(bool use);
    public abstract void ProgramStart();
    public abstract void ProgramEnd();
    public abstract void BufferedEnd();
    public abstract void WaitInpos();
    public abstract void SetHomePos();
    public abstract void SetGalvoPosZero();
    public abstract void End(bool bufferedRun = false);
    public abstract ST_AUTOMATION1_SCRIPT Save(CancellationToken cancellationToken = default);
}

public abstract class CAutomationScriptFileBase
{
    public abstract string ScriptFileName { get; }

    public abstract CAutomation1ScriptBase Create(string? fileName = null);
    public abstract ST_AUTOMATION1_SCRIPT Build(
            ST_PROCESS_MODEL processModel,
            CancellationToken cancellationToken = default);
    public abstract ST_AUTOMATION1_SCRIPT Build(
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
        CAutomationScriptFileBase automationScriptFile,
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

    public ST_STATION_PROCESS_STATUS GetStatus(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _processStation.Current;
    }

    public IReadOnlyList<ST_STATION_STATUS> GetStationStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [_processStation.Status];
    }

    public IReadOnlyList<ST_STATION_PROCESS_FLOW_ITEM> GetProcessFlow()
    {
        return CStationProcess.GetProcessFlow();
    }

    public ST_STATION_PROCESS_STATUS PrepareProcessPlan(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken = default)
    {
        return _processStation.PrepareProcessPlan(processPlan, cancellationToken);
    }

    public ST_STATION_PROCESS_STATUS Start(CancellationToken cancellationToken = default)
    {
        return _processStation.Start(cancellationToken);
    }

    public ST_STATION_PROCESS_STATUS Stop(CancellationToken cancellationToken = default)
    {
        return _processStation.Stop(cancellationToken);
    }

    public ST_STATION_PROCESS_STATUS Reset(CancellationToken cancellationToken = default)
    {
        return _processStation.Reset(cancellationToken);
    }

    public void Destroy()
    {
        _processStation.Shutdown();
    }
}
