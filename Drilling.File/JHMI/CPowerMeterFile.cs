using System.Globalization;
using Drilling.Common.Interface;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CPowerMeterFile(string configRoot) : IPowerMeterFile
{
    private const string DefaultProcessFile = "POWER_CHECK.pwm";

    private static readonly IReadOnlyList<string> Headers =
    [
        "STEP",
        "OPTION_NAME",
        "POWER_OUT",
        "POWER_UNIT",
        "SETTING_ATT",
        "SETTING_POWER",
        "SETTING_FREQ",
        "MEASURE_CYCLE",
        "MEASURE_TIME",
        "MEASURE_INTERVAL",
        "START_DELAY",
        "COOLING_TIME",
        "ROTATOR",
        "MEASURE_POWER",
        "STATE"
    ];

    private readonly string _powerMeterDirectory = Path.Combine(configRoot, "PowerMeter");

    public Task<IReadOnlyList<string>> List(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();
        bool FilterName1(string? name)
        {
            return !string.IsNullOrWhiteSpace(name);
        }

        string GetNameSortKey2(string name)
        {
            return name;
        }

        var files = Directory
            .EnumerateFiles(_powerMeterDirectory, "*.pwm")
            .Select(Path.GetFileName)
            .Where(FilterName1)
            .Cast<string>()
            .OrderBy(GetNameSortKey2, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task Create(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();

        var path = GetProcessPath(processFile);
        if (System.IO.File.Exists(path))
        {
            throw new InvalidOperationException($"PowerMeter process file already exists: {Path.GetFileName(path)}");
        }
        ST_POWER_METER_STEP_DATA SelectStep3(ST_POWER_METER_STEP_DATA step)
        {
            return step with
            {
                State = step.StepNo == 1 ? "READY" : "WAIT",
                MeasurePower = null
            };
        }

        WriteSteps(path, CreateDefaultSteps().Select(SelectStep3).ToArray());
        return Task.CompletedTask;
    }

    public Task Delete(
        string processFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();

        var path = GetProcessPath(processFile);
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }

        if (!Directory.EnumerateFiles(_powerMeterDirectory, "*.pwm").Any())
        {
            WriteSteps(GetProcessPath(DefaultProcessFile), CreateDefaultSteps());
        }

        return Task.CompletedTask;
    }

    public Task Rename(
        string oldProcessFile,
        string newProcessFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();

        var oldPath = GetProcessPath(oldProcessFile);
        var newPath = GetProcessPath(newProcessFile);

        if (!System.IO.File.Exists(oldPath))
        {
            throw new FileNotFoundException("PowerMeter process file does not exist.", Path.GetFileName(oldPath));
        }

        if (System.IO.File.Exists(newPath))
        {
            throw new InvalidOperationException($"PowerMeter process file already exists: {Path.GetFileName(newPath)}");
        }

        System.IO.File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public Task<ST_POWER_METER_TABLE_DATA> Load(
        string processFile = "",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();
        bool FilterName4(string? name)
        {
            return !string.IsNullOrWhiteSpace(name);
        }

        string GetNameSortKey5(string name)
        {
            return name;
        }

        var files = Directory
            .EnumerateFiles(_powerMeterDirectory, "*.pwm")
            .Select(Path.GetFileName)
            .Where(FilterName4)
            .Cast<string>()
            .OrderBy(GetNameSortKey5, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectedFile = SelectProcessFile(files, processFile);
        var steps = ReadSteps(GetProcessPath(selectedFile));
        ST_POWER_METER_PROCESS_DATA SelectName6(string name)
        {
            return new ST_POWER_METER_PROCESS_DATA(
                            name,
                            name.Equals(selectedFile, StringComparison.OrdinalIgnoreCase));
        }

        var processes = files
            .Select(SelectName6)
            .ToArray();

        return Task.FromResult(new ST_POWER_METER_TABLE_DATA(processes, selectedFile, steps));
    }

    public Task Save(
        string processFile,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFiles();
        WriteSteps(GetProcessPath(NormalizeProcessFile(processFile)), steps);
        return Task.CompletedTask;
    }

    private void EnsureFiles()
    {
        Directory.CreateDirectory(_powerMeterDirectory);

        var defaults = CreateDefaultSteps();

        if (!System.IO.File.Exists(GetFormPath()))
        {
            WriteSteps(GetFormPath(), defaults);
        }

        if (!System.IO.File.Exists(GetProcessPath(DefaultProcessFile)))
        {
            WriteSteps(GetProcessPath(DefaultProcessFile), defaults);
        }

        var powerCalPath = GetProcessPath("POWER_CAL.pwm");
        if (!System.IO.File.Exists(powerCalPath))
        {
            ST_POWER_METER_STEP_DATA SelectStep7(ST_POWER_METER_STEP_DATA step)
            {
                return step with
                {
                    OptionName = step.OptionName.Replace("CHECK", "CAL", StringComparison.OrdinalIgnoreCase),
                    State = "WAIT",
                    MeasurePower = null
                };
            }

            WriteSteps(powerCalPath, defaults.Select(SelectStep7).ToArray());
        }

        var dailyCheckPath = GetProcessPath("DAILY_CHECK.pwm");
        if (!System.IO.File.Exists(dailyCheckPath))
        {
            ST_POWER_METER_STEP_DATA SelectStep8(ST_POWER_METER_STEP_DATA step)
            {
                return step with
                {
                    OptionName = step.OptionName.Replace("CHECK", "DAILY", StringComparison.OrdinalIgnoreCase),
                    State = "WAIT",
                    MeasurePower = null
                };
            }

            WriteSteps(dailyCheckPath, defaults.Take(3).Select(SelectStep8).ToArray());
        }
    }

    private List<ST_POWER_METER_STEP_DATA> ReadSteps(string path)
    {
        bool FilterRow9(IReadOnlyDictionary<string, string> row)
        {
            return !string.IsNullOrWhiteSpace(CCsvParser.Get(row, "STEP"));
        }

        ST_POWER_METER_STEP_DATA SelectRow10(IReadOnlyDictionary<string, string> row, int order)
        {
            return new ST_POWER_METER_STEP_DATA(
                            ReadInt(CCsvParser.Get(row, "STEP"), order + 1),
                            ReadText(CCsvParser.Get(row, "OPTION_NAME"), $"PWM_STEP_{order + 1:000}"),
                            ReadBool(CCsvParser.Get(row, "POWER_OUT"), true),
                            ReadText(CCsvParser.Get(row, "POWER_UNIT"), "W"),
                            ReadDouble(CCsvParser.Get(row, "SETTING_ATT"), 23.50),
                            ReadDouble(CCsvParser.Get(row, "SETTING_POWER"), 1.200),
                            ReadDouble(CCsvParser.Get(row, "SETTING_FREQ"), 20.0),
                            ReadInt(CCsvParser.Get(row, "MEASURE_CYCLE"), 3),
                            ReadInt(CCsvParser.Get(row, "MEASURE_TIME"), 1000),
                            ReadInt(CCsvParser.Get(row, "MEASURE_INTERVAL"), 100),
                            ReadInt(CCsvParser.Get(row, "START_DELAY"), 500),
                            ReadInt(CCsvParser.Get(row, "COOLING_TIME"), 300),
                            ReadDouble(CCsvParser.Get(row, "ROTATOR"), 0.0),
                            ReadNullableDouble(CCsvParser.Get(row, "MEASURE_POWER")),
                            ReadText(CCsvParser.Get(row, "STATE"), "WAIT"));
        }

        int GetStepSortKey11(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        return CCsvParser.Read(path)
            .Where(FilterRow9)
            .Select(SelectRow10)
            .OrderBy(GetStepSortKey11)
            .ToList();
    }

    private static void WriteSteps(
        string path,
        IReadOnlyList<ST_POWER_METER_STEP_DATA> steps)
    {
        int GetStepSortKey12(ST_POWER_METER_STEP_DATA step)
        {
            return step.StepNo;
        }

        Dictionary<string, string> SelectStep13(ST_POWER_METER_STEP_DATA step)
        {
            return new Dictionary<string, string>
            {
                ["STEP"] = step.StepNo.ToString(CultureInfo.InvariantCulture),
                ["OPTION_NAME"] = step.OptionName,
                ["POWER_OUT"] = step.PowerOut ? "ON" : "OFF",
                ["POWER_UNIT"] = step.PowerUnit,
                ["SETTING_ATT"] = step.SettingAtt.ToString("F2", CultureInfo.InvariantCulture),
                ["SETTING_POWER"] = step.SettingPower.ToString("F3", CultureInfo.InvariantCulture),
                ["SETTING_FREQ"] = step.SettingFreq.ToString("F1", CultureInfo.InvariantCulture),
                ["MEASURE_CYCLE"] = step.MeasureCycle.ToString(CultureInfo.InvariantCulture),
                ["MEASURE_TIME"] = step.MeasureTimeMs.ToString(CultureInfo.InvariantCulture),
                ["MEASURE_INTERVAL"] = step.MeasureIntervalMs.ToString(CultureInfo.InvariantCulture),
                ["START_DELAY"] = step.StartDelayMs.ToString(CultureInfo.InvariantCulture),
                ["COOLING_TIME"] = step.CoolingTimeMs.ToString(CultureInfo.InvariantCulture),
                ["ROTATOR"] = step.Rotator.ToString("F4", CultureInfo.InvariantCulture),
                ["MEASURE_POWER"] = step.MeasurePower?.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                ["STATE"] = step.State
            };
        }

        var rows = steps
            .OrderBy(GetStepSortKey12)
            .Select(SelectStep13);

        CCsvParser.Write(path, Headers, rows);
    }

    private static IReadOnlyList<ST_POWER_METER_STEP_DATA> CreateDefaultSteps()
    {
        return
        [
            new(1, "PWM_CHECK_HEAD01", true, "W", 23.50, 1.200, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.2040, "READY"),
            new(2, "PWM_CHECK_HEAD02", true, "W", 23.50, 1.200, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.2052, "WAIT"),
            new(3, "PWM_CHECK_HEAD03", true, "W", 23.50, 1.000, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.0068, "WAIT"),
            new(4, "PWM_CHECK_HEAD04", true, "W", 23.50, 1.000, 20.0, 3, 1000, 100, 500, 300, 0.0000, 1.0034, "WAIT"),
            new(5, "PWM_CHECK_HEAD05", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8020, "WAIT"),
            new(6, "PWM_CHECK_HEAD06", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8015, "WAIT"),
            new(7, "PWM_CHECK_HEAD07", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8008, "WAIT"),
            new(8, "PWM_CHECK_HEAD08", true, "W", 23.50, 0.800, 20.0, 2, 800, 100, 300, 200, 0.0000, 0.8024, "WAIT")
        ];
    }

    private string GetFormPath()
    {
        return Path.Combine(configRoot, "JHMI_POWERMETER.csv");
    }

    private string GetProcessPath(string processFile)
    {
        return Path.Combine(_powerMeterDirectory, NormalizeProcessFile(processFile));
    }

    private static string SelectProcessFile(
        IReadOnlyList<string> files,
        string processFile)
    {
        var normalized = NormalizeProcessFile(processFile);
        bool CheckFile14(string file)
        {
            return file.Equals(normalized, StringComparison.OrdinalIgnoreCase);
        }

        if (files.Any(CheckFile14))
        {
            return normalized;
        }
        bool CheckFile15(string file)
        {
            return file.Equals(DefaultProcessFile, StringComparison.OrdinalIgnoreCase);
        }

        if (files.Any(CheckFile15))
        {
            return DefaultProcessFile;
        }

        return files.FirstOrDefault() ?? DefaultProcessFile;
    }

    private static string NormalizeProcessFile(string processFile)
    {
        var fileName = Path.GetFileName(processFile.Trim());

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = DefaultProcessFile;
        }

        return fileName.EndsWith(".pwm", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pwm";
    }

    private static string ReadText(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim();
    }

    private static bool ReadBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var normalized = value.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("USE", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(string value, int defaultValue)
    {
        if (int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleResult)
            ? (int)Math.Round(doubleResult, MidpointRounding.AwayFromZero)
            : defaultValue;
    }

    private static double ReadDouble(string value, double defaultValue)
    {
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static double? ReadNullableDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
