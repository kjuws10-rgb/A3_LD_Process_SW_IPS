using System.Globalization;
using ACS.SPiiPlusNET;
using Drilling.Common.Interface;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CCommType("AcsNet")]
internal sealed class CACSComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private readonly SemaphoreSlim _commLock = new(1, 1);
    private Api? _api;

    public override async Task Connect(CancellationToken cancellationToken = default)
    {
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            ConnectLocked();
        }
        catch (Exception ex)
        {
            CloseApi();
            SetError(ex);
        }
        finally
        {
            _commLock.Release();
        }
    }

    public override async Task Disconnect(CancellationToken cancellationToken = default)
    {
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            CloseApi();
            SetState(EN_COMM_STATE.Offline);
        }
        finally
        {
            _commLock.Release();
        }
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            if (_api is null || ConnectionState != EN_COMM_STATE.Online)
            {
                ConnectLocked();
            }

            if (_api is null || ConnectionState != EN_COMM_STATE.Online)
            {
                return "";
            }

            cancellationToken.ThrowIfCancellationRequested();
            LastSent = function;
            LastReceived = ExecuteACSFunction(_api, function);
            LastError = "";
            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }
        catch (Exception ex)
        {
            LastReceived = "";
            SetError(ex);
            return "";
        }
        finally
        {
            _commLock.Release();
        }
    }

    private void ConnectLocked()
    {
        CloseApi();
        _api = new Api();

        if (IsSimulatorEndpoint())
        {
            _api.OpenCommSimulator();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Option.RemoteAddress) || Option.Port <= 0)
            {
                SetError("ACS endpoint is invalid.");
                return;
            }

            _api.OpenCommEthernet(Option.RemoteAddress, Option.Port);
        }

        LastError = "";
        SetState(EN_COMM_STATE.Online);
    }

    private string ExecuteACSFunction(Api api, string function)
    {
        var tokens = function.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2 || !tokens[0].Equals("ACS", StringComparison.OrdinalIgnoreCase))
        {
            api.Command(function);
            return "OK:COMMAND";
        }

        return tokens[1].ToUpperInvariant() switch
        {
            "AXIS" => ExecuteAxisFunction(api, tokens),
            "IO" => ExecuteIoFunction(api, tokens),
            _ => ExecuteRawACSCommand(api, function)
        };
    }

    private static string ExecuteAxisFunction(Api api, IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 5)
        {
            throw new InvalidOperationException("ACS axis command is invalid.");
        }

        var axisNo = ReadInt(tokens[2], "AXIS");
        var axis = ToAcsAxis(axisNo);
        var axisName = tokens[3];
        var command = tokens[4].ToUpperInvariant();

        switch (command)
        {
            case "SERVO_ON":
                api.Enable(axis);
                return $"OK:AXIS:{axisName}:SERVO_ON";
            case "SERVO_OFF":
                api.Disable(axis);
                return $"OK:AXIS:{axisName}:SERVO_OFF";
            case "HOME":
                api.Command($"HOME {axisNo}");
                return $"OK:AXIS:{axisName}:HOME";
            case "MOVE_ABS":
                api.ToPoint(
                    MotionFlags.ACSC_AMF_WAIT,
                    axis,
                    ReadDouble(tokens, 5, "MOVE_ABS"));
                return $"OK:AXIS:{axisName}:MOVE_ABS";
            case "MOVE_REL":
                api.ToPoint(
                    MotionFlags.ACSC_AMF_WAIT | MotionFlags.ACSC_AMF_RELATIVE,
                    axis,
                    ReadDouble(tokens, 5, "MOVE_REL"));
                return $"OK:AXIS:{axisName}:MOVE_REL";
            case "STOP":
                api.Halt(axis);
                return $"OK:AXIS:{axisName}:STOP";
            case "RESET_ALARM":
                api.FaultClear(axis);
                return $"OK:AXIS:{axisName}:RESET_ALARM";
            case "READ":
                return $"OK:AXIS:{axisName}:FPOS:{api.GetFPosition(axis):F6}";
            default:
                throw new InvalidOperationException($"ACS axis command is unknown: {command}");
        }
    }

    private static string ExecuteIoFunction(Api api, IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 4)
        {
            throw new InvalidOperationException("ACS IO command is invalid.");
        }

        var address = tokens[2].Trim().ToUpperInvariant();
        var (port, bit) = ParseIoAddress(address);
        var command = tokens[3].ToUpperInvariant();

        if (command == "READ")
        {
            var value = address.StartsWith('Y')
                ? api.GetOutput(port, bit)
                : api.GetInput(port, bit);

            return $"OK:IO:{address}:{(value != 0 ? "ON" : "OFF")}";
        }

        if (command is not ("ON" or "OFF"))
        {
            throw new InvalidOperationException($"ACS IO command is unknown: {command}");
        }

        var isOn = command == "ON";

        if (!address.StartsWith('Y'))
        {
            throw new InvalidOperationException($"ACS IO output command can use Y address only: {address}");
        }

        api.SetOutput(port, bit, isOn ? 1 : 0);

        return $"OK:IO:{address}:{(isOn ? "ON" : "OFF")}";
    }

    private static string ExecuteRawACSCommand(Api api, string function)
    {
        api.Command(function);
        return "OK:COMMAND";
    }

    private bool IsSimulatorEndpoint()
    {
        return Option.RemoteAddress.Equals("SIM", StringComparison.OrdinalIgnoreCase) ||
            Option.RemoteAddress.Equals("SIMUL", StringComparison.OrdinalIgnoreCase) ||
            Option.RemoteAddress.Equals("SIMULATOR", StringComparison.OrdinalIgnoreCase);
    }

    private void CloseApi()
    {
        if (_api is null)
        {
            return;
        }

        try
        {
            _api.CloseComm();
        }
        catch
        {
            // Close should never block application shutdown.
        }
        finally
        {
            _api = null;
        }
    }

    private static Axis ToAcsAxis(int axisNo)
    {
        if (axisNo < 0 || axisNo > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(axisNo), axisNo, "ACS axis number is out of range.");
        }

        return (Axis)axisNo;
    }

    private static (int Port, int Bit) ParseIoAddress(string address)
    {
        var normalized = address.Trim().ToUpperInvariant();

        if (normalized.Length < 2 || normalized[0] is not ('X' or 'Y') ||
            !int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException($"ACS IO address is invalid: {address}");
        }

        return (number / 8, number % 8);
    }

    private static int ReadInt(string value, string fieldName)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidOperationException($"ACS {fieldName} value is invalid: {value}");
    }

    private static double ReadDouble(IReadOnlyList<string> tokens, int index, string command)
    {
        if (tokens.Count <= index ||
            !double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"ACS {command} position is invalid.");
        }

        return result;
    }
}


