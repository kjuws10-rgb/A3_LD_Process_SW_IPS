using System.Globalization;
using System.Runtime.ExceptionServices;
using Aerotech.Automation1.DotNet;

namespace Drilling.Common.Interface;

[CCommType("Automation1Net", "Automation1", "A1")]
internal sealed class CAutomation1Comm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private const int DefaultTaskIndex = 1;
    private const int CommandQueuePollIntervalMs = 100;
    private readonly SemaphoreSlim _commLock = new(1, 1);
    private readonly object _activeBufferedRunLock = new();
    private Controller? _controller;
    private CancellationTokenSource? _activeBufferedRunCancellation;

    public override async Task Connect(CancellationToken cancellationToken = default)
    {
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            ConnectLocked();
        }
        catch (Exception ex)
        {
            CloseController();
            SetError(ex);
        }
        finally
        {
            _commLock.Release();
        }
    }

    public override async Task Disconnect(CancellationToken cancellationToken = default)
    {
        CancelActiveBufferedRun();
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            CloseController();
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
        CancelActiveBufferedRunIfStopCommand(function);

        CancellationTokenSource? bufferedRunCancellation = null;
        var executeCancellationToken = cancellationToken;
        await _commLock.WaitAsync(cancellationToken);

        try
        {
            if (_controller is null || ConnectionState != EN_COMM_STATE.Online)
            {
                ConnectLocked();
            }

            var controller = EnsureController();
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBufferedRunCommand(function))
            {
                bufferedRunCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                executeCancellationToken = bufferedRunCancellation.Token;
                RegisterActiveBufferedRun(bufferedRunCancellation);
            }

            var response = await Task.Run(
                () => ExecuteAutomation1Function(controller, function, executeCancellationToken),
                executeCancellationToken);

            LastSent = function;
            LastReceived = response;
            LastError = "";
            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }
        catch (OperationCanceledException) when (executeCancellationToken.IsCancellationRequested)
        {
            LastReceived = "";
            throw;
        }
        catch (Exception ex)
        {
            LastReceived = "";
            SetError(ex);
            return "";
        }
        finally
        {
            if (bufferedRunCancellation is not null)
            {
                UnregisterActiveBufferedRun(bufferedRunCancellation);
                bufferedRunCancellation.Dispose();
            }

            _commLock.Release();
        }
    }

    private void RegisterActiveBufferedRun(CancellationTokenSource cancellation)
    {
        lock (_activeBufferedRunLock)
        {
            _activeBufferedRunCancellation = cancellation;
        }
    }

    private void UnregisterActiveBufferedRun(CancellationTokenSource cancellation)
    {
        lock (_activeBufferedRunLock)
        {
            if (ReferenceEquals(_activeBufferedRunCancellation, cancellation))
            {
                _activeBufferedRunCancellation = null;
            }
        }
    }

    private void CancelActiveBufferedRunIfStopCommand(string function)
    {
        if (IsBufferedRunStopCommand(function))
        {
            CancelActiveBufferedRun();
        }
    }

    private void CancelActiveBufferedRun()
    {
        lock (_activeBufferedRunLock)
        {
            _activeBufferedRunCancellation?.Cancel();
        }
    }

    private static bool IsBufferedRunCommand(string function)
    {
        if (!TryGetAutomationBody(function, out var body))
        {
            return false;
        }

        var section = ReadAutomationSection(body);
        if (!section.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fields = SplitPipe(body);
        return fields.Length >= 2 &&
            (fields[1].Equals("BUFFERED_RUN", StringComparison.OrdinalIgnoreCase) ||
                fields[1].Equals("BUFFERED_RUN_GROUP", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBufferedRunStopCommand(string function)
    {
        if (!TryGetAutomationBody(function, out var body))
        {
            return false;
        }

        var section = ReadAutomationSection(body);
        if (section.Equals("SCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            var fields = SplitPipe(body);
            return fields.Length >= 2 &&
                fields[1].Equals("STOP", StringComparison.OrdinalIgnoreCase);
        }

        if (section.Equals("TASK", StringComparison.OrdinalIgnoreCase))
        {
            return IsTaskStopCommand(body);
        }

        if (section.Equals("AXIS", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitColon(body);
            return tokens.Length >= 3 &&
                tokens[2].Equals("STOP", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsTaskStopCommand(string body)
    {
        if (body.Contains('|', StringComparison.Ordinal))
        {
            var fields = SplitPipe(body);
            return fields.Length >= 2 &&
                fields[1].Equals("STOP", StringComparison.OrdinalIgnoreCase);
        }

        var tokens = SplitColon(body);
        return tokens.Length >= 3 &&
            tokens[2].Equals("STOP", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetAutomationBody(
        string function,
        out string body)
    {
        body = "";
        if (string.IsNullOrWhiteSpace(function))
        {
            return false;
        }

        var command = function.Trim();
        if (!command.StartsWith("AUTOMATION1:", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("A1:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefixEnd = command.IndexOf(':', StringComparison.Ordinal);
        if (prefixEnd < 0 ||
            prefixEnd + 1 >= command.Length)
        {
            return false;
        }

        body = command[(prefixEnd + 1)..];
        return true;
    }

    private static string ReadAutomationSection(string body)
    {
        var sectionEnd = body.IndexOfAny([':', '|']);
        return sectionEnd >= 0 ? body[..sectionEnd] : body;
    }

    private void ConnectLocked()
    {
        CloseController();

        var retryCount = Math.Max(1, Option.RetryCount);
        Exception? lastException = null;

        for (var tryNo = 0; tryNo < retryCount; tryNo++)
        {
            try
            {
                _controller = ConnectController();

                if (!_controller.IsRunning)
                {
                    _controller.Start();
                }

                LastError = "";
                SetState(EN_COMM_STATE.Online);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                CloseController();
            }
        }

        throw new InvalidOperationException(
            $"Automation1 connection failed. Endpoint={Option.Endpoint}",
            lastException);
    }

    private Controller ConnectController()
    {
        if (string.IsNullOrWhiteSpace(Option.RemoteAddress) ||
            Option.RemoteAddress.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) ||
            Option.RemoteAddress.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
        {
            return Controller.Connect();
        }

        if (Option.Port <= 0)
        {
            return Controller.Connect(Option.RemoteAddress);
        }

        return Controller.Connect(Option.RemoteAddress, Option.Port);
    }

    private string ExecuteAutomation1Function(
        Controller controller,
        string function,
        CancellationToken cancellationToken)
    {
        var command = function.Trim();

        if (command.Equals("STATUS", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("AUTOMATION1:STATUS", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteStatus(controller);
        }

        if (!command.StartsWith("AUTOMATION1:", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("A1:", StringComparison.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            controller.Runtime.Commands.Execute(command, DefaultTaskIndex);
            return "OK:COMMAND";
        }

        var prefixEnd = command.IndexOf(':', StringComparison.Ordinal);
        var body = command[(prefixEnd + 1)..];
        var sectionEnd = body.IndexOfAny([':', '|']);
        var section = sectionEnd >= 0 ? body[..sectionEnd] : body;

        return section.ToUpperInvariant() switch
        {
            "SCRIPT" => ExecuteScriptFunction(controller, body, cancellationToken),
            "TASK" => ExecuteTaskFunction(controller, body),
            "COMMAND" => ExecuteCommandFunction(controller, body),
            "AXIS" => ExecuteAxisFunction(controller, SplitColon(body)),
            "IO" => ExecuteIoFunction(controller, SplitColon(body)),
            _ => throw new InvalidOperationException($"Automation1 command is unknown: {function}")
        };
    }

    private static string ExecuteScriptFunction(
        Controller controller,
        string body,
        CancellationToken cancellationToken)
    {
        var fields = SplitPipe(body);

        if (fields.Length < 2)
        {
            throw new InvalidOperationException("Automation1 script command is invalid.");
        }

        var command = fields[1].ToUpperInvariant();

        return command switch
        {
            "UPLOAD" => UploadScript(controller, fields),
            "RUN" => RunControllerScript(controller, fields),
            "RUN_LOCAL" => RunLocalScript(controller, fields),
            "BUFFERED_RUN" => RunBufferedScript(controller, fields, cancellationToken),
            "BUFFERED_RUN_GROUP" => RunBufferedScriptGroup(controller, fields, cancellationToken),
            "STOP" => StopTask(controller, ReadInt(fields, 2, DefaultTaskIndex, "TASK")),
            "STATUS" => ReadTaskStatus(controller, ReadInt(fields, 2, DefaultTaskIndex, "TASK")),
            _ => throw new InvalidOperationException($"Automation1 script command is unknown: {command}")
        };
    }

    private static string ExecuteTaskFunction(
        Controller controller,
        string body)
    {
        if (body.Contains('|', StringComparison.Ordinal))
        {
            var fields = SplitPipe(body);
            if (fields.Length < 2)
            {
                throw new InvalidOperationException("Automation1 task command is invalid.");
            }

            var pipeCommand = fields[1].ToUpperInvariant();
            var pipeTaskIndex = ReadInt(fields, 2, DefaultTaskIndex, "TASK");

            return pipeCommand switch
            {
                "STATUS" => ReadTaskStatus(controller, pipeTaskIndex),
                "STOP" => StopTask(controller, pipeTaskIndex),
                _ => throw new InvalidOperationException($"Automation1 task command is unknown: {pipeCommand}")
            };
        }

        var tokens = SplitColon(body);

        if (tokens.Length < 3)
        {
            throw new InvalidOperationException("Automation1 task command is invalid.");
        }

        var taskIndex = ReadInt(tokens[1], "TASK");
        var command = tokens[2].ToUpperInvariant();

        return command switch
        {
            "STATUS" => ReadTaskStatus(controller, taskIndex),
            "STOP" => StopTask(controller, taskIndex),
            _ => throw new InvalidOperationException($"Automation1 task command is unknown: {command}")
        };
    }

    private static string ExecuteCommandFunction(
        Controller controller,
        string body)
    {
        var fields = SplitPipe(body);

        if (fields.Length < 3)
        {
            throw new InvalidOperationException("Automation1 direct command is invalid. Use AUTOMATION1:COMMAND|task|aeroscript.");
        }

        var taskIndex = ReadInt(fields, 1, DefaultTaskIndex, "TASK");
        var aeroScript = fields[2];
        ValidateTask(controller, taskIndex);
        controller.Runtime.Commands.Execute(aeroScript, taskIndex);
        return $"OK:COMMAND:{taskIndex}";
    }

    private static string UploadScript(
        Controller controller,
        IReadOnlyList<string> fields)
    {
        if (fields.Count < 4)
        {
            throw new InvalidOperationException("Automation1 script upload command is invalid. Use AUTOMATION1:SCRIPT|UPLOAD|localPath|controllerFile.");
        }

        var localPath = fields[2];
        var controllerFile = fields[3];
        controller.Files.Upload(localPath, controllerFile);
        return $"OK:SCRIPT:UPLOAD:{Sanitize(controllerFile)}";
    }

    private static string RunControllerScript(
        Controller controller,
        IReadOnlyList<string> fields)
    {
        if (fields.Count < 4)
        {
            throw new InvalidOperationException("Automation1 script run command is invalid. Use AUTOMATION1:SCRIPT|RUN|task|controllerFile.");
        }

        var taskIndex = ReadInt(fields, 2, DefaultTaskIndex, "TASK");
        var controllerFile = fields[3];
        ValidateTask(controller, taskIndex);
        var compiled = controller.Compiler.CompileControllerFile(controllerFile, compileWithDebugInformation: true);
        controller.Runtime.Tasks[taskIndex].Program.Run(compiled);
        return ReadTaskStatus(controller, taskIndex);
    }

    private static string RunLocalScript(
        Controller controller,
        IReadOnlyList<string> fields)
    {
        if (fields.Count < 5)
        {
            throw new InvalidOperationException("Automation1 local script run command is invalid. Use AUTOMATION1:SCRIPT|RUN_LOCAL|task|localPath|controllerFile.");
        }

        var taskIndex = ReadInt(fields, 2, DefaultTaskIndex, "TASK");
        var localPath = fields[3];
        var controllerFile = fields[4];
        ValidateTask(controller, taskIndex);
        controller.Files.Upload(localPath, controllerFile);
        var compiled = controller.Compiler.CompileControllerFile(controllerFile, compileWithDebugInformation: true);
        controller.Runtime.Tasks[taskIndex].Program.Run(compiled);
        return ReadTaskStatus(controller, taskIndex);
    }

    private static string RunBufferedScript(
        Controller controller,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        if (fields.Count < 7)
        {
            throw new InvalidOperationException(
                "Automation1 buffered run command is invalid. Use AUTOMATION1:SCRIPT|BUFFERED_RUN|task|controllerFile|queueSize|linesPerCommand|timeoutMs.");
        }

        var taskIndex = ReadInt(fields, 2, DefaultTaskIndex, "TASK");
        var controllerFile = fields[3];
        var queueSize = Math.Max(1, ReadInt(fields, 4, 100, "QUEUE_SIZE"));
        var linesPerCommand = Math.Max(2, ReadInt(fields, 5, 1000, "LINES_PER_COMMAND"));
        var timeoutMs = Math.Max(0, ReadInt(fields, 6, 600000, "TIMEOUT_MS"));

        ValidateTask(controller, taskIndex);

        return RunBufferedControllerFiles(
            controller,
            [new CBufferedRunRequest(taskIndex, controllerFile)],
            queueSize,
            linesPerCommand,
            timeoutMs,
            cancellationToken);
    }

    private static string RunBufferedScriptGroup(
        Controller controller,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        if (fields.Count < 7 ||
            (fields.Count - 5) % 2 != 0)
        {
            throw new InvalidOperationException(
                "Automation1 buffered run group command is invalid. Use AUTOMATION1:SCRIPT|BUFFERED_RUN_GROUP|queueSize|linesPerCommand|timeoutMs|task|controllerFile...");
        }

        var queueSize = Math.Max(1, ReadInt(fields, 2, 100, "QUEUE_SIZE"));
        var linesPerCommand = Math.Max(2, ReadInt(fields, 3, 1000, "LINES_PER_COMMAND"));
        var timeoutMs = Math.Max(0, ReadInt(fields, 4, 600000, "TIMEOUT_MS"));
        var requests = new List<CBufferedRunRequest>();
        var taskIndexes = new HashSet<int>();

        for (var index = 5; index + 1 < fields.Count; index += 2)
        {
            var taskIndex = ReadInt(fields, index, DefaultTaskIndex, "TASK");
            var controllerFile = fields[index + 1];

            if (!taskIndexes.Add(taskIndex))
            {
                throw new InvalidOperationException($"Automation1 buffered run group has duplicated task: {taskIndex}");
            }

            ValidateTask(controller, taskIndex);
            requests.Add(new CBufferedRunRequest(taskIndex, controllerFile));
        }

        return RunBufferedControllerFiles(
            controller,
            requests,
            queueSize,
            linesPerCommand,
            timeoutMs,
            cancellationToken);
    }

    private static string RunBufferedControllerFiles(
        Controller controller,
        IReadOnlyList<CBufferedRunRequest> requests,
        int queueSize,
        int linesPerCommand,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var commandQueues = new List<CBufferedRunQueue>();
        var cleanupExceptions = new List<Exception>();
        Exception? runException = null;

        try
        {
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var commandQueue = controller.Runtime.Commands.BeginCommandQueue(
                    request.TaskIndex,
                    queueSize,
                    shouldBlockIfFull: false);
                var commandQueueEntry = new CBufferedRunQueue(request, commandQueue, 0);
                commandQueues.Add(commandQueueEntry);
                commandQueues[^1] = commandQueueEntry with
                {
                    InitialNumberOfTimesEmptied = commandQueue.Status.NumberOfTimesEmptied
                };
            }

            foreach (var item in commandQueues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Queue.Commands.ExecuteFromControllerFile(item.Request.ControllerFile, linesPerCommand);
            }

            WaitForBufferedRunTasksComplete(
                controller,
                commandQueues,
                timeoutMs,
                cancellationToken);
        }
        catch (Exception ex)
        {
            runException = ex;
            StopBufferedRunTasks(
                controller,
                commandQueues.Select(item => item.Request),
                cleanupExceptions);
        }
        finally
        {
            EndBufferedCommandQueues(controller, commandQueues, timeoutMs, cleanupExceptions);
        }

        if (runException is not null)
        {
            AddCleanupExceptions(runException, cleanupExceptions);
            ExceptionDispatchInfo.Capture(runException).Throw();
        }

        if (cleanupExceptions.Count > 0)
        {
            throw new AggregateException("Automation1 buffered run cleanup failed.", cleanupExceptions);
        }

        return string.Join(
            ":",
            "OK",
            "SCRIPT",
            "BUFFERED_RUN",
            requests.Count.ToString(CultureInfo.InvariantCulture),
            queueSize.ToString(CultureInfo.InvariantCulture),
            linesPerCommand.ToString(CultureInfo.InvariantCulture));
    }

    private static void WaitForBufferedRunTasksComplete(
        Controller controller,
        IReadOnlyList<CBufferedRunQueue> commandQueues,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var queueList = commandQueues.ToArray();
        var startedAt = Environment.TickCount64;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var states = queueList
                .Select(item => ReadBufferedRunTaskState(controller, item))
                .ToArray();
            var errorState = states.FirstOrDefault(state => state.State == TaskState.Error);
            if (errorState is not null)
            {
                throw new InvalidOperationException(
                    $"Automation1 buffered run task error. Task={errorState.TaskIndex}, Error={errorState.Error}");
            }

            if (states.All(state =>
                    state.State == TaskState.Idle &&
                    state.HasQueueEmptiedAfterStart))
            {
                return;
            }

            if (timeoutMs > 0)
            {
                var elapsedMs = Environment.TickCount64 - startedAt;
                if (elapsedMs >= timeoutMs)
                {
                    throw new TimeoutException(
                        $"Automation1 buffered run task idle timeout. TimeoutMs={timeoutMs}, States={FormatBufferedRunTaskStates(states)}");
                }

                Thread.Sleep((int)Math.Min(CommandQueuePollIntervalMs, Math.Max(1, timeoutMs - elapsedMs)));
            }
            else
            {
                Thread.Sleep(CommandQueuePollIntervalMs);
            }
        }
    }

    private static CBufferedRunTaskState ReadBufferedRunTaskState(
        Controller controller,
        CBufferedRunQueue commandQueue)
    {
        var request = commandQueue.Request;
        ValidateTask(controller, request.TaskIndex);
        var taskStatus = controller.Runtime.Tasks[request.TaskIndex].Status;
        var queueStatus = commandQueue.Queue.Status;
        return new CBufferedRunTaskState(
            request.TaskIndex,
            taskStatus.TaskState,
            queueStatus.NumberOfTimesEmptied,
            commandQueue.InitialNumberOfTimesEmptied,
            queueStatus.NumberOfExecutedCommands,
            queueStatus.NumberOfUnexecutedCommands,
            taskStatus.Error?.ToString() ?? "");
    }

    private static string FormatBufferedRunTaskStates(
        IReadOnlyList<CBufferedRunTaskState> states)
    {
        return string.Join(
            ", ",
            states.Select(state =>
                $"T{state.TaskIndex}={state.State}/QueueEmptied={state.NumberOfTimesEmptied}/Initial={state.InitialNumberOfTimesEmptied}/Executed={state.NumberOfExecutedCommands}/Pending={state.NumberOfUnexecutedCommands}{(string.IsNullOrWhiteSpace(state.Error) ? "" : $"/{state.Error}")}"));
    }

    private static void StopBufferedRunTasks(
        Controller controller,
        IEnumerable<CBufferedRunRequest> requests,
        ICollection<Exception> cleanupExceptions)
    {
        foreach (var taskIndex in requests.Select(request => request.TaskIndex).Distinct())
        {
            try
            {
                controller.Runtime.Tasks[taskIndex].Program.Stop(5000);
            }
            catch (Exception ex)
            {
                cleanupExceptions.Add(new InvalidOperationException(
                    $"Automation1 buffered run task stop failed. Task={taskIndex}",
                    ex));
            }
        }
    }

    private static void EndBufferedCommandQueues(
        Controller controller,
        IReadOnlyList<CBufferedRunQueue> commandQueues,
        int timeoutMs,
        ICollection<Exception> cleanupExceptions)
    {
        foreach (var item in commandQueues.AsEnumerable().Reverse())
        {
            try
            {
                if (timeoutMs <= 0)
                {
                    controller.Runtime.Commands.EndCommandQueue(item.Queue);
                }
                else
                {
                    controller.Runtime.Commands.EndCommandQueue(item.Queue, timeoutMs);
                }
            }
            catch (Exception ex)
            {
                cleanupExceptions.Add(new InvalidOperationException(
                    $"Automation1 command queue end failed. Task={item.Request.TaskIndex}",
                    ex));
            }
        }
    }

    private static void AddCleanupExceptions(
        Exception exception,
        IReadOnlyCollection<Exception> cleanupExceptions)
    {
        if (cleanupExceptions.Count == 0)
        {
            return;
        }

        exception.Data["Automation1BufferedRunCleanupErrors"] = string.Join(
            Environment.NewLine,
            cleanupExceptions.Select(error => error.Message));
    }

    private static string StopTask(
        Controller controller,
        int taskIndex)
    {
        ValidateTask(controller, taskIndex);
        controller.Runtime.Tasks[taskIndex].Program.Stop(5000);
        return ReadTaskStatus(controller, taskIndex);
    }

    private static string ReadTaskStatus(
        Controller controller,
        int taskIndex)
    {
        ValidateTask(controller, taskIndex);
        var status = controller.Runtime.Tasks[taskIndex].Status;
        var error = status.Error?.ToString() ?? "";
        return string.Join(
            ":",
            "OK",
            "TASK",
            taskIndex.ToString(CultureInfo.InvariantCulture),
            Sanitize(status.TaskState.ToString()),
            Sanitize(status.AeroScriptSourceFileName ?? ""),
            Sanitize(error));
    }

    private static string ExecuteAxisFunction(
        Controller controller,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
        {
            throw new InvalidOperationException("Automation1 axis command is invalid.");
        }

        var axis = CreateAxisReference(tokens[1]);
        var axisName = tokens[1];
        const int commandIndex = 2;
        var valueIndex = commandIndex + 1;
        var command = tokens[commandIndex].ToUpperInvariant();
        var taskIndex = tokens.Count > valueIndex + 1 && tokens[^2].Equals("TASK", StringComparison.OrdinalIgnoreCase)
            ? ReadInt(tokens[^1], "TASK")
            : DefaultTaskIndex;

        switch (command)
        {
            case "SERVO_ON":
                ExecuteAxis(
                    axis,
                    axisNo => controller.Runtime.Commands.Motion.Enable(axisNo),
                    axisText => controller.Runtime.Commands.Motion.Enable(axisText));
                return $"OK:AXIS:{Sanitize(axisName)}:SERVO_ON";
            case "SERVO_OFF":
                ExecuteAxis(
                    axis,
                    axisNo => controller.Runtime.Commands.Motion.Disable(axisNo),
                    axisText => controller.Runtime.Commands.Motion.Disable(axisText));
                return $"OK:AXIS:{Sanitize(axisName)}:SERVO_OFF";
            case "HOME":
                ExecuteAxis(
                    axis,
                    axisNo => controller.Runtime.Commands.Motion.Home(axisNo),
                    axisText => controller.Runtime.Commands.Motion.Home(axisText));
                return $"OK:AXIS:{Sanitize(axisName)}:HOME";
            case "MOVE_ABS":
                ExecuteAxis(
                    axis,
                    (axisNo, position, velocity) => controller.Runtime.Commands.Motion.MoveAbsolute(axisNo, position, velocity),
                    (axisText, position, velocity) => controller.Runtime.Commands.Motion.MoveAbsolute(axisText, position, velocity),
                    ReadDouble(tokens, valueIndex, "MOVE_ABS"),
                    ReadDouble(tokens, valueIndex + 1, 100.0, "VELOCITY"));
                return $"OK:AXIS:{Sanitize(axisName)}:MOVE_ABS";
            case "MOVE_REL":
                ExecuteAxis(
                    axis,
                    (axisNo, distance, velocity) => controller.Runtime.Commands.Motion.MoveIncremental(axisNo, distance, velocity),
                    (axisText, distance, velocity) => controller.Runtime.Commands.Motion.MoveIncremental(axisText, distance, velocity),
                    ReadDouble(tokens, valueIndex, "MOVE_REL"),
                    ReadDouble(tokens, valueIndex + 1, 100.0, "VELOCITY"));
                return $"OK:AXIS:{Sanitize(axisName)}:MOVE_REL";
            case "STOP":
                controller.Runtime.Commands.Execute($"Abort({axis.CommandText})", taskIndex);
                return $"OK:AXIS:{Sanitize(axisName)}:STOP";
            case "RESET_ALARM":
                ExecuteAxis(
                    axis,
                    axisNo => controller.Runtime.Commands.FaultAndError.FaultAcknowledge(axisNo),
                    axisText => controller.Runtime.Commands.FaultAndError.FaultAcknowledge(axisText));
                return $"OK:AXIS:{Sanitize(axisName)}:RESET_ALARM";
            case "READ":
                return ReadAxisStatus(controller, axis, axisName);
            default:
                throw new InvalidOperationException($"Automation1 axis command is unknown: {command}");
        }
    }

    private static string ReadAxisStatus(
        Controller controller,
        CAutoAxis axis,
        string axisName)
    {
        var configuration = new StatusItemConfiguration();

        if (axis.AxisNo.HasValue)
        {
            configuration.Axis.Add(AxisStatusItem.PositionFeedback, axis.AxisNo.Value);
            configuration.Axis.Add(AxisStatusItem.PositionCommand, axis.AxisNo.Value);
            configuration.Axis.Add(AxisStatusItem.AuxiliaryFeedback, axis.AxisNo.Value);
            configuration.Axis.Add(AxisStatusItem.DriveStatus, axis.AxisNo.Value);
            configuration.Axis.Add(AxisStatusItem.AxisFault, axis.AxisNo.Value);
            configuration.Axis.Add(AxisStatusItem.HomeState, axis.AxisNo.Value);
        }
        else
        {
            configuration.Axis.Add(AxisStatusItem.PositionFeedback, axis.AxisName);
            configuration.Axis.Add(AxisStatusItem.PositionCommand, axis.AxisName);
            configuration.Axis.Add(AxisStatusItem.AuxiliaryFeedback, axis.AxisName);
            configuration.Axis.Add(AxisStatusItem.DriveStatus, axis.AxisName);
            configuration.Axis.Add(AxisStatusItem.AxisFault, axis.AxisName);
            configuration.Axis.Add(AxisStatusItem.HomeState, axis.AxisName);
        }

        var results = controller.Runtime.Status.GetStatusItems(configuration);
        var feedback = Convert.ToDouble(
            GetAxisStatusValue(results, AxisStatusItem.PositionFeedback, axis),
            CultureInfo.InvariantCulture);
        var command = Convert.ToDouble(
            GetAxisStatusValue(results, AxisStatusItem.PositionCommand, axis),
            CultureInfo.InvariantCulture);
        var auxiliaryFeedback = Convert.ToDouble(
            GetAxisStatusValue(results, AxisStatusItem.AuxiliaryFeedback, axis),
            CultureInfo.InvariantCulture);
        var driveStatus = Convert.ToString(
            GetAxisStatusValue(results, AxisStatusItem.DriveStatus, axis),
            CultureInfo.InvariantCulture) ?? "";
        var fault = Convert.ToInt64(
            GetAxisStatusValue(results, AxisStatusItem.AxisFault, axis),
            CultureInfo.InvariantCulture);
        var homeState = Convert.ToString(
            GetAxisStatusValue(results, AxisStatusItem.HomeState, axis),
            CultureInfo.InvariantCulture) ?? "";

        return string.Join(
            ":",
            "OK",
            "AXIS",
            Sanitize(axisName),
            "FPOS",
            feedback.ToString("F6", CultureInfo.InvariantCulture),
            "CPOS",
            command.ToString("F6", CultureInfo.InvariantCulture),
            "AUX",
            auxiliaryFeedback.ToString("F6", CultureInfo.InvariantCulture),
            "DRIVE",
            Sanitize(driveStatus),
            "FAULT",
            fault.ToString(CultureInfo.InvariantCulture),
            "HOME",
            Sanitize(homeState));
    }

    private static CAutoAxis CreateAxisReference(string value)
    {
        var axisName = value.Trim();

        if (int.TryParse(axisName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var axisNo))
        {
            return new CAutoAxis(axisName, axisNo);
        }

        if (string.IsNullOrWhiteSpace(axisName))
        {
            throw new InvalidOperationException("Automation1 axis name is empty.");
        }

        return new CAutoAxis(axisName, null);
    }

    private static void ExecuteAxis(
        CAutoAxis axis,
        Action<int> byNumber,
        Action<string> byName)
    {
        if (axis.AxisNo.HasValue)
        {
            byNumber(axis.AxisNo.Value);
            return;
        }

        byName(axis.AxisName);
    }

    private static void ExecuteAxis(
        CAutoAxis axis,
        Action<int, double, double> byNumber,
        Action<string, double, double> byName,
        double value,
        double velocity)
    {
        if (axis.AxisNo.HasValue)
        {
            byNumber(axis.AxisNo.Value, value, velocity);
            return;
        }

        byName(axis.AxisName, value, velocity);
    }

    private static object GetAxisStatusValue(
        StatusItemResults results,
        AxisStatusItem item,
        CAutoAxis axis)
    {
        return axis.AxisNo.HasValue
            ? results.Axis[item, axis.AxisNo.Value].Value
            : results.Axis[item, axis.AxisName].Value;
    }

    private static string ExecuteIoFunction(
        Controller controller,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
        {
            throw new InvalidOperationException("Automation1 IO command is invalid.");
        }

        var address = tokens[1];
        var (axisNo, bitNo, isOutput) = ParseIoAddress(address);
        var command = tokens[2].ToUpperInvariant();

        if (command == "READ")
        {
            return ReadIo(controller, address, axisNo, bitNo, isOutput);
        }

        if (command is not ("ON" or "OFF"))
        {
            throw new InvalidOperationException($"Automation1 IO command is unknown: {command}");
        }

        if (!isOutput)
        {
            throw new InvalidOperationException($"Automation1 IO output command can use DO/Y address only: {address}");
        }

        controller.Runtime.Commands.IO.DigitalOutputSet(axisNo, bitNo, command == "ON" ? 1 : 0);
        return $"OK:IO:{Sanitize(address)}:{command}";
    }

    private static string ReadIo(
        Controller controller,
        string address,
        int axisNo,
        int bitNo,
        bool isOutput)
    {
        var statusItem = isOutput ? AxisStatusItem.DigitalOutput : AxisStatusItem.DigitalInput;
        var configuration = new StatusItemConfiguration();
        configuration.Axis.Add(statusItem, axisNo);
        var results = controller.Runtime.Status.GetStatusItems(configuration);
        var maskValue = Convert.ToInt64(
            results.Axis[statusItem, axisNo].Value,
            CultureInfo.InvariantCulture);
        var isOn = (maskValue & (1L << bitNo)) != 0;
        return $"OK:IO:{Sanitize(address)}:{(isOn ? "ON" : "OFF")}";
    }

    private static string ExecuteStatus(Controller controller)
    {
        var host = controller.Information.Host ?? "";
        var port = controller.Information.Port;
        var taskCount = controller.IsRunning ? controller.Runtime.Tasks.Count : 0;
        var axisCount = controller.IsRunning ? controller.Runtime.Axes.Count : 0;
        return $"OK:STATUS:{Sanitize(host)}:{port}:{controller.IsRunning}:{taskCount}:{axisCount}";
    }

    private Controller EnsureController()
    {
        if (_controller is null)
        {
            throw new InvalidOperationException("Automation1 is not connected.");
        }

        if (!_controller.IsRunning)
        {
            throw new InvalidOperationException("Automation1 controller is not running.");
        }

        return _controller;
    }

    private void CloseController()
    {
        if (_controller is null)
        {
            return;
        }

        try
        {
            _controller.Disconnect();
        }
        catch
        {
            // Disconnect should never block application shutdown.
        }
        finally
        {
            _controller = null;
        }
    }

    private static void ValidateTask(
        Controller controller,
        int taskIndex)
    {
        var taskCount = controller.Runtime.Tasks.Count;

        if (taskIndex <= 0 || taskIndex > taskCount)
        {
            throw new InvalidOperationException(
                $"Automation1 task index is out of range. Task={taskIndex}, Range=1~{taskCount}");
        }
    }

    private static string[] SplitColon(string value)
    {
        return value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] SplitPipe(string value)
    {
        return value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static int ReadInt(
        string value,
        string fieldName)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidOperationException($"Automation1 {fieldName} value is invalid: {value}");
    }

    private static int ReadInt(
        IReadOnlyList<string> values,
        int index,
        int defaultValue,
        string fieldName)
    {
        return values.Count <= index || string.IsNullOrWhiteSpace(values[index])
            ? defaultValue
            : ReadInt(values[index], fieldName);
    }

    private static double ReadDouble(
        IReadOnlyList<string> values,
        int index,
        string fieldName)
    {
        if (values.Count <= index ||
            !double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"Automation1 {fieldName} value is invalid.");
        }

        return result;
    }

    private static double ReadDouble(
        IReadOnlyList<string> values,
        int index,
        double defaultValue,
        string fieldName)
    {
        return values.Count <= index || string.IsNullOrWhiteSpace(values[index])
            ? defaultValue
            : ReadDouble(values, index, fieldName);
    }

    private static (int AxisNo, int BitNo, bool IsOutput) ParseIoAddress(string address)
    {
        var normalized = address.Trim().ToUpperInvariant();
        var isOutput = normalized.StartsWith('Y') || normalized.Contains("DO", StringComparison.Ordinal);
        normalized = normalized
            .Replace("AXIS", "", StringComparison.OrdinalIgnoreCase)
            .Replace("A", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DO", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DI", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Y", "", StringComparison.OrdinalIgnoreCase)
            .Replace("X", "", StringComparison.OrdinalIgnoreCase);

        var parts = normalized.Split(['.', ':', '/', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var axisNo) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitNo) ||
            axisNo < 0 ||
            bitNo < 0 ||
            bitNo > 63)
        {
            throw new InvalidOperationException(
                $"Automation1 IO address is invalid: {address}. Use A0.DO1, A0.DI1, 0.1, or 0:1.");
        }

        return (axisNo, bitNo, isOutput);
    }

    private static string Sanitize(string value)
    {
        return value
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed record CAutoAxis(string AxisName, int? AxisNo)
    {
        public string CommandText => AxisNo.HasValue
            ? AxisNo.Value.ToString(CultureInfo.InvariantCulture)
            : AxisName;
    }

    private sealed record CBufferedRunRequest(
        int TaskIndex,
        string ControllerFile);

    private sealed record CBufferedRunQueue(
        CBufferedRunRequest Request,
        CommandQueue Queue,
        int InitialNumberOfTimesEmptied);

    private sealed record CBufferedRunTaskState(
        int TaskIndex,
        TaskState State,
        int NumberOfTimesEmptied,
        int InitialNumberOfTimesEmptied,
        int NumberOfExecutedCommands,
        int NumberOfUnexecutedCommands,
        string Error)
    {
        public bool HasQueueEmptiedAfterStart => NumberOfTimesEmptied > InitialNumberOfTimesEmptied;
    }
}
