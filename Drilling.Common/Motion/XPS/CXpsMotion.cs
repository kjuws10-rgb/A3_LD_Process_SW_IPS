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
    protected override string CommandPrefix
    {
        get
        {
            return "XPS";
        }
    }

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
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_MOTION_COMMAND.ServoOn:
                    return "SERVO_ON";
                case EN_MOTION_COMMAND.ServoOff:
                    return "SERVO_OFF";
                case EN_MOTION_COMMAND.Home:
                    return "HOME";
                case EN_MOTION_COMMAND.MoveAbs:
                    return $"MOVE_ABS:{parameter:F6}";
                case EN_MOTION_COMMAND.MoveRel:
                    return $"MOVE_REL:{parameter:F6}";
                case EN_MOTION_COMMAND.Stop:
                    return "STOP";
                case EN_MOTION_COMMAND.ResetAlarm:
                    return "RESET_ALARM";
                case EN_MOTION_COMMAND.Refresh:
                    return "READ";
                default:
                    return "READ";
            }
        }

        var commandText = EvaluateCommandSwitch1();

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
        bool CheckPattern1(string pattern)
        {
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        return patterns.Any(CheckPattern1);
    }
}
