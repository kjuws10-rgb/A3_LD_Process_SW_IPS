using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("XPS", "XPS.NET", "XPS_NET", "NEWPORT_XPS")]
internal sealed class CXpsMotion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("XPS", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "XPS";

    public override async Task<ST_MOTOR_AXIS_STATUS?> ReadAxisStatus(
        ST_MOTOR_DATA axis,
        CancellationToken cancellationToken = default)
    {
        var response = await Send(
            BuildAxisCommand(axis, EN_MOTION_COMMAND.Refresh, 0.0),
            cancellationToken);

        return TryParseAxisStatus(axis, response);
    }

    protected override string BuildAxisCommand(
        ST_MOTOR_DATA axis,
        EN_MOTION_COMMAND command,
        double parameter)
    {
        var commandText = command switch
        {
            EN_MOTION_COMMAND.ServoOn => "SERVO_ON",
            EN_MOTION_COMMAND.ServoOff => "SERVO_OFF",
            EN_MOTION_COMMAND.Home => "HOME",
            EN_MOTION_COMMAND.MoveAbs => $"MOVE_ABS:{parameter:F6}",
            EN_MOTION_COMMAND.MoveRel => $"MOVE_REL:{parameter:F6}",
            EN_MOTION_COMMAND.Stop => "STOP",
            EN_MOTION_COMMAND.ResetAlarm => "RESET_ALARM",
            EN_MOTION_COMMAND.Refresh => "READ",
            _ => "READ"
        };

        return $"{CommandPrefix}:AXIS:{axis.Axis}:{axis.Name}:{GetGroupName(axis)}:{commandText}";
    }

    private static string GetGroupName(ST_MOTOR_DATA axis)
    {
        var axisName = axis.Name.Trim();
        var dotIndex = axisName.IndexOf('.', StringComparison.Ordinal);

        return dotIndex > 0 ? axisName[..dotIndex] : axisName;
    }

    private static ST_MOTOR_AXIS_STATUS? TryParseAxisStatus(
        ST_MOTOR_DATA axis,
        string response)
    {
        var tokens = response.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 5 ||
            !tokens[0].Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            !tokens[1].Equals("AXIS", StringComparison.OrdinalIgnoreCase) ||
            !tokens[3].Equals("FPOS", StringComparison.OrdinalIgnoreCase) ||
            !double.TryParse(tokens[4], out var position))
        {
            return null;
        }

        var statusText = ReadTokenValue(tokens, "GROUP_STATUS_TEXT");
        var motionStatus = ReadTokenValue(tokens, "MOTION_STATUS");
        var alarmOn = ContainsAny(statusText, "FAULT", "ERROR", "DISABLE", "NOT REFERENCED", "KILLED");
        var servoOn = !ContainsAny(statusText, "DISABLE", "NOT INITIALIZED", "KILLED");
        var homeCompleted = !ContainsAny(statusText, "NOT REFERENCED", "NOT INITIALIZED", "HOMING");
        var moving = ContainsAny(motionStatus, "MOVING", "TRAJECTORY", "RUNNING");
        var axisId = axis.Name.Trim().ToUpperInvariant();

        return new ST_MOTOR_AXIS_STATUS(
            axisId,
            string.IsNullOrWhiteSpace(axis.DisplayName) ? axisId : axis.DisplayName,
            position,
            position,
            moving ? double.NaN : position,
            servoOn,
            homeCompleted,
            false,
            false,
            alarmOn);
    }

    private static string ReadTokenValue(
        IReadOnlyList<string> tokens,
        string name)
    {
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return tokens[index + 1];
            }
        }

        return "";
    }

    private static bool ContainsAny(
        string value,
        params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
