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
    protected readonly SemaphoreSlim SerialLock = new(1, 1);

    public override Task Connect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseSerialPort();

        try
        {
            if (string.IsNullOrWhiteSpace(Option.SerialPort) || Option.BaudRate <= 0)
            {
                SetError("Serial port option is invalid.");
                return Task.CompletedTask;
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

        return Task.CompletedTask;
    }

    public override Task Disconnect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseSerialPort();
        SetState(EN_COMM_STATE.Offline);
        return Task.CompletedTask;
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        await SerialLock.WaitAsync(cancellationToken);

        try
        {
            return await ExecuteSerial(function, cancellationToken);
        }
        finally
        {
            SerialLock.Release();
        }
    }

    private async Task<string> ExecuteSerial(
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

        LastSent = FormatCommand(function);

        try
        {
            LastReceived = await Task.Run(() =>
            {
                SerialPort.WriteLine(LastSent);

                try
                {
                    return SerialPort.ReadLine();
                }
                catch (TimeoutException)
                {
                    return SerialPort.ReadExisting().Trim();
                }
            }, cancellationToken);

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

    protected virtual string CommandNewLine => "\r\n";

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
        return NormalizeEnumValue(value) switch
        {
            "" or "NONE" or "NO" or "OFF" => Handshake.None,
            "XONXOFF" or "XONOFF" => Handshake.XOnXOff,
            "REQUESTTOSEND" or "RTSCTS" => Handshake.RequestToSend,
            "REQUESTTOSENDXONXOFF" or "RTSCTSXONXOFF" => Handshake.RequestToSendXOnXOff,
            _ => Handshake.None
        };
    }

    private static string NormalizeEnumValue(string value)
    {
        return value.Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}


