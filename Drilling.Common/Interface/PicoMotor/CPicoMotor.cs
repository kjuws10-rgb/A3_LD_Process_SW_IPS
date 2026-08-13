namespace Drilling.Common.Interface;

using System.Globalization;
using NewFocus.Picomotor;

public enum EN_PICO_MOTOR_COMMAND
{
    Connect,
    Disconnect,
    SelectMotor,
    SetVelocity,
    SetAcceleration,
    StopMotion,
    Home,
    JogNegative,
    JogPositive,
    MoveRelativeNegative,
    MoveRelativePositive,
    MoveAbsolute,
    AllMotorStart,
    AllMotorStop,
    Refresh
}

public enum EN_PICO_MOTOR_ERROR
{
    Ok = 0,
    Error = 1,
    OverTemp = 3,
    CommandNotExist = 6,
    ParameterOutOfRange = 7,
    AxisNoOutOfRange = 9,
    EepromWriteFail = 10,
    EepromReadFail = 11,
    AxisNoMissing = 37,
    CommandParameterMissing = 38,
    Rs485ExtFault = 46,
    Rs485CrcFault = 47,
    ControllerNoOutOfRange = 48,
    ScanInProgress = 49,
    Timeout = -1,
    InvalidResponse = -2,
    NotSupported = -99
}

public sealed record ST_PICO_MOTOR_STATUS(
    bool IsConnected,
    string Controller,
    int SelectedMotorNo,
    double Motor1Position,
    double Motor2Position,
    double Motor3Position,
    double Motor4Position,
    long HomePosition,
    double CurrentVelocity,
    double CurrentAcceleration,
    string MotionState,
    int ErrorCode,
    int AllMoveCurrentCount = 0,
    int AllMoveSetCount = 0,
    double AllMovePosition = 0.0,
    bool CommOk = true,
    EN_PICO_MOTOR_ERROR LastError = EN_PICO_MOTOR_ERROR.Ok,
    DateTimeOffset? UpdatedAt = null)
{
    public static ST_PICO_MOTOR_STATUS Empty { get; } = new(
        false,
        "8742",
        1,
        0.0,
        0.0,
        0.0,
        0.0,
        0,
        0.0,
        0.0,
        "IDLE",
        0,
        0,
        0,
        0.0,
        true,
        EN_PICO_MOTOR_ERROR.Ok,
        null);
}

public static class CPicoMotor
{
    public const int StepPerMillimeter = 50000;

    public static string BuildAxisCommand(
        int motorNo,
        string command,
        long? value = null,
        bool isQuery = false)
    {
        var axis = Math.Clamp(motorNo, 1, 4);

        if (isQuery)
        {
            return $"{axis}{command}?\n";
        }

        return value is null
            ? $"{axis}{command}\n"
            : $"{axis}{command}{value.Value.ToString(CultureInfo.InvariantCulture)}\n";
    }

    public static string BuildAxisCommand(
        int motorNo,
        string command,
        string value)
    {
        var axis = Math.Clamp(motorNo, 1, 4);
        return $"{axis}{command}{value}\n";
    }

    public static string BuildGlobalCommand(
        string command,
        bool isQuery = false)
    {
        return isQuery
            ? $"{command}?\n"
            : $"{command}\n";
    }

    public static long MillimeterToStep(double value)
    {
        return Convert.ToInt64(Math.Round(value * StepPerMillimeter, MidpointRounding.AwayFromZero));
    }

    public static double StepToMillimeter(long value)
    {
        return value / (double)StepPerMillimeter;
    }

    public static bool TryParseLong(string response, out long value)
    {
        return long.TryParse(NormalizeResponse(response), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseInt(string response, out int value)
    {
        return int.TryParse(NormalizeResponse(response), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static string NormalizeResponse(string response)
    {
        return response.Trim().TrimEnd('\0').Trim();
    }

    public static EN_PICO_MOTOR_ERROR ToError(int errorCode)
    {
        return Enum.IsDefined(typeof(EN_PICO_MOTOR_ERROR), errorCode)
            ? (EN_PICO_MOTOR_ERROR)errorCode
            : EN_PICO_MOTOR_ERROR.Error;
    }
}

public sealed class CPicoMotorCommandSession : IDisposable
{
    private readonly object _syncRoot = new();
    private CmdLib8742? _cmdLib;
    private string _deviceKey = string.Empty;

    public bool IsConnected
    {
        get
        {
            return _cmdLib is not null && !string.IsNullOrWhiteSpace(_deviceKey);
        }
    }

    public string DeviceKey
    {
        get
        {
            return _deviceKey;
        }
    }

    public bool Connect(int discoveryDelayMs = 5000)
    {
        lock (_syncRoot)
        {
            if (IsConnected)
            {
                return true;
            }

            var deviceKey = string.Empty;
            var cmdLib = new CmdLib8742(false, discoveryDelayMs, ref deviceKey);

            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                cmdLib.Shutdown();
                return false;
            }

            if (!cmdLib.Open(deviceKey))
            {
                cmdLib.Shutdown();
                return false;
            }

            _cmdLib = cmdLib;
            _deviceKey = deviceKey;
            return true;
        }
    }

    public void Disconnect()
    {
        lock (_syncRoot)
        {
            if (_cmdLib is null)
            {
                _deviceKey = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_deviceKey))
            {
                _cmdLib.Close(_deviceKey);
            }

            _cmdLib.Shutdown();
            _cmdLib = null;
            _deviceKey = string.Empty;
        }
    }

    public string GetIdentification()
    {
        var id = string.Empty;
        return Execute(command => command.GetIdentification(_deviceKey, ref id))
            ? id
            : string.Empty;
    }

    public int GetPositionStep(int motorNo)
    {
        var position = 0;
        ExecuteOrThrow(command => command.GetPosition(_deviceKey, ClampMotorNo(motorNo), ref position), "GetPosition");
        return position;
    }

    public int GetVelocityStep(int motorNo)
    {
        var velocity = 0;
        ExecuteOrThrow(command => command.GetVelocity(_deviceKey, ClampMotorNo(motorNo), ref velocity), "GetVelocity");
        return velocity;
    }

    public int GetAccelerationStep(int motorNo)
    {
        var acceleration = 0;
        ExecuteOrThrow(command => command.GetAcceleration(_deviceKey, ClampMotorNo(motorNo), ref acceleration), "GetAcceleration");
        return acceleration;
    }

    public bool GetMotionDone(int motorNo)
    {
        var isDone = false;
        ExecuteOrThrow(command => command.GetMotionDone(_deviceKey, ClampMotorNo(motorNo), ref isDone), "GetMotionDone");
        return isDone;
    }

    public int GetErrorCode()
    {
        var error = string.Empty;
        ExecuteOrThrow(command => command.GetErrorNum(_deviceKey, ref error), "GetErrorNum");
        return int.TryParse(CPicoMotor.NormalizeResponse(error), NumberStyles.Integer, CultureInfo.InvariantCulture, out var errorCode)
            ? errorCode
            : (int)EN_PICO_MOTOR_ERROR.InvalidResponse;
    }

    public long GetHomePositionStep(int motorNo)
    {
        var home = string.Empty;
        ExecuteOrThrow(command => command.Query(_deviceKey, $"{ClampMotorNo(motorNo)}DH?", ref home), "GetHomePosition");
        return long.TryParse(CPicoMotor.NormalizeResponse(home), NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeStep)
            ? homeStep
            : 0L;
    }

    public void SetVelocity(int motorNo, int velocityStep)
    {
        ExecuteOrThrow(command => command.SetVelocity(_deviceKey, ClampMotorNo(motorNo), velocityStep), "SetVelocity");
    }

    public void SetAcceleration(int motorNo, int accelerationStep)
    {
        ExecuteOrThrow(command => command.SetAcceleration(_deviceKey, ClampMotorNo(motorNo), accelerationStep), "SetAcceleration");
    }

    public void StopMotion(int motorNo)
    {
        ExecuteOrThrow(command => command.StopMotion(_deviceKey, ClampMotorNo(motorNo)), "StopMotion");
    }

    public void AbortMotion()
    {
        ExecuteOrThrow(command => command.AbortMotion(_deviceKey), "AbortMotion");
    }

    public void MoveHome(int motorNo)
    {
        var homeStep = GetHomePositionStep(motorNo);
        AbsoluteMove(motorNo, homeStep);
    }

    public void JogNegative(int motorNo)
    {
        ExecuteOrThrow(command => command.JogNegative(_deviceKey, ClampMotorNo(motorNo)), "JogNegative");
    }

    public void JogPositive(int motorNo)
    {
        ExecuteOrThrow(command => command.JogPositive(_deviceKey, ClampMotorNo(motorNo)), "JogPositive");
    }

    public void RelativeMove(int motorNo, long step)
    {
        ExecuteOrThrow(command => command.RelativeMove(_deviceKey, ClampMotorNo(motorNo), ToInt32Step(step)), "RelativeMove");
    }

    public void AbsoluteMove(int motorNo, long step)
    {
        ExecuteOrThrow(command => command.AbsoluteMove(_deviceKey, ClampMotorNo(motorNo), ToInt32Step(step)), "AbsoluteMove");
    }

    public void Dispose()
    {
        Disconnect();
    }

    private bool Execute(Func<CmdLib8742, bool> action)
    {
        lock (_syncRoot)
        {
            if (_cmdLib is null || string.IsNullOrWhiteSpace(_deviceKey))
            {
                return false;
            }

            return action(_cmdLib);
        }
    }

    private void ExecuteOrThrow(Func<CmdLib8742, bool> action, string commandName)
    {
        if (!Execute(action))
        {
            throw new InvalidOperationException($"PicoMotor CmdLib command failed. Command={commandName}");
        }
    }

    private static int ClampMotorNo(int motorNo)
    {
        return Math.Clamp(motorNo, 1, 4);
    }

    private static int ToInt32Step(long step)
    {
        return step switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)step
        };
    }
}
