using System.Globalization;
using System.Runtime.ExceptionServices;
using Drilling.Common.Log;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Product;
using Drilling.Common.Automation;
using Drilling.Common.Recipe;
using Drilling.Common.Station;
using Drilling.Common.Threading;

namespace Drilling.Common.Station;

public enum EN_STATION_SEQUENCE
{
    Ready,
    PreCheck,
    OpticReady,
    PowerCheck,
    Align,
    Process,
    Inspection,
    Complete,
    Stop,
    Reset,
    Error
}

public sealed class CStationProcess : CtrlThread
{
    private delegate void CProcessStepAction(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken);
    private const string AutoStepIdle = "IDLE";
    private const string AutoStepPreCheck = "PRECHECK";
    private const string AutoStepOpticReady = "OPTIC_READY";
    private const string AutoStepPowerCheck = "POWER_CHECK";
    private const string AutoStepAlign = "ALIGN";
    private const string AutoStepProcess = "PROCESS";
    private const string AutoStepInspection = "INSPECTION";
    private const string AutoStepComplete = "COMPLETE";

    private const string SettingAutoPowerCheckUse = "AUTO_POWER_CHECK_USE";
    private const string SettingAutoAlignUse = "AUTO_ALIGN_USE";
    private const string SettingAutoProcessUse = "AUTO_PROCESS_USE";
    private const string SettingAutoInspectionUse = "AUTO_INSPECTION_USE";
    private const string ScriptBufferedRunUseKey = "SCRIPT_BUFFERED_RUN_USE";
    private const string ScriptBufferedRunLinesPerCommandKey = "SCRIPT_BUFFERED_RUN_LINES_PER_COMMAND";
    private const string ScriptBufferedRunTimeoutMsKey = "SCRIPT_BUFFERED_RUN_TIMEOUT_MS";
    private const string ScriptBufferedRunQueueSizeKey = "SCRIPT_BUFFERED_RUN_QUEUE_SIZE";
    private const string StandardScriptDirectoryName = "Standard";
    private const string BufferedRunScriptDirectoryName = "BufferedRun";
    private const int OpticReadyDefaultTimeoutMs = 30000;
    private const int OpticReadyPollIntervalMs = 200;
    private const double OpticReadyPositionTolerance = 0.01;

    private const string AutoStepWait = "WAIT";
    private const string AutoStepRunning = "RUNNING";
    private const string AutoStepOk = "OK";
    private const string AutoStepDone = "DONE";
    private const string AutoStepSkip = "SKIP";
    private const string AutoStepError = "ERROR";
    private const string AutoStepStop = "STOP";

    private static readonly IReadOnlyList<ST_AUTO_STEP_INFO> AutoStepInfos =
    [
        new(AutoStepIdle, "IDLE"),
        new(AutoStepPreCheck, "PRECHECK"),
        new(AutoStepOpticReady, "OPTIC READY"),
        new(AutoStepPowerCheck, "POWERCHECK"),
        new(AutoStepAlign, "ALIGN"),
        new(AutoStepProcess, "PROCESS"),
        new(AutoStepInspection, "INSPECTION"),
        new(AutoStepComplete, "COMPLETE")
    ];

    private static readonly IReadOnlyList<ST_STATION_PROCESS_FLOW_ITEM> ProcessFlowItems =
    [
        new(1, AutoStepIdle, "IDLE", EN_STATION_STATE.Idle, EN_PROCESS_STEP.Idle, EN_SCRIPT_STATUS.NotCreated, AutoStepPreCheck, "ALARM"),
        new(2, AutoStepPreCheck, "PRECHECK", EN_STATION_STATE.Check, EN_PROCESS_STEP.PreCheck, EN_SCRIPT_STATUS.NotCreated, AutoStepOpticReady, "ALARM"),
        new(3, AutoStepOpticReady, "OPTIC READY", EN_STATION_STATE.Check, EN_PROCESS_STEP.OpticReady, EN_SCRIPT_STATUS.NotCreated, AutoStepPowerCheck, "ALARM"),
        new(4, AutoStepPowerCheck, "POWERCHECK", EN_STATION_STATE.Check, EN_PROCESS_STEP.PowerCheck, EN_SCRIPT_STATUS.NotCreated, AutoStepAlign, "ALARM"),
        new(5, AutoStepAlign, "ALIGN", EN_STATION_STATE.Process, EN_PROCESS_STEP.Align, EN_SCRIPT_STATUS.NotCreated, AutoStepProcess, "ALARM"),
        new(6, AutoStepProcess, "PROCESS", EN_STATION_STATE.Process, EN_PROCESS_STEP.Process, EN_SCRIPT_STATUS.Running, AutoStepInspection, "ALARM"),
        new(7, AutoStepInspection, "INSPECTION", EN_STATION_STATE.Process, EN_PROCESS_STEP.Inspection, EN_SCRIPT_STATUS.Running, AutoStepComplete, "ALARM"),
        new(8, AutoStepComplete, "COMPLETE", EN_STATION_STATE.Complete, EN_PROCESS_STEP.Completed, EN_SCRIPT_STATUS.Completed, "IDLE/RESET", "ALARM")
    ];

    private static readonly IReadOnlySet<EN_EQP_MODULE> OpticReadyModules =
        new HashSet<EN_EQP_MODULE>
        {
            EN_EQP_MODULE.TalonLaser,
            EN_EQP_MODULE.Chiller,
            EN_EQP_MODULE.Attenuator,
            EN_EQP_MODULE.Bet
        };

    private readonly CInterfaceManager _interfaceManager;
    private readonly CMotionManager _motionManager;
    private readonly CInterLockManager _interLockManager;
    private readonly CSettingManager _settingManager;
    private readonly CProductManager? _productManager;
    private readonly CAutomationScriptFileBase _automationScriptFile;
    private readonly CAutomationManager _automationManager;
    private readonly CLogManager? _logManager;
    private readonly object mobjSequenceLock = new object();
    private readonly List<ST_PROCESS_LOG_ITEM> _processLogs = [];
    private readonly Dictionary<string, string> _autoStepStates = CreateAutoStepStateMap();

    private DateTimeOffset? _scriptCreatedAt;
    private DateTimeOffset? _scriptStartedAt;
    private DateTimeOffset? _scriptCompletedAt;
    private ST_AUTOMATION1_SCRIPT? _lastScript;
    private IReadOnlyList<ST_INTERLOCK_ITEM> _lastInterLockItems = [];
    private ST_PROCESS_MODEL? _processModel;
    private ST_PROCESS_STATISTICS _statistics = EmptyStatistics();
    private ST_STATION_PROCESS_STATUS _snapshot;
    private ST_STATION_STATUS _stationStatus;
    private EN_STATION_SEQUENCE meSequence = EN_STATION_SEQUENCE.Ready;
    private CancellationTokenSource? mobjSequenceCancellation;
    private string mstrSequenceError = "";

    public CStationProcess(
        CInterfaceManager interfaceManager,
        CMotionManager motionManager,
        CInterLockManager interLockManager,
        CSettingManager settingManager,
        CAutomationScriptFileBase automationScriptFile,
        CAutomationManager automationManager,
        CProductManager? productManager = null,
        CLogManager? logManager = null,
        string stationName = "PROCESS",
        string? scriptDirectory = null)
    {
        _interfaceManager = interfaceManager;
        _motionManager = motionManager;
        _interLockManager = interLockManager;
        _settingManager = settingManager;
        _productManager = productManager;
        _automationScriptFile = automationScriptFile;
        _automationManager = automationManager;
        _logManager = logManager;
        _snapshot = CreateSnapshot(
            null,
            [],
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.Idle,
            null);
        _stationStatus = new ST_STATION_STATUS(
            EN_STATION_ID.Process,
            stationName,
            EN_STATION_STATE.Idle,
            EN_PROCESS_STEP.Idle,
            EN_SCRIPT_STATUS.NotCreated,
            "Station idle.",
            DateTimeOffset.Now);
        base.Start(10, "StationProcess");
    }

    public ST_STATION_PROCESS_STATUS Current
    {
        get
        {
            return _snapshot;
        }
    }

    public ST_STATION_STATUS Status
    {
        get
        {
            return _stationStatus;
        }
    }

    public EN_STATION_SEQUENCE SequenceState
    {
        get
        {
            lock (mobjSequenceLock)
            {
                return meSequence;
            }
        }
    }

    public static IReadOnlyList<ST_STATION_PROCESS_FLOW_ITEM> GetProcessFlow()
    {
        return ProcessFlowItems;
    }

    public ST_STATION_PROCESS_STATUS PrepareProcessPlan(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _processLogs.Clear();
        _lastInterLockItems = [];
        _scriptCreatedAt = null;
        _scriptStartedAt = null;
        _scriptCompletedAt = null;
        _lastScript = null;
        ResetAutoStepStates();

        processPlan = BuildRuntimeProcessPlan(processPlan, cancellationToken);
        _processModel = BuildProcessModel(processPlan);
        var preview = BuildPreview(_processModel, EN_HEAD_PROCESS_STATUS.Ready);
        _statistics = BuildStatistics(preview, 0.0, TimeSpan.Zero);
        CreateProduct(_processModel, preview, cancellationToken);

        AddProcessLog("INFO", "SCAN_PC", $"Process plan prepared ({processPlan.ProcessId})");
        AddProcessLog("INFO", "PARSER", $"Head parameter parsed ({preview.Count} heads / {_statistics.TotalPoints} points)");
        SetAutoStepState(AutoStepIdle, AutoStepOk);

        _snapshot = CreateSnapshot(
            processPlan,
            preview,
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.Idle,
            null);

        SetStationState(
            EN_STATION_STATE.Idle,
            EN_PROCESS_STEP.Idle,
            EN_SCRIPT_STATUS.NotCreated,
            $"Process plan prepared: {processPlan.ProcessId}");

        return _snapshot;
    }

    public ST_STATION_PROCESS_STATUS Start(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (mobjSequenceLock)
        {
            if (meSequence != EN_STATION_SEQUENCE.Ready)
            {
                return _snapshot;
            }

            EnsureStartAllowed();
            CheckProcessPlan();
            mobjSequenceCancellation?.Dispose();
            mobjSequenceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            mstrSequenceError = "";
            meSequence = EN_STATION_SEQUENCE.PreCheck;
        }

        base.Start(10, "StationProcess");
        return _snapshot;
    }

    public override void Run()
    {
        EN_STATION_SEQUENCE sequence = SequenceState;
        try
        {
            switch (sequence)
            {
                case EN_STATION_SEQUENCE.Ready:
                    break;
                case EN_STATION_SEQUENCE.PreCheck:
                    RunSequenceStep(AutoStepPreCheck, RunPreCheck, EN_STATION_SEQUENCE.OpticReady);
                    break;
                case EN_STATION_SEQUENCE.OpticReady:
                    RunSequenceStep(AutoStepOpticReady, RunOpticReadyStep, EN_STATION_SEQUENCE.PowerCheck);
                    break;
                case EN_STATION_SEQUENCE.PowerCheck:
                    RunOptionalSequenceStep(
                        AutoStepPowerCheck,
                        SettingAutoPowerCheckUse,
                        RunPowerCheckStep,
                        EN_STATION_SEQUENCE.Align);
                    break;
                case EN_STATION_SEQUENCE.Align:
                    RunOptionalSequenceStep(
                        AutoStepAlign,
                        SettingAutoAlignUse,
                        RunAlignStep,
                        EN_STATION_SEQUENCE.Process);
                    break;
                case EN_STATION_SEQUENCE.Process:
                    RunOptionalSequenceStep(
                        AutoStepProcess,
                        SettingAutoProcessUse,
                        RunProcessStep,
                        EN_STATION_SEQUENCE.Inspection);
                    break;
                case EN_STATION_SEQUENCE.Inspection:
                    RunOptionalSequenceStep(
                        AutoStepInspection,
                        SettingAutoInspectionUse,
                        RunInspectionStep,
                        EN_STATION_SEQUENCE.Complete);
                    break;
                case EN_STATION_SEQUENCE.Complete:
                    RunCompleteSequence();
                    break;
                case EN_STATION_SEQUENCE.Stop:
                    ProcessStop(CancellationToken.None);
                    FinishSequence();
                    break;
                case EN_STATION_SEQUENCE.Reset:
                    ProcessReset(CancellationToken.None);
                    FinishSequence();
                    break;
                case EN_STATION_SEQUENCE.Error:
                    SetAlarm(mstrSequenceError, CancellationToken.None);
                    FinishSequence();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            SetSequence(EN_STATION_SEQUENCE.Stop);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or TimeoutException or IOException or KeyNotFoundException)
        {
            mstrSequenceError = exception.Message;
            SetSequence(EN_STATION_SEQUENCE.Error);
        }
    }

    public void Shutdown()
    {
        lock (mobjSequenceLock)
        {
            mobjSequenceCancellation?.Cancel();
        }

        base.Stop();
        lock (mobjSequenceLock)
        {
            mobjSequenceCancellation?.Dispose();
            mobjSequenceCancellation = null;
            meSequence = EN_STATION_SEQUENCE.Ready;
        }
    }

    private void RunSequenceStep(
        string stepKey,
        CProcessStepAction action,
        EN_STATION_SEQUENCE nextSequence)
    {
        CancellationToken cancellationToken = GetSequenceCancellationToken();
        ST_PROCESS_PLAN processPlan = GetSequenceProcessPlan();
        ExecuteAutoStep(stepKey, processPlan, action, cancellationToken);
        SetSequence(nextSequence);
    }

    private void RunOptionalSequenceStep(
        string stepKey,
        string settingKey,
        CProcessStepAction action,
        EN_STATION_SEQUENCE nextSequence)
    {
        CancellationToken cancellationToken = GetSequenceCancellationToken();
        ST_PROCESS_PLAN processPlan = GetSequenceProcessPlan();
        ExecuteOptionalAutoStep(stepKey, settingKey, processPlan, action, cancellationToken);
        SetSequence(nextSequence);
    }

    private void RunCompleteSequence()
    {
        void CompleteStep(ST_PROCESS_PLAN unusedPlan, CancellationToken cancellationToken)
        {
            CompleteProcess(cancellationToken);
        }

        RunSequenceStep(AutoStepComplete, CompleteStep, EN_STATION_SEQUENCE.Ready);
        FinishSequence();
    }

    private ST_PROCESS_PLAN GetSequenceProcessPlan()
    {
        return _snapshot.ProcessPlan ??
            throw new InvalidOperationException("Process Plan is not loaded.");
    }

    private CancellationToken GetSequenceCancellationToken()
    {
        lock (mobjSequenceLock)
        {
            return mobjSequenceCancellation?.Token ?? CancellationToken.None;
        }
    }

    private void SetSequence(EN_STATION_SEQUENCE sequence)
    {
        lock (mobjSequenceLock)
        {
            meSequence = sequence;
        }
    }

    private void FinishSequence()
    {
        lock (mobjSequenceLock)
        {
            meSequence = EN_STATION_SEQUENCE.Ready;
            mobjSequenceCancellation?.Dispose();
            mobjSequenceCancellation = null;
        }
    }

    private void EnsureStartAllowed()
    {
        if (_stationStatus.State == EN_STATION_STATE.Alarm)
        {
            throw new InvalidOperationException("Station alarm is active. Reset alarm before auto start.");
        }
    }

    private ST_PROCESS_PLAN CheckProcessPlan()
    {
        if (_snapshot.ProcessPlan is null)
        {
            SetAutoStepState(AutoStepIdle, AutoStepError);
            throw new InvalidOperationException("Process Plan is not loaded.");
        }

        SetAutoStepState(AutoStepIdle, AutoStepOk);
        SetStationState(
            EN_STATION_STATE.Check,
            EN_PROCESS_STEP.PreCheck,
            EN_SCRIPT_STATUS.NotCreated,
            "Checking InterLock and process plan.");

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            _snapshot.HeadPreviews,
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.PreCheck,
            null);

        return _snapshot.ProcessPlan
            ?? throw new InvalidOperationException("Process Plan is not loaded.");
    }

    private void ExecuteAutoStep(
        string stepKey,
        ST_PROCESS_PLAN processPlan,
        CProcessStepAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stepName = GetAutoStepName(stepKey);
        var flowItem = GetProcessFlowItem(stepKey);
        var runningScriptStatus = ResolveStepScriptStatus(stepKey, flowItem.ScriptStatus);
        SetAutoStepState(stepKey, AutoStepRunning);
        AddProcessLog("INFO", "STEP", $"{stepName} started.");
        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            SetHeadStatus(_snapshot.HeadPreviews, flowItem.RunningStep),
            runningScriptStatus,
            flowItem.RunningStep,
            null);
        SetStationState(
            flowItem.RunningState,
            flowItem.RunningStep,
            runningScriptStatus,
            $"{stepName} started.");
        RefreshSnapshot();

        try
        {
            action(processPlan, cancellationToken);
            SetAutoStepState(stepKey, stepKey == AutoStepComplete ? AutoStepDone : AutoStepOk);
            AddProcessLog("INFO", "STEP", $"{stepName} completed.");
            RefreshSnapshot();
        }
        catch (Exception exception)
        {
            SetAutoStepState(stepKey, AutoStepError);
            AddProcessLog("ERROR", "STEP", $"{stepName} failed. {exception.Message}");
            RefreshSnapshot();
            throw;
        }
    }

    private void ExecuteOptionalAutoStep(
        string stepKey,
        string settingKey,
        ST_PROCESS_PLAN processPlan,
        CProcessStepAction action,
        CancellationToken cancellationToken)
    {
        if (!ReadSettingBool(settingKey, true, cancellationToken))
        {
            SetAutoStepState(stepKey, AutoStepSkip);
            AddProcessLog("INFO", "STEP", $"{GetAutoStepName(stepKey)} skipped by setting option ({settingKey}=OFF).");
            RefreshSnapshot();
            return;
        }

        ExecuteAutoStep(stepKey, processPlan, action, cancellationToken);
    }

    private void RunPreCheck(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        AddProcessLog("INFO", "PRECHECK", "Auto run precheck started.");
        LoadRecipeProduct(processPlan);
        var interLock = GetInterLockSummary(cancellationToken);

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            _snapshot.HeadPreviews,
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.PreCheck,
            null);

        if (!interLock.CanAutoRun)
        {
            throw new InvalidOperationException(FormatInterLockBlockedMessage(interLock));
        }

        AddProcessLog("INFO", "PRECHECK", "Auto run precheck OK.");
    }

    private void LoadRecipeProduct(ST_PROCESS_PLAN processPlan)
    {
        AddProcessLog("INFO", "RECIPE", $"Recipe/Product data loaded. Recipe={processPlan.RecipeId}, Product={processPlan.ProductId}");
        AddProcessLog("INFO", "PARAM", $"Process parameter loaded ({processPlan.Parameters.Count} items).");
        string SelectHeadNo2(int headNo)
        {
            return $"H{headNo:00}={FormatDouble(ReadDoubleAny(
                            processPlan.Parameters,
                            23.50,
                            CreateHeadKeys(headNo, "ATTENUATOR_POSITION")))}";
        }

        var attenuatorTargets = Enumerable.Range(1, 8)
            .Select(SelectHeadNo2)
            .ToArray();
        AddProcessLog(
            "INFO",
            "ATTN",
            $"Head attenuator targets loaded ({string.Join(", ", attenuatorTargets)} deg)");
    }

    private void RunAlignStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = processPlan.Parameters;
        var reviewCameraAlignKeyPosX = ReadDoubleAny(parameters, 0.0, "REVIEW_CAMERA_ALIGN_KEY_POS_X");
        var reviewCameraAlignKeyPosY = ReadDoubleAny(parameters, 0.0, "REVIEW_CAMERA_ALIGN_KEY_POS_Y");
        var stageYSpeed = ReadDoubleAny(parameters, 100.0, "STAGE_Y_SPEED");

        SendProcessControlCommand(
            "ALIGN_REVIEW_CAMERA_MOVE",
            [
                ("CAMERA_X", FormatDouble(reviewCameraAlignKeyPosX)),
                ("STAGE_Y", FormatDouble(reviewCameraAlignKeyPosY)),
                ("SPEED_Y", FormatDouble(stageYSpeed))
            ],
            cancellationToken);

        AddProcessLog(
            "INFO",
            "ALIGN",
            $"Review camera align key position requested. CameraX={FormatDouble(reviewCameraAlignKeyPosX)}, StageY={FormatDouble(reviewCameraAlignKeyPosY)}");
    }

    private void RunOpticReadyStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        AddSequenceLog("INFO", "OPTIC", $"Optic Ready started. Recipe={processPlan.RecipeId}");

        EnsureOpticCommunicationReady(cancellationToken);
        PrepareLaserProcessReady(cancellationToken);
        PrepareChillerRun(cancellationToken);
        PrepareBET(processPlan, cancellationToken);
        PrepareAttenuator(processPlan, cancellationToken);
        AddSequenceLog("INFO", "LASER", "Laser ready state is maintained. Automation script controls laser emission.");

        AddSequenceLog("INFO", "OPTIC", "Optic Ready completed.");
    }

    private void EnsureOpticCommunicationReady(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool FilterStatus3(ST_INTERFACE_COMM_STATUS status)
        {
            return OpticReadyModules.Contains(status.Module);
        }

        EN_EQP_MODULE GetStatusSortKey4(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Module;
        }

        int GetStatusSortKey5(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Number;
        }

        var statuses = _interfaceManager.GetInterfaceCommunicationList()
            .Where(FilterStatus3)
            .OrderBy(GetStatusSortKey4)
            .ThenBy(GetStatusSortKey5)
            .ToArray();

        if (statuses.Length == 0)
        {
            AddSequenceLog("WARN", "OPTIC", "No optic interface is registered. Optic Ready device commands were skipped.");
            return;
        }

        foreach (var status in statuses)
        {
            AddSequenceLog(
                "INFO",
                "OPTIC",
                $"{FormatInterfaceName(status)} communication state is {FormatCommState(status.ConnectionState)}.");
        }
        bool FilterStatus6(ST_INTERFACE_COMM_STATUS status)
        {
            return status.ConnectionState == EN_COMM_STATE.Offline;
        }

        var offlineDevices = statuses
            .Where(FilterStatus6)
            .Select(FormatInterfaceName)
            .ToArray();

        if (offlineDevices.Length > 0)
        {
            throw new InvalidOperationException(
                $"Optic Ready blocked. Offline optic interface: {string.Join(", ", offlineDevices)}");
        }

        AddSequenceLog("INFO", "OPTIC", "Optic interface communication check OK.");
        return;
    }

    private void PrepareLaserProcessReady(CancellationToken cancellationToken)
    {
        var devices = GetOpticDevices(EN_EQP_MODULE.TalonLaser);

        if (devices.Count == 0)
        {
            AddSequenceLog("WARN", "LASER", "Laser interface is not registered. Laser process ready check skipped.");
            return;
        }

        foreach (var device in devices)
        {
            ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback7(CancellationToken token)
            {
                return _interfaceManager.ExecuteTalonLaserCommand(
                                    device.Number,
                                    EN_TALON_COMMAND.SetLaserOnOff,
                                    1.0,
                                    token);
            }

            ExecuteOpticCommand(
                "LASER",
                device,
                "Laser ON",
ExecuteOpticCommandTokenCallback7,
                cancellationToken);
            ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback8(CancellationToken token)
            {
                return _interfaceManager.ExecuteTalonLaserCommand(
                                    device.Number,
                                    EN_TALON_COMMAND.SetGateOpenClose,
                                    1.0,
                                    token);
            }

            ExecuteOpticCommand(
                "LASER",
                device,
                "Gate OPEN",
ExecuteOpticCommandTokenCallback8,
                cancellationToken);
            ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback9(CancellationToken token)
            {
                return _interfaceManager.ExecuteTalonLaserCommand(
                                    device.Number,
                                    EN_TALON_COMMAND.SetShutterOpenClose,
                                    1.0,
                                    token);
            }

            ExecuteOpticCommand(
                "LASER",
                device,
                "Shutter OPEN",
ExecuteOpticCommandTokenCallback9,
                cancellationToken);
            ST_LASER_STATUS WaitForOpticReadyTokenCallback10(CancellationToken token)
            {
                return _interfaceManager.GetLaserStatus(device.Number, token);
            }

            bool WaitForOpticReadyStatusCallback11(ST_LASER_STATUS status)
            {
                return status.PowerOn && status.GateOn && status.ShutterOpen;
            }

            WaitForOpticReady(
                "LASER",
                $"{FormatInterfaceName(device)} process ready state",
WaitForOpticReadyTokenCallback10,
WaitForOpticReadyStatusCallback11,
                FormatLaserStatus,
                cancellationToken);
        }
    }

    private void ReturnLaserSafe(CancellationToken cancellationToken)
    {
        var devices = GetOpticDevices(EN_EQP_MODULE.TalonLaser);

        foreach (var device in devices)
        {
            try
            {
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback12(CancellationToken token)
                {
                    return _interfaceManager.ExecuteTalonLaserCommand(
                                            device.Number,
                                            EN_TALON_COMMAND.SetLaserOnOff,
                                            0.0,
                                            token);
                }

                ExecuteOpticCommand(
                    "LASER",
                    device,
                    "Laser OFF",
ExecuteOpticCommandTokenCallback12,
                    cancellationToken);
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback13(CancellationToken token)
                {
                    return _interfaceManager.ExecuteTalonLaserCommand(
                                            device.Number,
                                            EN_TALON_COMMAND.SetGateOpenClose,
                                            0.0,
                                            token);
                }

                ExecuteOpticCommand(
                    "LASER",
                    device,
                    "Gate CLOSE",
ExecuteOpticCommandTokenCallback13,
                    cancellationToken);
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback14(CancellationToken token)
                {
                    return _interfaceManager.ExecuteTalonLaserCommand(
                                            device.Number,
                                            EN_TALON_COMMAND.SetShutterOpenClose,
                                            0.0,
                                            token);
                }

                ExecuteOpticCommand(
                    "LASER",
                    device,
                    "Shutter CLOSE",
ExecuteOpticCommandTokenCallback14,
                    cancellationToken);
                ST_LASER_STATUS WaitForOpticReadyTokenCallback15(CancellationToken token)
                {
                    return _interfaceManager.GetLaserStatus(device.Number, token);
                }

                bool WaitForOpticReadyStatusCallback16(ST_LASER_STATUS status)
                {
                    return !status.PowerOn && !status.GateOn && !status.ShutterOpen;
                }

                WaitForOpticReady(
                    "LASER",
                    $"{FormatInterfaceName(device)} safe return state",
WaitForOpticReadyTokenCallback15,
WaitForOpticReadyStatusCallback16,
                    FormatLaserStatus,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
            {
                AddSequenceLog("WARN", "LASER", $"{FormatInterfaceName(device)} safe return failed. {exception.Message}");
            }
        }
    }

    private void PrepareChillerRun(CancellationToken cancellationToken)
    {
        var devices = GetOpticDevices(EN_EQP_MODULE.Chiller);

        if (devices.Count == 0)
        {
            AddSequenceLog("WARN", "CHILLER", "Chiller interface is not registered. Chiller run check skipped.");
            return;
        }

        foreach (var device in devices)
        {
            ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback17(CancellationToken token)
            {
                return _interfaceManager.ExecuteChillerCommand(
                                    device.Number,
                                    EN_CHILLER_COMMAND.Run,
                                    cancellationToken: token);
            }

            ExecuteOpticCommand(
                "CHILLER",
                device,
                "Run",
ExecuteOpticCommandTokenCallback17,
                cancellationToken);
            ST_CHILLER_STATUS WaitForOpticReadyTokenCallback18(CancellationToken token)
            {
                return _interfaceManager.GetChillerStatus(device.Number, token);
            }

            bool WaitForOpticReadyStatusCallback19(ST_CHILLER_STATUS status)
            {
                return status.Running && !status.AlarmOn;
            }

            WaitForOpticReady(
                "CHILLER",
                $"{FormatInterfaceName(device)} run state",
WaitForOpticReadyTokenCallback18,
WaitForOpticReadyStatusCallback19,
                FormatChillerStatus,
                cancellationToken);
        }
    }

    private void PrepareBET(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var devices = GetOpticDevices(EN_EQP_MODULE.Bet);

        if (devices.Count == 0)
        {
            AddSequenceLog("WARN", "BET", "BET interface is not registered. BET ready check skipped.");
            return;
        }

        var target = ReadBETTarget(processPlan, cancellationToken);

        foreach (var device in devices)
        {
            if (target is null)
            {
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback20(CancellationToken token)
                {
                    return _interfaceManager.ExecuteBETCommand(
                                            device.Number,
                                            EN_BET_COMMAND.Refresh,
                                            cancellationToken: token);
                }

                ExecuteOpticCommand(
                    "BET",
                    device,
                    "Refresh",
ExecuteOpticCommandTokenCallback20,
                    cancellationToken);

                var status = _interfaceManager.GetBETStatus(device.Number, cancellationToken);
                ValidateBETStatus(device, status);
                AddSequenceLog("INFO", "BET", $"{FormatInterfaceName(device)} refresh OK. {FormatBETStatus(status)}");
                continue;
            }

            if (target.TableIndex is not null)
            {
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback21(CancellationToken token)
                {
                    return _interfaceManager.ExecuteBETCommand(
                                            device.Number,
                                            EN_BET_COMMAND.MoveTable,
                                            target.TableIndex.Value,
                                            cancellationToken: token);
                }

                ExecuteOpticCommand(
                    "BET",
                    device,
                    $"MoveTable {target.TableIndex.Value}",
ExecuteOpticCommandTokenCallback21,
                    cancellationToken);
            }
            else
            {
                ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback22(CancellationToken token)
                {
                    return _interfaceManager.ExecuteBETCommand(
                                            device.Number,
                                            EN_BET_COMMAND.MoveManual,
                                            target.Magnification,
                                            target.Divergence,
                                            token);
                }

                ExecuteOpticCommand(
                    "BET",
                    device,
                    $"MoveManual MAG={FormatDouble(target.Magnification)}, DIV={FormatDouble(target.Divergence)}",
ExecuteOpticCommandTokenCallback22,
                    cancellationToken);
            }
            ST_BET_STATUS WaitForOpticReadyTokenCallback23(CancellationToken token)
            {
                return _interfaceManager.GetBETStatus(device.Number, token);
            }

            bool WaitForOpticReadyStatusCallback24(ST_BET_STATUS status)
            {
                return IsBETReady(status) &&
                                    IsNear(status.CurrentMagnification, target.Magnification, OpticReadyPositionTolerance) &&
                                    IsNear(status.CurrentDivergence, target.Divergence, OpticReadyPositionTolerance);
            }

            WaitForOpticReady(
                "BET",
                $"{FormatInterfaceName(device)} target position",
WaitForOpticReadyTokenCallback23,
WaitForOpticReadyStatusCallback24,
                FormatBETStatus,
                cancellationToken);
        }
    }

    private void PrepareAttenuator(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var devices = GetOpticDevices(EN_EQP_MODULE.Attenuator);

        if (devices.Count == 0)
        {
            AddSequenceLog("WARN", "ATT", "Attenuator interface is not registered. Attenuator ready check skipped.");
            return;
        }

        foreach (var device in devices)
        {
            var headNo = Math.Clamp(device.Number + 1, 1, 8);
            var target = ReadDoubleAny(
                processPlan.Parameters,
                23.50,
                CreateHeadKeys(headNo, "ATTENUATOR_POSITION"));
            ST_DEVICE_COMMAND_RESULT ExecuteOpticCommandTokenCallback25(CancellationToken token)
            {
                return _interfaceManager.ExecuteAttenuatorCommand(
                                    device.Number,
                                    EN_ATTENUATOR_COMMAND.MoveAbs,
                                    target,
                                    token);
            }

            ExecuteOpticCommand(
                "ATT",
                device,
                $"H{headNo:00} MoveAbs {FormatDouble(target)}",
ExecuteOpticCommandTokenCallback25,
                cancellationToken);
            ST_ATTENUATOR_STATUS WaitForOpticReadyTokenCallback26(CancellationToken token)
            {
                return _interfaceManager.GetAttenuatorStatus(device.Number, token);
            }

            bool WaitForOpticReadyStatusCallback27(ST_ATTENUATOR_STATUS status)
            {
                return IsAttenuatorReady(status) &&
                                    IsNear(status.CurrentPosition, target, OpticReadyPositionTolerance);
            }

            WaitForOpticReady(
                "ATT",
                $"{FormatInterfaceName(device)} H{headNo:00} target position",
WaitForOpticReadyTokenCallback26,
WaitForOpticReadyStatusCallback27,
                FormatAttenuatorStatus,
                cancellationToken);
        }
    }

    private IReadOnlyList<ST_INTERFACE_DATA> GetOpticDevices(EN_EQP_MODULE module)
    {
        int GetDataSortKey28(ST_INTERFACE_DATA data)
        {
            return data.Number;
        }

        string GetDataSortKey29(ST_INTERFACE_DATA data)
        {
            return data.NickName;
        }

        return _interfaceManager.GetInterfaceList(module)
            .OrderBy(GetDataSortKey28)
            .ThenBy(GetDataSortKey29, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ExecuteOpticCommand(
        string source,
        ST_INTERFACE_DATA device,
        string action,
        Func<CancellationToken, ST_DEVICE_COMMAND_RESULT> command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddSequenceLog("INFO", source, $"{FormatInterfaceName(device)} {action} command requested.");
        var result = command(cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"{FormatInterfaceName(device)} {action} command failed. {result.Message}");
        }

        AddSequenceLog("INFO", source, $"{FormatInterfaceName(device)} {action} command OK. {result.Message}");
    }

    private TStatus WaitForOpticReady<TStatus>(
        string source,
        string targetName,
        Func<CancellationToken, TStatus> readStatus,
        Func<TStatus, bool> isReady,
        Func<TStatus, string> describe,
        CancellationToken cancellationToken)
    {
        var timeoutMs = ReadOpticReadyTimeout(cancellationToken);
        var deadline = DateTimeOffset.Now.AddMilliseconds(timeoutMs);
        TStatus lastStatus;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastStatus = readStatus(cancellationToken);

            if (isReady(lastStatus))
            {
                AddSequenceLog("INFO", source, $"{targetName} ready. {describe(lastStatus)}");
                return lastStatus;
            }

            if (DateTimeOffset.Now >= deadline)
            {
                throw new TimeoutException(
                    $"{targetName} ready timeout. Last status: {describe(lastStatus)}");
            }

            if (cancellationToken.WaitHandle.WaitOne(OpticReadyPollIntervalMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private int ReadOpticReadyTimeout(CancellationToken cancellationToken)
    {
        var value = _settingManager.GetValue(
            EN_SETTING_TAB.Motor,
            "MoveTimeout",
            OpticReadyDefaultTimeoutMs.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(result, OpticReadyDefaultTimeoutMs)
            : OpticReadyDefaultTimeoutMs;
    }

    private ST_OPTIC_READY_BET_TARGET? ReadBETTarget(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var tableIndex = ReadNullableIntAny(
            processPlan.Parameters,
            "BET_TABLE_INDEX",
            "BET_INDEX",
            "BET_NO",
            "BET_TABLE_NO");

        if (tableIndex is not null)
        {
            var table = _interfaceManager.LoadBETData(cancellationToken);
            bool MatchItem30(ST_BET_TABLE_DATA item)
            {
                return item.Index == tableIndex.Value;
            }

            var row = table.FirstOrDefault(MatchItem30);

            if (row is null)
            {
                throw new InvalidOperationException($"BET table row was not found: {tableIndex.Value}");
            }

            AddSequenceLog(
                "INFO",
                "BET",
                $"BET target loaded from table. Index={tableIndex.Value}, MAG={FormatDouble(row.Magnification)}, DIV={FormatDouble(row.Divergence)}");

            return new ST_OPTIC_READY_BET_TARGET(
                tableIndex.Value,
                row.Magnification,
                row.Divergence);
        }

        var magnification = ReadNullableDoubleAny(
            processPlan.Parameters,
            "BET_MAGNIFICATION",
            "BET_TARGET_MAGNIFICATION",
            "BET_MAG_TARGET",
            "BET_MAG",
            "BEAM_EXPANDER_MAGNIFICATION");
        var divergence = ReadNullableDoubleAny(
            processPlan.Parameters,
            "BET_DIVERGENCE",
            "BET_TARGET_DIVERGENCE",
            "BET_DIV_TARGET",
            "BET_DIV",
            "BEAM_EXPANDER_DIVERGENCE");

        if (magnification is not null && divergence is not null)
        {
            AddSequenceLog(
                "INFO",
                "BET",
                $"BET target loaded from recipe. MAG={FormatDouble(magnification.Value)}, DIV={FormatDouble(divergence.Value)}");

            return new ST_OPTIC_READY_BET_TARGET(
                null,
                magnification.Value,
                divergence.Value);
        }

        AddSequenceLog("WARN", "BET", "BET recipe target is not configured. BET move skipped, status refresh only.");
        return null;
    }

    private void RunProcessStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        BuildAutomationScript(processPlan, cancellationToken);
        UploadAutomationScript(processPlan, cancellationToken);
        MoveStageToScanStart(processPlan, cancellationToken);
        RunScannerProcess(processPlan, cancellationToken);
    }

    private void MoveStageToScanStart(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = processPlan.Parameters;
        var startX = ReadDoubleAny(parameters, 0.0, "STAGE_START_POS_X");
        var startY = ReadDoubleAny(parameters, 0.0, "STAGE_START_POS_Y");
        var reviewCameraAlignKeyPosX = ReadDoubleAny(parameters, 0.0, "REVIEW_CAMERA_ALIGN_KEY_POS_X");
        var reviewCameraAlignKeyPosY = ReadDoubleAny(parameters, startY, "REVIEW_CAMERA_ALIGN_KEY_POS_Y");
        var reviewToHead1GapY = ReadDoubleAny(parameters, 0.0, "REVIEW_TO_HEAD1_GAP_Y");
        var headGapY = ReadDoubleAny(parameters, 0.0, "HeadGapY");
        var head1AlignKeyY = reviewCameraAlignKeyPosY + reviewToHead1GapY;
        var head2AlignKeyY = head1AlignKeyY + headGapY;
        var delayLengthY = Math.Abs(ReadDoubleAny(parameters, 0.0, "SCAN_START_DELAY_LENGTH_Y"));
        var stageScanDirectionY = ReadDirectionAny(
            parameters,
            -1.0,
            "STAGE_SCAN_DIRECTION_Y");
        var firstShotStageY = ResolveFirstShotStageY(processPlan, stageScanDirectionY);
        var scanStartY = reviewCameraAlignKeyPosY + firstShotStageY - (stageScanDirectionY * delayLengthY);
        var stageYSpeed = ReadDoubleAny(parameters, 100.0, "STAGE_Y_SPEED");

        SendProcessControlCommand(
            "PROCESS_STAGE_START_MOVE",
            [
                ("X", FormatDouble(startX)),
                ("Y", FormatDouble(startY)),
                ("SPEED_Y", FormatDouble(stageYSpeed))
            ],
            cancellationToken);

        SendProcessControlCommand(
            "PROCESS_STAGE_SCAN_START_MOVE",
            [
                ("Y", FormatDouble(scanStartY)),
                ("REVIEW_CAMERA_X", FormatDouble(reviewCameraAlignKeyPosX)),
                ("REVIEW_CAMERA_ALIGN_KEY_Y", FormatDouble(reviewCameraAlignKeyPosY)),
                ("REVIEW_TO_HEAD1_GAP_Y", FormatDouble(reviewToHead1GapY)),
                ("HeadGapY", FormatDouble(headGapY)),
                ("HEAD1_ALIGN_KEY_Y", FormatDouble(head1AlignKeyY)),
                ("HEAD2_ALIGN_KEY_Y", FormatDouble(head2AlignKeyY)),
                ("FIRST_SHOT_STAGE_Y", FormatDouble(reviewCameraAlignKeyPosY + firstShotStageY)),
                ("DELAY_LENGTH_Y", FormatDouble(delayLengthY)),
                ("SCAN_DIRECTION_Y", FormatDouble(stageScanDirectionY)),
                ("SPEED_Y", FormatDouble(stageYSpeed))
            ],
            cancellationToken);

        AddProcessLog(
            "INFO",
            "STAGE",
            $"Stage scan start ready. Start=({FormatDouble(startX)}, {FormatDouble(startY)}), ReviewCameraAlignKey=({FormatDouble(reviewCameraAlignKeyPosX)}, {FormatDouble(reviewCameraAlignKeyPosY)}), ReviewToHead1GapY={FormatDouble(reviewToHead1GapY)}, HeadGapY={FormatDouble(headGapY)}, FirstShotY={FormatDouble(reviewCameraAlignKeyPosY + firstShotStageY)}, DelayY={FormatDouble(delayLengthY)}, ScanStartY={FormatDouble(scanStartY)}");
    }

    private double ResolveFirstShotStageY(
        ST_PROCESS_PLAN processPlan,
        double stageScanDirectionY)
    {
        var processModel = _processModel?.Plan.ProcessId.Equals(
                processPlan.ProcessId,
                StringComparison.OrdinalIgnoreCase) == true
            ? _processModel
            : BuildProcessModel(processPlan);
        IEnumerable<ST_RECIPE_HOLE_POINT> SelectHead31(ST_HEAD_PROCESS_DATA head)
        {
            return head.ProcessPoints;
        }

        var processPoints = processModel.Heads
            .SelectMany(SelectHead31)
            .ToArray();

        if (processPoints.Length == 0)
        {
            var reviewToHead1GapY = ReadDoubleAny(processPlan.Parameters, 0.0, "REVIEW_TO_HEAD1_GAP_Y");
            var headGapY = ReadDoubleAny(processPlan.Parameters, 0.0, "HeadGapY");
            return reviewToHead1GapY + headGapY;
        }
        double MaxPointCallback32(ST_RECIPE_HOLE_POINT point)
        {
            return point.StageY;
        }

        double MinPointCallback33(ST_RECIPE_HOLE_POINT point)
        {
            return point.StageY;
        }

        return stageScanDirectionY < 0.0
            ? processPoints.Max(MaxPointCallback32)
            : processPoints.Min(MinPointCallback33);
    }

    private void SendProcessControlCommand(
        string commandName,
        IReadOnlyList<(string Key, string Value)> arguments,
        CancellationToken cancellationToken)
    {
        var command = FormatCommand(commandName, arguments);

        AddProcessLog("INFO", "STAGE", $"Stage command requested. {command}");
        var response = _interfaceManager.ExecuteFunction(
            EN_EQP_MODULE.WonikCtrl,
            0,
            command,
            cancellationToken);

        if (!IsProcessControlSuccessResponse(response))
        {
            AddProcessLog("ERROR", "STAGE", $"Stage command rejected. {commandName}: {FormatResponseForLog(response)}");
            throw new InvalidOperationException(
                $"Stage command failed: {commandName}. Response={FormatResponseForLog(response)}");
        }

        AddProcessLog("INFO", "STAGE", $"Stage command accepted. {response}");
    }

    private void RunInspectionStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        RunReviewStep(processPlan, cancellationToken);
    }

    private void BuildAutomationScript(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var bufferedRun = IsBufferedRunEnabled(processPlan.Parameters);
        var scriptDirectoryName = bufferedRun
            ? BufferedRunScriptDirectoryName
            : StandardScriptDirectoryName;

        _lastScript = _automationScriptFile.Build(
            GetProcessModel(),
            scriptDirectoryName,
            cancellationToken);
        _scriptCreatedAt = _lastScript.CreatedAt;
        _statistics = BuildStatistics(_snapshot.HeadPreviews, 35.0, TimeSpan.FromSeconds(12));
        AddProcessLog(
            "INFO",
            "SCRIPT",
            $"{_lastScript.FileName} generated in {scriptDirectoryName}. ({_lastScript.TotalPoints} points)");

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            _snapshot.HeadPreviews,
            EN_SCRIPT_STATUS.Created,
            EN_PROCESS_STEP.Process,
            null);
    }

    private void UploadAutomationScript(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_lastScript is null)
        {
            throw new InvalidOperationException("Automation script is not built.");
        }
        bool FilterScript34(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return script.TotalPoints > 0;
        }

        foreach (var headScript in _lastScript.HeadScripts.Where(FilterScript34))
        {
            AddProcessLog(
                "INFO",
                "A1_UPLOAD",
                $"Upload requested. H{headScript.HeadNo:00} -> Automation1 #{headScript.AutomationNo}, {headScript.FileName}");
            var response = _automationManager.UploadScript(
                headScript.FilePath,
                headScript.FileName,
                headScript.AutomationNo,
                cancellationToken: cancellationToken);
            AddProcessLog(
                "INFO",
                "A1_UPLOAD",
                $"Upload completed. H{headScript.HeadNo:00}, Automation1 #{headScript.AutomationNo}: {response}");
        }
    }

    private void RunScannerProcess(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        SetStationState(
            EN_STATION_STATE.Process,
            EN_PROCESS_STEP.Process,
            EN_SCRIPT_STATUS.Running,
            $"Scanner run started. Process: {processPlan.ProcessId}");

        _scriptStartedAt = DateTimeOffset.Now;
        var runningPreview = SetHeadStatus(_snapshot.HeadPreviews, EN_PROCESS_STEP.Process);
        _statistics = BuildStatistics(runningPreview, 56.3, TimeSpan.FromSeconds(45));
        StartProduct(runningPreview, cancellationToken);

        if (_lastScript is null)
        {
            throw new InvalidOperationException("Automation script is not built.");
        }
        bool FilterScript35(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return script.TotalPoints > 0;
        }

        var headScripts = _lastScript.HeadScripts
            .Where(FilterScript35)
            .ToArray();
        var bufferedRun = IsBufferedRunEnabled(processPlan.Parameters);

        if (bufferedRun)
        {
            RunBufferedHeadScripts(processPlan, headScripts, cancellationToken);
        }
        else
        {
            foreach (var headScript in headScripts)
            {
                AddProcessLog(
                    "INFO",
                    "A1_RUN",
                    $"Automation1 #{headScript.AutomationNo} Task {headScript.TaskNo} run requested. H{headScript.HeadNo:00}, {headScript.FileName}");
                var response = _automationManager.RunScript(
                    headScript.FileName,
                    headScript.TaskNo,
                    headScript.AutomationNo,
                    cancellationToken: cancellationToken);
                AddProcessLog(
                    "INFO",
                    "A1_RUN",
                    $"Run command accepted. H{headScript.HeadNo:00}, Automation1 #{headScript.AutomationNo}, Task {headScript.TaskNo}: {response}");
            }
        }

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            runningPreview,
            EN_SCRIPT_STATUS.Running,
            EN_PROCESS_STEP.Process,
            null);
    }

    private void RunBufferedHeadScripts(
        ST_PROCESS_PLAN processPlan,
        IReadOnlyList<ST_AUTOMATION1_HEAD_SCRIPT> headScripts,
        CancellationToken cancellationToken)
    {
        var queueSize = ReadBufferedRunQueueSize(processPlan.Parameters);
        var linesPerCommand = ReadBufferedRunLinesPerCommand(processPlan.Parameters);
        var timeoutMs = ReadBufferedRunTimeoutMs(processPlan.Parameters);
        int HandleGroups36(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return script.AutomationNo;
        }

        var groups = headScripts
            .GroupBy(HandleGroups36)
            .ToArray();

        foreach (var group in groups)
        {
            string SelectScript37(ST_AUTOMATION1_HEAD_SCRIPT script)
            {
                return $"H{script.HeadNo:00}/T{script.TaskNo}";
            }

            AddProcessLog(
                "INFO",
                "A1_RUN",
                $"Automation1 #{group.Key} buffered run group requested. Heads={string.Join(", ", group.Select(SelectScript37))}, Queue={queueSize}, LinesPerCommand={linesPerCommand}, TimeoutMs={timeoutMs}");
        }

        using var bufferedRunCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ST_BUFFERED_RUN_GROUP_RESULT SelectGroup38(IGrouping<int, ST_AUTOMATION1_HEAD_SCRIPT> group)
        {
            return RunBufferedHeadScriptGroup(
                            group.Key,
                            group.ToArray(),
                            queueSize,
                            linesPerCommand,
                            timeoutMs,
                            bufferedRunCancellation.Token);
        }

        var results = new List<ST_BUFFERED_RUN_GROUP_RESULT>();
        Exception? firstException = null;

        foreach (IGrouping<int, ST_AUTOMATION1_HEAD_SCRIPT> group in groups)
        {
            try
            {
                results.Add(SelectGroup38(group));
            }
            catch (Exception exception)
            {
                firstException = exception;
                AddProcessLog("ERROR", "A1_RUN", $"Buffered run group failed. {firstException.Message}");
                break;
            }
        }

        if (firstException is not null)
        {
            bufferedRunCancellation.Cancel();
            StopBufferedRunTasks(headScripts, firstException);

            ExceptionDispatchInfo.Capture(firstException).Throw();
        }

        foreach (var result in results)
        {
            AddProcessLog(
                "INFO",
                "A1_RUN",
                $"Buffered run completed. Automation1 #{result.AutomationNo}, Heads={result.HeadSummary}: {result.Response}");
        }
    }

    private ST_BUFFERED_RUN_GROUP_RESULT RunBufferedHeadScriptGroup(
        int automationNo,
        IReadOnlyList<ST_AUTOMATION1_HEAD_SCRIPT> headScripts,
        int queueSize,
        int linesPerCommand,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        ST_BUFFERED_SCRIPT_RUN_ITEM SelectScript39(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return new ST_BUFFERED_SCRIPT_RUN_ITEM(
                                script.FilePath,
                                script.FileName,
                                script.TaskNo);
        }

        var response = _automationManager.RunBufferedScripts(
            headScripts
                .Select(SelectScript39)
                .ToArray(),
            automationNo,
            queueSize,
            linesPerCommand,
            timeoutMs,
            cancellationToken);
        string SelectScript40(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return $"H{script.HeadNo:00}/T{script.TaskNo}";
        }

        return new ST_BUFFERED_RUN_GROUP_RESULT(
            automationNo,
            string.Join(", ", headScripts.Select(SelectScript40)),
            response);
    }

    private void StopBufferedRunTasks(
        IReadOnlyList<ST_AUTOMATION1_HEAD_SCRIPT> headScripts,
        Exception cause)
    {
        List<CBufferedRunTarget> targets = new List<CBufferedRunTarget>();
        foreach (ST_AUTOMATION1_HEAD_SCRIPT script in headScripts)
        {
            if (script.TotalPoints <= 0)
            {
                continue;
            }

            CBufferedRunTarget? matchingTarget = null;
            foreach (CBufferedRunTarget target in targets)
            {
                if (target.AutomationNo == script.AutomationNo && target.TaskNo == script.TaskNo)
                {
                    matchingTarget = target;
                    break;
                }
            }

            if (matchingTarget is null)
            {
                matchingTarget = new CBufferedRunTarget(script.AutomationNo, script.TaskNo);
                targets.Add(matchingTarget);
            }

            matchingTarget.AddHead(script.HeadNo);
        }

        AddProcessLog(
            "WARN",
            "A1_RUN",
            $"Stopping buffered run tasks after failure. Reason={cause.Message}, Targets={targets.Count}");

        List<ST_BUFFERED_RUN_STOP_RESULT> stopTasks = new List<ST_BUFFERED_RUN_STOP_RESULT>();
        foreach (CBufferedRunTarget target in targets)
        {
            stopTasks.Add(StopBufferedRunTask(target));
        }

        ST_BUFFERED_RUN_STOP_RESULT[] stopResults = stopTasks.ToArray();

        foreach (var stopResult in stopResults)
        {
            if (stopResult.IsSuccess)
            {
                AddProcessLog(
                    "WARN",
                    "A1_RUN",
                    $"Buffered run stop requested. Automation1 #{stopResult.AutomationNo}, Task {stopResult.TaskNo}, Heads={stopResult.HeadSummary}: {stopResult.Response}");
            }
            else
            {
                AddProcessLog(
                    "ERROR",
                    "A1_RUN",
                    $"Buffered run stop failed. Automation1 #{stopResult.AutomationNo}, Task {stopResult.TaskNo}, Heads={stopResult.HeadSummary}: {stopResult.ErrorMessage}");
            }
        }
    }

    private ST_BUFFERED_RUN_STOP_RESULT StopBufferedRunTask(CBufferedRunTarget target)
    {
        try
        {
            string response = _automationManager.StopTask(
                target.TaskNo,
                target.AutomationNo,
                CancellationToken.None);
            return new ST_BUFFERED_RUN_STOP_RESULT(
                target.AutomationNo,
                target.TaskNo,
                target.HeadSummary,
                true,
                response,
                "");
        }
        catch (Exception ex)
        {
            return new ST_BUFFERED_RUN_STOP_RESULT(
                target.AutomationNo,
                target.TaskNo,
                target.HeadSummary,
                false,
                "",
                ex.Message);
        }
    }

    private void RunReviewStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddProcessLog("INFO", "REVIEW", "Review step reserved. Stage PC Y move, Vision X move, and Vision measure will be connected later.");
        return;
    }

    private void RunPowerCheckStep(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var powerMeterStatus = _interfaceManager.GetPowerMeterStatus(cancellationToken);
        AddProcessLog(
            "INFO",
            "POWER",
            $"Power check step reserved. Current power={powerMeterStatus.MeasuredPower.ToString("F4", CultureInfo.InvariantCulture)} {powerMeterStatus.Unit}");
    }

    private void CompleteProcess(CancellationToken cancellationToken)
    {
        var scriptStatus = GetCurrentScriptStatusForComplete();
        if (scriptStatus == EN_SCRIPT_STATUS.Completed)
        {
            _scriptCompletedAt = DateTimeOffset.Now;
            AddProcessLog("INFO", "A1_TASK", "Automation1 task completed.");
        }
        else
        {
            AddProcessLog("INFO", "A1_TASK", "Automation1 script was not executed. PROCESS step was skipped.");
        }

        var completedPreview = SetHeadStatus(_snapshot.HeadPreviews, EN_PROCESS_STEP.Completed);
        _statistics = BuildStatistics(completedPreview, 100.0, TimeSpan.FromSeconds(80));
        var result = new ST_PROCESS_RESULT(true, "Station PROCESS completed.", DateTimeOffset.Now);
        CompleteProduct(completedPreview, result, cancellationToken);
        ReportProcessResult(result, "COMPLETE", cancellationToken);

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            completedPreview,
            scriptStatus,
            EN_PROCESS_STEP.Completed,
            result);

        SetStationState(
            EN_STATION_STATE.Complete,
            EN_PROCESS_STEP.Completed,
            scriptStatus,
            "Process station completed.");
    }

    public ST_STATION_PROCESS_STATUS Stop(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (mobjSequenceLock)
        {
            mobjSequenceCancellation?.Cancel();
            meSequence = EN_STATION_SEQUENCE.Stop;
        }

        base.Start(10, "StationProcess");
        return _snapshot;
    }

    private ST_STATION_PROCESS_STATUS ProcessStop(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddProcessLog("WARN", "OPERATOR", "Station PROCESS stopped by operator.");
        ReturnLaserSafe(cancellationToken);
        _statistics = BuildStatistics(_snapshot.HeadPreviews, _statistics.ProgressPercent, _statistics.ElapsedTime);
        var result = new ST_PROCESS_RESULT(false, "Station PROCESS stopped by operator.", DateTimeOffset.Now);
        StopProduct(result.Message, cancellationToken);
        ReportProcessResult(result, "STOP", cancellationToken);

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            SetHeadStatus(_snapshot.HeadPreviews, EN_PROCESS_STEP.Stopped),
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.Stopped,
            result);
        MarkRunningAutoSteps(AutoStepStop);
        RefreshSnapshot();

        SetStationState(
            EN_STATION_STATE.Stopped,
            EN_PROCESS_STEP.Stopped,
            EN_SCRIPT_STATUS.NotCreated,
            "Station stopped by operator.");

        return _snapshot;
    }

    public ST_STATION_PROCESS_STATUS Reset(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (mobjSequenceLock)
        {
            if (meSequence != EN_STATION_SEQUENCE.Ready ||
                _stationStatus.State == EN_STATION_STATE.Process)
            {
                AddProcessLog("WARN", "RESET", "Reset ignored while station is running.");
                RefreshSnapshot();
                return _snapshot;
            }

            meSequence = EN_STATION_SEQUENCE.Reset;
        }

        base.Start(10, "StationProcess");
        return _snapshot;
    }

    private ST_STATION_PROCESS_STATUS ProcessReset(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_stationStatus.State == EN_STATION_STATE.Alarm)
        {
            var interLock = GetInterLockSummary(cancellationToken);

            if (!interLock.CanAutoRun)
            {
                var message = FormatInterLockBlockedMessage(interLock);
                AddProcessLog("WARN", "RESET", $"Reset blocked. {message}");
                _snapshot = CreateSnapshot(
                    _snapshot.ProcessPlan,
                    SetHeadStatus(_snapshot.HeadPreviews, EN_PROCESS_STEP.Error),
                    EN_SCRIPT_STATUS.Error,
                    EN_PROCESS_STEP.Error,
                    new ST_PROCESS_RESULT(false, message, DateTimeOffset.Now));
                SetStationState(
                    EN_STATION_STATE.Alarm,
                    EN_PROCESS_STEP.Error,
                    EN_SCRIPT_STATUS.Error,
                    $"Reset blocked. {message}");
                return _snapshot;
            }

            AddProcessLog("INFO", "RESET", "Alarm reset accepted. InterLock is clear.");
        }

        _processLogs.Clear();
        _lastInterLockItems = [];
        _scriptCreatedAt = null;
        _scriptStartedAt = null;
        _scriptCompletedAt = null;
        _lastScript = null;
        _processModel = null;
        ResetAutoStepStates();
        _statistics = EmptyStatistics();
        _snapshot = CreateSnapshot(
            null,
            [],
            EN_SCRIPT_STATUS.NotCreated,
            EN_PROCESS_STEP.Idle,
            null);

        SetStationState(
            EN_STATION_STATE.Idle,
            EN_PROCESS_STEP.Idle,
            EN_SCRIPT_STATUS.NotCreated,
            "Station reset to idle.");

        return _snapshot;
    }

    private ST_STATION_PROCESS_STATUS CreateSnapshot(
        ST_PROCESS_PLAN? processPlan,
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep,
        ST_PROCESS_RESULT? result)
    {
        return new ST_STATION_PROCESS_STATUS(
            processPlan,
            _processModel,
            preview,
            scriptStatus,
            processStep,
            result,
            BuildProcessSequence(scriptStatus, processStep),
            BuildCurrentStepDetails(processPlan, scriptStatus, processStep),
            BuildProcessSummary(processPlan, _statistics),
            _processLogs.ToArray(),
            BuildScriptStatusItems(processPlan, scriptStatus, result),
            BuildScriptLifecycleItems(scriptStatus, processStep),
            _lastInterLockItems,
            _statistics);
    }

    private ST_INTERLOCK_SUMMARY GetInterLockSummary(CancellationToken cancellationToken)
    {
        var snapshot = GetDeviceStatus(cancellationToken);
        var interLock = _interLockManager.Evaluate(snapshot);
        _lastInterLockItems = interLock.Items;
        return interLock;
    }

    private ST_DEVICE_STATUS GetDeviceStatus(CancellationToken cancellationToken)
    {
        var io = _motionManager.GetIoStatus(cancellationToken);
        var motors = _motionManager.GetAxisStatus(cancellationToken);
        var laserStatus = _interfaceManager.GetLaserStatus(cancellationToken);
        var chillerStatus = _interfaceManager.GetChillerStatus(cancellationToken);
        var attenuatorStatus = _interfaceManager.GetAttenuatorStatus(cancellationToken);
        var betStatus = _interfaceManager.GetBETStatus(cancellationToken);
        var powerMeterStatus = _interfaceManager.GetPowerMeterStatus(cancellationToken);

        return new ST_DEVICE_STATUS(
            io,
            motors,
            laserStatus,
            chillerStatus,
            attenuatorStatus,
            betStatus,
            powerMeterStatus);
    }

    private void CreateProduct(
        ST_PROCESS_MODEL processModel,
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        CancellationToken cancellationToken)
    {
        if (_productManager is null)
        {
            return;
        }

        var processPlan = processModel.Plan;
        var productId = ReadAnyParameter(processPlan, processPlan.ProductId, "ProductId", "PRODUCT_ID");
        var panelId = ReadAnyParameter(processPlan, processPlan.PanelId, "PanelId", "PANEL_ID", "PanelID");
        var lotId = ReadAnyParameter(processPlan, processPlan.LotId, "LotId", "LOT_ID", "LotID");
        int HandleHeadPointCounts42(ST_HEAD_PATH_DATA head)
        {
            return head.HeadNo;
        }

        int HandleHeadPointCounts43(ST_HEAD_PATH_DATA head)
        {
            return head.Points.Count;
        }

        var headPointCounts = preview.ToDictionary(
HandleHeadPointCounts42,
HandleHeadPointCounts43);

        _productManager.CreateProduct(
            processPlan.ProcessId,
            productId,
            panelId,
            lotId,
            processPlan.RecipeId,
            processPlan.Parameters,
            headPointCounts,
            cancellationToken);
        _processModel = processModel with { Product = _productManager.Current };
        AddProcessLog("INFO", "PRODUCT", $"Product created ({_productManager.Current?.ProductId ?? processPlan.ProcessId})");
    }

    private void StartProduct(
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        CancellationToken cancellationToken)
    {
        var productId = GetCurrentProcessProductId();
        if (_productManager is null || string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        _productManager.StartProduct(productId, cancellationToken);
        bool FilterHead44(ST_HEAD_PATH_DATA head)
        {
            return head.Status == EN_HEAD_PROCESS_STATUS.Running;
        }

        foreach (var head in preview.Where(FilterHead44))
        {
            _productManager.SetHeadRunning(productId, head.HeadNo, cancellationToken);
        }

        AddProcessLog("INFO", "PRODUCT", $"Product started ({productId})");
    }

    private void CompleteProduct(
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        ST_PROCESS_RESULT result,
        CancellationToken cancellationToken)
    {
        var productId = GetCurrentProcessProductId();
        if (_productManager is null || string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        foreach (var head in preview)
        {
            _productManager.SetHeadResult(
                productId,
                head.HeadNo,
                result.IsSuccess,
                result.IsSuccess ? "" : "1",
                result.Message,
                cancellationToken);
        }

        _productManager.CompleteProduct(
            productId,
            result.IsSuccess,
            result.Message,
            cancellationToken);
        AddProcessLog("INFO", "PRODUCT", $"Product completed ({productId})");
    }

    private void StopProduct(
        string message,
        CancellationToken cancellationToken)
    {
        var productId = GetCurrentProcessProductId();
        if (_productManager is null || string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        _productManager.StopProduct(productId, message, cancellationToken);
        AddProcessLog("WARN", "PRODUCT", $"Product stopped ({productId})");
    }

    private void SetProductError(
        string message,
        CancellationToken cancellationToken)
    {
        var productId = GetCurrentProcessProductId();
        if (_productManager is null || string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        _productManager.SetError(productId, message, cancellationToken);
        AddProcessLog("ERROR", "PRODUCT", $"Product error ({productId})");
    }

    private void ReportProcessResult(
        ST_PROCESS_RESULT result,
        string action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processId = _snapshot.ProcessPlan?.ProcessId ?? "-";
        var recipeId = _snapshot.ProcessPlan?.RecipeId ?? "-";
        var productId = GetCurrentProcessProductId() ?? processId;
        var reportState = result.IsSuccess ? "OK" : "NG";
        var detail = FormatProcessResultDetail(result, processId, recipeId, productId);
        var hostNickName = _interfaceManager.GetInterfaceData(EN_EQP_MODULE.WonikCtrl, 0)?.NickName ?? "WONIK_CTRL";

        AddProcessLog("INFO", "RESULT", $"{action} result reported. {detail}");
        _logManager?.WriteStationState(
            _stationStatus.StationName,
            _stationStatus.State.ToString().ToUpperInvariant(),
            $"RESULT_{action}",
            detail);
        _logManager?.WriteInterfaceCommand(
            EN_EQP_MODULE.WonikCtrl,
            hostNickName,
            $"PROCESS_RESULT_{action}",
            reportState,
            detail);

        return;
    }

    private string? GetCurrentProcessProductId()
    {
        if (_productManager?.Current is null || _snapshot.ProcessPlan is null)
        {
            return null;
        }

        return _productManager.Current.ProcessId.Equals(
            _snapshot.ProcessPlan.ProcessId,
            StringComparison.OrdinalIgnoreCase)
                ? _productManager.Current.ProductId
                : null;
    }

    private static IReadOnlyList<ST_HEAD_PATH_DATA> BuildPreview(
        ST_PROCESS_MODEL processModel,
        EN_HEAD_PROCESS_STATUS status)
    {
        ST_HEAD_PATH_DATA SelectHead45(ST_HEAD_PROCESS_DATA head)
        {
            return new ST_HEAD_PATH_DATA(
                            head.HeadNo,
                            status,
                            head.Path);
        }

        return processModel.Heads
            .Select(SelectHead45)
            .ToArray();
    }

    private ST_PROCESS_PLAN BuildRuntimeProcessPlan(
        ST_PROCESS_PLAN processPlan,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(
            processPlan.Parameters,
            StringComparer.OrdinalIgnoreCase);
        var processSettings = new List<ST_SYSTEM_PARAMETER>();
        processSettings.AddRange(_settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken));
        processSettings.AddRange(_settingManager.LoadSection(EN_SETTING_TAB.Motor, cancellationToken));

        foreach (var setting in processSettings)
        {
            if (string.IsNullOrWhiteSpace(setting.Key))
            {
                continue;
            }

            parameters[setting.Key] = setting.Value;
        }

        return processPlan with { Parameters = parameters };
    }

    private static ST_PROCESS_MODEL BuildProcessModel(ST_PROCESS_PLAN processPlan)
    {
        var parameters = new Dictionary<string, string>(
            processPlan.Parameters,
            StringComparer.OrdinalIgnoreCase);
        var holePlan = CRecipeHolePlan.Build(parameters);
        var defaultMarkSpeed = ReadDoubleAny(
            parameters,
            900.0,
            "SCANNER_MARK_SPEED");
        var defaultJumpSpeed = ReadDoubleAny(
            parameters,
            1500.0,
            "SCANNER_JUMP_SPEED");
        int HandlePointsByHead46(ST_RECIPE_HOLE_POINT point)
        {
            return point.HeadNo;
        }

        int HandlePointsByHead47(IGrouping<int, ST_RECIPE_HOLE_POINT> group)
        {
            return group.Key;
        }

        ST_RECIPE_HOLE_POINT[] HandlePointsByHead48(IGrouping<int, ST_RECIPE_HOLE_POINT> group)
        {
            return group.ToArray();
        }

        var pointsByHead = holePlan.Points
            .GroupBy(HandlePointsByHead46)
            .ToDictionary(
HandlePointsByHead47,
HandlePointsByHead48,
                EqualityComparer<int>.Default);
        ST_HEAD_PROCESS_DATA SelectHeadNo49(int headNo)
        {
            var headPoints = pointsByHead.TryGetValue(headNo, out var values)
                ? values
                : [];
            ST_PATH_POINT SelectPoint1(ST_RECIPE_HOLE_POINT point)
            {
                return new ST_PATH_POINT(point.DesignX, point.DesignY);
            }

            var previewPath = headPoints
                .Select(SelectPoint1)
                .ToArray();
            var scannerMarkSpeed = ReadDoubleAny(
                parameters,
                defaultMarkSpeed,
                CreateHeadKeys(headNo, "SCANNER_MARK_SPEED"));
            var scannerJumpSpeed = ReadDoubleAny(
                parameters,
                defaultJumpSpeed,
                CreateHeadKeys(headNo, "SCANNER_JUMP_SPEED"));

            return new ST_HEAD_PROCESS_DATA(
                headNo,
                ReadDoubleAny(parameters, 1.0, CreateHeadKeys(headNo, "LASER_POWER")),
                ReadDoubleAny(parameters, 20.0, CreateHeadKeys(headNo, "LASER_FREQUENCY")),
                ReadIntAny(parameters, 10, CreateHeadKeys(headNo, "SHOT_COUNT")),
                ReadDoubleAny(parameters, 0.0, CreateHeadKeys(headNo, "SHOT_TIME_DELAY")),
                scannerMarkSpeed,
                scannerJumpSpeed,
                ReadDoubleAny(parameters, 0.0, CreateHeadKeys(headNo, "DOE_Z_POSITION")),
                previewPath)
            {
                AutomationNo = ReadHeadAutomationNo(parameters, headNo),
                TaskNo = ReadHeadAutomationTaskNo(parameters, headNo),
                ScriptFileName = $"PROCESS_H{headNo:00}.ascript",
                ProcessPoints = headPoints
            };
        }
        var heads = Enumerable.Range(1, holePlan.HeadCount)
            .Select(SelectHeadNo49)
            .ToArray();

        return new ST_PROCESS_MODEL(
            processPlan,
            null,
            heads,
            parameters,
            DateTimeOffset.Now);
    }

    private static IReadOnlyList<ST_HEAD_PATH_DATA> SetHeadStatus(
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        EN_PROCESS_STEP processStep)
    {
        ST_HEAD_PATH_DATA SelectHead50(ST_HEAD_PATH_DATA head)
        {
            EN_HEAD_PROCESS_STATUS EvaluateProcessStepSwitch1()
            {
                var switchValue = processStep;
                switch (switchValue)
                {
                    case EN_PROCESS_STEP.Process:
                        return head.Points.Count > 0
                                ? EN_HEAD_PROCESS_STATUS.Running
                                : EN_HEAD_PROCESS_STATUS.Disabled;
                    case EN_PROCESS_STEP.Completed:
                        return head.Points.Count > 0
                                ? EN_HEAD_PROCESS_STATUS.Completed
                                : EN_HEAD_PROCESS_STATUS.Disabled;
                    case EN_PROCESS_STEP.Stopped:
                        return EN_HEAD_PROCESS_STATUS.Ready;
                    case EN_PROCESS_STEP.Error:
                        return EN_HEAD_PROCESS_STATUS.Error;
                    default:
                        return head.Status;
                }
            }

            var status = EvaluateProcessStepSwitch1();

            return head with { Status = status };
        }
        return preview
            .Select(SelectHead50)
            .ToArray();
    }

    private IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> BuildProcessSequence(
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep)
    {
        ST_PROCESS_DISPLAY_ITEM SelectStep51(ST_STATION_PROCESS_FLOW_ITEM step)
        {
            return new ST_PROCESS_DISPLAY_ITEM(
                            step.Order.ToString(CultureInfo.InvariantCulture),
                            step.StepName,
                            ReadAutoStepState(step.StepKey, scriptStatus, processStep));
        }

        return ProcessFlowItems
            .Select(SelectStep51)
            .ToArray();
    }

    private IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> BuildCurrentStepDetails(
        ST_PROCESS_PLAN? processPlan,
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep)
    {
        if (processPlan is null)
        {
            ST_PROCESS_DISPLAY_ITEM SelectStep52(ST_AUTO_STEP_INFO step)
            {
                return new ST_PROCESS_DISPLAY_ITEM(step.DisplayName, AutoStepWait);
            }

            return AutoStepInfos
                .Select(SelectStep52)
                .ToArray();
        }
        ST_PROCESS_DISPLAY_ITEM SelectStep53(ST_AUTO_STEP_INFO step)
        {
            return new ST_PROCESS_DISPLAY_ITEM(
                            step.DisplayName,
                            ReadAutoStepState(step.Key, scriptStatus, processStep));
        }

        return AutoStepInfos
            .Select(SelectStep53)
            .ToArray();
    }

    private static IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> BuildProcessSummary(
        ST_PROCESS_PLAN? processPlan,
        ST_PROCESS_STATISTICS statistics)
    {
        var glassId = processPlan is null
            ? "-"
            : string.IsNullOrWhiteSpace(processPlan.PanelId)
                ? processPlan.ProductId
                : processPlan.PanelId;

        return
        [
            new("Product Glass ID", string.IsNullOrWhiteSpace(glassId) ? "-" : glassId),
            new("Tact Time", FormatDuration(statistics.EstimatedTime))
        ];
    }

    private IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> BuildScriptStatusItems(
        ST_PROCESS_PLAN? processPlan,
        EN_SCRIPT_STATUS scriptStatus,
        ST_PROCESS_RESULT? result)
    {
        var hasScript = processPlan is not null && _lastScript is not null;
        string SelectScript54(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return script.FileName;
        }

        var scriptFiles = hasScript && _lastScript!.HeadScripts.Count > 0
            ? string.Join(", ", _lastScript.HeadScripts.Select(SelectScript54))
            : hasScript
                ? _lastScript!.FileName
                : "-";
        string SelectScript55(ST_AUTOMATION1_HEAD_SCRIPT script)
        {
            return script.TaskNo.ToString(CultureInfo.InvariantCulture);
        }

        var taskNumbers = hasScript && _lastScript!.HeadScripts.Count > 0
            ? string.Join(", ", _lastScript.HeadScripts.Select(SelectScript55))
            : hasScript
                ? "1"
                : "-";

        return
        [
            new("Script Build", FormatScriptStatus(scriptStatus)),
            new("Script File", scriptFiles, hasScript ? _lastScript!.FilePath : ""),
            new("Task No", taskNumbers),
            new("Execute State", FormatExecuteState(scriptStatus, result, hasScript)),
            new("Created Time", FormatDateTime(_scriptCreatedAt)),
            new("Started Time", FormatDateTime(_scriptStartedAt)),
            new("Completed Time", FormatDateTime(_scriptCompletedAt)),
            new("Result", FormatScriptResult(scriptStatus, result, hasScript)),
            new("Error Code", result is { IsSuccess: false } ? "1" : "0")
        ];
    }

    private static IReadOnlyList<ST_PROCESS_DISPLAY_ITEM> BuildScriptLifecycleItems(
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep)
    {
        return
        [
            new("Not Created", LifecycleState(scriptStatus, processStep, 0)),
            new("Created", LifecycleState(scriptStatus, processStep, 1)),
            new("Started", LifecycleState(scriptStatus, processStep, 2)),
            new("Running", LifecycleState(scriptStatus, processStep, 3)),
            new("Completed", LifecycleState(scriptStatus, processStep, 4))
        ];
    }

    private static ST_PROCESS_STATISTICS BuildStatistics(
        IReadOnlyList<ST_HEAD_PATH_DATA> preview,
        double progressPercent,
        TimeSpan elapsedTime)
    {
        int HandleTotalPoints56(ST_HEAD_PATH_DATA head)
        {
            return head.Points.Count;
        }

        var totalPoints = preview.Sum(HandleTotalPoints56);

        return new ST_PROCESS_STATISTICS(
            totalPoints,
            (int)Math.Round(totalPoints * 0.60),
            (int)Math.Round(totalPoints * 0.40),
            totalPoints == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(80),
            elapsedTime,
            Math.Clamp(progressPercent, 0.0, 100.0));
    }

    private static ST_PROCESS_STATISTICS EmptyStatistics()
    {
        return new ST_PROCESS_STATISTICS(0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, 0.0);
    }

    private void RefreshSnapshot()
    {
        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            _snapshot.HeadPreviews,
            _snapshot.ScriptStatus,
            _snapshot.ProcessStep,
            _snapshot.Result);
    }

    private void ResetAutoStepStates()
    {
        _autoStepStates.Clear();

        foreach (var step in AutoStepInfos)
        {
            _autoStepStates[step.Key] = AutoStepWait;
        }
    }

    private void SetAutoStepState(string stepKey, string state)
    {
        _autoStepStates[stepKey] = state;
    }

    private void MarkRunningAutoSteps(string state)
    {
        foreach (var step in AutoStepInfos)
        {
            if (_autoStepStates.TryGetValue(step.Key, out var currentState) &&
                currentState == AutoStepRunning)
            {
                _autoStepStates[step.Key] = state;
            }
        }
    }

    private string ReadAutoStepState(
        string stepKey,
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep)
    {
        if (_autoStepStates.TryGetValue(stepKey, out var state))
        {
            return state;
        }
        string EvaluateValueSwitch1()
        {
            var switchValue = (stepKey, scriptStatus, processStep);
            switch (switchValue)
            {
                case (AutoStepProcess, EN_SCRIPT_STATUS.Created or EN_SCRIPT_STATUS.Running or EN_SCRIPT_STATUS.Completed, EN_PROCESS_STEP.Process):
                    return AutoStepRunning;
                case (AutoStepInspection, EN_SCRIPT_STATUS.Running, EN_PROCESS_STEP.Inspection):
                    return AutoStepRunning;
                case (AutoStepComplete, EN_SCRIPT_STATUS.Completed, EN_PROCESS_STEP.Completed):
                    return AutoStepDone;
                default:
                    return AutoStepWait;
            }
        }

        return EvaluateValueSwitch1();
    }

    private EN_SCRIPT_STATUS ResolveStepScriptStatus(
        string stepKey,
        EN_SCRIPT_STATUS defaultStatus)
    {
        if (stepKey.Equals(AutoStepComplete, StringComparison.OrdinalIgnoreCase))
        {
            return GetCurrentScriptStatusForComplete();
        }

        if (stepKey.Equals(AutoStepInspection, StringComparison.OrdinalIgnoreCase) &&
            _lastScript is null)
        {
            return EN_SCRIPT_STATUS.NotCreated;
        }

        return defaultStatus;
    }

    private EN_SCRIPT_STATUS GetCurrentScriptStatusForComplete()
    {
        if (_lastScript is null)
        {
            return EN_SCRIPT_STATUS.NotCreated;
        }

        return _scriptStartedAt is null
            ? EN_SCRIPT_STATUS.Created
            : EN_SCRIPT_STATUS.Completed;
    }

    private static ST_STATION_PROCESS_FLOW_ITEM GetProcessFlowItem(string stepKey)
    {
        bool MatchStep57(ST_STATION_PROCESS_FLOW_ITEM step)
        {
            return step.StepKey.Equals(stepKey, StringComparison.OrdinalIgnoreCase);
        }

        return ProcessFlowItems.FirstOrDefault(MatchStep57)
            ?? throw new InvalidOperationException($"Auto step is not defined: {stepKey}");
    }

    private static string GetAutoStepName(string stepKey)
    {
        bool MatchStep58(ST_AUTO_STEP_INFO step)
        {
            return step.Key == stepKey;
        }

        return AutoStepInfos.FirstOrDefault(MatchStep58)?.DisplayName ?? stepKey;
    }

    private static Dictionary<string, string> CreateAutoStepStateMap()
    {
        string ToDictionaryStepCallback59(ST_AUTO_STEP_INFO step)
        {
            return step.Key;
        }

        string ToDictionaryValueCallback60(ST_AUTO_STEP_INFO _)
        {
            return AutoStepWait;
        }

        return AutoStepInfos.ToDictionary(
ToDictionaryStepCallback59,
ToDictionaryValueCallback60,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string LifecycleState(
        EN_SCRIPT_STATUS scriptStatus,
        EN_PROCESS_STEP processStep,
        int stepNo)
    {
        string EvaluateValueSwitch2()
        {
            var switchValue = (scriptStatus, processStep, stepNo);
            switch (switchValue)
            {
                case (EN_SCRIPT_STATUS.NotCreated, _, 0):
                    return "ACTIVE";
                case (EN_SCRIPT_STATUS.Created, _, 0):
                    return "DONE";
                case (EN_SCRIPT_STATUS.Created, _, 1):
                    return "ACTIVE";
                case (EN_SCRIPT_STATUS.Running, _, <= 2):
                    return "DONE";
                case (EN_SCRIPT_STATUS.Running, _, 3):
                    return "ACTIVE";
                case (EN_SCRIPT_STATUS.Completed, _, _):
                    return stepNo < 4 ? "DONE" : "ACTIVE";
                case (EN_SCRIPT_STATUS.Error, _, _):
                    return stepNo == 3 ? "ERROR" : "-";
                default:
                    return "-";
            }
        }

        return EvaluateValueSwitch2();
    }

    private static bool IsAttenuatorReady(ST_ATTENUATOR_STATUS status)
    {
        return status.CommOk &&
            status.LastError == EN_CONEX_AGP_ERROR.Ok &&
            !IsMovingState(status.CommandState);
    }

    private static bool IsBETReady(ST_BET_STATUS status)
    {
        return status.CommOk &&
            status.LastError == EN_BET_ERROR.Ok &&
            !status.AlarmOn &&
            !status.IsMoving;
    }

    private static void ValidateBETStatus(
        ST_INTERFACE_DATA device,
        ST_BET_STATUS status)
    {
        if (!IsBETReady(status))
        {
            throw new InvalidOperationException(
                $"{FormatInterfaceName(device)} is not ready. {FormatBETStatus(status)}");
        }
    }

    private static bool IsMovingState(string state)
    {
        var normalized = state.Trim().ToUpperInvariant();
        return normalized is "MOVING" or "HOMING" or "BUSY";
    }

    private static bool IsNear(
        double current,
        double target,
        double tolerance)
    {
        return Math.Abs(current - target) <= tolerance;
    }

    private static string FormatLaserStatus(ST_LASER_STATUS status)
    {
        return $"Laser={(status.PowerOn ? "ON" : "OFF")}, Gate={(status.GateOn ? "OPEN" : "CLOSE")}, Shutter={(status.ShutterOpen ? "OPEN" : "CLOSE")}";
    }

    private static string FormatChillerStatus(ST_CHILLER_STATUS status)
    {
        return $"Run={(status.Running ? "RUN" : "STOP")}, Alarm={(status.AlarmOn ? "ON" : "OFF")}, Temp={FormatDouble(status.Temperature)}";
    }

    private static string FormatAttenuatorStatus(ST_ATTENUATOR_STATUS status)
    {
        return $"Current={FormatDouble(status.CurrentPosition)}, Target={FormatDouble(status.TargetPosition)}, State={status.CommandState}, Error={status.LastError}";
    }

    private static string FormatBETStatus(ST_BET_STATUS status)
    {
        return $"MAG={FormatDouble(status.CurrentMagnification)}/{FormatDouble(status.TargetMagnification)}, DIV={FormatDouble(status.CurrentDivergence)}/{FormatDouble(status.TargetDivergence)}, Moving={status.IsMoving}, Alarm={status.AlarmOn}, Error={status.LastError}";
    }

    private static string FormatCommState(EN_COMM_STATE state)
    {
        string EvaluateStateSwitch3()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case EN_COMM_STATE.Online:
                    return "ONLINE";
                case EN_COMM_STATE.Simulation:
                    return "SIMULATION";
                default:
                    return "OFFLINE";
            }
        }

        return EvaluateStateSwitch3();
    }

    private static string FormatInterfaceName(ST_INTERFACE_DATA data)
    {
        return $"{data.Device}[{data.Number}]/{data.NickName}";
    }

    private static string FormatInterfaceName(ST_INTERFACE_COMM_STATUS status)
    {
        return $"{status.Module}[{status.Number}]/{status.NickName}";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static double ReadDirectionAny(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        bool FilterKey61(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey61))
        {
            if (!parameters.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return NormalizeDirection(value.Trim(), key);
        }

        return NormalizeDirection(defaultValue, "DEFAULT_DIRECTION");
    }

    private static double NormalizeDirection(
        string value,
        string key)
    {
        var normalized = value.Trim().ToUpperInvariant();

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return NormalizeDirection(number, key);
        }

        if (normalized is "-" or "-1" or "REVERSE" or "REV" or "NEGATIVE" or "MINUS")
        {
            return -1.0;
        }

        if (normalized is "+" or "+1" or "FORWARD" or "FWD" or "POSITIVE" or "PLUS")
        {
            return 1.0;
        }

        throw new InvalidOperationException($"Invalid direction value. {key}={value}");
    }

    private static double NormalizeDirection(
        double value,
        string key)
    {
        if (value < 0.0)
        {
            return -1.0;
        }

        if (value > 0.0)
        {
            return 1.0;
        }

        throw new InvalidOperationException($"Direction value cannot be 0. {key}=0");
    }

    private static string FormatCommand(
        string command,
        IReadOnlyList<(string Key, string Value)> arguments)
    {
        string SelectArgument62((string Key, string Value) argument)
        {
            return $"{argument.Key}={argument.Value}";
        }

        return string.Join(
            ";",
            new[] { command }.Concat(arguments.Select(SelectArgument62)));
    }

    private static bool IsProcessControlSuccessResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var firstToken = response
            .Trim()
            .ToUpperInvariant()
            .Split(
                [';', '|', ':', ',', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstToken is "OK" or "ACK" or "SUCCESS" or "ACCEPTED" or "DONE";
    }

    private static string FormatResponseForLog(string response)
    {
        return string.IsNullOrWhiteSpace(response)
            ? "<empty>"
            : response.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static double? ReadNullableDoubleAny(
        IReadOnlyDictionary<string, string> parameters,
        params string[] keys)
    {
        bool FilterKey63(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey63))
        {
            if (parameters.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return null;
    }

    private static int? ReadNullableIntAny(
        IReadOnlyDictionary<string, string> parameters,
        params string[] keys)
    {
        bool FilterKey64(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey64))
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                continue;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
            {
                return intResult;
            }

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleResult))
            {
                return (int)Math.Round(doubleResult);
            }
        }

        return null;
    }

    private static double ReadDouble(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        double defaultValue)
    {
        return parameters.TryGetValue(key, out var value) &&
            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        int defaultValue)
    {
        return parameters.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
    }

    private static double ReadDoubleAny(
        IReadOnlyDictionary<string, string> parameters,
        double defaultValue,
        params string[] keys)
    {
        bool FilterKey65(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey65))
        {
            if (parameters.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return defaultValue;
    }

    private static int ReadIntAny(
        IReadOnlyDictionary<string, string> parameters,
        int defaultValue,
        params string[] keys)
    {
        bool FilterKey66(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey66))
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                continue;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
            {
                return intResult;
            }

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleResult))
            {
                return (int)Math.Round(doubleResult);
            }
        }

        return defaultValue;
    }

    private static bool IsBufferedRunEnabled(IReadOnlyDictionary<string, string> parameters)
    {
        return ReadBoolAny(parameters, false, ScriptBufferedRunUseKey);
    }

    private static int ReadBufferedRunLinesPerCommand(IReadOnlyDictionary<string, string> parameters)
    {
        return Math.Max(
            2,
            ReadIntAny(parameters, 1000, ScriptBufferedRunLinesPerCommandKey));
    }

    private static int ReadBufferedRunQueueSize(IReadOnlyDictionary<string, string> parameters)
    {
        return Math.Max(
            1,
            ReadIntAny(parameters, 100, ScriptBufferedRunQueueSizeKey));
    }

    private static int ReadBufferedRunTimeoutMs(IReadOnlyDictionary<string, string> parameters)
    {
        return Math.Max(
            0,
            ReadIntAny(parameters, 600000, ScriptBufferedRunTimeoutMsKey));
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        bool defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        bool EvaluateValueSwitch4()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "1" or "Y" or "YES" or "TRUE" or "ON" or "USE":
                    return true;
                case "0" or "N" or "NO" or "FALSE" or "OFF" or "SKIP":
                    return false;
                default:
                    return defaultValue;
            }
        }

        return EvaluateValueSwitch4();
    }

    private static bool ReadBoolAny(
        IReadOnlyDictionary<string, string> parameters,
        bool defaultValue,
        params string[] keys)
    {
        bool FilterKey67(string key)
        {
            return !string.IsNullOrWhiteSpace(key);
        }

        foreach (var key in keys.Where(FilterKey67))
        {
            if (parameters.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                bool EvaluateValueSwitch5()
                {
                    var switchValue = value.Trim().ToUpperInvariant();
                    switch (switchValue)
                    {
                        case "1" or "Y" or "YES" or "TRUE" or "ON" or "USE":
                            return true;
                        case "0" or "N" or "NO" or "FALSE" or "OFF" or "SKIP":
                            return false;
                        default:
                            return defaultValue;
                    }
                }

                return EvaluateValueSwitch5();
            }
        }

        return defaultValue;
    }

    private bool ReadSettingBool(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken)
    {
        var value = _settingManager.GetValue(
            EN_SETTING_TAB.Option,
            key,
            defaultValue ? "ON" : "OFF",
            cancellationToken);

        return ReadBoolValue(value, defaultValue);
    }

    private static bool ReadBoolValue(
        string value,
        bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        bool EvaluateValueSwitch6()
        {
            var switchValue = value.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "1" or "Y" or "YES" or "TRUE" or "ON" or "USE":
                    return true;
                case "0" or "N" or "NO" or "FALSE" or "OFF" or "SKIP":
                    return false;
                default:
                    return defaultValue;
            }
        }

        return EvaluateValueSwitch6();
    }

    private static string ReadText(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        string defaultValue)
    {
        return parameters.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : defaultValue;
    }

    private static string[] CreateHeadKeys(int headNo, params string[] names)
    {
        var paddedHeadNoText = headNo.ToString("00", CultureInfo.InvariantCulture);
        string SelectName68(string name)
        {
            return $"H{paddedHeadNoText}_{name}";
        }

        return names
            .Select(SelectName68)
            .ToArray();
    }

    private static int ReadHeadAutomationNo(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        return Math.Max(
            0,
            ReadIntAny(
                parameters,
                headNo <= 4 ? 0 : 1,
                CreateHeadKeys(headNo, "AUTOMATION_NO")));
    }

    private static int ReadHeadAutomationTaskNo(
        IReadOnlyDictionary<string, string> parameters,
        int headNo)
    {
        var defaultTaskNo = ((headNo - 1) % 4) + 1;

        return Math.Max(
            1,
            ReadIntAny(
                parameters,
                defaultTaskNo,
                CreateHeadKeys(headNo, "AUTOMATION_TASK_NO")));
    }

    private static string ReadAnyParameter(
        ST_PROCESS_PLAN processPlan,
        string defaultValue,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (processPlan.Parameters.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return defaultValue;
    }

    private ST_PROCESS_MODEL GetProcessModel()
    {
        return _processModel
            ?? throw new InvalidOperationException("Process Model is not built.");
    }

    private ST_STATION_PROCESS_STATUS SetAlarm(
        string message,
        CancellationToken cancellationToken)
    {
        AddProcessLog("ERROR", "STATION", message);
        ReturnLaserSafe(cancellationToken);
        MarkRunningAutoSteps(AutoStepError);
        SetProductError(message, cancellationToken);
        var result = new ST_PROCESS_RESULT(false, message, DateTimeOffset.Now);
        ReportProcessResult(result, "ALARM", cancellationToken);

        _snapshot = CreateSnapshot(
            _snapshot.ProcessPlan,
            SetHeadStatus(_snapshot.HeadPreviews, EN_PROCESS_STEP.Error),
            EN_SCRIPT_STATUS.Error,
            EN_PROCESS_STEP.Error,
            result);

        SetStationState(
            EN_STATION_STATE.Alarm,
            EN_PROCESS_STEP.Error,
            EN_SCRIPT_STATUS.Error,
            message);

        return _snapshot;
    }

    private void SetStationState(
        EN_STATION_STATE state,
        EN_PROCESS_STEP processStep,
        EN_SCRIPT_STATUS scriptStatus,
        string message)
    {
        _stationStatus = _stationStatus with
        {
            State = state,
            ProcessStep = processStep,
            ScriptStatus = scriptStatus,
            LastMessage = message,
            ChangedAt = DateTimeOffset.Now
        };

        _logManager?.WriteStationState(
            _stationStatus.StationName,
            state.ToString().ToUpperInvariant(),
            processStep.ToString().ToUpperInvariant(),
            message);
    }

    private void AddProcessLog(
        string level,
        string source,
        string message)
    {
        _processLogs.Add(new ST_PROCESS_LOG_ITEM(DateTimeOffset.Now, level, source, message));
    }

    private void AddSequenceLog(
        string level,
        string source,
        string message)
    {
        AddProcessLog(level, source, message);
        _logManager?.WriteStationState(
            _stationStatus.StationName,
            _stationStatus.State.ToString().ToUpperInvariant(),
            $"SEQ_{source}_{level}",
            message);
    }

    private static string FormatInterLockBlockedMessage(ST_INTERLOCK_SUMMARY interLock)
    {
        bool MatchX69(ST_INTERLOCK_ITEM x)
        {
            return x.Level != EN_INTERLOCK_LEVEL.Ok;
        }

        var item = interLock.Items.FirstOrDefault(MatchX69);
        return item is null
            ? "InterLock is not ready."
            : $"InterLock is not ready. {item.Signal}: {item.Detail}";
    }

    private static string FormatScriptStatus(EN_SCRIPT_STATUS status)
    {
        string EvaluateStatusSwitch7()
        {
            var switchValue = status;
            switch (switchValue)
            {
                case EN_SCRIPT_STATUS.NotCreated:
                    return "Not Created";
                default:
                    return status.ToString();
            }
        }

        return EvaluateStatusSwitch7();
    }

    private static string FormatExecuteState(
        EN_SCRIPT_STATUS scriptStatus,
        ST_PROCESS_RESULT? result,
        bool hasScript)
    {
        if (!hasScript)
        {
            return result is null ? "-" : "Not Executed";
        }

        if (result is not null)
        {
            return result.IsSuccess ? "Completed" : "Failed";
        }
        string EvaluateScriptStatusSwitch8()
        {
            var switchValue = scriptStatus;
            switch (switchValue)
            {
                case EN_SCRIPT_STATUS.Running:
                    return "Running";
                case EN_SCRIPT_STATUS.Created:
                    return "Ready";
                case EN_SCRIPT_STATUS.Error:
                    return "Error";
                default:
                    return "-";
            }
        }

        return EvaluateScriptStatusSwitch8();
    }

    private static string FormatScriptResult(
        EN_SCRIPT_STATUS scriptStatus,
        ST_PROCESS_RESULT? result,
        bool hasScript)
    {
        if (!hasScript)
        {
            return result is null ? "-" : "Not Executed";
        }

        if (result is not null)
        {
            return result.IsSuccess ? "OK" : "NG";
        }

        return scriptStatus is EN_SCRIPT_STATUS.Created or EN_SCRIPT_STATUS.Running
            ? "In Progress"
            : "-";
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "-";
    }

    private static string FormatProcessResultDetail(
        ST_PROCESS_RESULT result,
        string processId,
        string recipeId,
        string productId)
    {
        var state = result.IsSuccess ? "OK" : "NG";
        return string.Join(
            ", ",
            $"Process={processId}",
            $"Recipe={recipeId}",
            $"Product={productId}",
            $"Result={state}",
            $"Message={result.Message}",
            $"Time={result.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}");
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value == TimeSpan.Zero
            ? "00:00:00"
            : value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private sealed record ST_OPTIC_READY_BET_TARGET(
        int? TableIndex,
        double Magnification,
        double Divergence);

    private sealed record ST_BUFFERED_RUN_GROUP_RESULT(
        int AutomationNo,
        string HeadSummary,
        string Response);

    private sealed class CBufferedRunTarget
    {
        private readonly List<string> _headNames = new List<string>();

        public CBufferedRunTarget(int automationNo, int taskNo)
        {
            AutomationNo = automationNo;
            TaskNo = taskNo;
        }

        public int AutomationNo { get; }
        public int TaskNo { get; }

        public string HeadSummary
        {
            get
            {
                return string.Join(", ", _headNames);
            }
        }

        public void AddHead(int headNo)
        {
            _headNames.Add($"H{headNo:00}");
        }
    }

    private sealed record ST_BUFFERED_RUN_STOP_RESULT(
        int AutomationNo,
        int TaskNo,
        string HeadSummary,
        bool IsSuccess,
        string Response,
        string ErrorMessage);

    private sealed record ST_AUTO_STEP_INFO(string Key, string DisplayName);
}
