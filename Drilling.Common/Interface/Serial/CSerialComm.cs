using System.IO;
using System.IO.Ports;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

[CCommType("Serial")]
[CCommType("ModbusSerial")]
internal class CSerialComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    protected SerialPort? SerialPort;

    protected override void ConnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseSerialPort();

        try
        {
            if (string.IsNullOrWhiteSpace(Option.SerialPort) || Option.BaudRate <= 0)
            {
                SetError("Serial port option is invalid.");
                return;
            }

            SerialPort = new SerialPort(
                Option.SerialPort,
                Option.BaudRate,
                ParseParity(Option.Parity),
                Option.DataBits,
                ParseStopBits(Option.StopBits))
            {
                Handshake = ParseHandshake(Option.Handshake),
                ReadTimeout = Math.Max(100, Option.TimeoutMs),
                WriteTimeout = Math.Max(100, Option.TimeoutMs),
                NewLine = CommandNewLine
            };

            SerialPort.Open();
            LastError = "";
            SetState(EN_COMM_STATE.Online);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            CloseSerialPort();
            SetError(ex);
        }
    }

    protected override void DisconnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseSerialPort();
        SetState(EN_COMM_STATE.Offline);
    }

    protected override string ExecuteCore(
        string function,
        CancellationToken cancellationToken)
    {
        return ExecuteSerial(function, cancellationToken);
    }

    private string ExecuteSerial(
        string function,
        CancellationToken cancellationToken)
    {
        if (SerialPort is null || !SerialPort.IsOpen)
        {
            ConnectCore(cancellationToken);
        }

        if (SerialPort is null || !SerialPort.IsOpen)
        {
            return "";
        }

        LastSent = FormatCommand(function);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SerialPort.WriteLine(LastSent);

            try
            {
                LastReceived = SerialPort.ReadLine();
            }
            catch (TimeoutException)
            {
                LastReceived = SerialPort.ReadExisting().Trim();
            }

            LastError = string.IsNullOrWhiteSpace(LastReceived)
                ? "Serial response timeout."
                : "";

            if (!string.IsNullOrWhiteSpace(LastError))
            {
                SetError(LastError);
                return "";
            }

            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ObjectDisposedException)
        {
            CloseSerialPort();
            SetError(ex);
            return "";
        }
    }

    protected virtual string CommandNewLine
    {
        get
        {
            return "\r\n";
        }
    }

    protected virtual string FormatCommand(string function)
    {
        return function;
    }

    protected void CloseSerialPort()
    {
        if (SerialPort is null)
        {
            return;
        }

        try
        {
            if (SerialPort.IsOpen)
            {
                SerialPort.Close();
            }
        }
        finally
        {
            SerialPort.Dispose();
            SerialPort = null;
        }
    }

    private static Parity ParseParity(string value)
    {
        return Enum.TryParse<Parity>(NormalizeEnumValue(value), ignoreCase: true, out var parity)
            ? parity
            : Parity.None;
    }

    private static StopBits ParseStopBits(string value)
    {
        return Enum.TryParse<StopBits>(NormalizeEnumValue(value), ignoreCase: true, out var stopBits)
            ? stopBits
            : StopBits.One;
    }

    private static Handshake ParseHandshake(string value)
    {
        Handshake EvaluateValueSwitch1()
        {
            var switchValue = NormalizeEnumValue(value);
            switch (switchValue)
            {
                case "" or "NONE" or "NO" or "OFF":
                    return Handshake.None;
                case "XONXOFF" or "XONOFF":
                    return Handshake.XOnXOff;
                case "REQUESTTOSEND" or "RTSCTS":
                    return Handshake.RequestToSend;
                case "REQUESTTOSENDXONXOFF" or "RTSCTSXONXOFF":
                    return Handshake.RequestToSendXOnXOff;
                default:
                    return Handshake.None;
            }
        }

        return EvaluateValueSwitch1();
    }

    private static string NormalizeEnumValue(string value)
    {
        return value.Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
