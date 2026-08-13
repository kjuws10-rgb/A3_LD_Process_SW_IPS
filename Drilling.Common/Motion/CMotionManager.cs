using Drilling.Common.Interface;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;
using System.Reflection;

namespace Drilling.Common.Motion;

public enum EN_MOTION_COMMAND
{
    ServoOn,
    ServoOff,
    Home,
    MoveAbs,
    MoveRel,
    Stop,
    ResetAlarm,
    Refresh
}

public sealed record ST_IO_STATUS(
    string Id,
    string Address,
    string Name,
    bool IsOn,
    bool IsOutput);

public sealed record ST_IO_DATA(
    string Id,
    bool Use,
    string Address,
    string Name,
    bool IsOutput,
    string DevType,
    int DevNo,
    bool InitialState,
    int DisplayOrder,
    string Description);

public sealed record ST_MOTOR_AXIS_STATUS(
    string AxisId,
    string Name,
    double CurrentPosition,
    double TargetPosition,
    double CommandPosition,
    bool ServoOn,
    bool HomeCompleted,
    bool LimitPlusOn,
    bool LimitMinusOn,
    bool AlarmOn);

public sealed record ST_MOTOR_DATA(
    string Name,
    bool Use,
    int Axis,
    int VirtureAxis,
    string DevType,
    int DevNo,
    int CoordinateNo,
    int MotorType,
    double Scale,
    string System,
    string StationName,
    string Subordinate,
    string DisplayName,
    string AxisDir,
    bool AlignReverse,
    bool ProcessReverse,
    string Dir,
    string ProductIndex,
    string AxisColor,
    bool ReverseDir,
    double CorrectionAngle,
    double OffsetX,
    double OffsetY,
    double OffsetZ,
    double OffsetXT,
    double OffsetYT,
    double OffsetZT,
    string Unit,
    double MaxVel,
    double InterlockMaxVel,
    double MaxAcc,
    double Min,
    double Max,
    int HomePlc,
    int HomeTimeout,
    string HomePlcFlag,
    string Description,
    double LoadAlarmValue,
    string PreCheckIo);

public sealed record ST_MOTION_STATION_STATUS(
    string StationName,
    string SystemName,
    bool HasAlarm,
    IReadOnlyList<ST_MOTOR_AXIS_STATUS> Axes);

public sealed record ST_MOTION_CONTROLLER_STATUS(
    string DevType,
    int DevNo,
    bool IsRegistered,
    bool IsSimulation,
    int AxisCount,
    IReadOnlyList<string> AxisIds);

public abstract class CMotorFileBase
{
    public abstract IReadOnlyList<ST_MOTOR_DATA> LoadAll(CancellationToken cancellationToken = default);
}

public abstract class CIoFileBase
{
    public abstract IReadOnlyList<ST_IO_DATA> LoadAll(CancellationToken cancellationToken = default);
}
public sealed class CMotionManager {
    private const string DefaultControllerName = "XPS";

    private static readonly IReadOnlyDictionary<string, Type> MotionControllerTypes =
        LoadMotionControllerTypes();

    private readonly CInterfaceManager? _interfaceManager;
    private readonly IReadOnlyList<ST_MOTOR_DATA> _motors;
    private readonly Dictionary<string, ST_MOTOR_DATA> _axisData;
    private readonly Dictionary<string, ST_AXIS_STATE> _axes;
    private readonly Dictionary<string, ST_IO_STATE> _io;
    private readonly Dictionary<string, CMotionController> _controllers = new(StringComparer.OrdinalIgnoreCase);
    private bool _simulationMode;

    public CMotionManager(bool isSimulation = true)
        : this(null, null, null, isSimulation)
    {
    }

    public CMotionManager(
        CInterfaceManager? interfaceManager,
        IReadOnlyList<ST_MOTOR_DATA>? motors = null,
        IReadOnlyList<ST_IO_DATA>? ioData = null,
        bool isSimulation = true)
    {
        _interfaceManager = interfaceManager;
        _simulationMode = isSimulation;
        _motors = NormalizeMotors(motors);
        bool FilterAxis1(ST_MOTOR_DATA axis)
        {
            return axis.Use;
        }

        string HandleAxisData2(ST_MOTOR_DATA axis)
        {
            return NormalizeAxisId(axis.Name);
        }

        _axisData = _motors
            .Where(FilterAxis1)
            .ToDictionary(HandleAxisData2, StringComparer.OrdinalIgnoreCase);
        _axes = CreateAxes(_motors);
        _io = CreateIo(ioData);
        (string DevType, int DevNo) SelectAxis3(ST_MOTOR_DATA axis)
        {
            return (axis.DevType, axis.DevNo);
        }

        (string DevType, int DevNo) SelectChannel4(ST_IO_STATE channel)
        {
            return (channel.DevType, channel.DevNo);
        }

        string HandleControllerRequests5((string DevType, int DevNo) item)
        {
            return GetControllerKey(item.DevType, item.DevNo);
        }

        var controllerRequests = _axisData.Values
            .Select(SelectAxis3)
            .Concat(_io.Values.Select(SelectChannel4))
            .GroupBy(HandleControllerRequests5);

        foreach (var group in controllerRequests)
        {
            var controllerData = group.First();
            var controller = CreateMotionController(controllerData.DevType, controllerData.DevNo);

            if (controller is not null)
            {
                _controllers[group.Key] = controller;
            }
        }
    }

    public bool IsSimulation
    {
        get
        {
            bool CheckController6(CMotionController controller)
            {
                return controller.IsSimulation();
            }

            return _simulationMode || _controllers.Values.All(CheckController6);
        }
    }

    public void SetSimulationMode(bool enabled)
    {
        _simulationMode = enabled;
    }

    public void Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string GroupByAxisCallback7(ST_MOTOR_DATA axis)
        {
            return GetControllerKey(axis.DevType, axis.DevNo);
        }

        foreach (var group in _axisData.Values.GroupBy(GroupByAxisCallback7))
        {
            if (_simulationMode)
            {
                continue;
            }

            var axis = group.First();

            if (!_controllers.TryGetValue(group.Key, out var controller))
            {
                throw CreateMotionControllerNotRegisteredException(axis.DevType, axis.DevNo);
            }

            if (controller.IsSimulation())
            {
                continue;
            }

            controller.Initialize(group.ToArray(), cancellationToken);
        }
    }

    public void Destroy(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var controller in _controllers.Values)
        {
            controller.Destroy(cancellationToken);
        }
    }

    public void RefreshStatus(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_simulationMode)
        {
            return;
        }

        RefreshAxisStatus(cancellationToken);
        RefreshIoStatus(cancellationToken);
    }

    public IReadOnlyList<ST_MOTOR_AXIS_STATUS> GetAxisStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshStatus(cancellationToken);
        int GetAxisSortKey8(ST_AXIS_STATE axis)
        {
            return axis.DisplayOrder;
        }

        ST_MOTOR_AXIS_STATUS SelectAxis9(ST_AXIS_STATE axis)
        {
            return new ST_MOTOR_AXIS_STATUS(
                            axis.AxisId,
                            axis.Name,
                            axis.CurrentPosition,
                            axis.TargetPosition,
                            axis.CommandPosition,
                            axis.ServoOn,
                            axis.HomeCompleted,
                            axis.LimitPlusOn,
                            axis.LimitMinusOn,
                            axis.AlarmOn);
        }

        var axes = _axes.Values
            .OrderBy(GetAxisSortKey8)
            .Select(SelectAxis9)
            .ToArray();

        return axes;
    }

    public IReadOnlyList<ST_IO_STATUS> GetIoStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshStatus(cancellationToken);
        int GetChannelSortKey10(ST_IO_STATE channel)
        {
            return channel.DisplayOrder;
        }

        ST_IO_STATUS SelectChannel11(ST_IO_STATE channel)
        {
            return new ST_IO_STATUS(
                            channel.Id,
                            channel.Address,
                            channel.Name,
                            channel.IsOn,
                            channel.IsOutput);
        }

        var io = _io.Values
            .OrderBy(GetChannelSortKey10)
            .Select(SelectChannel11)
            .ToArray();

        return io;
    }

    public IReadOnlyList<ST_MOTION_STATION_STATUS> GetStationStatus(
        CancellationToken cancellationToken = default)
    {
        var axes = GetAxisStatus(cancellationToken);
        string HandleStatusMap12(ST_MOTOR_AXIS_STATUS axis)
        {
            return axis.AxisId;
        }

        var statusMap = axes.ToDictionary(HandleStatusMap12, StringComparer.OrdinalIgnoreCase);
        string GroupByAxisCallback13(ST_MOTOR_DATA axis)
        {
            return NormalizeStationName(axis.StationName);
        }

        string GetGroupSortKey14(IGrouping<string, ST_MOTOR_DATA> group)
        {
            return group.Key;
        }

        ST_MOTION_STATION_STATUS SelectGroup15(IGrouping<string, ST_MOTOR_DATA> group)
        {
            ST_MOTOR_AXIS_STATUS? SelectAxis1(ST_MOTOR_DATA axis)
            {
                return statusMap.TryGetValue(NormalizeAxisId(axis.Name), out var status)
                                    ? status
                                    : null;
            }

            bool FilterStatus2(ST_MOTOR_AXIS_STATUS? status)
            {
                return status is not null;
            }

            string GetStatusSortKey3(ST_MOTOR_AXIS_STATUS status)
            {
                return status.AxisId;
            }

            var stationAxes = group
                .Select(SelectAxis1)
                .Where(FilterStatus2)
                .Cast<ST_MOTOR_AXIS_STATUS>()
                .OrderBy(GetStatusSortKey3, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string SelectAxis4(ST_MOTOR_DATA axis)
            {
                return axis.System;
            }

            bool MatchSystem5(string system)
            {
                return !string.IsNullOrWhiteSpace(system);
            }

            bool CheckAxis6(ST_MOTOR_AXIS_STATUS axis)
            {
                return axis.AlarmOn;
            }

            return new ST_MOTION_STATION_STATUS(
                group.Key,
                group.Select(SelectAxis4).FirstOrDefault(MatchSystem5) ?? "",
                stationAxes.Any(CheckAxis6),
                stationAxes);
        }
        return _axisData.Values
            .GroupBy(GroupByAxisCallback13)
            .OrderBy(GetGroupSortKey14, StringComparer.OrdinalIgnoreCase)
            .Select(SelectGroup15)
            .ToArray();
    }

    public IReadOnlyList<ST_MOTION_CONTROLLER_STATUS> GetControllerStatus(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string GroupByAxisCallback16(ST_MOTOR_DATA axis)
        {
            return GetControllerKey(axis.DevType, axis.DevNo);
        }

        ST_MOTION_CONTROLLER_STATUS SelectGroup17(IGrouping<string, ST_MOTOR_DATA> group)
        {
            var first = group.First();
            var registered = _controllers.TryGetValue(group.Key, out var controller);
            string SelectAxis7(ST_MOTOR_DATA axis)
            {
                return NormalizeAxisId(axis.Name);
            }

            string GetAxisSortKey9(string axis)
            {
                return axis;
            }

            var axes = group
                .Select(SelectAxis7)
                .OrderBy(GetAxisSortKey9, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ST_MOTION_CONTROLLER_STATUS(
                NormalizeControllerName(first.DevType),
                first.DevNo,
                registered,
                controller?.IsSimulation() ?? true,
                axes.Length,
                axes);
        }
        string GetItemSortKey18(ST_MOTION_CONTROLLER_STATUS item)
        {
            return item.DevType;
        }

        int GetItemSortKey19(ST_MOTION_CONTROLLER_STATUS item)
        {
            return item.DevNo;
        }

        var status = _axisData.Values
            .GroupBy(GroupByAxisCallback16, StringComparer.OrdinalIgnoreCase)
            .Select(SelectGroup17)
            .OrderBy(GetItemSortKey18, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetItemSortKey19)
            .ToArray();

        return status;
    }

    public void MoveAxis(
        string axisId,
        double targetPosition,
        CancellationToken cancellationToken = default)
    {
        Move(axisId, targetPosition, cancellationToken);
    }

    public void ServoOn(
        string axisId,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.ServoOn, cancellationToken: cancellationToken);
    }

    public void ServoOff(
        string axisId,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.ServoOff, cancellationToken: cancellationToken);
    }

    public void Home(
        string axisId,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.Home, cancellationToken: cancellationToken);
    }

    public void Move(
        string axisId,
        double targetPosition,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.MoveAbs, targetPosition, cancellationToken);
    }

    public void MoveRel(
        string axisId,
        double distance,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.MoveRel, distance, cancellationToken);
    }

    public void Stop(
        string axisId,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.Stop, cancellationToken: cancellationToken);
    }

    public void ResetAlarm(
        string axisId,
        CancellationToken cancellationToken = default)
    {
        ExecuteAxisCommand(axisId, EN_MOTION_COMMAND.ResetAlarm, cancellationToken: cancellationToken);
    }

    public void ExecuteAxisCommand(
        string axisId,
        EN_MOTION_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedAxisId = NormalizeAxisId(axisId);

        if (!_axisData.TryGetValue(normalizedAxisId, out var axisData))
        {
            throw new InvalidOperationException($"Motion axis was not registered in JHMI_MOTOR.csv: {axisId}");
        }

        if (!_axes.TryGetValue(normalizedAxisId, out var axisState))
        {
            throw new InvalidOperationException($"Motion axis state was not registered: {axisId}");
        }

        ValidateAxisCommand(axisData, axisState, command, parameter);

        if (!_simulationMode)
        {
            if (!_controllers.TryGetValue(GetControllerKey(axisData.DevType, axisData.DevNo), out var controller))
            {
                throw CreateMotionControllerNotRegisteredException(axisData.DevType, axisData.DevNo);
            }

            if (!controller.IsSimulation())
            {
                controller.ExecuteAxisCommand(axisData, command, parameter, cancellationToken);
            }
        }

        ApplyAxisCommand(normalizedAxisId, command, parameter);
    }

    public void StopMotion(string axisId, CancellationToken cancellationToken = default)
    {
        Stop(axisId, cancellationToken);
    }

    public void SetOutput(
        string ioName,
        bool isOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = GetIoChannelOrThrow(ioName);

        if (!channel.IsOutput)
        {
            throw new InvalidOperationException($"Motion IO is input only: {FormatIoReference(channel)}");
        }

        var controller = GetMotionController(channel.DevType, channel.DevNo);

        if (!_simulationMode && controller is null)
        {
            throw CreateMotionControllerNotRegisteredException(channel.DevType, channel.DevNo);
        }

        if (!_simulationMode && controller is not null && !controller.IsSimulation())
        {
            controller.SetOutput(channel.Address, isOn, cancellationToken);
        }

        channel.IsOn = isOn;
    }

    public ST_DEVICE_COMMAND_RESULT ExecuteMotionCommand(
        string axisId,
        EN_MOTION_COMMAND command,
        double parameter = 0.0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ExecuteAxisCommand(axisId, command, parameter, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"Motion {axisId} {FormatMotionCommand(command)} OK.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or TimeoutException or IOException)
        {
            return new ST_DEVICE_COMMAND_RESULT(
                false,
                $"Motion {axisId} {FormatMotionCommand(command)} failed. {exception.Message}");
        }
    }

    public ST_DEVICE_COMMAND_RESULT SetOutputCommand(
        string ioName,
        bool isOn,
        CancellationToken cancellationToken = default)
    {
        var command = isOn ? "ON" : "OFF";

        try
        {
            var channel = GetIoChannelOrThrow(ioName);
            SetOutput(channel.Address, isOn, cancellationToken);

            return new ST_DEVICE_COMMAND_RESULT(
                true,
                $"Motion IO {FormatIoReference(channel)} {command} OK.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or TimeoutException or IOException)
        {
            return new ST_DEVICE_COMMAND_RESULT(
                false,
                $"Motion IO {ioName} {command} failed. {exception.Message}");
        }
    }

    private CMotionController? GetMotionController(
        string devType,
        int devNo)
    {
        return _controllers.TryGetValue(GetControllerKey(devType, devNo), out var controller)
            ? controller
            : null;
    }

    private void RefreshAxisStatus(CancellationToken cancellationToken)
    {
        foreach (var axisData in _axisData.Values)
        {
            if (!_controllers.TryGetValue(GetControllerKey(axisData.DevType, axisData.DevNo), out var controller) || controller.IsSimulation())
            {
                continue;
            }

            try
            {
                var status = controller.ReadAxisStatus(axisData, cancellationToken);

                if (status is not null)
                {
                    ApplyAxisStatus(status);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or TimeoutException or IOException)
            {
                MarkAxisAlarm(axisData.Name);
            }
        }
    }

    private void RefreshIoStatus(CancellationToken cancellationToken)
    {
        foreach (var channel in _io.Values)
        {
            var controller = GetMotionController(channel.DevType, channel.DevNo);

            if (controller is null || controller.IsSimulation())
            {
                continue;
            }

            try
            {
                var isOn = controller.ReadIo(channel.Address, channel.IsOutput, cancellationToken);

                if (isOn.HasValue)
                {
                    channel.IsOn = isOn.Value;
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or TimeoutException or IOException)
            {
                // One IO read failure should not hide the rest of the monitor snapshot.
            }
        }
    }

    private void ApplyAxisStatus(ST_MOTOR_AXIS_STATUS status)
    {
        var axisId = NormalizeAxisId(status.AxisId);

        if (!_axes.TryGetValue(axisId, out var axis))
        {
            return;
        }

        axis.CurrentPosition = status.CurrentPosition;
        axis.TargetPosition = status.TargetPosition;
        axis.CommandPosition = status.CommandPosition;
        axis.ServoOn = status.ServoOn;
        axis.HomeCompleted = status.HomeCompleted;
        axis.LimitPlusOn = status.LimitPlusOn;
        axis.LimitMinusOn = status.LimitMinusOn;
        axis.AlarmOn = status.AlarmOn;
    }

    private void MarkAxisAlarm(string axisId)
    {
        var normalizedAxisId = NormalizeAxisId(axisId);

        if (_axes.TryGetValue(normalizedAxisId, out var axis))
        {
            axis.AlarmOn = true;
        }
    }

    private CMotionController? CreateMotionController(
        string controller,
        int deviceNo)
    {
        var controllerName = NormalizeControllerName(controller);

        if (!MotionControllerTypes.TryGetValue(controllerName, out var controllerType))
        {
            return null;
        }

        return Activator.CreateInstance(controllerType, _interfaceManager, deviceNo) as CMotionController
            ?? throw new InvalidOperationException($"Motion controller creation failed: {controllerName}");
    }

    private void ValidateAxisCommand(
        ST_MOTOR_DATA axisData,
        ST_AXIS_STATE axisState,
        EN_MOTION_COMMAND command,
        double parameter)
    {
        if (command is not (EN_MOTION_COMMAND.Home or EN_MOTION_COMMAND.MoveAbs or EN_MOTION_COMMAND.MoveRel))
        {
            return;
        }

        ValidatePreCheckIo(axisData);

        var targetPosition = command == EN_MOTION_COMMAND.MoveRel
            ? axisState.CurrentPosition + parameter
            : parameter;

        if (command == EN_MOTION_COMMAND.Home || axisData.Min >= axisData.Max)
        {
            return;
        }

        if (targetPosition < axisData.Min || targetPosition > axisData.Max)
        {
            throw new InvalidOperationException(
                $"Motion target is out of range. Axis={axisData.Name}, Station={NormalizeStationName(axisData.StationName)}, Target={targetPosition:F3}, Range={axisData.Min:F3}~{axisData.Max:F3}");
        }
    }

    private void ValidatePreCheckIo(ST_MOTOR_DATA axisData)
    {
        if (string.IsNullOrWhiteSpace(axisData.PreCheckIo))
        {
            return;
        }

        foreach (var condition in SplitPreCheckIo(axisData.PreCheckIo))
        {
            var channel = GetIoChannelOrThrow(condition.IoName);

            if (channel.IsOn != condition.ExpectedOn)
            {
                throw new InvalidOperationException(
                    $"Motion pre-check IO failed. Axis={axisData.Name}, IO={FormatIoReference(channel)}, Expected={(condition.ExpectedOn ? "ON" : "OFF")}, Current={(channel.IsOn ? "ON" : "OFF")}");
            }
        }
    }

    private static IEnumerable<ST_PRE_CHECK_IO> SplitPreCheckIo(string preCheckIo)
    {
        foreach (var token in preCheckIo.Split([';', ',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split(['=', ':'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var ioName = parts[0].Trim();
            var expectedOn = parts.Length < 2 || IsOnText(parts[1]);

            yield return new ST_PRE_CHECK_IO(ioName, expectedOn);
        }
    }

    private void ApplyAxisCommand(
        string axisId,
        EN_MOTION_COMMAND command,
        double parameter)
    {
        if (!_axes.TryGetValue(axisId, out var axis))
        {
            return;
        }

        switch (command)
        {
            case EN_MOTION_COMMAND.ServoOn:
                axis.ServoOn = true;
                break;
            case EN_MOTION_COMMAND.ServoOff:
                axis.ServoOn = false;
                break;
            case EN_MOTION_COMMAND.Home:
                axis.CurrentPosition = 0.0;
                axis.TargetPosition = 0.0;
                axis.CommandPosition = 0.0;
                axis.HomeCompleted = true;
                axis.AlarmOn = false;
                break;
            case EN_MOTION_COMMAND.MoveAbs:
                UpdateAxisPosition(axis, parameter);
                break;
            case EN_MOTION_COMMAND.MoveRel:
                UpdateAxisPosition(axis, axis.CurrentPosition + parameter);
                break;
            case EN_MOTION_COMMAND.Stop:
                axis.CommandPosition = axis.CurrentPosition;
                axis.TargetPosition = axis.CurrentPosition;
                break;
            case EN_MOTION_COMMAND.ResetAlarm:
                axis.AlarmOn = false;
                break;
        }
    }

    private static void UpdateAxisPosition(
        ST_AXIS_STATE axis,
        double targetPosition)
    {
        axis.TargetPosition = targetPosition;
        axis.CommandPosition = targetPosition;
        axis.CurrentPosition = targetPosition;
    }

    private static IReadOnlyList<ST_MOTOR_DATA> NormalizeMotors(IReadOnlyList<ST_MOTOR_DATA>? motors)
    {
        bool HandleLoaded20(ST_MOTOR_DATA axis)
        {
            return !string.IsNullOrWhiteSpace(axis.Name);
        }

        var loaded = motors?
            .Where(HandleLoaded20)
            .ToArray();

        return loaded is { Length: > 0 } ? loaded : CreateDefaultMotorData();
    }

    private static Dictionary<string, ST_AXIS_STATE> CreateAxes(IReadOnlyList<ST_MOTOR_DATA> motors)
    {
        bool FilterAxis21(ST_MOTOR_DATA axis)
        {
            return axis.Use;
        }

        ST_AXIS_STATE SelectAxis22(ST_MOTOR_DATA axis)
        {
            var position = GetInitialPosition(axis.Name);
            return new ST_AXIS_STATE(
                NormalizeAxisId(axis.Name),
                string.IsNullOrWhiteSpace(axis.DisplayName) ? axis.Name : axis.DisplayName,
                position,
                position,
                position,
                true,
                true,
                false,
                false,
                false,
                axis.Axis);
        }
        string ToDictionaryAxisCallback23(ST_AXIS_STATE axis)
        {
            return axis.AxisId;
        }

        return motors
            .Where(FilterAxis21)
            .Select(SelectAxis22)
            .ToDictionary(ToDictionaryAxisCallback23, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ST_MOTOR_DATA> CreateDefaultMotorData()
    {
        return [];
    }

    private static ST_MOTOR_DATA Motor(
        string name,
        int axis,
        string displayName,
        double initialPosition,
        string unit)
    {
        _ = initialPosition;

        return new ST_MOTOR_DATA(
            name,
            true,
            axis,
            -1,
            DefaultControllerName,
            0,
            0,
            0,
            1000.0,
            "MOTION",
            "DRILLING",
            "",
            displayName,
            "",
            false,
            false,
            "",
            "",
            "CYAN",
            false,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            unit,
            300.0,
            300.0,
            500.0,
            -120.0,
            120.0,
            0,
            30000,
            "",
            displayName,
            0.0,
            "");
    }

    private static double GetInitialPosition(string axisName)
    {
        double EvaluateValueSwitch1()
        {
            var switchValue = NormalizeAxisId(axisName);
            switch (switchValue)
            {
                case "GX":
                    return 12.340;
                case "GY":
                    return -8.960;
                case "X":
                    return 125.000;
                case "Y":
                    return -75.000;
                case "Z":
                    return 23.500;
                case "THETA":
                    return 0.002;
                case "ATTENUATOR":
                    return 55.000;
                case "BET_MAG":
                    return 1.000;
                case "BET_DIV":
                    return 1.000;
                default:
                    return 0.0;
            }
        }

        return EvaluateValueSwitch1();
    }

    private static string NormalizeAxisId(string axisId)
    {
        return axisId.Trim().ToUpperInvariant();
    }

    private static string NormalizeAddress(string address)
    {
        return address.Trim().ToUpperInvariant();
    }

    private ST_IO_STATE GetIoChannelOrThrow(string ioName)
    {
        if (string.IsNullOrWhiteSpace(ioName))
        {
            throw new InvalidOperationException("Motion IO name is empty.");
        }

        // Existing UI may still pass the raw address. Process code should pass the logical ID.
        var normalizedAddress = NormalizeAddress(ioName);

        if (_io.TryGetValue(NormalizeIoName(ioName), out var channel))
        {
            return channel;
        }
        bool FilterItem24(ST_IO_STATE item)
        {
            return item.Address.Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase);
        }

        var addressMatches = _io.Values
            .Where(FilterItem24)
            .ToArray();

        if (addressMatches.Length == 1)
        {
            return addressMatches[0];
        }

        var normalizedName = NormalizeIoName(ioName);
        bool FilterItem25(ST_IO_STATE item)
        {
            return NormalizeIoName(item.Name).Equals(normalizedName, StringComparison.OrdinalIgnoreCase);
        }

        var matches = _io.Values
            .Where(FilterItem25)
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Motion IO name is ambiguous: {ioName}. Matches={string.Join(", ", matches.Select(FormatIoReference))}");
        }
        int GetItemSortKey26(ST_IO_STATE item)
        {
            return item.DisplayOrder;
        }

        throw new InvalidOperationException(
            $"Motion IO was not registered: {ioName}. Available={string.Join(", ", _io.Values.OrderBy(GetItemSortKey26).Select(FormatIoReference))}");
    }

    private static string FormatIoReference(ST_IO_STATE channel)
    {
        return $"{channel.Id}({channel.Address})";
    }

    private static string NormalizeIoName(string value)
    {
        char SelectCh27(char ch)
        {
            return char.IsLetterOrDigit(ch) ? ch : '_';
        }

        var chars = value
            .Trim()
            .ToUpperInvariant()
            .Select(SelectCh27)
            .ToArray();

        var compact = new string(chars);

        while (compact.Contains("__", StringComparison.Ordinal))
        {
            compact = compact.Replace("__", "_", StringComparison.Ordinal);
        }

        return compact.Trim('_');
    }

    private static Dictionary<string, ST_IO_STATE> CreateIo(IReadOnlyList<ST_IO_DATA>? ioData)
    {
        bool FilterChannel28(ST_IO_DATA channel)
        {
            return channel.Use;
        }

        ST_IO_STATE SelectChannel29(ST_IO_DATA channel)
        {
            return new ST_IO_STATE(
                            NormalizeIoName(channel.Id),
                            NormalizeAddress(channel.Address),
                            string.IsNullOrWhiteSpace(channel.Name) ? channel.Id : channel.Name.Trim(),
                            channel.InitialState,
                            channel.IsOutput,
                            NormalizeControllerName(channel.DevType),
                            channel.DevNo,
                            channel.DisplayOrder,
                            channel.Description);
        }

        string ToDictionaryChannelCallback30(ST_IO_STATE channel)
        {
            return channel.Id;
        }

        return (ioData ?? [])
            .Where(FilterChannel28)
            .Select(SelectChannel29)
            .ToDictionary(ToDictionaryChannelCallback30, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeStationName(string stationName)
    {
        return string.IsNullOrWhiteSpace(stationName)
            ? "DRILLING"
            : stationName.Trim().ToUpperInvariant();
    }

    private static string GetControllerKey(
        string controller,
        int deviceNo)
    {
        return $"{NormalizeControllerName(controller)}:{deviceNo}";
    }

    private static IReadOnlyDictionary<string, Type> LoadMotionControllerTypes()
    {
        Dictionary<string, Type> controllerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        Type[] discoveredTypes = typeof(CMotionController).Assembly.GetTypes();
        foreach (Type discoveredType in discoveredTypes)
        {
            if (discoveredType.IsAbstract || !typeof(CMotionController).IsAssignableFrom(discoveredType))
            {
                continue;
            }

            CMotionControllerTypeAttribute? attribute =
                discoveredType.GetCustomAttribute<CMotionControllerTypeAttribute>();
            if (attribute is null || attribute.ControllerNames.Count == 0)
            {
                continue;
            }

            foreach (string controllerName in attribute.ControllerNames)
            {
                string normalizedName = NormalizeControllerName(controllerName);
                if (!controllerTypes.ContainsKey(normalizedName))
                {
                    controllerTypes.Add(normalizedName, discoveredType);
                }
            }
        }

        return controllerTypes;
    }

    private static InvalidOperationException CreateMotionControllerNotRegisteredException(
        string controller,
        int deviceNo)
    {
        return new InvalidOperationException(
            $"Motion controller is not registered. DevType={controller}, DevNo={deviceNo}. Add a C*Motion.cs class with CMotionControllerType.");
    }

    internal static string NormalizeControllerName(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultControllerName
            : value.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMotionCommand(EN_MOTION_COMMAND command)
    {
        string EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_MOTION_COMMAND.ServoOn:
                    return "SERVO ON";
                case EN_MOTION_COMMAND.ServoOff:
                    return "SERVO OFF";
                case EN_MOTION_COMMAND.MoveAbs:
                    return "ABS MOVE";
                case EN_MOTION_COMMAND.MoveRel:
                    return "REL MOVE";
                case EN_MOTION_COMMAND.ResetAlarm:
                    return "RESET ALARM";
                default:
                    return command.ToString().ToUpperInvariant();
            }
        }

        return EvaluateCommandSwitch2();
    }

    private static bool IsOnText(string value)
    {
        return value.Trim().ToUpperInvariant() is "1" or "ON" or "TRUE" or "YES";
    }

    private sealed record ST_PRE_CHECK_IO(
        string IoName,
        bool ExpectedOn);

    private sealed class ST_AXIS_STATE(
        string axisId,
        string name,
        double currentPosition,
        double targetPosition,
        double commandPosition,
        bool servoOn,
        bool homeCompleted,
        bool limitPlusOn,
        bool limitMinusOn,
        bool alarmOn,
        int displayOrder)
    {
        public string AxisId { get; } = axisId;

        public string Name { get; } = name;

        public double CurrentPosition { get; set; } = currentPosition;

        public double TargetPosition { get; set; } = targetPosition;

        public double CommandPosition { get; set; } = commandPosition;

        public bool ServoOn { get; set; } = servoOn;

        public bool HomeCompleted { get; set; } = homeCompleted;

        public bool LimitPlusOn { get; set; } = limitPlusOn;

        public bool LimitMinusOn { get; set; } = limitMinusOn;

        public bool AlarmOn { get; set; } = alarmOn;

        public int DisplayOrder { get; } = displayOrder;
    }

    private sealed class ST_IO_STATE(
        string id,
        string address,
        string name,
        bool isOn,
        bool isOutput,
        string devType,
        int devNo,
        int displayOrder,
        string description)
    {
        public string Id { get; } = id;

        public string Address { get; } = address;

        public string Name { get; } = name;

        public bool IsOn { get; set; } = isOn;

        public bool IsOutput { get; } = isOutput;

        public string DevType { get; } = devType;

        public int DevNo { get; } = devNo;

        public int DisplayOrder { get; } = displayOrder;

        public string Description { get; } = description;
    }
}
