using System.Globalization;
using CommandInterfaceXPS;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CCommType("XpsNet")]
internal sealed class CXpsComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private readonly SemaphoreSlim _commLock = new(1, 1);
    private XPS? _api;

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
            LastReceived = ExecuteXpsFunction(_api, function);
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

        if (string.IsNullOrWhiteSpace(Option.RemoteAddress) || Option.Port <= 0)
        {
            SetError("XPS endpoint is invalid.");
            return;
        }

        _api = new XPS();
        var result = _api.OpenInstrument(
            Option.RemoteAddress,
            Option.Port,
            Option.TimeoutMs);

        CheckResult(_api, result, "OpenInstrument");
        LastError = "";
        SetState(EN_COMM_STATE.Online);
    }

    private static string ExecuteXpsFunction(
        XPS api,
        string function)
    {
        var tokens = function.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2 || !tokens[0].Equals("XPS", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRawCommand(api, function);
        }

        return tokens[1].ToUpperInvariant() switch
        {
            "AXIS" => ExecuteAxisFunction(api, tokens),
            "IO" => ExecuteIoFunction(api, tokens),
            _ => ExecuteRawCommand(api, function)
        };
    }

    private static string ExecuteAxisFunction(
        XPS api,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 5)
        {
            throw new InvalidOperationException("XPS axis command is invalid.");
        }

        var axisName = tokens[3];
        var groupName = tokens.Count >= 6 ? tokens[4] : axisName;
        var commandIndex = tokens.Count >= 6 ? 5 : 4;
        var command = tokens[commandIndex].ToUpperInvariant();
        switch (command)
        {
            case "SERVO_ON":
                CheckResult(api, api.GroupMotionEnable(groupName, out var servoOnError), servoOnError);
                return $"OK:AXIS:{axisName}:SERVO_ON";
            case "SERVO_OFF":
                CheckResult(api, api.GroupMotionDisable(groupName, out var servoOffError), servoOffError);
                return $"OK:AXIS:{axisName}:SERVO_OFF";
            case "HOME":
                CheckResult(api, api.GroupInitialize(groupName, out var initializeError), initializeError);
                CheckResult(api, api.GroupHomeSearch(groupName, out var homeError), homeError);
                return $"OK:AXIS:{axisName}:HOME";
            case "MOVE_ABS":
                CheckResult(
                    api,
                    api.GroupMoveAbsolute(groupName, [ReadDouble(tokens, commandIndex + 1, "MOVE_ABS")], 1, out var moveAbsError),
                    moveAbsError);
                return $"OK:AXIS:{axisName}:MOVE_ABS";
            case "MOVE_REL":
                CheckResult(
                    api,
                    api.GroupMoveRelative(groupName, [ReadDouble(tokens, commandIndex + 1, "MOVE_REL")], 1, out var moveRelError),
                    moveRelError);
                return $"OK:AXIS:{axisName}:MOVE_REL";
            case "STOP":
                CheckResult(api, api.GroupMoveAbort(groupName, out var stopError), stopError);
                return $"OK:AXIS:{axisName}:STOP";
            case "RESET_ALARM":
                CheckResult(api, api.GroupInitialize(groupName, out var resetError), resetError);
                return $"OK:AXIS:{axisName}:RESET_ALARM";
            case "READ":
                CheckResult(
                    api,
                    api.GroupPositionCurrentGet(groupName, out var position, 1, out var readError),
                    readError);

                var groupStatusText = "";
                var motionStatusText = "";

                if (api.GroupStatusGet(groupName, out var groupStatus, out var groupStatusError) == 0)
                {
                    _ = api.GroupStatusStringGet(groupStatus, out groupStatusText, out _);
                }

                if (api.GroupMotionStatusGet(groupName, out var motionStatus, 1, out var motionStatusError) == 0 &&
                    motionStatus.Length > 0)
                {
                    motionStatusText = motionStatus[0].ToString(CultureInfo.InvariantCulture);
                }

                return string.Join(
                    ":",
                    "OK",
                    "AXIS",
                    axisName,
                    "FPOS",
                    position[0].ToString("F6", CultureInfo.InvariantCulture),
                    "GROUP_STATUS_TEXT",
                    SanitizeResponseText(groupStatusText),
                    "MOTION_STATUS",
                    SanitizeResponseText(motionStatusText));
            default:
                throw new InvalidOperationException($"XPS axis command is unknown: {command}");
        }
    }

    private static string ExecuteIoFunction(
        XPS api,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 4)
        {
            throw new InvalidOperationException("XPS IO command is invalid.");
        }

        var address = tokens[2].Trim().ToUpperInvariant();
        var (gpioName, bitNo) = ParseDigitalIoAddress(address);
        var command = tokens[3].ToUpperInvariant();
        var mask = checked((ushort)(1 << bitNo));

        if (command == "READ")
        {
            CheckResult(api, api.GPIODigitalGet(gpioName, out var value, out var readError), readError);
            return $"OK:IO:{address}:{((value & mask) != 0 ? "ON" : "OFF")}";
        }

        if (command is not ("ON" or "OFF"))
        {
            throw new InvalidOperationException($"XPS IO command is unknown: {command}");
        }

        var output = command == "ON" ? mask : (ushort)0;
        CheckResult(api, api.GPIODigitalSet(gpioName, mask, output, out var writeError), writeError);
        return $"OK:IO:{address}:{command}";
    }

    private static string ExecuteRawCommand(
        XPS api,
        string function)
    {
        return function.Trim().ToUpperInvariant() switch
        {
            "STATUS" => ExecuteStatus(api),
            "SOCKETS" => ExecuteSockets(api),
            _ => throw new InvalidOperationException($"XPS raw command is not supported yet: {function}")
        };

        string ExecuteStatus(XPS xps)
        {
            CheckResult(xps, xps.ControllerStatusGet(out var status, out var statusError), statusError);

            CheckResult(
                xps,
                xps.ControllerStatusStringGet(status, out var statusText, out var statusTextError),
                statusTextError);

            return $"OK:STATUS:{status}:{statusText}";
        }

        string ExecuteSockets(XPS xps)
        {
            CheckResult(xps, xps.SocketsStatusGet(out var result, out var socketError), socketError);
            return $"OK:SOCKETS:{result}";
        }
    }

    private void CloseApi()
    {
        if (_api is null)
        {
            return;
        }

        try
        {
            _api.CloseInstrument();
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

    private static void CheckResult(
        XPS api,
        int result,
        string detail)
    {
        if (result == 0)
        {
            return;
        }

        var error = "";
        try
        {
            _ = api.ErrorStringGet(result, out error, out _);
        }
        catch
        {
            error = "";
        }

        var message = string.IsNullOrWhiteSpace(error)
            ? detail
            : error;

        throw new InvalidOperationException($"XPS command failed. Code={result}, Detail={message}");
    }

    private static double ReadDouble(
        IReadOnlyList<string> tokens,
        int index,
        string command)
    {
        if (tokens.Count <= index ||
            !double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"XPS {command} position is invalid.");
        }

        return result;
    }

    private static (string GpioName, int BitNo) ParseDigitalIoAddress(string address)
    {
        var normalized = address.Trim().ToUpperInvariant();
        var separatorIndex = normalized.LastIndexOfAny(['.', ':', '/', '[']);

        if (separatorIndex < 1)
        {
            throw new InvalidOperationException(
                $"XPS digital IO address is invalid: {address}. Use GPIOName.BitNo, for example GPIO1.0");
        }

        var gpioName = normalized[..separatorIndex].TrimEnd(']');
        var bitText = normalized[(separatorIndex + 1)..].TrimEnd(']');

        if (string.IsNullOrWhiteSpace(gpioName) ||
            !int.TryParse(bitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitNo) ||
            bitNo < 0 ||
            bitNo > 15)
        {
            throw new InvalidOperationException(
                $"XPS digital IO address is invalid: {address}. Bit number must be 0~15.");
        }

        return (gpioName, bitNo);
    }

    private static string SanitizeResponseText(string value)
    {
        return value
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
