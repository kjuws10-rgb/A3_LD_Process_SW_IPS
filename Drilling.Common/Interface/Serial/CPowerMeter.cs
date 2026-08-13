using System.Globalization;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

public enum EN_POWER_METER_COMMAND
{
    ReadPower,
    QueryHardwareDescription,
    QuerySerialNumber,
    QueryWaveLength,
    SetWaveLength,
    QueryBeamPosition,
    StartStreaming,
    StopStreaming,
    Reset,
    Refresh
}

public enum EN_POWER_METER_ERROR
{
    Ok = 0,
    Error = 1,
    Timeout = -1,
    InvalidResponse = -2,
    NotSupported = -99
}

public sealed record ST_POWER_METER_PROCESS_DATA(
    string FileName,
    bool IsSelected = false);

public sealed record ST_POWER_METER_STEP_DATA(
    int StepNo,
    string OptionName,
    bool PowerOut,
    string PowerUnit,
    double SettingAtt,
    double SettingPower,
    double SettingFreq,
    int MeasureCycle,
    int MeasureTimeMs,
    int MeasureIntervalMs,
    int StartDelayMs,
    int CoolingTimeMs,
    double Rotator,
    double? MeasurePower,
    string State);

public sealed record ST_POWER_METER_TABLE_DATA(
    IReadOnlyList<ST_POWER_METER_PROCESS_DATA> Processes,
    string SelectedFileName,
    IReadOnlyList<ST_POWER_METER_STEP_DATA> Steps)
{
    public static ST_POWER_METER_TABLE_DATA Empty => new([], "", []);
}

public interface IPowerMeterFile
{
    Task<IReadOnlyList<string>> List(CancellationToken cancellationToken = default);

    Task Create(
        string processFile,
        CancellationToken cancellationToken = default);

    Task Delete(
        string processFile,
        CancellationToken cancellationToken = default);

    Task Rename(
        string oldProcessFile,
        string newProcessFile,
        CancellationToken cancellationToken = default);

    Task<ST_POWER_METER_TABLE_DATA> Load(
        string processFile = "",
        CancellationToken cancellationToken = default);

    Task Save(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken = default);
}

[CCommType("Serial", "PowerMeter")]
[CCommType("ModbusSerial", "PowerMeter")]
internal sealed class CPowerMeter(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CSerialComm(data, option)
{
    protected override string CommandNewLine => "\r";

    protected override string FormatCommand(string function)
    {
        return function.Trim();
    }

    public static string Build(
        EN_POWER_METER_COMMAND command,
        double parameter = 0.0)
    {
        return command switch
        {
            EN_POWER_METER_COMMAND.ReadPower => "pw?",
            EN_POWER_METER_COMMAND.QueryHardwareDescription => "*ind",
            EN_POWER_METER_COMMAND.QuerySerialNumber => "msn?",
            EN_POWER_METER_COMMAND.QueryWaveLength => "wv?",
            EN_POWER_METER_COMMAND.SetWaveLength => $"wv {ToMeter(parameter).ToString("F8", CultureInfo.InvariantCulture)}",
            EN_POWER_METER_COMMAND.QueryBeamPosition => "pos",
            EN_POWER_METER_COMMAND.StartStreaming => "dst",
            EN_POWER_METER_COMMAND.StopStreaming => "dsp",
            EN_POWER_METER_COMMAND.Reset => "*rst",
            _ => ""
        };
    }

    public static bool IsSuccessResponse(string response)
    {
        return !string.IsNullOrWhiteSpace(response) &&
            !response.Trim().StartsWith("ERR", StringComparison.OrdinalIgnoreCase);
    }

    public static ST_POWER_METER_STATUS Apply(
        EN_POWER_METER_COMMAND command,
        double parameter,
        string response,
        ST_POWER_METER_STATUS current,
        bool simulation)
    {
        var value = simulation
            ? CreateSimulationResponse(command, parameter, current)
            : response.Trim();

        if (!simulation && command != EN_POWER_METER_COMMAND.Reset && !IsSuccessResponse(value))
        {
            return current with
            {
                LastError = EN_POWER_METER_ERROR.InvalidResponse,
                MeasuredAt = DateTimeOffset.Now
            };
        }

        var ok = current with
        {
            LastCommand = command.ToString().ToUpperInvariant(),
            LastError = EN_POWER_METER_ERROR.Ok,
            MeasuredAt = DateTimeOffset.Now
        };

        return command switch
        {
            EN_POWER_METER_COMMAND.ReadPower => ApplyPowerValue(ok, ReadDouble(value)),
            EN_POWER_METER_COMMAND.QueryHardwareDescription => ok with { ModelName = value },
            EN_POWER_METER_COMMAND.QuerySerialNumber => ok with { SerialNumber = value },
            EN_POWER_METER_COMMAND.QueryWaveLength => ok with { WaveLengthNm = ReadWaveLengthNm(value) },
            EN_POWER_METER_COMMAND.SetWaveLength => ok with { WaveLengthNm = parameter },
            EN_POWER_METER_COMMAND.QueryBeamPosition => ApplyBeamPosition(ok, value),
            EN_POWER_METER_COMMAND.StartStreaming => ok with { IsMeasuring = true },
            EN_POWER_METER_COMMAND.StopStreaming => ok with { IsMeasuring = false },
            EN_POWER_METER_COMMAND.Reset => ST_POWER_METER_STATUS.Empty with
            {
                Unit = ok.Unit,
                MeasuredAt = DateTimeOffset.Now,
                LastCommand = "RESET"
            },
            _ => ok
        };
    }

    private static ST_POWER_METER_STATUS ApplyPowerValue(
        ST_POWER_METER_STATUS current,
        double power)
    {
        var sampleCount = current.SampleCount + 1;
        var average = current.SampleCount <= 0
            ? power
            : ((current.AveragePower * current.SampleCount) + power) / sampleCount;
        var min = current.SampleCount <= 0 ? power : Math.Min(current.MinPower, power);
        var max = current.SampleCount <= 0 ? power : Math.Max(current.MaxPower, power);

        return current with
        {
            MeasuredPower = power,
            AveragePower = average,
            MinPower = min,
            MaxPower = max,
            SampleCount = sampleCount,
            Unit = "W"
        };
    }

    private static ST_POWER_METER_STATUS ApplyBeamPosition(
        ST_POWER_METER_STATUS current,
        string value)
    {
        var parts = value.Split(
            [',', ';', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return current;
        }

        return current with
        {
            BeamPositionX = ReadDouble(parts[0]),
            BeamPositionY = ReadDouble(parts[1])
        };
    }

    private static string CreateSimulationResponse(
        EN_POWER_METER_COMMAND command,
        double parameter,
        ST_POWER_METER_STATUS current)
    {
        var power = current.MeasuredPower <= 0.0
            ? 1.204
            : current.MeasuredPower + 0.003;

        return command switch
        {
            EN_POWER_METER_COMMAND.ReadPower => power.ToString("F4", CultureInfo.InvariantCulture),
            EN_POWER_METER_COMMAND.QueryHardwareDescription => string.IsNullOrWhiteSpace(current.ModelName) ? "PowerMax" : current.ModelName,
            EN_POWER_METER_COMMAND.QuerySerialNumber => string.IsNullOrWhiteSpace(current.SerialNumber) ? "PM_SIM_0000" : current.SerialNumber,
            EN_POWER_METER_COMMAND.QueryWaveLength => ToMeter(current.WaveLengthNm).ToString("0.########E+0", CultureInfo.InvariantCulture),
            EN_POWER_METER_COMMAND.SetWaveLength => ToMeter(parameter).ToString("0.########E+0", CultureInfo.InvariantCulture),
            EN_POWER_METER_COMMAND.QueryBeamPosition => $"{current.BeamPositionX.ToString("F3", CultureInfo.InvariantCulture)},{current.BeamPositionY.ToString("F3", CultureInfo.InvariantCulture)}",
            _ => "OK"
        };
    }

    private static double ReadWaveLengthNm(string value)
    {
        var number = ReadDouble(value);
        return number < 0.01 ? number * 1_000_000_000.0 : number;
    } 

    private static double ToMeter(double waveLengthNm)
    {
        return waveLengthNm <= 0.0 ? 355e-9 : waveLengthNm * 1e-9;
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

    private static string ReadLeadingNumber(string value)
    {
        var chars = value.Trim()
            .TakeWhile(character =>
                char.IsDigit(character) ||
                character == '-' ||
                character == '+' ||
                character == '.' ||
                character == 'E' ||
                character == 'e')
            .ToArray();

        return chars.Length == 0 ? "0" : new string(chars);
    }
}
