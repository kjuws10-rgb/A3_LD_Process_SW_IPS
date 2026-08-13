using System.Globalization;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

public enum EN_TALON_COMMAND
{
    SetDiodeCurrent,
    SetQsw,
    SetEprf,
    SetLaserOnOff,
    SetShutterOpenClose,
    SetGateOpenClose,
    SetExtGateEnableDisable,
    SetShg,
    SetShgAutotune,
    SetQMode,
    GetDiodeCurrent,
    GetQsw,
    GetEprf,
    GetShutterOpenClose,
    GetGateOpenClose,
    GetExtGateEnableDisable,
    GetOutputPower,
    GetShg,
    GetShgAutotune,
    GetThgSpot,
    GetThgHour,
    GetQMode,
    GetDiodeTemp,
    GetTowerTemp,
    GetLaserOnOff,
    RequestStatusString,
    RequestStatusCode,
    RequestSave
}

public enum EN_TALON_ERROR
{
    Ok = 0,
    Warning = 1,
    Error = 2,
    Timeout = -1,
    InvalidResponse = -2
}

[CCommType("Serial", "TalonLaser")]
[CCommType("ModbusSerial", "TalonLaser")]
internal sealed class CTalonLaser(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CSerialComm(data, option)
{
    protected override string CommandNewLine
    {
        get
        {
            return "\r";
        }
    }

    protected override string FormatCommand(string function)
    {
        return function.Trim();
    }

    public static string Build(EN_TALON_COMMAND command, double parameter)
    {
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_TALON_COMMAND.SetDiodeCurrent:
                    return $"C1:{parameter.ToString("F2", CultureInfo.InvariantCulture)}";
                case EN_TALON_COMMAND.SetQsw:
                    return $"Q:{(int)parameter}";
                case EN_TALON_COMMAND.SetEprf:
                    return $"EPRF:{(int)parameter}";
                case EN_TALON_COMMAND.SetLaserOnOff:
                    return parameter > 0 ? "ON" : "OFF";
                case EN_TALON_COMMAND.SetShutterOpenClose:
                    return $"SHT:{(int)parameter}";
                case EN_TALON_COMMAND.SetGateOpenClose:
                    return $"G:{(int)parameter}";
                case EN_TALON_COMMAND.SetExtGateEnableDisable:
                    return $"GEXT:{(int)parameter}";
                case EN_TALON_COMMAND.SetShg:
                    return $"SHG:{(int)parameter}";
                case EN_TALON_COMMAND.SetShgAutotune:
                    return $"SAUTO:{(int)parameter}";
                case EN_TALON_COMMAND.SetQMode:
                    return $"QMODE:{(int)parameter}";
                case EN_TALON_COMMAND.GetDiodeCurrent:
                    return "?C1";
                case EN_TALON_COMMAND.GetQsw:
                    return "?Q";
                case EN_TALON_COMMAND.GetEprf:
                    return "?EPRF";
                case EN_TALON_COMMAND.GetShutterOpenClose:
                    return "?SHT";
                case EN_TALON_COMMAND.GetGateOpenClose:
                    return "?G";
                case EN_TALON_COMMAND.GetExtGateEnableDisable:
                    return "?GEXT";
                case EN_TALON_COMMAND.GetOutputPower:
                    return "?P";
                case EN_TALON_COMMAND.GetShg:
                    return "?SHG";
                case EN_TALON_COMMAND.GetShgAutotune:
                    return "?SAUTO";
                case EN_TALON_COMMAND.GetThgSpot:
                    return "?MTR:TSPOT";
                case EN_TALON_COMMAND.GetThgHour:
                    return "?MTR:THR";
                case EN_TALON_COMMAND.GetQMode:
                    return "?QMODE";
                case EN_TALON_COMMAND.GetDiodeTemp:
                    return "?T1";
                case EN_TALON_COMMAND.GetTowerTemp:
                    return "?TT";
                case EN_TALON_COMMAND.GetLaserOnOff:
                    return "?F";
                case EN_TALON_COMMAND.RequestStatusString:
                    return "?F";
                case EN_TALON_COMMAND.RequestStatusCode:
                    return "?FH";
                case EN_TALON_COMMAND.RequestSave:
                    return "SAVE";
                default:
                    return "";
            }
        }

        return EvaluateCommandSwitch1();
    }

    public static bool IsValidResponse(string response)
    {
        return !string.IsNullOrWhiteSpace(response) &&
            !response.Trim().Equals("ERR", StringComparison.OrdinalIgnoreCase);
    }

    public static ST_TALON_STATUS Apply(
        EN_TALON_COMMAND command,
        double parameter,
        string response,
        ST_TALON_STATUS current,
        bool simulation)
    {
        var value = simulation
            ? CreateSimulationResponse(command, parameter, current)
            : response.Trim();

        if (!simulation && !IsValidResponse(value))
        {
            return current with { LastError = EN_TALON_ERROR.InvalidResponse };
        }

        var ok = current with { LastError = EN_TALON_ERROR.Ok };
        ST_TALON_STATUS EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_TALON_COMMAND.SetDiodeCurrent:
                    return ok with { DiodeCurrent = parameter };
                case EN_TALON_COMMAND.SetQsw:
                    return ok with { Qsw = (int)parameter };
                case EN_TALON_COMMAND.SetEprf:
                    return ok with { Eprf = (int)parameter };
                case EN_TALON_COMMAND.SetLaserOnOff:
                    return ok with { LaserOn = parameter > 0 };
                case EN_TALON_COMMAND.SetShutterOpenClose:
                    return ok with { ShutterOpen = parameter > 0 };
                case EN_TALON_COMMAND.SetGateOpenClose:
                    return ok with { GateOpen = parameter > 0 };
                case EN_TALON_COMMAND.SetExtGateEnableDisable:
                    return ok with { ExtGateEnable = parameter > 0 };
                case EN_TALON_COMMAND.SetShg:
                    return ok with { ShgReadBackCount = (uint)Math.Max(0, parameter) };
                case EN_TALON_COMMAND.SetShgAutotune:
                    return ok with { ShgAutoTuneActive = parameter > 0 };
                case EN_TALON_COMMAND.SetQMode:
                    return ok with { QMode = (int)parameter };
                case EN_TALON_COMMAND.GetDiodeCurrent:
                    return ok with { DiodeCurrent = ReadDouble(value) };
                case EN_TALON_COMMAND.GetQsw:
                    return ok with { Qsw = ReadInt(value) };
                case EN_TALON_COMMAND.GetEprf:
                    return ok with { Eprf = ReadInt(value) };
                case EN_TALON_COMMAND.GetDiodeTemp:
                    return ok with { DiodeTemp = ReadDouble(value) };
                case EN_TALON_COMMAND.GetTowerTemp:
                    return ok with { TowerTemp = ReadDouble(value) };
                case EN_TALON_COMMAND.GetOutputPower:
                    return ok with { OutputPower = ReadDouble(value) };
                case EN_TALON_COMMAND.GetShutterOpenClose:
                    return ok with { ShutterOpen = ReadDouble(value) > 0.5 };
                case EN_TALON_COMMAND.GetGateOpenClose:
                    return ok with { GateOpen = ReadBool(value) };
                case EN_TALON_COMMAND.GetExtGateEnableDisable:
                    return ok with { ExtGateEnable = ReadBool(value) };
                case EN_TALON_COMMAND.GetShg:
                    return ok with { ShgReadBackCount = (uint)Math.Max(0, ReadInt(value)) };
                case EN_TALON_COMMAND.GetShgAutotune:
                    return ok with { ShgAutoTuneActive = ReadDouble(value) > 0.5 };
                case EN_TALON_COMMAND.GetThgSpot:
                    return ok with { ThgSpot = ReadInt(value) };
                case EN_TALON_COMMAND.GetThgHour:
                    return ok with { ThgHour = ReadDouble(value) };
                case EN_TALON_COMMAND.GetQMode:
                    return ok with { QMode = ReadInt(value) };
                case EN_TALON_COMMAND.GetLaserOnOff:
                    return ok with { LaserOn = ReadLaserEmission(value) };
                case EN_TALON_COMMAND.RequestStatusString:
                    return ok with { StatusMessage = value };
                case EN_TALON_COMMAND.RequestStatusCode:
                    return ok with { StatusCode = ReadInt(value) };
                default:
                    return ok;
            }
        }

        return EvaluateCommandSwitch2();
    }

    private static string CreateSimulationResponse(
        EN_TALON_COMMAND command,
        double parameter,
        ST_TALON_STATUS current)
    {
        string EvaluateCommandSwitch3()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_TALON_COMMAND.GetDiodeCurrent:
                    return current.DiodeCurrent.ToString("F2", CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetQsw:
                    return current.Qsw.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetEprf:
                    return current.Eprf.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetDiodeTemp:
                    return (current.DiodeTemp <= 0 ? 24.6 : current.DiodeTemp).ToString("F1", CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetTowerTemp:
                    return (current.TowerTemp <= 0 ? 24.2 : current.TowerTemp).ToString("F1", CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetOutputPower:
                    return current.OutputPower.ToString("F3", CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetShutterOpenClose:
                    return current.ShutterOpen ? "1" : "0";
                case EN_TALON_COMMAND.GetGateOpenClose:
                    return current.GateOpen ? "1" : "0";
                case EN_TALON_COMMAND.GetExtGateEnableDisable:
                    return current.ExtGateEnable ? "1" : "0";
                case EN_TALON_COMMAND.GetShg:
                    return current.ShgReadBackCount.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetShgAutotune:
                    return current.ShgAutoTuneActive ? "1" : "0";
                case EN_TALON_COMMAND.GetThgSpot:
                    return current.ThgSpot.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetThgHour:
                    return current.ThgHour.ToString("F1", CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetQMode:
                    return current.QMode.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.GetLaserOnOff:
                    return current.LaserOn ? "1" : "0";
                case EN_TALON_COMMAND.RequestStatusString:
                    return current.LaserOn ? "Emission" : "Standby";
                case EN_TALON_COMMAND.RequestStatusCode:
                    return current.StatusCode.ToString(CultureInfo.InvariantCulture);
                case EN_TALON_COMMAND.SetLaserOnOff:
                    return parameter > 0 ? "Emission" : "Standby";
                default:
                    return "OK";
            }
        }

        return EvaluateCommandSwitch3();
    }

    private static double ReadDouble(string value)
    {
        var normalized = ReadLeadingNumber(value);
        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0.0;
    }

    private static int ReadInt(string value)
    {
        return (int)Math.Round(ReadDouble(value));
    }

    private static bool ReadBool(string value)
    {
        var normalized = value.Trim();

        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("OPEN", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadLaserEmission(string value)
    {
        var normalized = value.Trim();

        return normalized.Contains("emission", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadLeadingNumber(string value)
    {
        var chars = value.Trim()
            .TakeWhile(character =>
                char.IsDigit(character) ||
                character == '-' ||
                character == '+' ||
                character == '.')
            .ToArray();

        return chars.Length == 0 ? "0" : new string(chars);
    }
}

public sealed record ST_LASER_STATUS(
    bool PowerOn,
    bool ShutterOpen,
    bool GateOn,
    double OutputPower);

public sealed record ST_TALON_STATUS(
    double DiodeCurrent,
    int Qsw,
    int Eprf,
    double DiodeTemp,
    double TowerTemp,
    double OutputPower,
    bool LaserOn,
    bool ShutterOpen,
    bool GateOpen,
    bool ExtGateEnable,
    bool ShgAutoTuneActive,
    int ThgSpot,
    double ThgHour,
    int QMode,
    uint ShgReadBackCount,
    string StatusMessage,
    int StatusCode,
    EN_TALON_ERROR LastError)
{
    public static ST_TALON_STATUS Empty { get; } = new(
        0.0,
        0,
        0,
        0.0,
        0.0,
        0.0,
        false,
        false,
        false,
        false,
        false,
        0,
        0.0,
        0,
        0,
        "",
        0,
        EN_TALON_ERROR.Ok);
}

