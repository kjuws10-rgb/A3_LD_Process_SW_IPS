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

        return await Task.Run(() =>
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
        }, cancellationToken);
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

            await Task.Run(() => ExecuteLive(number, command, motorNo, parameter), cancellationToken);
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
        var motors = motorNos
            .Where(value => value is >= 1 and <= 4)
            .Distinct()
            .OrderBy(value => value)
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
                    await Task.Run(() =>
                    {
                        foreach (var motorNo in motors)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            session.RelativeMove(motorNo, moveStep);
                        }
                    }, linked.Token);
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

    private static bool IsMoveCommand(EN_PICO_MOTOR_COMMAND command) =>
        command is EN_PICO_MOTOR_COMMAND.Home
            or EN_PICO_MOTOR_COMMAND.JogNegative
            or EN_PICO_MOTOR_COMMAND.JogPositive
            or EN_PICO_MOTOR_COMMAND.MoveRelativeNegative
            or EN_PICO_MOTOR_COMMAND.MoveRelativePositive
            or EN_PICO_MOTOR_COMMAND.MoveAbsolute;

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
        var targets = motorNos.ToDictionary(
            motorNo => motorNo,
            motorNo => GetPosition(start, motorNo) + deltaPosition);

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
            var completed = await Task.Run(
                () => motorNos.All(session.GetMotionDone),
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

    private static double GetSimulationStepDistance(ST_PICO_MOTOR_STATUS status) =>
        Math.Max(0.000001, status.CurrentVelocity * SimulationIntervalMs / 1000.0);

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

    public void Dispose() => DisconnectAll();

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
        SetStatus(number, status with
        {
            SelectedMotorNo = motorNo,
            CurrentVelocity = command == EN_PICO_MOTOR_COMMAND.SetVelocity ? parameter : status.CurrentVelocity,
            CurrentAcceleration = command == EN_PICO_MOTOR_COMMAND.SetAcceleration ? parameter : status.CurrentAcceleration,
            MotionState = command switch
            {
                EN_PICO_MOTOR_COMMAND.StopMotion or EN_PICO_MOTOR_COMMAND.AllMotorStop => "IDLE",
                EN_PICO_MOTOR_COMMAND.Home => "HOME",
                EN_PICO_MOTOR_COMMAND.JogNegative or EN_PICO_MOTOR_COMMAND.JogPositive => "JOG",
                EN_PICO_MOTOR_COMMAND.MoveRelativeNegative or EN_PICO_MOTOR_COMMAND.MoveRelativePositive => "REL MOVE",
                EN_PICO_MOTOR_COMMAND.MoveAbsolute => "ABS MOVE",
                _ => status.MotionState
            },
            CommOk = true,
            LastError = EN_PICO_MOTOR_ERROR.Ok,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private void ExecuteSimulation(int number, EN_PICO_MOTOR_COMMAND command, int motorNo, double parameter)
    {
        var status = GetStatus(number);
        var position = GetPosition(status, motorNo);
        status = command switch
        {
            EN_PICO_MOTOR_COMMAND.Connect => status with { IsConnected = true },
            EN_PICO_MOTOR_COMMAND.Disconnect => status with { IsConnected = false },
            EN_PICO_MOTOR_COMMAND.SetVelocity => status with { CurrentVelocity = parameter },
            EN_PICO_MOTOR_COMMAND.SetAcceleration => status with { CurrentAcceleration = parameter },
            EN_PICO_MOTOR_COMMAND.Home => SetPosition(status, motorNo, CPicoMotor.StepToMillimeter(status.HomePosition)),
            EN_PICO_MOTOR_COMMAND.MoveRelativeNegative => SetPosition(status, motorNo, position - parameter),
            EN_PICO_MOTOR_COMMAND.MoveRelativePositive => SetPosition(status, motorNo, position + parameter),
            EN_PICO_MOTOR_COMMAND.MoveAbsolute => SetPosition(status, motorNo, parameter),
            EN_PICO_MOTOR_COMMAND.JogNegative => SetPosition(status, motorNo, position - GetSimulationStepDistance(status)),
            EN_PICO_MOTOR_COMMAND.JogPositive => SetPosition(status, motorNo, position + GetSimulationStepDistance(status)),
            _ => status
        };
        var motionState = command switch
        {
            EN_PICO_MOTOR_COMMAND.JogNegative or EN_PICO_MOTOR_COMMAND.JogPositive => "JOG",
            EN_PICO_MOTOR_COMMAND.StopMotion or EN_PICO_MOTOR_COMMAND.AllMotorStop or EN_PICO_MOTOR_COMMAND.Disconnect => "IDLE",
            _ => status.MotionState
        };
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
            var target = command switch
            {
                EN_PICO_MOTOR_COMMAND.Home => CPicoMotor.StepToMillimeter(status.HomePosition),
                EN_PICO_MOTOR_COMMAND.MoveRelativeNegative => start - Math.Abs(parameter),
                EN_PICO_MOTOR_COMMAND.MoveRelativePositive => start + Math.Abs(parameter),
                _ => parameter
            };
            var motionState = command switch
            {
                EN_PICO_MOTOR_COMMAND.Home => "HOME",
                EN_PICO_MOTOR_COMMAND.MoveAbsolute => "ABS MOVE",
                _ => "REL MOVE"
            };

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

    private static double GetPosition(ST_PICO_MOTOR_STATUS status, int motorNo) => motorNo switch
    {
        1 => status.Motor1Position, 2 => status.Motor2Position,
        3 => status.Motor3Position, _ => status.Motor4Position
    };

    private static ST_PICO_MOTOR_STATUS SetPosition(ST_PICO_MOTOR_STATUS status, int motorNo, double value) => motorNo switch
    {
        1 => status with { Motor1Position = value }, 2 => status with { Motor2Position = value },
        3 => status with { Motor3Position = value }, _ => status with { Motor4Position = value }
    };
}
