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
    protected override string CommandNewLine => "\r";

    protected override string FormatCommand(string function)
    {
        return function.Trim();
    }

    public static string Build(EN_TALON_COMMAND command, double parameter)
    {
        return command switch
        {
            EN_TALON_COMMAND.SetDiodeCurrent => $"C1:{parameter.ToString("F2", CultureInfo.InvariantCulture)}",
            EN_TALON_COMMAND.SetQsw => $"Q:{(int)parameter}",
            EN_TALON_COMMAND.SetEprf => $"EPRF:{(int)parameter}",
            EN_TALON_COMMAND.SetLaserOnOff => parameter > 0 ? "ON" : "OFF",
            EN_TALON_COMMAND.SetShutterOpenClose => $"SHT:{(int)parameter}",
            EN_TALON_COMMAND.SetGateOpenClose => $"G:{(int)parameter}",
            EN_TALON_COMMAND.SetExtGateEnableDisable => $"GEXT:{(int)parameter}",
            EN_TALON_COMMAND.SetShg => $"SHG:{(int)parameter}",
            EN_TALON_COMMAND.SetShgAutotune => $"SAUTO:{(int)parameter}",
            EN_TALON_COMMAND.SetQMode => $"QMODE:{(int)parameter}",
            EN_TALON_COMMAND.GetDiodeCurrent => "?C1",
            EN_TALON_COMMAND.GetQsw => "?Q",
            EN_TALON_COMMAND.GetEprf => "?EPRF",
            EN_TALON_COMMAND.GetShutterOpenClose => "?SHT",
            EN_TALON_COMMAND.GetGateOpenClose => "?G",
            EN_TALON_COMMAND.GetExtGateEnableDisable => "?GEXT",
            EN_TALON_COMMAND.GetOutputPower => "?P",
            EN_TALON_COMMAND.GetShg => "?SHG",
            EN_TALON_COMMAND.GetShgAutotune => "?SAUTO",
            EN_TALON_COMMAND.GetThgSpot => "?MTR:TSPOT",
            EN_TALON_COMMAND.GetThgHour => "?MTR:THR",
            EN_TALON_COMMAND.GetQMode => "?QMODE",
            EN_TALON_COMMAND.GetDiodeTemp => "?T1",
            EN_TALON_COMMAND.GetTowerTemp => "?TT",
            EN_TALON_COMMAND.GetLaserOnOff => "?F",
            EN_TALON_COMMAND.RequestStatusString => "?F",
            EN_TALON_COMMAND.RequestStatusCode => "?FH",
            EN_TALON_COMMAND.RequestSave => "SAVE",
            _ => ""
        };
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

        return command switch
        {
            EN_TALON_COMMAND.SetDiodeCurrent => ok with { DiodeCurrent = parameter },
            EN_TALON_COMMAND.SetQsw => ok with { Qsw = (int)parameter },
            EN_TALON_COMMAND.SetEprf => ok with { Eprf = (int)parameter },
            EN_TALON_COMMAND.SetLaserOnOff => ok with { LaserOn = parameter > 0 },
            EN_TALON_COMMAND.SetShutterOpenClose => ok with { ShutterOpen = parameter > 0 },
            EN_TALON_COMMAND.SetGateOpenClose => ok with { GateOpen = parameter > 0 },
            EN_TALON_COMMAND.SetExtGateEnableDisable => ok with { ExtGateEnable = parameter > 0 },
            EN_TALON_COMMAND.SetShg => ok with { ShgReadBackCount = (uint)Math.Max(0, parameter) },
            EN_TALON_COMMAND.SetShgAutotune => ok with { ShgAutoTuneActive = parameter > 0 },
            EN_TALON_COMMAND.SetQMode => ok with { QMode = (int)parameter },
            EN_TALON_COMMAND.GetDiodeCurrent => ok with { DiodeCurrent = ReadDouble(value) },
            EN_TALON_COMMAND.GetQsw => ok with { Qsw = ReadInt(value) },
            EN_TALON_COMMAND.GetEprf => ok with { Eprf = ReadInt(value) },
            EN_TALON_COMMAND.GetDiodeTemp => ok with { DiodeTemp = ReadDouble(value) },
            EN_TALON_COMMAND.GetTowerTemp => ok with { TowerTemp = ReadDouble(value) },
            EN_TALON_COMMAND.GetOutputPower => ok with { OutputPower = ReadDouble(value) },
            EN_TALON_COMMAND.GetShutterOpenClose => ok with { ShutterOpen = ReadDouble(value) > 0.5 },
            EN_TALON_COMMAND.GetGateOpenClose => ok with { GateOpen = ReadBool(value) },
            EN_TALON_COMMAND.GetExtGateEnableDisable => ok with { ExtGateEnable = ReadBool(value) },
            EN_TALON_COMMAND.GetShg => ok with { ShgReadBackCount = (uint)Math.Max(0, ReadInt(value)) },
            EN_TALON_COMMAND.GetShgAutotune => ok with { ShgAutoTuneActive = ReadDouble(value) > 0.5 },
            EN_TALON_COMMAND.GetThgSpot => ok with { ThgSpot = ReadInt(value) },
            EN_TALON_COMMAND.GetThgHour => ok with { ThgHour = ReadDouble(value) },
            EN_TALON_COMMAND.GetQMode => ok with { QMode = ReadInt(value) },
            EN_TALON_COMMAND.GetLaserOnOff => ok with { LaserOn = ReadLaserEmission(value) },
            EN_TALON_COMMAND.RequestStatusString => ok with { StatusMessage = value },
            EN_TALON_COMMAND.RequestStatusCode => ok with { StatusCode = ReadInt(value) },
            _ => ok
        };
    }

    private static string CreateSimulationResponse(
        EN_TALON_COMMAND command,
        double parameter,
        ST_TALON_STATUS current)
    {
        return command switch
        {
            EN_TALON_COMMAND.GetDiodeCurrent => current.DiodeCurrent.ToString("F2", CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetQsw => current.Qsw.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetEprf => current.Eprf.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetDiodeTemp => (current.DiodeTemp <= 0 ? 24.6 : current.DiodeTemp).ToString("F1", CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetTowerTemp => (current.TowerTemp <= 0 ? 24.2 : current.TowerTemp).ToString("F1", CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetOutputPower => current.OutputPower.ToString("F3", CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetShutterOpenClose => current.ShutterOpen ? "1" : "0",
            EN_TALON_COMMAND.GetGateOpenClose => current.GateOpen ? "1" : "0",
            EN_TALON_COMMAND.GetExtGateEnableDisable => current.ExtGateEnable ? "1" : "0",
            EN_TALON_COMMAND.GetShg => current.ShgReadBackCount.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetShgAutotune => current.ShgAutoTuneActive ? "1" : "0",
            EN_TALON_COMMAND.GetThgSpot => current.ThgSpot.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetThgHour => current.ThgHour.ToString("F1", CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetQMode => current.QMode.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.GetLaserOnOff => current.LaserOn ? "1" : "0",
            EN_TALON_COMMAND.RequestStatusString => current.LaserOn ? "Emission" : "Standby",
            EN_TALON_COMMAND.RequestStatusCode => current.StatusCode.ToString(CultureInfo.InvariantCulture),
            EN_TALON_COMMAND.SetLaserOnOff => parameter > 0 ? "Emission" : "Standby",
            _ => "OK"
        };
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

