namespace Drilling.Common.Interface;

public sealed class CPicoMotorService : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, CPicoMotorCommandSession> _sessions = [];
    private readonly Dictionary<int, ST_PICO_MOTOR_STATUS> _statuses = [];
    private readonly Dictionary<int, CancellationTokenSource> _allMoveTokens = [];
    private readonly Dictionary<int, CancellationTokenSource> _motionTokens = [];
    private const int SimulationIntervalMs = 100;

    public ST_PICO_MOTOR_STATUS GetStatus(int number)
    {
        lock (_syncRoot)
        {
            return _statuses.TryGetValue(number, out var status)
                ? status
                : ST_PICO_MOTOR_STATUS.Empty;
        }
    }

    public async Task<ST_PICO_MOTOR_STATUS> Refresh(
        int number,
        bool simulation,
        CancellationToken cancellationToken = default)
    {
        if (simulation)
        {
            return GetStatus(number) with { CommOk = true, UpdatedAt = DateTimeOffset.Now };
        }
        ST_PICO_MOTOR_STATUS RunTask1()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = GetSession(number, requireConnected: true);
            var before = GetStatus(number);
            var motorNo = Math.Clamp(before.SelectedMotorNo, 1, 4);
            var identification = session.GetIdentification();
            var errorCode = session.GetErrorCode();
            var status = before with
            {
                IsConnected = true,
                Controller = string.IsNullOrWhiteSpace(identification) ? "8742" : identification,
                Motor1Position = CPicoMotor.StepToMillimeter(session.GetPositionStep(1)),
                Motor2Position = CPicoMotor.StepToMillimeter(session.GetPositionStep(2)),
                Motor3Position = CPicoMotor.StepToMillimeter(session.GetPositionStep(3)),
                Motor4Position = CPicoMotor.StepToMillimeter(session.GetPositionStep(4)),
                HomePosition = session.GetHomePositionStep(motorNo),
                CurrentVelocity = CPicoMotor.StepToMillimeter(session.GetVelocityStep(motorNo)),
                CurrentAcceleration = CPicoMotor.StepToMillimeter(session.GetAccelerationStep(motorNo)),
                MotionState = session.GetMotionDone(motorNo) ? "IDLE" : "MOVING",
                ErrorCode = errorCode,
                CommOk = true,
                LastError = CPicoMotor.ToError(errorCode),
                UpdatedAt = DateTimeOffset.Now
            };
            SetStatus(number, status);
            return status;
        }
        return await Task.Run(RunTask1, cancellationToken);
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> Execute(
        int number,
        bool simulation,
        EN_PICO_MOTOR_COMMAND command,
        int motorNo,
        double parameter,
        CancellationToken cancellationToken = default)
    {
        motorNo = Math.Clamp(motorNo, 1, 4);

        try
        {
            if (command is EN_PICO_MOTOR_COMMAND.SetVelocity or EN_PICO_MOTOR_COMMAND.SetAcceleration)
            {
                var stepValue = CPicoMotor.MillimeterToStep(parameter);
                if (parameter < 0.0 || stepValue > int.MaxValue)
                {
                    return new ST_DEVICE_COMMAND_RESULT(false, $"PICO_MOTOR {command} value is out of range.");
                }
            }

            if (command == EN_PICO_MOTOR_COMMAND.AllMotorStop)
            {
                CancelAllMove(number);
                CancelMotion(number);
            }
            else if (command == EN_PICO_MOTOR_COMMAND.StopMotion)
            {
                CancelMotion(number);
            }

            if (simulation)
            {
                if (IsMoveCommand(command) && !ValidateSimulationMove(number, out var validationError))
                {
                    return validationError;
                }

                if (command is EN_PICO_MOTOR_COMMAND.Home
                    or EN_PICO_MOTOR_COMMAND.MoveRelativeNegative
                    or EN_PICO_MOTOR_COMMAND.MoveRelativePositive
                    or EN_PICO_MOTOR_COMMAND.MoveAbsolute)
                {
                    await ExecuteSimulationPositionMove(number, command, motorNo, parameter, cancellationToken);
                }
                else
                {
                    ExecuteSimulation(number, command, motorNo, parameter);
                }
                return new ST_DEVICE_COMMAND_RESULT(true, $"SIM:PICO_MOTOR:{command}:MOTOR={motorNo}:VALUE={parameter:0.###}");
            }
            void RunTask2()
            {
                ExecuteLive(number, command, motorNo, parameter);
            }

            await Task.Run(RunTask2, cancellationToken);
            return new ST_DEVICE_COMMAND_RESULT(true, $"PICO_MOTOR {command} OK. MOTOR={motorNo}");
        }
        catch (OperationCanceledException)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR command canceled.");
        }
        catch (Exception ex)
        {
            SetStatus(number, GetStatus(number) with
            {
                CommOk = false,
                LastError = EN_PICO_MOTOR_ERROR.Error,
                UpdatedAt = DateTimeOffset.Now
            });
            return new ST_DEVICE_COMMAND_RESULT(false, $"PICO_MOTOR {command} failed: {ex.Message}");
        }
    }

    public async Task<ST_DEVICE_COMMAND_RESULT> ExecuteAllMove(
        int number,
        bool simulation,
        IReadOnlyCollection<int> motorNos,
        double positionMm,
        int count,
        CancellationToken cancellationToken = default)
    {
        bool FilterValue3(int value)
        {
            return value is >= 1 and <= 4;
        }

        int GetValueSortKey4(int value)
        {
            return value;
        }

        var motors = motorNos
            .Where(FilterValue3)
            .Distinct()
            .OrderBy(GetValueSortKey4)
            .ToArray();
        if (motors.Length == 0)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR all move requires at least one selected motor.");
        }

        if (count <= 0)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR all move count must be greater than zero.");
        }

        var before = GetStatus(number);
        if (!before.IsConnected)
        {
            return new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR is disconnected. Connect first.");
        }

        if (simulation && !ValidateSimulationMove(number, out var validationError))
        {
            return validationError;
        }

        CancelAllMove(number);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_syncRoot)
        {
            _allMoveTokens[number] = linked;
        }

        var selectedMotorNo = Math.Clamp(before.SelectedMotorNo, 1, 4);
        SetStatus(number, before with
        {
            SelectedMotorNo = selectedMotorNo,
            MotionState = "ALL MOVE",
            AllMoveCurrentCount = 0,
            AllMoveSetCount = count,
            AllMovePosition = positionMm,
            CommOk = true,
            LastError = EN_PICO_MOTOR_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        });

        try
        {
            for (var current = 1; current <= count; current++)
            {
                linked.Token.ThrowIfCancellationRequested();

                if (simulation)
                {
                    await SimulateAllMotorMoveByDelta(number, motors, positionMm, linked.Token);
                }
                else
                {
                    var session = GetSession(number, requireConnected: true);
                    var moveStep = CPicoMotor.MillimeterToStep(positionMm);
                    void RunTask5()
                    {
                        foreach (var motorNo in motors)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            session.RelativeMove(motorNo, moveStep);
                        }
                    }
                    await Task.Run(RunTask5, linked.Token);
                    await WaitAllMotorsDone(session, motors, linked.Token);
                    await Refresh(number, false, linked.Token);
                }

                SetStatus(number, GetStatus(number) with
                {
                    SelectedMotorNo = selectedMotorNo,
                    MotionState = current == count ? "IDLE" : "ALL MOVE",
                    AllMoveCurrentCount = current,
                    AllMoveSetCount = count,
                    AllMovePosition = positionMm,
                    UpdatedAt = DateTimeOffset.Now
                });
            }

            return new ST_DEVICE_COMMAND_RESULT(true, "PICO_MOTOR all move completed.");
        }
        catch (OperationCanceledException)
        {
            SetStatus(number, GetStatus(number) with
            {
                SelectedMotorNo = selectedMotorNo,
                MotionState = "IDLE",
                UpdatedAt = DateTimeOffset.Now
            });
            return new ST_DEVICE_COMMAND_RESULT(true, "PICO_MOTOR all move stopped.");
        }
        catch (TimeoutException ex)
        {
            SetStatus(number, GetStatus(number) with
            {
                SelectedMotorNo = selectedMotorNo,
                MotionState = "IDLE",
                CommOk = false,
                LastError = EN_PICO_MOTOR_ERROR.Timeout,
                UpdatedAt = DateTimeOffset.Now
            });
            return new ST_DEVICE_COMMAND_RESULT(false, ex.Message);
        }
        finally
        {
            lock (_syncRoot)
            {
                if (_allMoveTokens.TryGetValue(number, out var token) && ReferenceEquals(token, linked))
                {
                    _allMoveTokens.Remove(number);
                    token.Dispose();
                }
            }
        }
    }

    public void CancelAllMove(int number)
    {
        lock (_syncRoot)
        {
            if (_allMoveTokens.TryGetValue(number, out var token)) token.Cancel();
        }
    }

    private void CancelMotion(int number)
    {
        lock (_syncRoot)
        {
            if (_motionTokens.TryGetValue(number, out var token))
            {
                token.Cancel();
            }
        }
    }

    private static bool IsMoveCommand(EN_PICO_MOTOR_COMMAND command)
    {
        return command is EN_PICO_MOTOR_COMMAND.Home
            or EN_PICO_MOTOR_COMMAND.JogNegative
            or EN_PICO_MOTOR_COMMAND.JogPositive
            or EN_PICO_MOTOR_COMMAND.MoveRelativeNegative
            or EN_PICO_MOTOR_COMMAND.MoveRelativePositive
            or EN_PICO_MOTOR_COMMAND.MoveAbsolute;
    }

    private bool ValidateSimulationMove(int number, out ST_DEVICE_COMMAND_RESULT error)
    {
        var status = GetStatus(number);
        if (!status.IsConnected)
        {
            error = new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR is disconnected. Connect first.");
            return false;
        }

        if (status.CurrentVelocity <= 0.0)
        {
            error = new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR velocity must be greater than 0 before move.");
            return false;
        }

        if (status.CurrentAcceleration <= 0.0)
        {
            error = new ST_DEVICE_COMMAND_RESULT(false, "PICO_MOTOR acceleration must be greater than 0 before move.");
            return false;
        }

        error = new ST_DEVICE_COMMAND_RESULT(true, string.Empty);
        return true;
    }

    private async Task SimulateAllMotorMoveByDelta(
        int number,
        IReadOnlyList<int> motorNos,
        double deltaPosition,
        CancellationToken cancellationToken)
    {
        var start = GetStatus(number);
        int HandleTargets6(int motorNo)
        {
            return motorNo;
        }

        double HandleTargets7(int motorNo)
        {
            return GetPosition(start, motorNo) + deltaPosition;
        }

        var targets = motorNos.ToDictionary(
HandleTargets6,
HandleTargets7);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = GetStatus(number);
            var next = status;
            var completed = true;
            var stepDistance = GetSimulationStepDistance(status);

            foreach (var motorNo in motorNos)
            {
                var current = GetPosition(next, motorNo);
                var distance = targets[motorNo] - current;
                if (Math.Abs(distance) <= 0.000001)
                {
                    next = SetPosition(next, motorNo, targets[motorNo]);
                    continue;
                }

                completed = false;
                var position = Math.Abs(distance) <= stepDistance
                    ? targets[motorNo]
                    : current + (Math.Sign(distance) * stepDistance);
                next = SetPosition(next, motorNo, position);
            }

            SetStatus(number, next with
            {
                MotionState = completed ? "IDLE" : "ALL MOVE",
                UpdatedAt = DateTimeOffset.Now
            });

            if (completed)
            {
                return;
            }

            await Task.Delay(SimulationIntervalMs, cancellationToken);
        }
    }

    private static async Task WaitAllMotorsDone(
        CPicoMotorCommandSession session,
        IReadOnlyList<int> motorNos,
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(120);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool RunTask8()
            {
                return motorNos.All(session.GetMotionDone);
            }

            var completed = await Task.Run(
RunTask8,
                cancellationToken);
            if (completed)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("PICO_MOTOR all move completion timeout.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static double GetSimulationStepDistance(ST_PICO_MOTOR_STATUS status)
    {
        return Math.Max(0.000001, status.CurrentVelocity * SimulationIntervalMs / 1000.0);
    }

    public void DisconnectAll()
    {
        lock (_syncRoot)
        {
            foreach (var token in _allMoveTokens.Values) token.Cancel();
            foreach (var token in _motionTokens.Values) token.Cancel();
            foreach (var session in _sessions.Values) session.Dispose();
            _allMoveTokens.Clear();
            _motionTokens.Clear();
            _sessions.Clear();
            _statuses.Clear();
        }
    }

    public void Dispose()
    {
        DisconnectAll();
    }

    private void ExecuteLive(int number, EN_PICO_MOTOR_COMMAND command, int motorNo, double parameter)
    {
        if (command == EN_PICO_MOTOR_COMMAND.Connect)
        {
            var session = GetSession(number, requireConnected: false);
            if (!session.Connect()) throw new InvalidOperationException("CmdLib device discovery/open failed.");
            SetStatus(number, GetStatus(number) with { IsConnected = true, CommOk = true, UpdatedAt = DateTimeOffset.Now });
            return;
        }

        if (command == EN_PICO_MOTOR_COMMAND.Disconnect)
        {
            if (_sessions.TryGetValue(number, out var session)) session.Disconnect();
            SetStatus(number, GetStatus(number) with { IsConnected = false, MotionState = "IDLE", UpdatedAt = DateTimeOffset.Now });
            return;
        }

        var live = GetSession(number, requireConnected: true);
        switch (command)
        {
            case EN_PICO_MOTOR_COMMAND.SelectMotor: break;
            case EN_PICO_MOTOR_COMMAND.SetVelocity: live.SetVelocity(motorNo, checked((int)CPicoMotor.MillimeterToStep(parameter))); break;
            case EN_PICO_MOTOR_COMMAND.SetAcceleration: live.SetAcceleration(motorNo, checked((int)CPicoMotor.MillimeterToStep(parameter))); break;
            case EN_PICO_MOTOR_COMMAND.StopMotion: live.StopMotion(motorNo); break;
            case EN_PICO_MOTOR_COMMAND.AllMotorStop: live.AbortMotion(); CancelAllMove(number); break;
            case EN_PICO_MOTOR_COMMAND.Home: live.MoveHome(motorNo); break;
            case EN_PICO_MOTOR_COMMAND.JogNegative: live.JogNegative(motorNo); break;
            case EN_PICO_MOTOR_COMMAND.JogPositive: live.JogPositive(motorNo); break;
            case EN_PICO_MOTOR_COMMAND.MoveRelativeNegative: live.RelativeMove(motorNo, -CPicoMotor.MillimeterToStep(parameter)); break;
            case EN_PICO_MOTOR_COMMAND.MoveRelativePositive: live.RelativeMove(motorNo, CPicoMotor.MillimeterToStep(parameter)); break;
            case EN_PICO_MOTOR_COMMAND.MoveAbsolute: live.AbsoluteMove(motorNo, CPicoMotor.MillimeterToStep(parameter)); break;
            case EN_PICO_MOTOR_COMMAND.Refresh: Refresh(number, false).GetAwaiter().GetResult(); return;
        }
        var status = GetStatus(number);
        string EvaluateCommandSwitch1()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_PICO_MOTOR_COMMAND.StopMotion or EN_PICO_MOTOR_COMMAND.AllMotorStop:
                    return "IDLE";
                case EN_PICO_MOTOR_COMMAND.Home:
                    return "HOME";
                case EN_PICO_MOTOR_COMMAND.JogNegative or EN_PICO_MOTOR_COMMAND.JogPositive:
                    return "JOG";
                case EN_PICO_MOTOR_COMMAND.MoveRelativeNegative or EN_PICO_MOTOR_COMMAND.MoveRelativePositive:
                    return "REL MOVE";
                case EN_PICO_MOTOR_COMMAND.MoveAbsolute:
                    return "ABS MOVE";
                default:
                    return status.MotionState;
            }
        }

        SetStatus(number, status with
        {
            SelectedMotorNo = motorNo,
            CurrentVelocity = command == EN_PICO_MOTOR_COMMAND.SetVelocity ? parameter : status.CurrentVelocity,
            CurrentAcceleration = command == EN_PICO_MOTOR_COMMAND.SetAcceleration ? parameter : status.CurrentAcceleration,
            MotionState = EvaluateCommandSwitch1(),
            CommOk = true,
            LastError = EN_PICO_MOTOR_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private void ExecuteSimulation(int number, EN_PICO_MOTOR_COMMAND command, int motorNo, double parameter)
    {
        var status = GetStatus(number);
        var position = GetPosition(status, motorNo);
        ST_PICO_MOTOR_STATUS EvaluateCommandSwitch2()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_PICO_MOTOR_COMMAND.Connect:
                    return status with { IsConnected = true };
                case EN_PICO_MOTOR_COMMAND.Disconnect:
                    return status with { IsConnected = false };
                case EN_PICO_MOTOR_COMMAND.SetVelocity:
                    return status with { CurrentVelocity = parameter };
                case EN_PICO_MOTOR_COMMAND.SetAcceleration:
                    return status with { CurrentAcceleration = parameter };
                case EN_PICO_MOTOR_COMMAND.Home:
                    return SetPosition(status, motorNo, CPicoMotor.StepToMillimeter(status.HomePosition));
                case EN_PICO_MOTOR_COMMAND.MoveRelativeNegative:
                    return SetPosition(status, motorNo, position - parameter);
                case EN_PICO_MOTOR_COMMAND.MoveRelativePositive:
                    return SetPosition(status, motorNo, position + parameter);
                case EN_PICO_MOTOR_COMMAND.MoveAbsolute:
                    return SetPosition(status, motorNo, parameter);
                case EN_PICO_MOTOR_COMMAND.JogNegative:
                    return SetPosition(status, motorNo, position - GetSimulationStepDistance(status));
                case EN_PICO_MOTOR_COMMAND.JogPositive:
                    return SetPosition(status, motorNo, position + GetSimulationStepDistance(status));
                default:
                    return status;
            }
        }

        status = EvaluateCommandSwitch2();
        string EvaluateCommandSwitch3()
        {
            var switchValue = command;
            switch (switchValue)
            {
                case EN_PICO_MOTOR_COMMAND.JogNegative or EN_PICO_MOTOR_COMMAND.JogPositive:
                    return "JOG";
                case EN_PICO_MOTOR_COMMAND.StopMotion or EN_PICO_MOTOR_COMMAND.AllMotorStop or EN_PICO_MOTOR_COMMAND.Disconnect:
                    return "IDLE";
                default:
                    return status.MotionState;
            }
        }

        var motionState = EvaluateCommandSwitch3();
        SetStatus(number, status with
        {
            SelectedMotorNo = motorNo,
            MotionState = motionState,
            CommOk = true,
            LastError = EN_PICO_MOTOR_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private async Task ExecuteSimulationPositionMove(
        int number,
        EN_PICO_MOTOR_COMMAND command,
        int motorNo,
        double parameter,
        CancellationToken cancellationToken)
    {
        CancelMotion(number);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_syncRoot)
        {
            _motionTokens[number] = linked;
        }

        try
        {
            var status = GetStatus(number);
            var start = GetPosition(status, motorNo);
            double EvaluateCommandSwitch4()
            {
                var switchValue = command;
                switch (switchValue)
                {
                    case EN_PICO_MOTOR_COMMAND.Home:
                        return CPicoMotor.StepToMillimeter(status.HomePosition);
                    case EN_PICO_MOTOR_COMMAND.MoveRelativeNegative:
                        return start - Math.Abs(parameter);
                    case EN_PICO_MOTOR_COMMAND.MoveRelativePositive:
                        return start + Math.Abs(parameter);
                    default:
                        return parameter;
                }
            }

            var target = EvaluateCommandSwitch4();
            string EvaluateCommandSwitch5()
            {
                var switchValue = command;
                switch (switchValue)
                {
                    case EN_PICO_MOTOR_COMMAND.Home:
                        return "HOME";
                    case EN_PICO_MOTOR_COMMAND.MoveAbsolute:
                        return "ABS MOVE";
                    default:
                        return "REL MOVE";
                }
            }

            var motionState = EvaluateCommandSwitch5();

            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                status = GetStatus(number);
                var current = GetPosition(status, motorNo);
                var distance = target - current;
                if (Math.Abs(distance) <= 0.000001)
                {
                    SetStatus(number, SetPosition(status, motorNo, target) with
                    {
                        SelectedMotorNo = motorNo,
                        MotionState = "IDLE",
                        UpdatedAt = DateTimeOffset.Now
                    });
                    return;
                }

                var stepDistance = GetSimulationStepDistance(status);
                var position = Math.Abs(distance) <= stepDistance
                    ? target
                    : current + (Math.Sign(distance) * stepDistance);
                SetStatus(number, SetPosition(status, motorNo, position) with
                {
                    SelectedMotorNo = motorNo,
                    MotionState = motionState,
                    CommOk = true,
                    UpdatedAt = DateTimeOffset.Now
                });
                await Task.Delay(SimulationIntervalMs, linked.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetStatus(number, GetStatus(number) with
            {
                MotionState = "IDLE",
                UpdatedAt = DateTimeOffset.Now
            });
        }
        finally
        {
            lock (_syncRoot)
            {
                if (_motionTokens.TryGetValue(number, out var token) && ReferenceEquals(token, linked))
                {
                    _motionTokens.Remove(number);
                    token.Dispose();
                }
            }
        }
    }

    private CPicoMotorCommandSession GetSession(int number, bool requireConnected)
    {
        lock (_syncRoot)
        {
            if (!_sessions.TryGetValue(number, out var session))
            {
                session = new CPicoMotorCommandSession();
                _sessions[number] = session;
            }
            if (requireConnected && !session.IsConnected) throw new InvalidOperationException("PicoMotor is not connected.");
            return session;
        }
    }

    private void SetStatus(int number, ST_PICO_MOTOR_STATUS status)
    {
        lock (_syncRoot) _statuses[number] = status;
    }

    private static double GetPosition(ST_PICO_MOTOR_STATUS status, int motorNo)
    {
        double EvaluateMotorNoSwitch6()
        {
            var switchValue = motorNo;
            switch (switchValue)
            {
                case 1:
                    return status.Motor1Position;
                case 2:
                    return status.Motor2Position;
                case 3:
                    return status.Motor3Position;
                default:
                    return status.Motor4Position;
            }
        }

        return EvaluateMotorNoSwitch6();
    }

    private static ST_PICO_MOTOR_STATUS SetPosition(ST_PICO_MOTOR_STATUS status, int motorNo, double value)
    {
        ST_PICO_MOTOR_STATUS EvaluateMotorNoSwitch7()
        {
            var switchValue = motorNo;
            switch (switchValue)
            {
                case 1:
                    return status with { Motor1Position = value };
                case 2:
                    return status with { Motor2Position = value };
                case 3:
                    return status with { Motor3Position = value };
                default:
                    return status with { Motor4Position = value };
            }
        }

        return EvaluateMotorNoSwitch7();
    }
}
