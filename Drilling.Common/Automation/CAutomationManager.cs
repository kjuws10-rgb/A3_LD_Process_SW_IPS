using System.Globalization;
using Drilling.Common.Interface;
using Drilling.Common.Managers;

namespace Drilling.Common.Automation;

public interface IAutomationManager
{
    Task Connect(
        int number = 0,
        CancellationToken cancellationToken = default);

    Task Disconnect(
        int number = 0,
        CancellationToken cancellationToken = default);

    bool IsConnect(int number = 0);

    bool IsSimul(int number = 0);

    Task<string> ReadStatus(
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> Move(
        string axisName,
        double targetPosition,
        double velocity = 100.0,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> MoveRel(
        string axisName,
        double distance,
        double velocity = 100.0,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> Stop(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> ServoOn(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> ServoOff(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> Home(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> ResetAlarm(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> ReadAxis(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<ST_AUTOMATION_AXIS_STATUS> ReadAxisStatus(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> RunLocalScript(
        string localScriptPath,
        string controllerFileName = "",
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> UploadScript(
        string localScriptPath,
        string scriptFileName = "",
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> RunScript(
        string scriptFileName,
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> RunBufferedScript(
        string localScriptPath,
        string scriptFileName = "",
        int taskIndex = 1,
        int number = 0,
        int queueSize = 100,
        int linesPerCommand = 1000,
        int timeoutMs = 600000,
        CancellationToken cancellationToken = default);

    Task<string> RunBufferedScripts(
        IReadOnlyList<ST_BUFFERED_SCRIPT_RUN_ITEM> scripts,
        int number = 0,
        int queueSize = 100,
        int linesPerCommand = 1000,
        int timeoutMs = 600000,
        CancellationToken cancellationToken = default);

    Task<string> StopTask(
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default);

    Task<string> ReadTaskStatus(
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default);
}

public sealed record ST_BUFFERED_SCRIPT_RUN_ITEM(
    string LocalScriptPath,
    string ScriptFileName,
    int TaskIndex);

public sealed record ST_AUTOMATION_AXIS_STATUS(
    int AutomationNo,
    string AxisName,
    int? AxisNo,
    double PositionFeedback,
    double PositionCommand,
    double AuxiliaryFeedback,
    bool Able,
    bool HomeDone,
    bool HasError,
    string DriveStatus,
    string HomeState,
    string Fault,
    string RawResponse,
    DateTimeOffset UpdatedAt);

public sealed class CAutomationManager : IAutomationManager
{
    private const string LocalScriptPathKey = "LocalScriptPath";
    private const string AutomationScriptPathKey = "AutomationScriptPath";

    private static readonly IReadOnlyDictionary<string, int> AxisNoMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GX"] = 0,
            ["GY"] = 1
        };

    private readonly IInterfaceManager _interfaceManager;
    private readonly ISettingManager? _settingManager;
    private readonly string _projectRoot;
    private readonly string _defaultLocalScriptDirectory;

    public CAutomationManager(
        IInterfaceManager interfaceManager,
        ISettingManager? settingManager = null,
        string projectRoot = "",
        string defaultLocalScriptDirectory = "")
    {
        _interfaceManager = interfaceManager;
        _settingManager = settingManager;
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? AppContext.BaseDirectory
            : projectRoot;
        _defaultLocalScriptDirectory = string.IsNullOrWhiteSpace(defaultLocalScriptDirectory)
            ? Path.Combine(_projectRoot, "Data", "Script")
            : defaultLocalScriptDirectory;
    }

    public Task Connect(
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return _interfaceManager.Connect(
            EN_EQP_MODULE.Automation1,
            number,
            cancellationToken: cancellationToken);
    }

    public Task Disconnect(
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return _interfaceManager.Disconnect(
            EN_EQP_MODULE.Automation1,
            number,
            cancellationToken);
    }

    public bool IsConnect(int number = 0)
    {
        return _interfaceManager.IsConnect(EN_EQP_MODULE.Automation1, number);
    }

    public bool IsSimul(int number = 0)
    {
        return _interfaceManager.IsSimul(EN_EQP_MODULE.Automation1, number);
    }

    public Task<string> ReadStatus(
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteRaw("AUTOMATION1:STATUS", number, cancellationToken);
    }

    private Task<string> ExecuteRaw(
        string command,
        int number,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return _interfaceManager.ExecuteFunction(
            EN_EQP_MODULE.Automation1,
            number,
            command,
            cancellationToken);
    }

    public Task<string> Move(
        string axisName,
        double targetPosition,
        double velocity = 100.0,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(
            axisName,
            $"MOVE_ABS:{Format(targetPosition)}:{Format(velocity)}",
            number,
            cancellationToken);
    }

    public Task<string> MoveRel(
        string axisName,
        double distance,
        double velocity = 100.0,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(
            axisName,
            $"MOVE_REL:{Format(distance)}:{Format(velocity)}",
            number,
            cancellationToken);
    }

    public Task<string> Stop(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "STOP", number, cancellationToken);
    }

    public Task<string> ServoOn(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "SERVO_ON", number, cancellationToken);
    }

    public Task<string> ServoOff(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "SERVO_OFF", number, cancellationToken);
    }

    public Task<string> Home(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "HOME", number, cancellationToken);
    }

    public Task<string> ResetAlarm(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "RESET_ALARM", number, cancellationToken);
    }

    public Task<string> ReadAxis(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAxis(axisName, "READ", number, cancellationToken);
    }

    public async Task<ST_AUTOMATION_AXIS_STATUS> ReadAxisStatus(
        string axisName,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);

        var normalizedAxis = axisName.Trim();
        if (IsSimul(number))
        {
            return CreateSimulationAxisStatus(number, normalizedAxis);
        }

        var response = await ReadAxis(normalizedAxis, number, cancellationToken);
        return ParseAxisStatus(number, normalizedAxis, response);
    }

    public async Task<string> RunLocalScript(
        string localScriptPath,
        string controllerFileName = "",
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localScriptPath);
        var resolvedLocalScriptPath = await ResolveLocalScriptPath(localScriptPath, cancellationToken);
        var resolvedControllerFileName = string.IsNullOrWhiteSpace(controllerFileName)
            ? await CreateControllerFileName(localScriptPath, cancellationToken)
            : await CreateControllerFileName(controllerFileName, cancellationToken);

        return await ExecuteRaw(
            $"AUTOMATION1:SCRIPT|RUN_LOCAL|{taskIndex}|{resolvedLocalScriptPath}|{resolvedControllerFileName}",
            number,
            cancellationToken);
    }

    public async Task<string> UploadScript(
        string localScriptPath,
        string scriptFileName = "",
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localScriptPath);
        var resolvedLocalScriptPath = await ResolveLocalScriptPath(localScriptPath, cancellationToken);
        var controllerFileName = string.IsNullOrWhiteSpace(scriptFileName)
            ? await CreateControllerFileName(localScriptPath, cancellationToken)
            : await CreateControllerFileName(scriptFileName, cancellationToken);

        return await ExecuteRaw(
            $"AUTOMATION1:SCRIPT|UPLOAD|{resolvedLocalScriptPath}|{controllerFileName}",
            number,
            cancellationToken);
    }

    public async Task<string> RunScript(
        string scriptFileName,
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);
        var controllerFileName = await CreateControllerFileName(scriptFileName, cancellationToken);

        return await ExecuteRaw(
            $"AUTOMATION1:SCRIPT|RUN|{taskIndex}|{controllerFileName}",
            number,
            cancellationToken);
    }

    public async Task<string> RunBufferedScript(
        string localScriptPath,
        string scriptFileName = "",
        int taskIndex = 1,
        int number = 0,
        int queueSize = 100,
        int linesPerCommand = 1000,
        int timeoutMs = 600000,
        CancellationToken cancellationToken = default)
    {
        return await RunBufferedScripts(
            [new ST_BUFFERED_SCRIPT_RUN_ITEM(localScriptPath, scriptFileName, taskIndex)],
            number,
            queueSize,
            linesPerCommand,
            timeoutMs,
            cancellationToken);
    }

    public async Task<string> RunBufferedScripts(
        IReadOnlyList<ST_BUFFERED_SCRIPT_RUN_ITEM> scripts,
        int number = 0,
        int queueSize = 100,
        int linesPerCommand = 1000,
        int timeoutMs = 600000,
        CancellationToken cancellationToken = default)
    {
        if (scripts.Count == 0)
        {
            return "OK:SCRIPT:BUFFERED_RUN:EMPTY";
        }

        var commandQueueSize = Math.Max(1, queueSize);
        var commandLinesPerCommand = Math.Max(2, linesPerCommand);
        var commandTimeoutMs = Math.Max(0, timeoutMs);
        var fields = new List<string>
        {
            "AUTOMATION1:SCRIPT",
            "BUFFERED_RUN_GROUP",
            commandQueueSize.ToString(CultureInfo.InvariantCulture),
            commandLinesPerCommand.ToString(CultureInfo.InvariantCulture),
            commandTimeoutMs.ToString(CultureInfo.InvariantCulture)
        };
        var controllerFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in scripts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(script.LocalScriptPath);
            var resolvedLocalScriptPath = await ResolveLocalScriptPath(script.LocalScriptPath, cancellationToken);
            ValidateBufferedRunScriptFile(resolvedLocalScriptPath);

            var controllerFileName = await CreateBufferedRunControllerFileName(
                script,
                resolvedLocalScriptPath,
                cancellationToken);
            if (!controllerFileNames.Add(controllerFileName))
            {
                throw new InvalidOperationException(
                    $"Automation1 buffered run controller file is duplicated: {controllerFileName}");
            }

            var uploadResponse = await UploadScript(
                resolvedLocalScriptPath,
                controllerFileName,
                number,
                cancellationToken);
            EnsureAutomationResponseSucceeded(
                uploadResponse,
                $"Buffered script upload failed. ControllerFile={controllerFileName}");

            fields.Add(script.TaskIndex.ToString(CultureInfo.InvariantCulture));
            fields.Add(controllerFileName);
        }

        var response = await ExecuteRaw(
            string.Join("|", fields),
            number,
            cancellationToken);
        EnsureAutomationResponseSucceeded(response, "Buffered script run failed.");
        return response;
    }

    private async Task<string> CreateBufferedRunControllerFileName(
        ST_BUFFERED_SCRIPT_RUN_ITEM script,
        string resolvedLocalScriptPath,
        CancellationToken cancellationToken)
    {
        var fileName = string.IsNullOrWhiteSpace(script.ScriptFileName)
            ? CreateTaskScopedScriptFileName(script.TaskIndex, resolvedLocalScriptPath)
            : script.ScriptFileName;

        return await CreateControllerFileName(fileName, cancellationToken);
    }

    private static string CreateTaskScopedScriptFileName(
        int taskIndex,
        string localScriptPath)
    {
        var fileName = Path.GetFileName(localScriptPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "PROCESS.ascript";
        }

        return $"T{taskIndex.ToString(CultureInfo.InvariantCulture)}_{fileName}";
    }

    public Task<string> StopTask(
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteRaw(
            $"AUTOMATION1:SCRIPT|STOP|{taskIndex}",
            number,
            cancellationToken);
    }

    public Task<string> ReadTaskStatus(
        int taskIndex = 1,
        int number = 0,
        CancellationToken cancellationToken = default)
    {
        return ExecuteRaw(
            $"AUTOMATION1:SCRIPT|STATUS|{taskIndex}",
            number,
            cancellationToken);
    }

    private Task<string> ExecuteAxis(
        string axisName,
        string command,
        int number,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);
        var automationAxis = ResolveAutomationAxis(axisName);

        return ExecuteRaw(
            $"AUTOMATION1:AXIS:{automationAxis}:{command}",
            number,
            cancellationToken);
    }

    private static void ValidateBufferedRunScriptFile(string localScriptPath)
    {
        if (!File.Exists(localScriptPath))
        {
            throw new FileNotFoundException("Buffered run script file was not found.", localScriptPath);
        }

        var lineNo = 0;
        foreach (var rawLine in File.ReadLines(localScriptPath))
        {
            lineNo++;
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var token = GetAeroScriptToken(line);
            if (IsBufferedRunBlockedToken(token) ||
                token.StartsWith('$'))
            {
                throw new InvalidDataException(
                    $"Buffered run script validation failed. {Path.GetFileName(localScriptPath)} line {lineNo} cannot be queued: {line}");
            }
        }
    }

    private static string GetAeroScriptToken(string line)
    {
        var endIndex = line.IndexOfAny(['(', ' ', '\t']);
        return endIndex <= 0
            ? line
            : line[..endIndex];
    }

    private static bool IsBufferedRunBlockedToken(string token)
    {
        return token.Equals("program", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("end", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("var", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("global", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("if", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("else", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("elseif", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("endif", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("while", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("endwhile", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("for", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("endfor", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("repeat", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("endrepeat", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("function", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("endfunction", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("return", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("break", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("continue", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureAutomationResponseSucceeded(
        string response,
        string message)
    {
        if (IsAutomationSuccessResponse(response))
        {
            return;
        }

        throw new InvalidOperationException($"{message} Response={FormatAutomationResponse(response)}");
    }

    private static bool IsAutomationSuccessResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ACK", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.StartsWith("SIM:", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith(":OK", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatAutomationResponse(string response)
    {
        return string.IsNullOrWhiteSpace(response)
            ? "<empty>"
            : response.Trim();
    }

    private static string ResolveAutomationAxis(string axisName)
    {
        var normalized = axisName.Trim();

        return AxisNoMap.TryGetValue(normalized, out var axisNo)
            ? axisNo.ToString(CultureInfo.InvariantCulture)
            : normalized;
    }

    private static ST_AUTOMATION_AXIS_STATUS CreateSimulationAxisStatus(
        int automationNo,
        string axisName)
    {
        return new ST_AUTOMATION_AXIS_STATUS(
            automationNo,
            axisName,
            TryGetAxisNo(axisName),
            0.0,
            0.0,
            0.0,
            true,
            true,
            false,
            "Enabled",
            "Homed",
            "0",
            $"SIM:AUTOMATION1:AXIS:{axisName}:READ:OK",
            DateTimeOffset.Now);
    }

    private static ST_AUTOMATION_AXIS_STATUS ParseAxisStatus(
        int automationNo,
        string requestedAxis,
        string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return CreateErrorAxisStatus(automationNo, requestedAxis, "No Response", response);
        }

        var tokens = response.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.None);
        if (tokens.Length < 3 ||
            !tokens[0].Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            !tokens[1].Equals("AXIS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateErrorAxisStatus(automationNo, requestedAxis, "Invalid Response", response);
        }

        var axisName = string.IsNullOrWhiteSpace(tokens[2]) ? requestedAxis : tokens[2];
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 3; index + 1 < tokens.Length; index += 2)
        {
            values[tokens[index]] = tokens[index + 1];
        }

        var driveStatus = GetValue(values, "DRIVE");
        var fault = GetValue(values, "FAULT");
        var homeState = GetValue(values, "HOME");

        return new ST_AUTOMATION_AXIS_STATUS(
            automationNo,
            axisName,
            TryGetAxisNo(axisName),
            ReadDouble(values, "FPOS"),
            ReadDouble(values, "CPOS"),
            ReadDouble(values, "AUX"),
            IsDriveAble(driveStatus),
            IsHomeDone(homeState),
            IsFaultActive(fault),
            driveStatus,
            homeState,
            fault,
            response,
            DateTimeOffset.Now);
    }

    private static ST_AUTOMATION_AXIS_STATUS CreateErrorAxisStatus(
        int automationNo,
        string axisName,
        string fault,
        string response)
    {
        return new ST_AUTOMATION_AXIS_STATUS(
            automationNo,
            axisName,
            TryGetAxisNo(axisName),
            0.0,
            0.0,
            0.0,
            false,
            false,
            true,
            "",
            "",
            fault,
            response,
            DateTimeOffset.Now);
    }

    private static int? TryGetAxisNo(string axisName)
    {
        var normalized = axisName.Trim();

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var axisNo))
        {
            return axisNo;
        }

        return AxisNoMap.TryGetValue(normalized, out axisNo) ? axisNo : null;
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out var value) ? value : "";
    }

    private static double ReadDouble(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out var value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0.0;
    }

    private static bool IsDriveAble(string driveStatus)
    {
        return driveStatus.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ||
            driveStatus.Contains("ServoControl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHomeDone(string homeState)
    {
        var normalized = homeState.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Homed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Done", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFaultActive(string fault)
    {
        var normalized = fault.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            !normalized.Equals("0", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveLocalScriptPath(
        string fileNameOrPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrPath);

        var trimmed = fileNameOrPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var fileNameOnly = Path.GetFileName(trimmed);
        if (trimmed.Equals(fileNameOnly, StringComparison.OrdinalIgnoreCase))
        {
            var scriptDirectory = await GetLocalScriptDirectory(cancellationToken);
            return Path.GetFullPath(Path.Combine(scriptDirectory, trimmed));
        }

        return Path.GetFullPath(Path.Combine(_projectRoot, trimmed));
    }

    private async Task<string> GetLocalScriptDirectory(CancellationToken cancellationToken)
    {
        var settingValue = _settingManager is null
            ? ""
            : await _settingManager.GetValue(
                EN_SETTING_TAB.Option,
                LocalScriptPathKey,
                "",
                cancellationToken);

        return ResolveLocalDirectory(settingValue, _defaultLocalScriptDirectory);
    }

    private string ResolveLocalDirectory(
        string settingValue,
        string defaultDirectory)
    {
        var value = string.IsNullOrWhiteSpace(settingValue)
            ? defaultDirectory
            : settingValue.Trim();

        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(_projectRoot, value));
    }

    private async Task<string> CreateControllerFileName(
        string fileNameOrPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrPath);
        var trimmed = fileNameOrPath.Trim().Replace('\\', '/');

        if (trimmed.StartsWith('/'))
        {
            return trimmed;
        }

        var controllerDirectory = await GetControllerScriptDirectory(cancellationToken);
        var fileName = Path.GetFileName(trimmed);

        return CombineControllerPath(controllerDirectory, fileName);
    }

    private async Task<string> GetControllerScriptDirectory(CancellationToken cancellationToken)
    {
        var settingValue = _settingManager is null
            ? ""
            : await _settingManager.GetValue(
                EN_SETTING_TAB.Option,
                AutomationScriptPathKey,
                "/",
                cancellationToken);

        if (string.IsNullOrWhiteSpace(settingValue))
        {
            return "/";
        }

        var normalized = settingValue.Trim().Replace('\\', '/');

        return normalized.StartsWith('/')
            ? normalized
            : "/" + normalized;
    }

    private static string CombineControllerPath(
        string directory,
        string fileName)
    {
        var normalizedDirectory = string.IsNullOrWhiteSpace(directory)
            ? "/"
            : directory.Trim().Replace('\\', '/');

        if (!normalizedDirectory.StartsWith('/'))
        {
            normalizedDirectory = "/" + normalizedDirectory;
        }

        normalizedDirectory = normalizedDirectory.TrimEnd('/');

        return normalizedDirectory.Length == 0
            ? "/" + fileName
            : $"{normalizedDirectory}/{fileName}";
    }

    private static string Format(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
