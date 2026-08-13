using Drilling.Common.Log;
using Drilling.Common.Automation;
using Drilling.Common.Interface;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Product;
using Drilling.Common.Review;
using Drilling.Common.Station;

namespace Drilling.Common.Managers;

public enum EN_SYSTEM_MODE
{
    Simulation,
    Auto,
    Manual
}

public enum EN_PM_LOCK_STATE
{
    Released,
    Locked
}

public enum EN_MANAGER_STARTUP_RESULT
{
    Ready,
    Warning,
    Failed
}

public sealed record ST_MANUAL_SCAN_PARAM(
    double ShapeSize,
    double OffsetX,
    double OffsetY,
    string Direction,
    string ShapeName,
    double LaserPower = 1.0,
    double JumpSpeed = 1.5,
    double MarkSpeed = 0.9,
    double AttenuatorPosition = 23.50,
    double LaserFrequency = 20.0,
    double LaserOnDelay = 8.0,
    double LaserOffDelay = 12.0,
    double Time = 10.0,
    int Count = 48000,
    int GridRowLines = 5,
    int GridColLines = 5);

public sealed record ST_MANUAL_SCAN_FORM(
    string Name,
    string DisplayName,
    EN_RECIPE_DATA_TYPE DataType,
    string Unit,
    bool Show,
    bool Use,
    string DefaultValue,
    double Min,
    double Max,
    string Description,
    int DisplayOrder);

public sealed record ST_POWER_METER_STATUS(
    double MeasuredPower,
    string Unit,
    DateTimeOffset MeasuredAt,
    double AveragePower = 0.0,
    double MinPower = 0.0,
    double MaxPower = 0.0,
    double WaveLengthNm = 355.0,
    double BeamPositionX = 0.0,
    double BeamPositionY = 0.0,
    int SampleCount = 0,
    bool IsMeasuring = false,
    string ModelName = "PowerMax",
    string SerialNumber = "-",
    string LastCommand = "",
    EN_POWER_METER_ERROR LastError = EN_POWER_METER_ERROR.Ok)
{
    public static ST_POWER_METER_STATUS Empty
    {
        get
        {
            return new(0.0, "W", DateTimeOffset.Now);
        }
    }
}

public sealed record ST_DEVICE_STATUS(
    IReadOnlyList<ST_IO_STATUS> Io,
    IReadOnlyList<ST_MOTOR_AXIS_STATUS> Motors,
    ST_LASER_STATUS Laser,
    ST_CHILLER_STATUS Chiller,
    ST_ATTENUATOR_STATUS Attenuator,
    ST_BET_STATUS Bet,
    ST_POWER_METER_STATUS PowerMeter);

public sealed record ST_SYSTEM_STATUS(
    string CurrentRecipeId,
    EN_SYSTEM_MODE OperationMode,
    EN_ALARM_STATE AlarmState,
    EN_PM_LOCK_STATE PMLockState,
    IReadOnlyList<ST_DEVICE_COMM_STATUS> Modules)
{
    public ST_DEVICE_COMM_STATUS GetModule(EN_EQP_MODULE module)
    {
        foreach (var status in Modules)
        {
            if (status.Module == module)
            {
                return status;
            }
        }

        return new ST_DEVICE_COMM_STATUS(module, EN_COMM_STATE.Offline);
    }
}

public sealed record ST_PM_LOCK_STATUS(
    bool IsLocked,
    DateTimeOffset? LockedAt);

public sealed record ST_MANAGER_STARTUP_STEP(
    int Order,
    string StepName,
    EN_MANAGER_STARTUP_RESULT Result,
    string Message);

public sealed record ST_CONFIG_LOAD_STATUS(
    string ConfigRoot,
    int InterfaceCount,
    int MotorCount,
    int IoCount,
    int MelsecMapCount,
    bool ActiveProductLoaded,
    IReadOnlyList<string> StartupMessages,
    IReadOnlyList<ST_MANAGER_STARTUP_STEP> StartupSteps);

public sealed record ST_CONFIG_FILE_STATUS(
    string ItemName,
    string Path,
    bool Required,
    bool Exists,
    bool IsValid,
    string Message);

public abstract class CConfigStructureFileBase
{
    public abstract IReadOnlyList<ST_CONFIG_FILE_STATUS> Validate(
            CancellationToken cancellationToken = default);
}

public abstract class CManualScanFileBase
{
    public abstract IReadOnlyList<string> List(CancellationToken cancellationToken = default);
    public abstract IReadOnlyList<ST_MANUAL_SCAN_FORM> LoadForm(CancellationToken cancellationToken = default);
    public abstract ST_MANUAL_SCAN_PARAM Load(CancellationToken cancellationToken = default);
    public abstract ST_MANUAL_SCAN_PARAM Load(string settingName, CancellationToken cancellationToken = default);
    public abstract void Save(ST_MANUAL_SCAN_PARAM settings, CancellationToken cancellationToken = default);
    public abstract void Save(
            string settingName,
            ST_MANUAL_SCAN_PARAM settings,
            CancellationToken cancellationToken = default);
    public abstract void Rename(
            string oldSettingName,
            string newSettingName,
            CancellationToken cancellationToken = default);
    public abstract void Delete(string settingName, CancellationToken cancellationToken = default);
}
public sealed class CManager
{
    private delegate void CInitializeStepAction();
    private readonly string _configRoot;

    private readonly CRecipeFileBase _recipeFile;
    private readonly CSettingFileBase _settingFile;
    private readonly CManualScanFileBase _manualScanFile;
    private readonly CInterfaceFileBase _interfaceFile;
    private readonly CBETFileBase _betFile;
    private readonly CPowerMeterFileBase _powerMeterFile;
    private readonly CMotorFileBase _motorFile;
    private readonly CIoFileBase _ioFile;
    private readonly CMelsecMapFileBase _melsecMapFile;
    private readonly CProductFileBase _productFile;
    private readonly CReviewResultFileBase _reviewResultFile;
    private readonly CReviewRuleFileBase _reviewRuleFile;
    private readonly CLogManager _logManager;
    private readonly CAutomationScriptFileBase _automationScriptFile;
    private readonly CConfigStructureFileBase? _configStructureFile;

    private readonly CInterfaceManager _interfaceManager;
    private readonly CAutomationManager _automationManager;
    private readonly CMotionManager _motionManager;
    private readonly CAlarmManager _alarmManager = new();
    private readonly CInterLockManager _interLockManager = new();

    private readonly CStationManager _stationManager;
    private readonly CRecipeManager _recipeManager;
    private readonly CSettingManager _settingManager;
    private readonly CProductManager _productManager;
    private readonly CReviewManager _reviewManager;
    private readonly object _startupLock = new();
    private readonly List<string> _startupMessages = [];
    private readonly List<ST_MANAGER_STARTUP_STEP> _startupSteps = [];
    private int _startupStepNo;
    private int _loadedInterfaceCount;
    private int _loadedMotorCount;
    private int _loadedIoCount;
    private int _loadedMelsecMapCount;
    private bool _activeProductLoaded;

    public CManager(
        string configRoot,
        CRecipeFileBase recipeFile,
        CSettingFileBase settingFile,
        CManualScanFileBase manualScanFile,
        CInterfaceFileBase interfaceFile,
        CBETFileBase betFile,
        CPowerMeterFileBase powerMeterFile,
        CMotorFileBase motorFile,
        CIoFileBase ioFile,
        CMelsecMapFileBase melsecMapFile,
        CProductFileBase productFile,
        CReviewResultFileBase reviewResultFile,
        CReviewRuleFileBase reviewRuleFile,
        CLogManager logManager,
        CAutomationScriptFileBase automationScriptFile,
        bool? simulationMode = null,
        CConfigStructureFileBase? configStructureFile = null)
    {
        _configRoot = configRoot;
        _recipeFile = recipeFile;
        _settingFile = settingFile;
        _manualScanFile = manualScanFile;
        _interfaceFile = interfaceFile;
        _betFile = betFile;
        _powerMeterFile = powerMeterFile;
        _motorFile = motorFile;
        _ioFile = ioFile;
        _melsecMapFile = melsecMapFile;
        _productFile = productFile;
        _reviewResultFile = reviewResultFile;
        _reviewRuleFile = reviewRuleFile;
        _logManager = logManager;
        _automationScriptFile = automationScriptFile;
        _configStructureFile = configStructureFile;

        CheckConfigRoot();
        ValidateConfigStructure();

        _interfaceManager = new CInterfaceManager(simulationMode, _logManager, _betFile, _powerMeterFile);
        AddStartupStep(
            "Create Interface Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            "CInterfaceManager created.");

        var interfaceData = LoadInterfaceList();
        RegisterInterfaceList(interfaceData);

        _settingManager = new CSettingManager(_settingFile, _interfaceFile, _interfaceManager);

        _automationManager = new CAutomationManager(
            _interfaceManager,
            _settingManager,
            GetProjectRoot(),
            GetScriptDirectory());
        AddStartupStep(
            "Create Automation Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            "CAutomationManager created.");

        var melsecMapData = LoadMelsecMapList();
        _interfaceManager.Melsec.ReloadMap(melsecMapData);
        AddStartupStep(
            "Register JHMI_MELSEC_MAP",
            EN_MANAGER_STARTUP_RESULT.Ready,
            $"Registered {melsecMapData.Count} MELSEC map row(s).");

        var motorData = LoadMotorList();
        var ioData = LoadIoList();

        _motionManager = new CMotionManager(
            _interfaceManager,
            motorData,
            ioData,
            isSimulation: GetMotionSimulationMode(simulationMode));
        AddStartupStep(
            "Create Motion Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            $"CMotionManager created. Axis={motorData.Count}, IO={ioData.Count}, Simul={_motionManager.IsSimulation}");

        _productManager = new CProductManager(_productFile, _logManager);
        AddStartupStep(
            "Create Product Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            "CProductManager created.");
        LoadActiveProduct();

        _reviewManager = new CReviewManager(_reviewResultFile, _interfaceManager, _settingManager);
        AddStartupStep(
            "Create Review Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            "CReviewManager created.");

        _stationManager = new CStationManager(
            _interfaceManager,
            _motionManager,
            _interLockManager,
            _settingManager,
            _automationScriptFile,
            _automationManager,
            _productManager,
            _logManager,
            GetScriptDirectory());
        AddStartupStep(
            "Create Station Manager",
            EN_MANAGER_STARTUP_RESULT.Ready,
            $"Script={GetScriptDirectory()}");

        _recipeManager = new CRecipeManager(_recipeFile);
        AddStartupStep(
            "Create Menu Managers",
            EN_MANAGER_STARTUP_RESULT.Ready,
            "Recipe/Setting managers created.");
    }

    public string ConfigRoot
    {
        get
        {
            return _configRoot;
        }
    }

    public IReadOnlyList<string> StartupMessages
    {
        get
        {
            lock (_startupLock)
            {
                return _startupMessages.ToArray();
            }
        }
    }

    public IReadOnlyList<ST_MANAGER_STARTUP_STEP> StartupSteps
    {
        get
        {
            lock (_startupLock)
            {
                return _startupSteps.ToArray();
            }
        }
    }

    public ST_CONFIG_LOAD_STATUS ConfigStatus()
    {
        lock (_startupLock)
        {
            return new ST_CONFIG_LOAD_STATUS(
                _configRoot,
                _loadedInterfaceCount,
                _loadedMotorCount,
                _loadedIoCount,
                _loadedMelsecMapCount,
                _activeProductLoaded,
                _startupMessages.ToArray(),
                _startupSteps.ToArray());
        }
    }

    public bool IsSimul(int systemId = 0)
    {
        return _interfaceManager.IsSimulation && _motionManager.IsSimulation;
    }

    public bool IsNotSimul(int systemId = 0)
    {
        return !IsSimul(systemId);
    }

    public void SetSimul(bool enabled)
    {
        _interfaceManager.SetSimulationMode(enabled);
        _motionManager.SetSimulationMode(enabled);
    }

    public void SetSimul(int systemId, bool enabled)
    {
        SetSimul(enabled);
    }

    public void Initialize(CancellationToken cancellationToken = default)
    {
        void RunInitializeStepCallbackCallback1()
        {
            _interfaceManager.Initialize(cancellationToken);
        }

        RunInitializeStep(
            "Initialize Interface Connection",
RunInitializeStepCallbackCallback1,
            cancellationToken);
        void RunInitializeStepCallbackCallback2()
        {
            _motionManager.Initialize(cancellationToken);
        }

        RunInitializeStep(
            "Initialize Motion Controller",
RunInitializeStepCallbackCallback2,
            cancellationToken);
    }

    public void Destroy(CancellationToken cancellationToken = default)
    {
        _stationManager.Destroy();
        _motionManager.Destroy(cancellationToken);
        _interfaceManager.Destroy(cancellationToken);
    }

    public int ConnectInterface(CancellationToken cancellationToken = default)
    {
        return _interfaceManager.Connect(cancellationToken: cancellationToken);
    }

    public int DisconnectInterface(CancellationToken cancellationToken = default)
    {
        return _interfaceManager.Disconnect(cancellationToken);
    }

    public void ReconnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        _interfaceManager.Reconnect(module, number, cancellationToken);
    }

    public CStationManager Station()
    {
        return _stationManager;
    }

    public CRecipeManager Recipe()
    {
        return _recipeManager;
    }

    public CSettingManager Setting()
    {
        return _settingManager;
    }

    public CProductManager Product()
    {
        return _productManager;
    }

    public CReviewManager Review()
    {
        return _reviewManager;
    }

    public CInterfaceManager Interface()
    {
        return _interfaceManager;
    }

    public CAutomationManager Automation()
    {
        return _automationManager;
    }

    public CAutomationManager automation
    {
        get
        {
            return _automationManager;
        }
    }

    public CMotionManager Motion()
    {
        return _motionManager;
    }

    public CAlarmManager Alarm()
    {
        return _alarmManager;
    }

    public CInterLockManager InterLock()
    {
        return _interLockManager;
    }

    public CLogManager Log()
    {
        return _logManager;
    }

    public CRecipeFileBase RecipeFile()
    {
        return _recipeFile;
    }

    public CSettingFileBase SettingFile()
    {
        return _settingFile;
    }

    public CManualScanFileBase ManualScanFile()
    {
        return _manualScanFile;
    }

    public CInterfaceFileBase InterfaceFile()
    {
        return _interfaceFile;
    }

    public CBETFileBase BETFile()
    {
        return _betFile;
    }

    public CPowerMeterFileBase PowerMeterFile()
    {
        return _powerMeterFile;
    }

    public CMotorFileBase MotorFile()
    {
        return _motorFile;
    }

    public CIoFileBase IoFile()
    {
        return _ioFile;
    }

    public CMelsecMapFileBase MelsecMapFile()
    {
        return _melsecMapFile;
    }

    public CProductFileBase ProductFile()
    {
        return _productFile;
    }

    public CReviewRuleFileBase ReviewRuleFile()
    {
        return _reviewRuleFile;
    }

    public CReviewResultFileBase ReviewResultFile()
    {
        return _reviewResultFile;
    }

    private void CheckConfigRoot()
    {
        if (Directory.Exists(_configRoot))
        {
            AddStartupStep(
                "Check Config Root",
                EN_MANAGER_STARTUP_RESULT.Ready,
                _configRoot);
            return;
        }

        AddStartupStep(
            "Check Config Root",
            EN_MANAGER_STARTUP_RESULT.Warning,
            $"Config root was not found: {_configRoot}");
    }

    private void ValidateConfigStructure()
    {
        if (_configStructureFile is null)
        {
            AddStartupStep(
                "Validate Config Structure",
                EN_MANAGER_STARTUP_RESULT.Warning,
                "Config structure validator was not registered.");
            return;
        }

        try
        {
            var statuses = _configStructureFile.Validate();

            foreach (var status in statuses)
            {
                AddStartupStep(
                    $"Check {status.ItemName}",
                    ToStartupResult(status),
                    FormatConfigStatusMessage(status));
            }
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Validate Config Structure", ex);
        }
    }

    private IReadOnlyList<ST_INTERFACE_DATA> LoadInterfaceList()
    {
        try
        {
            var interfaceData = _interfaceFile.LoadAll();
            _loadedInterfaceCount = interfaceData.Count;

            AddStartupStep(
                "Load JHMI_INTERFACE",
                EN_MANAGER_STARTUP_RESULT.Ready,
                $"Loaded {interfaceData.Count} interface row(s).");

            return interfaceData;
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Load JHMI_INTERFACE", ex);
            return [];
        }
    }

    private void RegisterInterfaceList(IReadOnlyList<ST_INTERFACE_DATA> interfaceData)
    {
        var registeredCount = 0;

        foreach (var data in interfaceData)
        {
            try
            {
                _interfaceManager.Register(data);
                registeredCount++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
            {
                AddStartupFailure(
                    $"Register Interface {data.Device}[{data.Number}]",
                    ex);
            }
        }

        AddStartupStep(
            "Register Interface List",
            registeredCount == interfaceData.Count
                ? EN_MANAGER_STARTUP_RESULT.Ready
                : EN_MANAGER_STARTUP_RESULT.Warning,
            $"Registered {registeredCount}/{interfaceData.Count} interface device(s).");
    }

    private IReadOnlyList<ST_MOTOR_DATA> LoadMotorList()
    {
        try
        {
            var motorData = _motorFile.LoadAll();
            _loadedMotorCount = motorData.Count;
            AddStartupStep(
                "Load JHMI_MOTOR",
                EN_MANAGER_STARTUP_RESULT.Ready,
                $"Loaded {motorData.Count} motor row(s).");

            return motorData;
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Load JHMI_MOTOR", ex);
            return [];
        }
    }

    private IReadOnlyList<ST_IO_DATA> LoadIoList()
    {
        try
        {
            var ioData = _ioFile.LoadAll();
            _loadedIoCount = ioData.Count;
            AddStartupStep(
                "Load JHMI_IO",
                EN_MANAGER_STARTUP_RESULT.Ready,
                $"Loaded {ioData.Count} IO row(s).");

            return ioData;
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Load JHMI_IO", ex);
            return [];
        }
    }

    private IReadOnlyList<ST_MELSEC_MAP_DATA> LoadMelsecMapList()
    {
        try
        {
            var melsecMapData = _melsecMapFile.LoadAll();
            _loadedMelsecMapCount = melsecMapData.Count;
            AddStartupStep(
                "Load JHMI_MELSEC_MAP",
                EN_MANAGER_STARTUP_RESULT.Ready,
                $"Loaded {melsecMapData.Count} MELSEC map row(s).");

            return melsecMapData;
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Load JHMI_MELSEC_MAP", ex);
            return [];
        }
    }

    private void LoadActiveProduct()
    {
        try
        {
            _productManager.LoadActive();
            _activeProductLoaded = true;
            AddStartupStep(
                "Load Active Product",
                EN_MANAGER_STARTUP_RESULT.Ready,
                "Active product loaded.");
        }
        catch (Exception ex) when (IsStartupDataException(ex))
        {
            AddStartupFailure("Load Active Product", ex);
        }
    }

    private void RunInitializeStep(
        string stepName,
        CInitializeStepAction action,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            AddStartupStep(
                stepName,
                EN_MANAGER_STARTUP_RESULT.Ready,
                "OK");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddStartupStep(
                stepName,
                EN_MANAGER_STARTUP_RESULT.Warning,
                "Canceled.");
        }
        catch (Exception ex) when (IsStartupRuntimeException(ex))
        {
            AddStartupFailure(stepName, ex);
        }
    }

    private void AddStartupFailure(
        string stepName,
        Exception exception)
    {
        AddStartupStep(
            stepName,
            EN_MANAGER_STARTUP_RESULT.Failed,
            exception.Message);
    }

    private void AddStartupStep(
        string stepName,
        EN_MANAGER_STARTUP_RESULT result,
        string message)
    {
        lock (_startupLock)
        {
            _startupSteps.Add(new ST_MANAGER_STARTUP_STEP(
                ++_startupStepNo,
                stepName,
                result,
                message));

            if (result != EN_MANAGER_STARTUP_RESULT.Ready && !string.IsNullOrWhiteSpace(message))
            {
                _startupMessages.Add($"{stepName}: {message}");
            }
        }
    }

    private bool GetMotionSimulationMode(bool? simulationMode)
    {
        bool CheckData3(ST_INTERFACE_DATA? data)
        {
            return data is null || data.IsSimulation;
        }

        return simulationMode ?? _interfaceManager
            .GetInterfaceList(EN_EQP_MODULE.Motion)
            .DefaultIfEmpty()
            .All(CheckData3);
    }

    private string GetScriptDirectory()
    {
        bool MatchParameter4(ST_SYSTEM_PARAMETER parameter)
        {
            return parameter.Key.Equals("LocalScriptPath", StringComparison.OrdinalIgnoreCase) ||
                            parameter.Name.Equals("LocalScriptPath", StringComparison.OrdinalIgnoreCase);
        }

        var settingPath = _settingFile
            .Load(EN_SETTING_TAB.Option)
            .FirstOrDefault(MatchParameter4)
            ?.Value;

        return ResolveProjectPath(settingPath, Path.Combine("Data", "Script"));
    }

    private string GetProjectRoot()
    {
        return Directory.GetParent(_configRoot)?.FullName ?? _configRoot;
    }

    private string ResolveProjectPath(
        string? settingPath,
        string defaultRelativePath)
    {
        var path = string.IsNullOrWhiteSpace(settingPath)
            ? defaultRelativePath
            : settingPath.Trim();

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(GetProjectRoot(), path));
    }

    private static bool IsStartupDataException(Exception exception)
    {
        return exception is InvalidDataException or IOException or UnauthorizedAccessException;
    }

    private static bool IsStartupRuntimeException(Exception exception)
    {
        return exception is InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException;
    }

    private static EN_MANAGER_STARTUP_RESULT ToStartupResult(ST_CONFIG_FILE_STATUS status)
    {
        if (status.IsValid)
        {
            return EN_MANAGER_STARTUP_RESULT.Ready;
        }

        return status.Required
            ? EN_MANAGER_STARTUP_RESULT.Failed
            : EN_MANAGER_STARTUP_RESULT.Warning;
    }

    private static string FormatConfigStatusMessage(ST_CONFIG_FILE_STATUS status)
    {
        var requiredText = status.Required ? "Required" : "Optional";
        var existsText = status.Exists ? "Exists" : "Missing";

        return $"{requiredText}, {existsText}, {status.Message} | {status.Path}";
    }
}
