using System.Globalization;
using System.Text;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Log;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Recipe;
using Drilling.Common.Station;
using Drilling.Common.Threading;
using Drilling.File.JHMI;
using Drilling.File.Script;

namespace Drilling.Regression;

internal static class Program
{
    private static readonly DateTimeOffset FixedTime =
        new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));

    private static int Main(string[] args)
    {
        try
        {
            string outputPath = ResolveOutputPath(args);
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "DrillingRegression",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(testRoot);

            List<string> snapshot = new List<string>();
            RunRecipeRoundTrip(testRoot, snapshot);
            RunSettingRoundTrip(testRoot, snapshot);
            RunProcessPlanAndScript(testRoot, snapshot);
            RunSimulationFlow(snapshot);
            RunProtocolFlow(testRoot, snapshot);
            RunAlarmFlow(snapshot);
            RunLogFlow(testRoot, snapshot);
            RunCtrlThreadFlow();

            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            System.IO.File.WriteAllLines(outputPath, snapshot, new UTF8Encoding(false));
            VerifyGoldenSnapshot(args, snapshot);
            Console.WriteLine($"REGRESSION_PASS {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"REGRESSION_FAIL {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void RunCtrlThreadFlow()
    {
        CTestCtrlThread thread = new CTestCtrlThread();
        thread.Start(1, "RegressionCtrlThread");
        WaitForRunCount(thread, 3, 1000);
        int firstRunCount = thread.RunCount;

        thread.Start(1, "RegressionCtrlThreadDuplicate");
        Assert(thread.StartIdentity == 1, "CtrlThread created a duplicate worker.");

        thread.Pause();
        int pausedCount = thread.RunCount;
        Thread.Sleep(30);
        Assert(thread.RunCount <= pausedCount + 1, "CtrlThread continued while paused.");
        Assert(thread.IsPaused, "CtrlThread did not report paused state.");

        thread.Resume();
        WaitForRunCount(thread, pausedCount + 3, 1000);
        Assert(!thread.IsPaused, "CtrlThread remained paused after resume.");

        thread.RequestOneError();
        int beforeError = thread.RunCount;
        WaitForRunCount(thread, beforeError + 3, 1000);
        Assert(thread.LastThreadError is InvalidOperationException,
            "CtrlThread did not retain the Run exception.");
        Assert(thread.IsRunning, "CtrlThread stopped after a Run exception.");

        thread.Stop();
        Assert(!thread.IsRunning, "CtrlThread remained alive after Stop.");

        int stoppedCount = thread.RunCount;
        thread.Start(1, "RegressionCtrlThreadRestart");
        WaitForRunCount(thread, stoppedCount + 3, 1000);
        Assert(thread.IsRunning, "CtrlThread did not restart after Stop.");
        thread.Stop();

        Assert(firstRunCount >= 3, "CtrlThread initial Run count was not reached.");
    }

    private static void WaitForRunCount(CTestCtrlThread thread, int target, int timeoutMsec)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsec);
        while (thread.RunCount < target && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(2);
        }

        Assert(thread.RunCount >= target,
            "CtrlThread did not reach the expected Run count.");
    }

    private sealed class CTestCtrlThread : CtrlThread
    {
        private readonly object mobjCountLock = new object();
        private int mintRunCount;
        private int mintStartIdentity;
        private bool mblnThrowOneError;

        public int RunCount
        {
            get
            {
                lock (mobjCountLock)
                {
                    return mintRunCount;
                }
            }
        }

        public int StartIdentity
        {
            get
            {
                lock (mobjCountLock)
                {
                    if (mintStartIdentity == 0 && IsRunning)
                    {
                        mintStartIdentity = 1;
                    }

                    return mintStartIdentity;
                }
            }
        }

        public void RequestOneError()
        {
            lock (mobjCountLock)
            {
                mblnThrowOneError = true;
            }
        }

        public override void Run()
        {
            bool throwError;
            lock (mobjCountLock)
            {
                mintRunCount++;
                throwError = mblnThrowOneError;
                mblnThrowOneError = false;
            }

            if (throwError)
            {
                throw new InvalidOperationException("Regression thread error.");
            }
        }
    }

    private static void VerifyGoldenSnapshot(string[] args, List<string> snapshot)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            return;
        }

        string goldenPath = Path.GetFullPath(args[1]);
        string[] golden = System.IO.File.ReadAllLines(goldenPath, Encoding.UTF8);
        Assert(golden.Length == snapshot.Count,
            $"Golden line count differs. Expected {golden.Length}, actual {snapshot.Count}.");

        for (int index = 0; index < golden.Length; index++)
        {
            Assert(string.Equals(golden[index], snapshot[index], StringComparison.Ordinal),
                $"Golden line {index + 1} differs. Expected '{golden[index]}', actual '{snapshot[index]}'.");
        }
    }

    private static string ResolveOutputPath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        return Path.Combine(AppContext.BaseDirectory, "regression-snapshot.txt");
    }

    private static void RunRecipeRoundTrip(string testRoot, ICollection<string> snapshot)
    {
        string configRoot = Path.Combine(testRoot, "RecipeConfig");
        Directory.CreateDirectory(configRoot);
        WriteRecipeForm(configRoot);

        CJhmiRecipeFile recipeFile = new CJhmiRecipeFile(configRoot);
        List<ST_RECIPE_PARAM> parameters = new List<ST_RECIPE_PARAM>();
        parameters.Add(CreateRecipeParameter("Speed", "12.345", "mm/s", "COMMON", "GENERAL", "SPEED", EN_RECIPE_DATA_TYPE.Double, 1));
        parameters.Add(CreateRecipeParameter("Mode", "A,\"B\"", "", "COMMON", "GENERAL", "MODE", EN_RECIPE_DATA_TYPE.String, 2));
        ST_RECIPE_DATA recipe = new ST_RECIPE_DATA(
            "RCP_001",
            "Recipe, \"Alpha\"",
            parameters,
            Array.Empty<ST_RECIPE_HISTORY>());

        recipeFile.Save(recipe).GetAwaiter().GetResult();
        ST_RECIPE_DATA? loaded = recipeFile.Find(recipe.Id).GetAwaiter().GetResult();
        Assert(loaded is not null, "Recipe load returned null.");

        string recipePath = Path.Combine(configRoot, "RECIPE", "RCP_001.csv");
        string[] rawLines = System.IO.File.ReadAllLines(recipePath);
        snapshot.Add("[Recipe]");
        snapshot.Add($"FileLineCount={rawLines.Length}");
        for (int index = 0; index < rawLines.Length; index++)
        {
            snapshot.Add($"File[{index}]Fields={CountCsvFields(rawLines[index])};Text={Escape(rawLines[index])}");
        }

        snapshot.Add($"LoadedId={Escape(loaded!.Id)}");
        snapshot.Add($"LoadedName={Escape(loaded.Name)}");
        snapshot.Add($"ParameterCount={loaded.Parameters.Count}");
        for (int index = 0; index < loaded.Parameters.Count; index++)
        {
            ST_RECIPE_PARAM parameter = loaded.Parameters[index];
            snapshot.Add(
                $"Parameter[{index}]={Escape(parameter.Tab)}|{Escape(parameter.Group)}|{Escape(parameter.Key)}|" +
                $"{Escape(parameter.Value)}|{parameter.DataType}|{parameter.DisplayOrder}");
        }
    }

    private static ST_RECIPE_PARAM CreateRecipeParameter(
        string name,
        string value,
        string unit,
        string tab,
        string group,
        string key,
        EN_RECIPE_DATA_TYPE dataType,
        int displayOrder)
    {
        return new ST_RECIPE_PARAM(
            name,
            value,
            unit,
            "",
            "",
            tab,
            group,
            key,
            name,
            true,
            true,
            displayOrder,
            dataType,
            0.0,
            -100000.0,
            100000.0);
    }

    private static void WriteRecipeForm(string configRoot)
    {
        string[] lines =
        {
            "TAB,GROUP,NAME,DISPLAY NAME,CIM NAME,DATA TYPE,UNIT,SHOW,USE,VALUE,SCALE,CHANGE LIMIT,MIN,MAX,DESCRIPTION,ORDER",
            "COMMON,GENERAL,SPEED,Speed,SPEED,DOUBLE,mm/s,TRUE,TRUE,1.5,1,0,-100000,100000,Speed value,1",
            "COMMON,GENERAL,MODE,Mode,MODE,STRING,,TRUE,TRUE,AUTO,1,0,0,0,Mode value,2"
        };
        System.IO.File.WriteAllLines(Path.Combine(configRoot, "JHMI_RCP.csv"), lines, new UTF8Encoding(false));
    }

    private static void RunSettingRoundTrip(string testRoot, ICollection<string> snapshot)
    {
        string configRoot = Path.Combine(testRoot, "SettingConfig");
        Directory.CreateDirectory(configRoot);
        string[] formLines =
        {
            "TAB,GROUP,NAME,DISPLAY NAME,DATA TYPE,UNIT,SHOW,USE,VALUE,MIN,MAX,DESCRIPTION,ORDER",
            "OPTION,GENERAL,SPEED,Speed,DOUBLE,mm/s,TRUE,TRUE,1.5,-100000,100000,Speed value,1",
            "OPTION,GENERAL,MODE,Mode,STRING,,TRUE,TRUE,AUTO,0,0,Mode value,2"
        };
        System.IO.File.WriteAllLines(Path.Combine(configRoot, "JHMI_SETTING.csv"), formLines, new UTF8Encoding(false));

        CSettingFile settingFile = new CSettingFile(configRoot);
        IReadOnlyList<ST_SYSTEM_PARAMETER> loaded = settingFile.Load(EN_SETTING_TAB.Option).GetAwaiter().GetResult();
        List<ST_SYSTEM_PARAMETER> edited = new List<ST_SYSTEM_PARAMETER>();
        foreach (ST_SYSTEM_PARAMETER parameter in loaded)
        {
            if (parameter.Key.Equals("SPEED", StringComparison.OrdinalIgnoreCase))
            {
                edited.Add(parameter with { Value = "9.876543" });
            }
            else if (parameter.Key.Equals("MODE", StringComparison.OrdinalIgnoreCase))
            {
                edited.Add(parameter with { Value = "MANUAL,\"SAFE\"" });
            }
            else
            {
                edited.Add(parameter);
            }
        }

        settingFile.Save(EN_SETTING_TAB.Option, edited).GetAwaiter().GetResult();
        IReadOnlyList<ST_SYSTEM_PARAMETER> reloaded = settingFile.Load(EN_SETTING_TAB.Option).GetAwaiter().GetResult();
        string valuePath = Path.Combine(configRoot, "Setting", "Setting.csv");
        string[] valueLines = System.IO.File.ReadAllLines(valuePath);

        snapshot.Add("[Setting]");
        snapshot.Add($"FileLineCount={valueLines.Length}");
        for (int index = 0; index < valueLines.Length; index++)
        {
            snapshot.Add($"File[{index}]Fields={CountCsvFields(valueLines[index])};Text={Escape(valueLines[index])}");
        }

        snapshot.Add($"ParameterCount={reloaded.Count}");
        for (int index = 0; index < reloaded.Count; index++)
        {
            ST_SYSTEM_PARAMETER parameter = reloaded[index];
            snapshot.Add(
                $"Parameter[{index}]={parameter.Section}|{Escape(parameter.Group)}|{Escape(parameter.Key)}|" +
                $"{Escape(parameter.Value)}|{parameter.DataType}|{parameter.DisplayOrder}");
        }
    }

    private static void RunProcessPlanAndScript(string testRoot, ICollection<string> snapshot)
    {
        Dictionary<string, string> parameters = CreateProcessParameters();
        ST_RECIPE_HOLE_PLAN holePlan = CRecipeHolePlan.Build(parameters);

        snapshot.Add("[ProcessPlan]");
        snapshot.Add(
            $"Summary={holePlan.HeadCount}|{holePlan.CellCount}|{Format(holePlan.GlassSizeX)}|" +
            $"{Format(holePlan.GlassSizeY)}|{Format(holePlan.EncoderScale)}|{holePlan.Points.Count}");
        foreach (ST_RECIPE_HOLE_POINT point in holePlan.Points)
        {
            snapshot.Add(
                $"Point={point.SequenceNo}|{Escape(point.HoleKey)}|H{point.HeadNo}|C{point.CellNo}|" +
                $"{point.HoleNo}|{point.Column}|{point.Row}|{Format(point.DesignX)}|{Format(point.DesignY)}|" +
                $"{Format(point.ScannerGx)}|{Format(point.ScannerGy)}|{Format(point.StageWaitPosition)}");
        }

        ST_PROCESS_PLAN plan = new ST_PROCESS_PLAN(
            "PROC-001",
            "RCP_001",
            "PRODUCT-001",
            "PANEL-001",
            "LOT-001",
            FixedTime,
            parameters);
        IReadOnlyList<ST_HEAD_PROCESS_DATA> heads = BuildHeads(holePlan);
        ST_PROCESS_MODEL model = new ST_PROCESS_MODEL(plan, null, heads, parameters, FixedTime);
        string scriptRoot = Path.Combine(testRoot, "Scripts");
        CAutomation1ScriptFile scriptFile = new CAutomation1ScriptFile(scriptRoot);
        ST_AUTOMATION1_SCRIPT script = scriptFile.Build(model).GetAwaiter().GetResult();

        snapshot.Add("[Script]");
        snapshot.Add($"Summary={Escape(script.FileName)}|{script.TotalPoints}|{script.HeadCount}|{script.HeadScripts.Count}");
        AddNormalizedScriptLines("Main", script.Lines, snapshot);
        foreach (ST_AUTOMATION1_HEAD_SCRIPT headScript in script.HeadScripts)
        {
            snapshot.Add(
                $"Head={headScript.HeadNo}|{headScript.AutomationNo}|{headScript.TaskNo}|" +
                $"{Escape(headScript.FileName)}|{headScript.TotalPoints}");
            AddNormalizedScriptLines($"Head{headScript.HeadNo}", headScript.Lines, snapshot);
        }
    }

    private static Dictionary<string, string> CreateProcessParameters()
    {
        Dictionary<string, string> parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        parameters["HEAD_COUNT"] = "2";
        parameters["CELL_COUNT"] = "1";
        parameters["GLASS_SIZE_X"] = "500";
        parameters["GLASS_SIZE_Y"] = "300";
        parameters["SCAN_ENCODER_SCALE"] = "16000";
        parameters["STAGE_SCAN_DIRECTION_Y"] = "-1";
        parameters["SCAN_START_DELAY_LENGTH_Y"] = "2.5";
        parameters["AK_MARGIN_X"] = "55";
        parameters["AK_MARGIN_Y"] = "45";
        parameters["CELL1_ALIGN_TO_1ST_PIXEL_X"] = "10";
        parameters["CELL1_ALIGN_TO_1ST_PIXEL_Y"] = "20";
        parameters["CELL1_ROTATION"] = "0";
        parameters["CELL1_HOLE_COUNT"] = "3";
        parameters["CELL1_HOLE1_X"] = "0";
        parameters["CELL1_HOLE1_Y"] = "0";
        parameters["CELL1_HOLE2_X"] = "25";
        parameters["CELL1_HOLE2_Y"] = "5";
        parameters["CELL1_HOLE3_X"] = "60";
        parameters["CELL1_HOLE3_Y"] = "10";
        parameters["CELL1_A01_RECIPE_OFFSET_X"] = "0.125";
        parameters["CELL1_A01_RECIPE_OFFSET_Y"] = "-0.25";
        parameters["H01_CENTER_X"] = "60";
        parameters["H02_CENTER_X"] = "140";
        parameters["H01_CENTER_Y"] = "0";
        parameters["H02_CENTER_Y"] = "0";
        parameters["H01_SCAN_FIELD_WIDTH_X"] = "120";
        parameters["H02_SCAN_FIELD_WIDTH_X"] = "120";
        parameters["H01_AUTOMATION_NO"] = "0";
        parameters["H02_AUTOMATION_NO"] = "1";
        parameters["H01_AUTOMATION_TASK_NO"] = "1";
        parameters["H02_AUTOMATION_TASK_NO"] = "2";
        parameters["SCANNER_MARK_SPEED"] = "1200";
        parameters["SCANNER_JUMP_SPEED"] = "1500";
        parameters["LASER_POWER"] = "3.25";
        parameters["LASER_FREQUENCY"] = "20";
        parameters["SHOT_COUNT"] = "2";
        parameters["SHOT_TIME_DELAY"] = "0.5";
        parameters["SCAN_START_JUMP_STEP"] = "0";
        return parameters;
    }

    private static IReadOnlyList<ST_HEAD_PROCESS_DATA> BuildHeads(ST_RECIPE_HOLE_PLAN holePlan)
    {
        List<ST_HEAD_PROCESS_DATA> heads = new List<ST_HEAD_PROCESS_DATA>();
        for (int headNo = 1; headNo <= holePlan.HeadCount; headNo++)
        {
            List<ST_RECIPE_HOLE_POINT> processPoints = new List<ST_RECIPE_HOLE_POINT>();
            List<ST_PATH_POINT> path = new List<ST_PATH_POINT>();
            foreach (ST_RECIPE_HOLE_POINT point in holePlan.Points)
            {
                if (point.HeadNo == headNo)
                {
                    processPoints.Add(point);
                    path.Add(new ST_PATH_POINT(point.ScannerGx, point.ScannerGy));
                }
            }

            ST_HEAD_PROCESS_DATA head = new ST_HEAD_PROCESS_DATA(
                headNo,
                3.25,
                20.0,
                2,
                0.5,
                1200.0,
                1500.0,
                0.0,
                path)
            {
                AutomationNo = headNo - 1,
                TaskNo = headNo,
                ScriptFileName = $"PROCESS_H{headNo:00}.ascript",
                ProcessPoints = processPoints
            };
            heads.Add(head);
        }

        return heads;
    }

    private static void AddNormalizedScriptLines(
        string prefix,
        IReadOnlyList<string> lines,
        ICollection<string> snapshot)
    {
        int normalizedIndex = 0;
        foreach (string line in lines)
        {
            if (line.StartsWith("// CreatedAt=", StringComparison.Ordinal))
            {
                continue;
            }

            snapshot.Add($"{prefix}[{normalizedIndex}]={Escape(line)}");
            normalizedIndex++;
        }
    }

    private static void RunSimulationFlow(ICollection<string> snapshot)
    {
        ST_INTERFACE_DATA data = new ST_INTERFACE_DATA(
            EN_INTERFACE_TYPE.SocketClient,
            EN_EQP_MODULE.Vision,
            7,
            "VISION_SIM",
            "COMMON",
            true,
            true,
            new[] { "127.0.0.1", "5000", "1000", "1000", "1000" });
        CInterfaceDevice device = new CInterfaceDevice(data, true);

        snapshot.Add("[Simulation]");
        snapshot.Add($"Initial={device.ConnectionState}|{device.IsSimulation}");
        device.Connect().GetAwaiter().GetResult();
        string firstResponse = device.ExecuteFunction("FIRST").GetAwaiter().GetResult();
        string secondResponse = device.ExecuteFunction("SECOND:1,2,3").GetAwaiter().GetResult();
        ST_INTERFACE_COMM_STATUS status = device.GetCommunicationStatus();
        device.Disconnect().GetAwaiter().GetResult();

        snapshot.Add($"Responses={Escape(firstResponse)}|{Escape(secondResponse)}");
        snapshot.Add(
            $"Status={status.ConnectionState}|{status.IsSimulation}|{Escape(status.Endpoint)}|" +
            $"{Escape(status.LastSent)}|{Escape(status.LastReceived)}|{Escape(status.LastError)}");
        snapshot.Add($"AfterDisconnect={device.ConnectionState}|{device.IsSimulation}");
    }

    private static void RunAlarmFlow(ICollection<string> snapshot)
    {
        List<ST_MOTOR_AXIS_STATUS> motors = new List<ST_MOTOR_AXIS_STATUS>();
        motors.Add(new ST_MOTOR_AXIS_STATUS(
            "SCANNER_01_GX",
            "Scanner GX",
            0.0,
            0.0,
            0.0,
            true,
            true,
            false,
            false,
            true));
        ST_DEVICE_STATUS deviceStatus = new ST_DEVICE_STATUS(
            Array.Empty<ST_IO_STATUS>(),
            motors,
            new ST_LASER_STATUS(false, false, false, 0.0),
            new ST_CHILLER_STATUS(false, 22.0, 0.0, 0.0, true),
            new ST_ATTENUATOR_STATUS(0.0, 0.0, "READY"),
            new ST_BET_STATUS(1.0, 1.0, 0.0, 0.0, 0.0, 0.0, false, true, true, true),
            new ST_POWER_METER_STATUS(0.0, "W", FixedTime));

        CInterLockManager interLockManager = new CInterLockManager();
        ST_INTERLOCK_SUMMARY interLock = interLockManager.Evaluate(deviceStatus);
        CAlarmManager alarmManager = new CAlarmManager();
        IReadOnlyList<ST_ALARM_DATA> alarms = alarmManager.Build(deviceStatus, interLock);
        IReadOnlyList<ST_ALARM_DATA> repeated = alarmManager.Build(deviceStatus, interLock);

        snapshot.Add("[Alarm]");
        snapshot.Add(
            $"InterLock={interLock.CanAutoRun}|{interLock.CanManualMove}|{interLock.CanLaserOn}|" +
            $"{interLock.HasError}|{interLock.Items.Count}");
        snapshot.Add($"AlarmCount={alarms.Count}");
        for (int index = 0; index < alarms.Count; index++)
        {
            ST_ALARM_DATA alarm = alarms[index];
            snapshot.Add(
                $"Alarm[{index}]={alarm.Code}|{alarm.Severity}|{Escape(alarm.Device)}|" +
                $"{Escape(alarm.StationName)}|{Escape(alarm.Message)}|{Escape(alarm.RecoveryAction)}");
        }

        bool occurredAtStable = alarms.Count == repeated.Count;
        if (occurredAtStable)
        {
            for (int index = 0; index < alarms.Count; index++)
            {
                if (alarms[index].OccurredAt != repeated[index].OccurredAt)
                {
                    occurredAtStable = false;
                    break;
                }
            }
        }

        snapshot.Add($"RepeatedOccurredAtStable={occurredAtStable}");
    }

    private static void RunProtocolFlow(string testRoot, ICollection<string> snapshot)
    {
        string configRoot = Path.Combine(testRoot, "Protocol", "Config");
        Directory.CreateDirectory(configRoot);
        CLogManager logManager = new CLogManager(configRoot);
        List<ST_MELSEC_MAP_DATA> melsecMap = new List<ST_MELSEC_MAP_DATA>();
        melsecMap.Add(new ST_MELSEC_MAP_DATA(
            "WORD_TEST",
            true,
            "REGRESSION",
            "Word Test",
            0,
            "D100",
            EN_MELSEC_DATA_TYPE.Word,
            EN_MELSEC_DIRECTION.InOut,
            EN_MELSEC_ACCESS.ReadWrite,
            1.0,
            1,
            100,
            "Protocol regression word"));

        CInterfaceManager manager = new CInterfaceManager(true, logManager, null, null, melsecMap);
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.Serial, EN_EQP_MODULE.TalonLaser, 1, "TALON_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.Serial, EN_EQP_MODULE.Chiller, 2, "CHILLER_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.Serial, EN_EQP_MODULE.Attenuator, 3, "ATT_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.Serial, EN_EQP_MODULE.Bet, 4, "BET_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.Serial, EN_EQP_MODULE.PowerMeter, 5, "POWER_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.PicoMotor, EN_EQP_MODULE.PicoMotor, 6, "PICO_TEST"));
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.SocketClient, EN_EQP_MODULE.Melsec, 0, "MELSEC_TEST"));

        manager.Initialize().GetAwaiter().GetResult();
        int connectedCount = 0;
        foreach (CInterfaceDevice device in manager.Devices)
        {
            if (device.ConnectionState == EN_COMM_STATE.Simulation)
            {
                connectedCount++;
            }
        }
        ST_DEVICE_COMMAND_RESULT talon = manager.ExecuteTalonLaserCommand(
            1,
            EN_TALON_COMMAND.SetDiodeCurrent,
            12.5).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT chiller = manager.ExecuteChillerCommand(
            2,
            EN_CHILLER_COMMAND.SetTemperature,
            21.5).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT attenuator = manager.ExecuteAttenuatorCommand(
            3,
            EN_ATTENUATOR_COMMAND.MoveAbs,
            33.125).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT bet = manager.ExecuteBETCommand(
            4,
            EN_BET_COMMAND.MoveManual,
            2.25,
            -1.5).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT power = manager.ExecutePowerMeterCommand(
            5,
            EN_POWER_METER_COMMAND.SetWaveLength,
            355.0).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT picoConnect = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.Connect,
            3,
            0.0).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT picoVelocity = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.SetVelocity,
            3,
            1.25).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT picoAcceleration = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.SetAcceleration,
            3,
            2.5).GetAwaiter().GetResult();
        ST_DEVICE_COMMAND_RESULT pico = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.MoveAbsolute,
            3,
            0.125).GetAwaiter().GetResult();
        manager.Melsec.WriteWord("WORD_TEST", 4660).GetAwaiter().GetResult();
        int melsecValue = manager.Melsec.ReadWord("WORD_TEST").GetAwaiter().GetResult();
        manager.Destroy().GetAwaiter().GetResult();

        snapshot.Add("[Protocol]");
        snapshot.Add($"Connected={connectedCount}");
        snapshot.Add($"Talon={talon.IsSuccess}|{Escape(talon.Message)}");
        snapshot.Add($"Chiller={chiller.IsSuccess}|{Escape(chiller.Message)}");
        snapshot.Add($"Attenuator={attenuator.IsSuccess}|{Escape(attenuator.Message)}");
        snapshot.Add($"BET={bet.IsSuccess}|{Escape(bet.Message)}");
        snapshot.Add($"PowerMeter={power.IsSuccess}|{Escape(power.Message)}");
        snapshot.Add($"PicoConnect={picoConnect.IsSuccess}|{Escape(picoConnect.Message)}");
        snapshot.Add($"PicoVelocity={picoVelocity.IsSuccess}|{Escape(picoVelocity.Message)}");
        snapshot.Add($"PicoAcceleration={picoAcceleration.IsSuccess}|{Escape(picoAcceleration.Message)}");
        snapshot.Add($"PicoMotor={pico.IsSuccess}|{Escape(pico.Message)}");
        snapshot.Add($"PicoAxisCommand={Escape(CPicoMotor.BuildAxisCommand(3, "PA", 6250))}");
        snapshot.Add($"PicoQueryCommand={Escape(CPicoMotor.BuildAxisCommand(3, "TP", null, true))}");
        snapshot.Add($"MelsecWord={melsecValue}");

        string interfaceLogRoot = Path.Combine(testRoot, "Protocol", "Log", "Interface");
        string[] logFiles = Directory.GetFiles(interfaceLogRoot, "*.txt", SearchOption.AllDirectories);
        Array.Sort(logFiles, StringComparer.OrdinalIgnoreCase);
        int logIndex = 0;
        foreach (string logFile in logFiles)
        {
            string[] lines = System.IO.File.ReadAllLines(logFile);
            foreach (string line in lines)
            {
                int payloadStart = line.IndexOf("\\INTERFACE\\", StringComparison.Ordinal);
                Assert(payloadStart >= 0, "Protocol interface log payload marker was not found.");
                snapshot.Add($"ProtocolLog[{logIndex}]={Escape(line.Substring(payloadStart))}");
                logIndex++;
            }
        }

        snapshot.Add($"ProtocolLogCount={logIndex}");
    }

    private static ST_INTERFACE_DATA CreateSimulatedInterface(
        EN_INTERFACE_TYPE interfaceType,
        EN_EQP_MODULE module,
        int number,
        string nickName)
    {
        return new ST_INTERFACE_DATA(
            interfaceType,
            module,
            number,
            nickName,
            "COMMON",
            true,
            true,
            Array.Empty<string>());
    }

    private static void RunLogFlow(string testRoot, ICollection<string> snapshot)
    {
        string configRoot = Path.Combine(testRoot, "LogConfig");
        Directory.CreateDirectory(configRoot);
        CLogManager logManager = new CLogManager(configRoot);
        logManager.WriteInterfaceCommand(
            EN_EQP_MODULE.Vision,
            "VISION_SIM",
            "FIRST:1,2",
            "ACK:OK",
            "detail,\"quoted\"");

        string logRoot = Path.Combine(testRoot, "Log", "Interface");
        string[] files = Directory.GetFiles(logRoot, "*.txt", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        Assert(files.Length == 1, "Expected one interface log file.");
        string[] lines = System.IO.File.ReadAllLines(files[0]);
        Assert(lines.Length == 1, "Expected one interface log line.");
        string line = lines[0];
        int payloadStart = line.IndexOf("\\INTERFACE\\", StringComparison.Ordinal);
        Assert(payloadStart >= 0, "Interface log payload marker was not found.");

        snapshot.Add("[Log]");
        snapshot.Add($"RelativePath={NormalizeDateDigits(Path.GetRelativePath(testRoot, files[0]).Replace('\\', '/'))}");
        snapshot.Add($"Payload={Escape(line.Substring(payloadStart))}");
    }

    private static int CountCsvFields(string line)
    {
        int fieldCount = 1;
        bool inQuotes = false;
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                fieldCount++;
            }
        }

        return fieldCount;
    }

    private static string NormalizeDateDigits(string value)
    {
        StringBuilder result = new StringBuilder();
        int index = 0;
        while (index < value.Length)
        {
            if (index + 8 <= value.Length && IsEightDigits(value, index))
            {
                result.Append("<DATE>");
                index += 8;
                continue;
            }

            result.Append(value[index]);
            index++;
        }

        return result.ToString();
    }

    private static bool IsEightDigits(string value, int startIndex)
    {
        for (int index = startIndex; index < startIndex + 8; index++)
        {
            if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string Format(double value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
