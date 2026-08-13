using System.Globalization;
using System.IO;
using System.Text;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

public enum EN_BET_COMMAND
{
    InitMotor,
    MoveManual,
    MoveMagnification,
    MoveDivergence,
    MoveTable,
    Stop,
    ResetAlarm,
    Refresh,
    PollMagnificationPosition,
    PollDivergencePosition
}

public enum EN_BET_ERROR
{
    Ok = 0,
    Error = 1,
    Timeout = -1,
    InvalidResponse = -2,
    NotSupported = -99
}

[CCommType("Serial", "Bet")]
[CCommType("ModbusSerial", "Bet")]
internal sealed class CBeamExpander(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CSerialComm(data, option)
{
    public static string Build(
        EN_BET_COMMAND command,
        double magnification = 0.0,
        double divergence = 0.0)
    {
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_BET_COMMAND.InitMotor:
                    return "#I:!";
                case EN_BET_COMMAND.MoveManual:
                    return string.Join(
                        ":",
                        "MOVE",
                        magnification.ToString("F3", CultureInfo.InvariantCulture),
                        divergence.ToString("F3", CultureInfo.InvariantCulture));
                case EN_BET_COMMAND.MoveMagnification:
                    return $"#2:{ToMotorStep(magnification).ToString(CultureInfo.InvariantCulture)}!";
                case EN_BET_COMMAND.MoveDivergence:
                    return $"#1:{ToMotorStep(divergence).ToString(CultureInfo.InvariantCulture)}!";
                case EN_BET_COMMAND.Refresh:
                    return "#8:$8:500";
                case EN_BET_COMMAND.PollMagnificationPosition:
                    return "#8:$8:500";
                case EN_BET_COMMAND.PollDivergencePosition:
                    return "#7:$7:500";
                case EN_BET_COMMAND.ResetAlarm:
                    return "#0:!";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch1();
    }

    public static string BuildLogText(
        EN_BET_COMMAND command,
        double magnification = 0.0,
        double divergence = 0.0)
    {
        string EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_BET_COMMAND.MoveManual:
                    return string.Join(
                        " / ",
                        $"#1:{ToMotorStep(divergence).ToString(CultureInfo.InvariantCulture)}!",
                        $"#2:{ToMotorStep(magnification).ToString(CultureInfo.InvariantCulture)}!");
                case EN_BET_COMMAND.MoveMagnification:
                    return $"#2:{ToMotorStep(magnification).ToString(CultureInfo.InvariantCulture)}!";
                case EN_BET_COMMAND.MoveDivergence:
                    return $"#1:{ToMotorStep(divergence).ToString(CultureInfo.InvariantCulture)}!";
                default:
                    return Build(command, magnification, divergence);
            }
        }

        return EvaluateCommandSwitch2();
    }

    public static bool IsSuccessResponse(string response)
    {
        return !string.IsNullOrWhiteSpace(response) &&
            !response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase);
    }

    public static ST_BET_STATUS Apply(
        EN_BET_COMMAND command,
        double magnification,
        double divergence,
        string response,
        ST_BET_STATUS current,
        bool simulation)
    {
        var value = simulation
            ? CreateSimulationResponse(command, magnification, divergence, current)
            : response.Trim();

        if (!simulation && !IsSuccessResponse(value))
        {
            return current with
            {
                CommOk = false,
                LastError = ReadError(value),
                UpdatedAt = DateTimeOffset.Now
            };
        }

        var ok = current with
        {
            CommOk = true,
            LastError = EN_BET_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        };
        ST_BET_STATUS EvaluateCommandSwitch3()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_BET_COMMAND.InitMotor:
                    return ok with
                    {
                        IsMoving = false,
                        MagHomeCompleted = true,
                        DivHomeCompleted = true,
                        LastCommand = "INIT"
                    };
                case EN_BET_COMMAND.MoveManual:
                    return ok with
                    {
                        CurrentMagnification = simulation ? magnification : ok.CurrentMagnification,
                        TargetMagnification = magnification,
                        CurrentDivergence = simulation ? divergence : ok.CurrentDivergence,
                        TargetDivergence = divergence,
                        MagnificationAxisPosition = simulation ? magnification : ok.MagnificationAxisPosition,
                        DivergenceAxisPosition = simulation ? divergence : ok.DivergenceAxisPosition,
                        IsMoving = !simulation,
                        LastCommand = "MOVE"
                    };
                case EN_BET_COMMAND.MoveMagnification:
                    return ok with
                    {
                        CurrentMagnification = simulation ? magnification : ok.CurrentMagnification,
                        TargetMagnification = magnification,
                        MagnificationAxisPosition = simulation ? magnification : ok.MagnificationAxisPosition,
                        IsMoving = !simulation,
                        LastCommand = "MOVE MAG"
                    };
                case EN_BET_COMMAND.MoveDivergence:
                    return ok with
                    {
                        CurrentDivergence = simulation ? divergence : ok.CurrentDivergence,
                        TargetDivergence = divergence,
                        DivergenceAxisPosition = simulation ? divergence : ok.DivergenceAxisPosition,
                        IsMoving = !simulation,
                        LastCommand = "MOVE DIV"
                    };
                case EN_BET_COMMAND.Stop:
                    return ok with
                    {
                        IsMoving = false,
                        LastCommand = "STOP"
                    };
                case EN_BET_COMMAND.ResetAlarm:
                    return ok with
                    {
                        AlarmOn = false,
                        LastCommand = "RESET"
                    };
                case EN_BET_COMMAND.Refresh or EN_BET_COMMAND.PollMagnificationPosition:
                    return ok with
                    {
                        CurrentMagnification = ReadTaggedDouble(value, "M2", ok.CurrentMagnification),
                        MagnificationAxisPosition = ReadTaggedDouble(value, "M2", ok.MagnificationAxisPosition),
                        IsMoving = false
                    };
                case EN_BET_COMMAND.PollDivergencePosition:
                    return ok with
                    {
                        CurrentDivergence = ReadTaggedDouble(value, "M1", ok.CurrentDivergence),
                        DivergenceAxisPosition = ReadTaggedDouble(value, "M1", ok.DivergenceAxisPosition),
                        IsMoving = false
                    };
                default:
                    return ok;
            }
        }

        return EvaluateCommandSwitch3();
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        await SerialLock.WaitAsync(cancellationToken);

        try
        {
            return await ExecuteBeamExpander(function, cancellationToken);
        }
        finally
        {
            SerialLock.Release();
        }
    }

    private async Task<string> ExecuteBeamExpander(
        string function,
        CancellationToken cancellationToken)
    {
        if (SerialPort is null || !SerialPort.IsOpen)
        {
            await Connect(cancellationToken);
        }

        if (SerialPort is null || !SerialPort.IsOpen)
        {
            return "";
        }

        LastSent = function;

        try
        {
            string RunTask1()
            {
                return ExecuteBeamExpander(function);
            }

            LastReceived = await Task.Run(RunTask1, cancellationToken);
            LastError = LastReceived.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)
                ? LastReceived
                : "";

            SetState(LastReceived.StartsWith("ERR:-1", StringComparison.OrdinalIgnoreCase)
                ? EN_COMM_STATE.Offline
                : EN_COMM_STATE.Online);

            return LastReceived;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or ObjectDisposedException)
        {
            CloseSerialPort();
            SetError(ex);
            return "";
        }
    }

    private string ExecuteBeamExpander(string function)
    {
        var parts = function.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = function.Trim().ToUpperInvariant();

        if (text is "#I:" or "#I:!" or "#0:" or "#0:!")
        {
            return SendSelecting(text);
        }

        if (text.StartsWith("#1:", StringComparison.Ordinal) ||
            text.StartsWith("#2:", StringComparison.Ordinal))
        {
            return SendSelecting(text);
        }

        if (IsPollingCommand(text))
        {
            return SendPolling(text);
        }
        string EvaluateValueSwitch4()
        {
            var switchValue = parts[0].ToUpperInvariant();
            switch (switchValue)
            {
                case "MOVE" when parts.Length >= 3 &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mag) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var div):
                    return SendMove(mag, div);
                default:
                    return "ERR:-2";
            }
        }

        return parts.Length > 0 ? EvaluateValueSwitch4() : "ERR:-2";
    }

    private string SendMove(double magnification, double divergence)
    {
        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();

        var divCommand = $"#1:{ToMotorStep(divergence).ToString(CultureInfo.InvariantCulture)}!";
        var magCommand = $"#2:{ToMotorStep(magnification).ToString(CultureInfo.InvariantCulture)}!";
        LastSent = $"{divCommand} / {magCommand}";

        if (!SendAndWaitAck(divCommand))
        {
            return "ERR:-2";
        }

        if (!SendAndWaitAck(magCommand))
        {
            return "ERR:-2";
        }

        return "OK";
    }

    private string SendSelecting(string command)
    {
        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        return SendAndWaitAck(command) ? "OK" : "ERR:-2";
    }

    private string SendPolling(string command)
    {
        string EvaluateValueSwitch5()
        {
            var switchValue = NormalizePollingCommand(command);
            switch (switchValue)
            {
                case "7":
                    return "M1";
                case "8":
                    return "M2";
                default:
                    return "";
            }
        }

        var normalized = EvaluateValueSwitch5();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "ERR:-2";
        }

        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        SerialPort.Write(command);

        try
        {
            var response = ReadDeviceResponse();
            return $"{normalized}:{ReadBeamPosition(response).ToString("F3", CultureInfo.InvariantCulture)}";
        }
        catch (TimeoutException)
        {
            var response = SerialPort.ReadExisting();

            if (string.IsNullOrWhiteSpace(response))
            {
                return "ERR:-1";
            }

            return $"{normalized}:{ReadBeamPosition(response).ToString("F3", CultureInfo.InvariantCulture)}";
        }
    }

    private bool SendAndWaitAck(string command)
    {
        SerialPort!.Write(command);
        var response = ReadDeviceResponse();
        return response.Trim().Contains('!');
    }

    private static bool IsPollingCommand(string command)
    {
        var normalized = NormalizePollingCommand(command);
        return normalized is "7" or "8";
    }

    private static string NormalizePollingCommand(string command)
    {
        var text = command.Trim().ToUpperInvariant();

        if (text.StartsWith("#7:", StringComparison.Ordinal))
        {
            return "7";
        }

        if (text.StartsWith("#8:", StringComparison.Ordinal))
        {
            return "8";
        }

        return "";
    }

    private string ReadDeviceResponse()
    {
        var builder = new StringBuilder();

        while (true)
        {
            var value = SerialPort!.ReadChar();

            if (value < 0)
            {
                break;
            }

            var character = (char)value;

            if (character is '\r' or '\n')
            {
                if (builder.Length > 0)
                {
                    break;
                }

                continue;
            }

            builder.Append(character);

            if (character == '!')
            {
                break;
            }

            if (builder.Length >= 4 &&
                builder[0] == '$' &&
                builder.ToString().Contains(':', StringComparison.Ordinal))
            {
                Thread.Sleep(5);

                if (SerialPort.BytesToRead == 0)
                {
                    break;
                }
            }
        }

        return builder.ToString();
    }

    private static int ToMotorStep(double value)
    {
        return Math.Clamp((int)Math.Round(value), 0, 4500);
    }

    private static string CreateSimulationResponse(
        EN_BET_COMMAND command,
        double magnification,
        double divergence,
        ST_BET_STATUS current)
    {
        string EvaluateCommandSwitch6()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_BET_COMMAND.PollMagnificationPosition or EN_BET_COMMAND.Refresh:
                    return $"M2:{current.CurrentMagnification.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_BET_COMMAND.PollDivergencePosition:
                    return $"M1:{current.CurrentDivergence.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_BET_COMMAND.MoveManual:
                    return $"MOVE:{magnification.ToString("F3", CultureInfo.InvariantCulture)}:{divergence.ToString("F3", CultureInfo.InvariantCulture)}";
                default:
                    return "OK";
            }
        }

        return EvaluateCommandSwitch6();
    }

    private static EN_BET_ERROR ReadError(string response)
    {
        var value = response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)
            ? response[4..]
            : "";

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return EN_BET_ERROR.Error;
        }
        EN_BET_ERROR EvaluateCodeSwitch7()
        {
            var switchValue = code;
            switch (switchValue)
            {
                case -99:
                    return EN_BET_ERROR.NotSupported;
                case -2:
                    return EN_BET_ERROR.InvalidResponse;
                case -1:
                    return EN_BET_ERROR.Timeout;
                default:
                    return EN_BET_ERROR.Error;
            }
        }

        return EvaluateCodeSwitch7();
    }

    private static double ReadTaggedDouble(string response, string tag, double defaultValue)
    {
        var prefix = $"{tag}:";
        var value = response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? response[prefix.Length..]
            : response;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static double ReadBeamPosition(string response)
    {
        var text = response.Trim();
        var colonIndex = text.IndexOf(':', StringComparison.Ordinal);
        var value = colonIndex >= 0 ? text[(colonIndex + 1)..] : text;
        bool TakeWhileCharacterCallback2(char character)
        {
            return char.IsDigit(character) ||
                            character == '-' ||
                            character == '+' ||
                            character == '.';
        }

        value = new string(value
            .TakeWhile(TakeWhileCharacterCallback2)
            .ToArray());

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var step))
        {
            return 0.0;
        }

        return step;
    }
}

public sealed record ST_BET_STATUS(
    double CurrentMagnification,
    double TargetMagnification,
    double CurrentDivergence,
    double TargetDivergence,
    double MagnificationAxisPosition,
    double DivergenceAxisPosition,
    bool IsMoving,
    bool MagHomeCompleted,
    bool DivHomeCompleted,
    bool AlarmOn,
    bool CommOk = true,
    EN_BET_ERROR LastError = EN_BET_ERROR.Ok,
    DateTimeOffset? UpdatedAt = null,
    string LastCommand = "");

public sealed record ST_BET_TABLE_DATA(
    int Index,
    double Magnification,
    double Divergence,
    string Description)
{
    private const double DefaultRowBeamSize = 32.64;

    public double SpotSize
    {
        get
        {
            return Magnification == 0.0
        ? 0.001
        : (DefaultRowBeamSize / Magnification) / 1000.0;
        }
    }
}


