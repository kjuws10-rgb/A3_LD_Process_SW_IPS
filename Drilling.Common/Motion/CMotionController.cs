using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class CMotionControllerTypeAttribute(params string[] controllerNames) : Attribute
{
    public IReadOnlyList<string> ControllerNames { get; } = controllerNames
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(NormalizeControllerName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string NormalizeControllerName(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
    }
}

internal abstract class CMotionController(
    string controller,
    IInterfaceManager? interfaceManager,
    int deviceNo = 0)
{
    private readonly IInterfaceManager? _interfaceManager = interfaceManager;

    public string Controller { get; } = controller.Trim().ToUpperInvariant();

    public int DeviceNo { get; } = deviceNo;

    protected virtual EN_EQP_MODULE PrimaryModule => EN_EQP_MODULE.Motion;

    protected abstract string CommandPrefix { get; }

    public bool IsSimulation()
    {
        var interfaceData = GetInterfaceData();
        return interfaceData is null ||
            _interfaceManager?.IsSimul(interfaceData.Device, interfaceData.Number) != false;
    }

    public virtual async Task Initialize(
        IReadOnlyList<ST_MOTOR_DATA> axes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interfaceData = GetInterfaceData();

        if (_interfaceManager is null || interfaceData is null)
        {
            return;
        }

        if (!_interfaceManager.IsConnect(interfaceData.Device, interfaceData.Number))
        {
            await _interfaceManager.Connect(
                interfaceData.Device,
                interfaceData.Number,
                cancellationToken: cancellationToken);
        }
    }

    public virtual async Task Destroy(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interfaceData = GetInterfaceData();

        if (_interfaceManager is null || interfaceData is null)
        {
            return;
        }

        if (_interfaceManager.IsConnect(interfaceData.Device, interfaceData.Number))
        {
            await _interfaceManager.Disconnect(interfaceData.Device, interfaceData.Number, cancellationToken);
        }
    }

    public virtual async Task ExecuteAxisCommand(
        ST_MOTOR_DATA axis,
        EN_MOTION_COMMAND command,
        double parameter,
        CancellationToken cancellationToken = default)
    {
        await Send(BuildAxisCommand(axis, command, parameter), cancellationToken);
    }

    public virtual async Task SetOutput(
        string address,
        bool isOn,
        CancellationToken cancellationToken = default)
    {
        await Send(
            $"{CommandPrefix}:IO:{address}:{(isOn ? "ON" : "OFF")}",
            cancellationToken);
    }

    public virtual async Task<ST_MOTOR_AXIS_STATUS?> ReadAxisStatus(
        ST_MOTOR_DATA axis,
        CancellationToken cancellationToken = default)
    {
        var response = await Send(
            BuildAxisCommand(axis, EN_MOTION_COMMAND.Refresh, 0.0),
            cancellationToken);

        return TryParseAxisStatus(axis, response);
    }

    public virtual async Task<bool?> ReadIo(
        string address,
        bool isOutput,
        CancellationToken cancellationToken = default)
    {
        var response = await Send(
            $"{CommandPrefix}:IO:{address}:READ",
            cancellationToken);

        var tokens = response.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return tokens.Length >= 4 &&
            tokens[0].Equals("OK", StringComparison.OrdinalIgnoreCase) &&
            tokens[1].Equals("IO", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals(address, StringComparison.OrdinalIgnoreCase)
                ? tokens[3].Equals("ON", StringComparison.OrdinalIgnoreCase)
                : null;
    }

    protected virtual string BuildAxisCommand(
        ST_MOTOR_DATA axis,
        EN_MOTION_COMMAND command,
        double parameter)
    {
        var commandText = command switch
        {
            EN_MOTION_COMMAND.ServoOn => "SERVO_ON",
            EN_MOTION_COMMAND.ServoOff => "SERVO_OFF",
            EN_MOTION_COMMAND.Home => "HOME",
            EN_MOTION_COMMAND.MoveAbs => $"MOVE_ABS:{parameter:F3}",
            EN_MOTION_COMMAND.MoveRel => $"MOVE_REL:{parameter:F3}",
            EN_MOTION_COMMAND.Stop => "STOP",
            EN_MOTION_COMMAND.ResetAlarm => "RESET_ALARM",
            EN_MOTION_COMMAND.Refresh => "READ",
            _ => "READ"
        };

        return $"{CommandPrefix}:AXIS:{axis.Axis}:{axis.Name}:{commandText}";
    }

    protected async Task<string> Send(
        string command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interfaceData = GetInterfaceData();

        if (_interfaceManager is null || interfaceData is null)
        {
            throw new InvalidOperationException($"{Controller} motion interface is not registered.");
        }

        if (!_interfaceManager.IsConnect(interfaceData.Device, interfaceData.Number))
        {
            await _interfaceManager.Connect(
                interfaceData.Device,
                interfaceData.Number,
                cancellationToken: cancellationToken);
        }

        if (!_interfaceManager.IsConnect(interfaceData.Device, interfaceData.Number))
        {
            throw new InvalidOperationException(
                $"{Controller} motion interface is offline: {interfaceData.Device}[{interfaceData.Number}]/{interfaceData.NickName}");
        }

        var response = await _interfaceManager.ExecuteFunction(
            interfaceData.Device,
            interfaceData.Number,
            command,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new TimeoutException($"{Controller} motion command timeout: {command}");
        }

        if (response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{Controller} motion command failed. Command={command}, Response={response}");
        }

        return response;
    }

    private static ST_MOTOR_AXIS_STATUS? TryParseAxisStatus(
        ST_MOTOR_DATA axis,
        string response)
    {
        var tokens = response.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 5 ||
            !tokens[0].Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            !tokens[1].Equals("AXIS", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var command = tokens[3].ToUpperInvariant();

        if (command != "FPOS" || !double.TryParse(tokens[4], out var position))
        {
            return null;
        }

        var axisId = string.IsNullOrWhiteSpace(axis.Name)
            ? axis.Axis.ToString()
            : axis.Name.Trim().ToUpperInvariant();

        return new ST_MOTOR_AXIS_STATUS(
            axisId,
            string.IsNullOrWhiteSpace(axis.DisplayName) ? axisId : axis.DisplayName,
            position,
            position,
            position,
            true,
            true,
            false,
            false,
            false);
    }

    private ST_INTERFACE_DATA? GetInterfaceData()
    {
        if (_interfaceManager is null)
        {
            return null;
        }

        var primary = _interfaceManager
            .GetInterfaceList(PrimaryModule)
            .Where(data => data.Number == DeviceNo)
            .OrderBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (primary is not null)
        {
            return primary;
        }

        return _interfaceManager
            .GetInterfaceList(EN_EQP_MODULE.Motion)
            .Where(data => data.Number == DeviceNo)
            .OrderBy(data => data.NickName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}


