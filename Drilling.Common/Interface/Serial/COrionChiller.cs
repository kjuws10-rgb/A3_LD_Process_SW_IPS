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

public enum EN_CHILLER_COMMAND
{
    Run,
    Stop,
    PumpOnly,
    SetTemperature,
    ResetAlarm,
    PollLiquidTemp,
    PollSetTemp,
    PollRunState,
    PollAlarmCode
}

public enum EN_CHILLER_RUN_STATE
{
    Stop = 0,
    Run = 1,
    PumpOnly = 2
}

public enum EN_CHILLER_ERROR
{
    Ok = 0,
    Error = 1,
    Timeout = -1,
    InvalidResponse = -2,
    NotSupported = -99
}

[CCommType("Serial", "Chiller")]
[CCommType("ModbusSerial", "Chiller")]
internal sealed class COrionChiller(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CSerialComm(data, option)
{
    private const byte Stx = 0x02;
    private const byte Etx = 0x03;
    private const byte Eot = 0x04;
    private const byte Enq = 0x05;
    private const byte Ack = 0x06;
    private const byte Nak = 0x15;
    private const int DataLength = 8;
    private const int DeviceAddress = 0;

    public static string Build(EN_CHILLER_COMMAND command, double parameter)
    {
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_CHILLER_COMMAND.Run:
                    return "ORION:RUN";
                case EN_CHILLER_COMMAND.Stop:
                    return "ORION:STOP";
                case EN_CHILLER_COMMAND.PumpOnly:
                    return "ORION:PUMP";
                case EN_CHILLER_COMMAND.SetTemperature:
                    return $"ORION:SETTEMP:{parameter.ToString("F1", CultureInfo.InvariantCulture)}";
                case EN_CHILLER_COMMAND.ResetAlarm:
                    return "ORION:RESETALARM";
                case EN_CHILLER_COMMAND.PollLiquidTemp:
                    return "ORION:POLL:M1";
                case EN_CHILLER_COMMAND.PollSetTemp:
                    return "ORION:POLL:S1";
                case EN_CHILLER_COMMAND.PollRunState:
                    return "ORION:POLL:JO";
                case EN_CHILLER_COMMAND.PollAlarmCode:
                    return "ORION:POLL:ER";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch1();
    }

    public static string DescribeCommand(EN_CHILLER_COMMAND command, double parameter)
    {
        string EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_CHILLER_COMMAND.Run:
                    return FormatTxFrame(CreateSelectingFrame("JO", CreateRunData(1)));
                case EN_CHILLER_COMMAND.Stop:
                    return FormatTxFrame(CreateSelectingFrame("JO", CreateRunData(0)));
                case EN_CHILLER_COMMAND.PumpOnly:
                    return FormatTxFrame(CreateSelectingFrame("JO", CreateRunData(2)));
                case EN_CHILLER_COMMAND.SetTemperature:
                    return DescribeSelectingCommand("S1", FormatTemperatureData(parameter));
                case EN_CHILLER_COMMAND.PollLiquidTemp:
                    return FormatTxFrame(CreatePollingFrame("M1"));
                case EN_CHILLER_COMMAND.PollSetTemp:
                    return FormatTxFrame(CreatePollingFrame("S1"));
                case EN_CHILLER_COMMAND.PollRunState:
                    return FormatTxFrame(CreatePollingFrame("JO"));
                case EN_CHILLER_COMMAND.PollAlarmCode:
                    return FormatTxFrame(CreatePollingFrame("ER"));
                case EN_CHILLER_COMMAND.ResetAlarm:
                    return "UNSUPPORTED:ORION:RESETALARM";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch2();
    }

    public static bool IsSuccessResponse(string response)
    {
        return !string.IsNullOrWhiteSpace(response) &&
            !response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase);
    }

    public static ST_ORION_CHILLER_STATUS Apply(
        EN_CHILLER_COMMAND command,
        double parameter,
        string response,
        ST_ORION_CHILLER_STATUS current,
        bool simulation)
    {
        if (command == EN_CHILLER_COMMAND.ResetAlarm)
        {
            return current with
            {
                LastError = EN_CHILLER_ERROR.NotSupported,
                UpdatedAt = DateTimeOffset.Now
            };
        }

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
            LastError = EN_CHILLER_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        };
        ST_ORION_CHILLER_STATUS EvaluateCommandSwitch3()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_CHILLER_COMMAND.Run:
                    return ok with { RunState = EN_CHILLER_RUN_STATE.Run };
                case EN_CHILLER_COMMAND.Stop:
                    return ok with { RunState = EN_CHILLER_RUN_STATE.Stop };
                case EN_CHILLER_COMMAND.PumpOnly:
                    return ok with { RunState = EN_CHILLER_RUN_STATE.PumpOnly };
                case EN_CHILLER_COMMAND.SetTemperature:
                    return ok with { SetTempC = parameter };
                case EN_CHILLER_COMMAND.PollLiquidTemp:
                    return ok with { LiquidTempC = ReadPollingDouble(value, "M1") };
                case EN_CHILLER_COMMAND.PollSetTemp:
                    return ok with { SetTempC = ReadPollingDouble(value, "S1") };
                case EN_CHILLER_COMMAND.PollRunState:
                    return ok with { RunState = ReadRunState(value) };
                case EN_CHILLER_COMMAND.PollAlarmCode:
                    return ok with { AlarmCode = ReadPollingData(value, "ER") };
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
            return await ExecuteOrion(function, cancellationToken);
        }
        finally
        {
            SerialLock.Release();
        }
    }

    private async Task<string> ExecuteOrion(
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

        LastSent = DescribeFunction(function);

        try
        {
            LastReceived = await Task.Run(() => ExecuteOrion(function), cancellationToken);
            LastError = LastReceived.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)
                ? LastReceived
                : "";

            SetState(LastReceived.StartsWith("ERR:-1", StringComparison.OrdinalIgnoreCase)
                ? EN_COMM_STATE.Offline
                : EN_COMM_STATE.Online);

            return LastReceived;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException or ObjectDisposedException)
        {
            CloseSerialPort();
            SetError(ex);
            return "";
        }
    }

    private string ExecuteOrion(string function)
    {
        var parts = function.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2 || !parts[0].Equals("ORION", StringComparison.OrdinalIgnoreCase))
        {
            return "ERR:-2";
        }
        string EvaluateValueSwitch4()
        {
            var switchValue = parts[1].ToUpperInvariant();
            switch (switchValue)
            {
                case "RUN":
                    return SendSelecting("JO", CreateRunData(1));
                case "STOP":
                    return SendSelecting("JO", CreateRunData(0));
                case "PUMP":
                    return SendSelecting("JO", CreateRunData(2));
                case "SETTEMP" when parts.Length >= 3 && double.TryParse(
                        parts[2],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var temp):
                    return SendSelecting("S1", FormatTemperatureData(temp));
                case "POLL" when parts.Length >= 3:
                    return SendPolling(parts[2]);
                case "RESETALARM":
                    return "ERR:-99";
                default:
                    return "ERR:-2";
            }
        }

        return EvaluateValueSwitch4();
    }

    private string SendPolling(string id)
    {
        var id2 = NormalizeId(id);

        if (id2.Length != 2)
        {
            return "ERR:-2";
        }

        var tx = CreatePollingFrame(id2);
        LastSent = FormatTxFrame(tx);

        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        SerialPort.Write(tx, 0, tx.Length);

        var frame = ReadFrame();

        if (frame.Length == 0)
        {
            return "ERR:-1";
        }

        return TryParsePollingFrame(frame, out var responseId, out var data)
            ? $"{responseId}:{data.Trim()}"
            : "ERR:-2";
    }

    private string SendSelecting(string id, string data)
    {
        var id2 = NormalizeId(id);

        if (id2.Length != 2 || data.Length != DataLength)
        {
            return "ERR:-2";
        }

        var tx = CreateSelectingFrame(id2, data);
        LastSent = FormatTxFrame(tx);

        SerialPort!.DiscardInBuffer();
        SerialPort.DiscardOutBuffer();
        SerialPort.Write(tx, 0, tx.Length);
        string EvaluateValueSwitch5()
        {
            var switchValue = WaitAck();
            switch (switchValue)
            {
                case 0:
                    return "OK";
                case -2:
                    return "ERR:-2";
                default:
                    return "ERR:-1";
            }
        }

        return EvaluateValueSwitch5();
    }

    private static string CreateRunData(int state)
    {
        var data = new char[DataLength];
        Array.Fill(data, ' ');
        data[0] = (char)('0' + state);
        return new string(data);
    }

    private static string FormatTemperatureData(double celsius)
    {
        if (celsius < 5.0 || celsius > 40.0)
        {
            return "";
        }

        var text = celsius.ToString("0.0", CultureInfo.InvariantCulture);
        return text.Length > DataLength
            ? ""
            : text.PadLeft(DataLength, ' ');
    }

    private static string DescribeFunction(string function)
    {
        var parts = function.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2 || !parts[0].Equals("ORION", StringComparison.OrdinalIgnoreCase))
        {
            return function;
        }
        string EvaluateValueSwitch6()
        {
            var switchValue = parts[1].ToUpperInvariant();
            switch (switchValue)
            {
                case "RUN":
                    return DescribeCommand(EN_CHILLER_COMMAND.Run, 0.0);
                case "STOP":
                    return DescribeCommand(EN_CHILLER_COMMAND.Stop, 0.0);
                case "PUMP":
                    return DescribeCommand(EN_CHILLER_COMMAND.PumpOnly, 0.0);
                case "SETTEMP" when parts.Length >= 3 && double.TryParse(
                        parts[2],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var temp):
                    return DescribeCommand(EN_CHILLER_COMMAND.SetTemperature, temp);
                case "POLL" when parts.Length >= 3:
                    return FormatTxFrame(CreatePollingFrame(parts[2]));
                case "RESETALARM":
                    return "UNSUPPORTED:ORION:RESETALARM";
                default:
                    return function;
            }
        }

        return EvaluateValueSwitch6();
    }

    private static string DescribeSelectingCommand(string id, string data)
    {
        return data.Length == DataLength
            ? FormatTxFrame(CreateSelectingFrame(id, data))
            : "";
    }

    private static byte[] CreatePollingFrame(string id)
    {
        var id2 = NormalizeId(id);
        var address = FormatAddress();

        return
        [
            Eot,
            (byte)address[0],
            (byte)address[1],
            (byte)id2[0],
            (byte)id2[1],
            Enq
        ];
    }

    private static byte[] CreateSelectingFrame(string id, string data)
    {
        var id2 = NormalizeId(id);
        var address = FormatAddress();
        var body = new List<byte>(2 + DataLength + 1)
        {
            (byte)id2[0],
            (byte)id2[1]
        };
        body.AddRange(Encoding.ASCII.GetBytes(data));
        body.Add(Etx);

        var tx = new List<byte>(1 + 2 + 1 + body.Count + 1)
        {
            Eot,
            (byte)address[0],
            (byte)address[1],
            Stx
        };
        tx.AddRange(body);
        tx.Add(CalcBcc(body));
        return tx.ToArray();
    }

    private static string FormatTxFrame(IReadOnlyList<byte> frame)
    {
        return frame.Count == 0
            ? ""
            : "TX HEX " + string.Join(" ", frame.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static string CreateSimulationResponse(
        EN_CHILLER_COMMAND command,
        double parameter,
        ST_ORION_CHILLER_STATUS current)
    {
        string EvaluateCommandSwitch7()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_CHILLER_COMMAND.PollLiquidTemp:
                    return $"M1:{current.LiquidTempC.ToString("F1", CultureInfo.InvariantCulture)}";
                case EN_CHILLER_COMMAND.PollSetTemp:
                    return $"S1:{current.SetTempC.ToString("F1", CultureInfo.InvariantCulture)}";
                case EN_CHILLER_COMMAND.PollRunState:
                    return $"JO:{(int)current.RunState}";
                case EN_CHILLER_COMMAND.PollAlarmCode:
                    return $"ER:{current.AlarmCode}";
                case EN_CHILLER_COMMAND.Run:
                    return "OK";
                case EN_CHILLER_COMMAND.Stop:
                    return "OK";
                case EN_CHILLER_COMMAND.PumpOnly:
                    return "OK";
                case EN_CHILLER_COMMAND.SetTemperature:
                    return "OK";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch7();
    }

    private static EN_CHILLER_ERROR ReadError(string response)
    {
        var value = response.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)
            ? response[4..]
            : "";

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return EN_CHILLER_ERROR.Error;
        }
        EN_CHILLER_ERROR EvaluateCodeSwitch8()
        {
            var switchValue = code;
            switch (switchValue)
            {
                case -99:
                    return EN_CHILLER_ERROR.NotSupported;
                case -2:
                    return EN_CHILLER_ERROR.InvalidResponse;
                case -1:
                    return EN_CHILLER_ERROR.Timeout;
                default:
                    return EN_CHILLER_ERROR.Error;
            }
        }

        return EvaluateCodeSwitch8();
    }

    private static double ReadPollingDouble(string response, string id)
    {
        var data = ReadPollingData(response, id);
        return double.TryParse(
            data,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0.0;
    }

    private static EN_CHILLER_RUN_STATE ReadRunState(string response)
    {
        var data = ReadPollingData(response, "JO");

        if (string.IsNullOrWhiteSpace(data))
        {
            data = ReadPollingData(response, "J0");
        }

        return data.Trim().StartsWith("2", StringComparison.Ordinal)
            ? EN_CHILLER_RUN_STATE.PumpOnly
            : data.Trim().StartsWith("1", StringComparison.Ordinal)
                ? EN_CHILLER_RUN_STATE.Run
                : EN_CHILLER_RUN_STATE.Stop;
    }

    private static string ReadPollingData(string response, string id)
    {
        var prefix = $"{id}:";
        return response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? response[prefix.Length..].Trim()
            : response.Trim();
    }

    private byte[] ReadFrame()
    {
        var frame = new List<byte>();

        try
        {
            while (true)
            {
                var value = SerialPort!.ReadByte();

                if (value == Stx)
                {
                    frame.Add((byte)value);
                    break;
                }
            }

            while (true)
            {
                var value = SerialPort!.ReadByte();
                frame.Add((byte)value);

                if (value == Etx)
                {
                    try
                    {
                        frame.Add((byte)SerialPort.ReadByte());
                    }
                    catch (TimeoutException)
                    {
                    }

                    return frame.ToArray();
                }
            }
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    private int WaitAck()
    {
        try
        {
            while (true)
            {
                var value = SerialPort!.ReadByte();

                if (value == Ack)
                {
                    return 0;
                }

                if (value == Nak)
                {
                    return -2;
                }
            }
        }
        catch (TimeoutException)
        {
            return -1;
        }
    }

    private static bool TryParsePollingFrame(
        IReadOnlyList<byte> frame,
        out string id,
        out string data)
    {
        id = "";
        data = "";

        if (frame.Count < 5 || frame[0] != Stx)
        {
            return false;
        }

        var etxIndex = -1;

        for (var index = 3; index < frame.Count; index++)
        {
            if (frame[index] == Etx)
            {
                etxIndex = index;
                break;
            }
        }

        if (etxIndex < 3)
        {
            return false;
        }

        id = Encoding.ASCII.GetString([frame[1], frame[2]]);
        data = Encoding.ASCII.GetString(frame.Skip(3).Take(etxIndex - 3).ToArray());
        return true;
    }

    private static string NormalizeId(string id)
    {
        string EvaluateValueSwitch9()
        {
            var switchValue = id.Trim().ToUpperInvariant();
            switch (switchValue)
            {
                case "J0":
                    return "JO";
                case var value:
                    return value;
                default:
                    throw new global::System.Runtime.CompilerServices.SwitchExpressionException(switchValue);
            }
        }

        return EvaluateValueSwitch9();
    }

    private static string FormatAddress()
    {
        return DeviceAddress.ToString("00", CultureInfo.InvariantCulture);
    }

    private static byte CalcBcc(IEnumerable<byte> bytes)
    {
        byte bcc = 0x00;

        foreach (var value in bytes)
        {
            bcc ^= value;
        }

        return bcc;
    }
}

public sealed record ST_CHILLER_STATUS(
    bool Running,
    double Temperature,
    double Flow,
    double Pressure,
    bool AlarmOn,
    double SetTemperature = 0.0,
    string RunState = "STOP",
    string AlarmCode = "");

public sealed record ST_ORION_CHILLER_STATUS(
    double LiquidTempC,
    double SetTempC,
    EN_CHILLER_RUN_STATE RunState,
    string AlarmCode,
    bool CommOk,
    EN_CHILLER_ERROR LastError,
    DateTimeOffset? UpdatedAt)
{
    public static ST_ORION_CHILLER_STATUS Empty { get; } = new(
        22.4,
        22.0,
        EN_CHILLER_RUN_STATE.Run,
        "",
        true,
        EN_CHILLER_ERROR.Ok,
        null);
}


