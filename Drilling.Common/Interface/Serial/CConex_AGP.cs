using System.Globalization;
using System.IO;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

public enum EN_ATTENUATOR_COMMAND
{
    MoveAbs,
    MoveRel,
    Home,
    Stop,
    ResetAlarm,
    Refresh,
    PollCurrentPosition,
    PollTargetPosition,
    PollState
}

public enum EN_CONEX_AGP_ERROR
{
    Ok = 0,
    Error = 1,
    Timeout = -1,
    InvalidResponse = -2,
    NotSupported = -99
}

[CCommType("Serial", "Attenuator")]
[CCommType("ModbusSerial", "Attenuator")]
internal sealed class CConex_AGP(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CSerialComm(data, option)
{
    public static string Build(EN_ATTENUATOR_COMMAND command, double parameter = 0.0)
    {
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_ATTENUATOR_COMMAND.MoveAbs:
                    return $"PA:{parameter.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_ATTENUATOR_COMMAND.MoveRel:
                    return $"PR:{parameter.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_ATTENUATOR_COMMAND.Home:
                    return "OR";
                case EN_ATTENUATOR_COMMAND.Stop:
                    return "ST";
                case EN_ATTENUATOR_COMMAND.ResetAlarm:
                    return "RS";
                case EN_ATTENUATOR_COMMAND.Refresh:
                    return "TP";
                case EN_ATTENUATOR_COMMAND.PollCurrentPosition:
                    return "TP";
                case EN_ATTENUATOR_COMMAND.PollTargetPosition:
                    return "TH";
                case EN_ATTENUATOR_COMMAND.PollState:
                    return "TS";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch1();
    }

    public static bool IsSuccessResponse(string response)
    {
        return !string.IsNullOrWhiteSpace(response) &&
            !response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase);
    }

    public static ST_ATTENUATOR_STATUS Apply(
        EN_ATTENUATOR_COMMAND command,
        double parameter,
        string response,
        ST_ATTENUATOR_STATUS current,
        bool simulation)
    {
        var value = simulation
            ? CreateSimulationResponse(command, parameter, current)
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
            LastError = EN_CONEX_AGP_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        };
        ST_ATTENUATOR_STATUS EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_ATTENUATOR_COMMAND.MoveAbs:
                    return ok with
                    {
                        CurrentPosition = simulation ? parameter : ok.CurrentPosition,
                        TargetPosition = parameter,
                        CommandState = simulation ? "READY" : "MOVING"
                    };
                case EN_ATTENUATOR_COMMAND.MoveRel:
                    return ok with
                    {
                        CurrentPosition = simulation ? ok.CurrentPosition + parameter : ok.CurrentPosition,
                        TargetPosition = simulation ? ok.CurrentPosition + parameter : ok.TargetPosition + parameter,
                        CommandState = simulation ? "READY" : "MOVING"
                    };
                case EN_ATTENUATOR_COMMAND.Home:
                    return ok with
                    {
                        CurrentPosition = simulation ? 0.0 : ok.CurrentPosition,
                        TargetPosition = simulation ? 0.0 : ok.TargetPosition,
                        CommandState = simulation ? "READY" : "HOMING"
                    };
                case EN_ATTENUATOR_COMMAND.Stop:
                    return ok with { CommandState = "STOP" };
                case EN_ATTENUATOR_COMMAND.ResetAlarm:
                    return ok with { CommandState = "READY" };
                case EN_ATTENUATOR_COMMAND.Refresh or EN_ATTENUATOR_COMMAND.PollCurrentPosition:
                    return ok with
                    {
                        CurrentPosition = ReadTaggedDouble(value, "TP", ok.CurrentPosition),
                        CommandState = IsMovingState(ok.CommandState) ? ok.CommandState : "READY"
                    };
                case EN_ATTENUATOR_COMMAND.PollTargetPosition:
                    return ok with
                    {
                        TargetPosition = ReadTaggedDouble(value, "TH", ok.TargetPosition)
                    };
                case EN_ATTENUATOR_COMMAND.PollState:
                    return ok with
                    {
                        CommandState = ReadControllerState(value, ok.CommandState)
                    };
                default:
                    return ok;
            }
        }

        return EvaluateCommandSwitch2();
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        await SerialLock.WaitAsync(cancellationToken);

        try
        {
            return await ExecuteConex(function, cancellationToken);
        }
        finally
        {
            SerialLock.Release();
        }
    }

    private async Task<string> ExecuteConex(
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
            LastReceived = await Task.Run(() => ExecuteConex(function), cancellationToken);
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

    private string ExecuteConex(string function)
    {
        var parts = function.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = function.Trim().ToUpperInvariant();

        var pollingCommand = text.EndsWith("?", StringComparison.Ordinal)
            ? text[..^1]
            : text;

        if (IsPollingCommand(pollingCommand))
        {
            return SendPolling(pollingCommand);
        }

        var command = parts.Length > 0 ? parts[0].ToUpperInvariant() : text;
        string EvaluateCommandSwitch3()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case "PA" when parts.Length >= 2:
                    return SendSelecting("PA", parts[1]);
                case "PR" when parts.Length >= 2:
                    return SendSelecting("PR", parts[1]);
                case "OR":
                    return SendSelecting("OR", "");
                case "ST":
                    return SendSelecting("ST", "");
                case "RS":
                    return SendSelecting("RS", "");
                default:
                    return "ERR:-2";
            }
        }

        return EvaluateCommandSwitch3();
    }

    private string SendSelecting(string command, string value)
    {
        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        var tx = FormatDeviceCommand(command, value);
        LastSent = tx.TrimEnd('\r', '\n');
        SerialPort.Write(tx);
        return "OK";
    }

    private string SendPolling(string command)
    {
        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        var tx = FormatDeviceCommand(command, "");
        LastSent = tx.TrimEnd('\r', '\n');
        SerialPort.Write(tx);

        try
        {
            var response = SerialPort.ReadLine();

            return TryParseResponse(response, out _, out var responseCommand, out var value)
                ? responseCommand.Equals("TS", StringComparison.OrdinalIgnoreCase)
                    ? $"TS:{ReadTsError(value)}:{ReadTsState(value)}"
                    : $"{responseCommand}:{value.Trim()}"
                : "ERR:-2";
        }
        catch (TimeoutException)
        {
            var response = SerialPort.ReadExisting().Trim();

            if (string.IsNullOrWhiteSpace(response))
            {
                return "ERR:-1";
            }

            return TryParseResponse(response, out _, out var responseCommand, out var value)
                ? responseCommand.Equals("TS", StringComparison.OrdinalIgnoreCase)
                    ? $"TS:{ReadTsError(value)}:{ReadTsState(value)}"
                    : $"{responseCommand}:{value.Trim()}"
                : "ERR:-2";
        }
    }

    private string FormatDeviceCommand(string command, string value)
    {
        var address = ReadControllerAddress();
        return $"{address.ToString(CultureInfo.InvariantCulture)}{command}{value}\r\n";
    }

    private static bool IsPollingCommand(string command)
    {
        return command.Equals("TP", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("TH", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("TS", StringComparison.OrdinalIgnoreCase);
    }

    private int ReadControllerAddress()
    {
        if (Data.Extra is not null)
        {
            foreach (var key in new[] { "ADDRESS", "DEV_ADDRESS", "CONTROLLER_ADDRESS" })
            {
                if (Data.Extra.TryGetValue(key, out var value) &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address) &&
                    address is >= 1 and <= 31)
                {
                    return address;
                }
            }
        }

        return 1;
    }

    private static bool TryParseResponse(
        string response,
        out int address,
        out string command,
        out string value)
    {
        address = 0;
        command = "";
        value = "";

        var text = response.Trim();

        if (text.Length < 3 || !char.IsDigit(text[0]))
        {
            return false;
        }

        var twoDigitAddress = text.Length >= 4 && char.IsDigit(text[1]);
        var addressLength = twoDigitAddress ? 2 : 1;

        if (text.Length < addressLength + 2 ||
            !int.TryParse(text[..addressLength], NumberStyles.Integer, CultureInfo.InvariantCulture, out address))
        {
            return false;
        }

        command = text.Substring(addressLength, 2);
        value = text.Length > addressLength + 2
            ? text[(addressLength + 2)..]
            : "";

        return true;
    }

    private static string CreateSimulationResponse(
        EN_ATTENUATOR_COMMAND command,
        double parameter,
        ST_ATTENUATOR_STATUS current)
    {
        string EvaluateCommandSwitch4()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_ATTENUATOR_COMMAND.PollCurrentPosition or EN_ATTENUATOR_COMMAND.Refresh:
                    return $"TP:{current.CurrentPosition.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_ATTENUATOR_COMMAND.PollTargetPosition:
                    return $"TH:{current.TargetPosition.ToString("F3", CultureInfo.InvariantCulture)}";
                case EN_ATTENUATOR_COMMAND.PollState:
                    return "TS:0:READY";
                default:
                    return "OK";
            }
        }

        return EvaluateCommandSwitch4();
    }

    private static EN_CONEX_AGP_ERROR ReadError(string response)
    {
        var value = response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)
            ? response[4..]
            : "";

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return EN_CONEX_AGP_ERROR.Error;
        }
        EN_CONEX_AGP_ERROR EvaluateCodeSwitch5()
        {
            var switchValue = code;
            switch (switchValue)
            {
                case -99:
                    return EN_CONEX_AGP_ERROR.NotSupported;
                case -2:
                    return EN_CONEX_AGP_ERROR.InvalidResponse;
                case -1:
                    return EN_CONEX_AGP_ERROR.Timeout;
                default:
                    return EN_CONEX_AGP_ERROR.Error;
            }
        }

        return EvaluateCodeSwitch5();
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

    private static string ReadControllerState(string response, string defaultState)
    {
        if (!response.StartsWith("TS:", StringComparison.OrdinalIgnoreCase))
        {
            return defaultState;
        }

        var parts = response.Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? NormalizeControllerState(parts[2]) : defaultState;
    }

    private static string NormalizeControllerState(string value)
    {
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var state))
        {
            return value.ToUpperInvariant();
        }
        string EvaluateStateSwitch6()
        {
            var switchValue = state;
            switch (switchValue)
            {
                case 0x1E:
                    return "HOMING";
                case 0x28:
                    return "MOVING";
                case 0x32 or 0x33 or 0x34:
                    return "READY";
                default:
                    return $"STATE_{state:X2}";
            }
        }

        return EvaluateStateSwitch6();
    }

    private static bool IsMovingState(string value)
    {
        return value.Equals("MOVING", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("HOMING", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTsError(string value)
    {
        return value.Length >= 4 ? value[..4] : "0";
    }

    private static string ReadTsState(string value)
    {
        return value.Length >= 6 ? value.Substring(4, 2) : "0";
    }
}

public sealed record ST_ATTENUATOR_STATUS(
    double CurrentPosition,
    double TargetPosition,
    string CommandState,
    bool CommOk = true,
    EN_CONEX_AGP_ERROR LastError = EN_CONEX_AGP_ERROR.Ok,
    DateTimeOffset? UpdatedAt = null);


