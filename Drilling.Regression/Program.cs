using System.Globalization;
using System.Text;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Log;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Recipe;
using Drilling.Common.Review;
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
            RunMelsecWriteConfirmFlow(testRoot);
            RunAlarmFlow(snapshot);
            RunLogFlow(testRoot, snapshot);
            RunCtrlThreadFlow();
            RunReviewThreadFlow();

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

    private static void RunReviewThreadFlow()
    {
        CInterfaceManager interfaceManager = new CInterfaceManager(true);
        interfaceManager.Register(CreateSimulatedInterface(
            EN_INTERFACE_TYPE.SocketClient,
            EN_EQP_MODULE.WonikCtrl,
            0,
            "REVIEW_STAGE_TEST"));
        interfaceManager.Register(CreateSimulatedInterface(
            EN_INTERFACE_TYPE.SocketClient,
            EN_EQP_MODULE.Vision,
            0,
            "REVIEW_VISION_TEST"));
        interfaceManager.Initialize();

        CRegressionSettingFile settingFile = new CRegressionSettingFile();
        CRegressionInterfaceFile interfaceFile = new CRegressionInterfaceFile();
        CSettingManager settingManager = new CSettingManager(settingFile, interfaceFile, interfaceManager);
        CRegressionReviewResultFile resultFile = new CRegressionReviewResultFile();
        CReviewManager reviewManager = new CReviewManager(resultFile, interfaceManager, settingManager);
        ST_REVIEW_PLAN_POINT point = new ST_REVIEW_PLAN_POINT(
            1,
            "C01-H0001",
            1,
            1,
            1,
            1,
            1,
            true,
            10.0,
            20.0,
            10.0,
            20.0,
            0.0,
            0.0,
            EN_REVIEW_POINT_STATE.Ready,
            "WAIT");
        ST_REVIEW_PLAN plan = new ST_REVIEW_PLAN(
            "REVIEW_TEST",
            "Review Test",
            1,
            1,
            0.030,
            0.030,
            EN_VISION_AXIS_MODE.Normal,
            FixedTime,
            new[] { point });

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ST_REVIEW_SEQUENCE_STATUS startStatus = reviewManager.Start(plan);
        stopwatch.Stop();
        Assert(stopwatch.ElapsedMilliseconds < 500, "Review Start blocked the caller thread.");
        Assert(startStatus.State == EN_REVIEW_SEQUENCE_STATE.Running,
            "Review Start did not enter the running state.");
        WaitForReviewState(reviewManager, EN_REVIEW_SEQUENCE_STATE.Completed, 5000);
        Assert(resultFile.SaveCount == 1, "Review result was not saved once.");

        reviewManager.Start(plan);
        Thread.Sleep(50);
        reviewManager.Stop();
        WaitForReviewState(reviewManager, EN_REVIEW_SEQUENCE_STATE.Stopped, 1000);
        reviewManager.Shutdown();
        Assert(!reviewManager.IsRunning, "Review thread remained alive after Shutdown.");
        interfaceManager.Destroy();
    }

    private static void WaitForReviewState(
        CReviewManager reviewManager,
        EN_REVIEW_SEQUENCE_STATE expectedState,
        int timeoutMsec)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsec);
        while (reviewManager.SequenceState != expectedState && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert(reviewManager.SequenceState == expectedState,
            "Review sequence did not reach state " + expectedState +
            ". Current state: " + reviewManager.SequenceState);
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

        recipeFile.Save(recipe);
        ST_RECIPE_DATA? loaded = recipeFile.Find(recipe.Id);
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
        IReadOnlyList<ST_SYSTEM_PARAMETER> loaded = settingFile.Load(EN_SETTING_TAB.Option);
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

        settingFile.Save(EN_SETTING_TAB.Option, edited);
        IReadOnlyList<ST_SYSTEM_PARAMETER> reloaded = settingFile.Load(EN_SETTING_TAB.Option);
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
        ST_AUTOMATION1_SCRIPT script = scriptFile.Build(model);

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
        device.Connect();
        string firstResponse = device.ExecuteFunction("FIRST");
        string secondResponse = device.ExecuteFunction("SECOND:1,2,3");
        ST_INTERFACE_COMM_STATUS status = device.GetCommunicationStatus();
        device.Disconnect();

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
        manager.Register(CreateSimulatedInterface(EN_INTERFACE_TYPE.MelsecNet, EN_EQP_MODULE.Melsec, 0, "MELSEC_TEST"));

        manager.Initialize();
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
            12.5);
        ST_DEVICE_COMMAND_RESULT chiller = manager.ExecuteChillerCommand(
            2,
            EN_CHILLER_COMMAND.SetTemperature,
            21.5);
        ST_DEVICE_COMMAND_RESULT attenuator = manager.ExecuteAttenuatorCommand(
            3,
            EN_ATTENUATOR_COMMAND.MoveAbs,
            33.125);
        ST_DEVICE_COMMAND_RESULT bet = manager.ExecuteBETCommand(
            4,
            EN_BET_COMMAND.MoveManual,
            2.25,
            -1.5);
        ST_DEVICE_COMMAND_RESULT power = manager.ExecutePowerMeterCommand(
            5,
            EN_POWER_METER_COMMAND.SetWaveLength,
            355.0);
        ST_DEVICE_COMMAND_RESULT picoConnect = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.Connect,
            3,
            0.0);
        ST_DEVICE_COMMAND_RESULT picoVelocity = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.SetVelocity,
            3,
            1.25);
        ST_DEVICE_COMMAND_RESULT picoAcceleration = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.SetAcceleration,
            3,
            2.5);
        ST_DEVICE_COMMAND_RESULT pico = manager.ExecutePicoMotorCommand(
            6,
            EN_PICO_MOTOR_COMMAND.MoveAbsolute,
            3,
            0.125);
        manager.Melsec.WriteWord("WORD_TEST", 4660);
        int melsecValue = manager.Melsec.ReadWord("WORD_TEST");
        manager.Destroy();

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

    private static void RunMelsecWriteConfirmFlow(string testRoot)
    {
        RunMelsecMapValidationFlow(testRoot);
        RunMelsecNetInterfaceConfigFlow(testRoot);
        RunConfiguredMelsecMapWriteConfirmFlow();
        List<ST_MELSEC_MAP_DATA> map = new List<ST_MELSEC_MAP_DATA>();
        map.Add(MelsecMap("BIT_WRITE", "W100.0", EN_MELSEC_DATA_TYPE.Bit, EN_MELSEC_DIRECTION.Out, EN_MELSEC_ACCESS.Write, 1.0, 1));
        map.Add(MelsecMap("BIT_READ", "W200.0", EN_MELSEC_DATA_TYPE.Bit, EN_MELSEC_DIRECTION.In, EN_MELSEC_ACCESS.Read, 1.0, 1));
        map.Add(MelsecMap("WORD_WRITE", "D100", EN_MELSEC_DATA_TYPE.Word, EN_MELSEC_DIRECTION.Out, EN_MELSEC_ACCESS.Write, 1.0, 1));
        map.Add(MelsecMap("WORD_READ", "D200", EN_MELSEC_DATA_TYPE.Word, EN_MELSEC_DIRECTION.In, EN_MELSEC_ACCESS.Read, 1.0, 1));
        map.Add(MelsecMap("BIT_RW_0", "W300.0", EN_MELSEC_DATA_TYPE.Bit, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 1.0, 1));
        map.Add(MelsecMap("BIT_RW_15", "W300.F", EN_MELSEC_DATA_TYPE.Bit, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 1.0, 1));
        map.Add(MelsecMap("WORD_RW", "D301", EN_MELSEC_DATA_TYPE.Word, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 1.0, 1));
        map.Add(MelsecMap("DWORD_RW", "D302", EN_MELSEC_DATA_TYPE.DWord, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 1.0, 2));
        map.Add(MelsecMap("DOUBLE_RW", "D304", EN_MELSEC_DATA_TYPE.Double, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 0.001, 2));
        map.Add(MelsecMap("STRING_RW", "D306", EN_MELSEC_DATA_TYPE.String, EN_MELSEC_DIRECTION.InOut, EN_MELSEC_ACCESS.ReadWrite, 1.0, 3));

        RunMelsecNetApiFlow(map);

        CInterfaceManager manager = new CInterfaceManager(true, null, null, null, map);
        manager.Register(CreateSimulatedInterface(
            EN_INTERFACE_TYPE.MelsecNet,
            EN_EQP_MODULE.Melsec,
            0,
            "MELSEC_HANDSHAKE_TEST"));
        manager.Initialize();
        CMelsec melsec = manager.Melsec;
        Assert(melsec.IsCommunicationAvailable,
            "MELSEC Simulation open did not become available.");

        melsec.WriteBit("BIT_RW_0", true);
        melsec.WriteBit("BIT_RW_15", true);
        Assert(melsec.ReadBit("BIT_RW_0"), "MELSEC Bit 0 conversion failed.");
        Assert(melsec.ReadBit("BIT_RW_15"), "MELSEC Bit 15 conversion failed.");
        melsec.WriteBit("BIT_RW_0", false);
        Assert(!melsec.ReadBit("BIT_RW_0"), "MELSEC Bit 0 reset failed.");
        Assert(melsec.ReadBit("BIT_RW_15"), "MELSEC Bit merge damaged Bit 15.");

        melsec.WriteWord("WORD_RW", 0);
        Assert(melsec.ReadWord("WORD_RW") == 0, "MELSEC Word zero conversion failed.");
        melsec.WriteWord("WORD_RW", 1);
        Assert(melsec.ReadWord("WORD_RW") == 1, "MELSEC Word one conversion failed.");
        melsec.WriteWord("WORD_RW", -1);
        Assert(melsec.ReadWord("WORD_RW") == ushort.MaxValue, "MELSEC Word -1 conversion changed.");
        melsec.WriteWord("DWORD_RW", int.MinValue);
        Assert(melsec.ReadWord("DWORD_RW") == int.MinValue, "MELSEC DWord minimum conversion failed.");
        melsec.WriteWord("DWORD_RW", int.MaxValue);
        Assert(melsec.ReadWord("DWORD_RW") == int.MaxValue, "MELSEC DWord maximum conversion failed.");

        melsec.WriteDouble("DOUBLE_RW", 12.345);
        Assert(Math.Abs(melsec.ReadDouble("DOUBLE_RW") - 12.345) < 0.0005,
            "MELSEC scaled Double conversion failed.");
        melsec.WriteString("STRING_RW", "ABCDE");
        Assert(melsec.ReadString("STRING_RW") == "ABCDE", "MELSEC odd ASCII conversion failed.");
        melsec.WriteString("STRING_RW", "ABCDEF");
        Assert(melsec.ReadString("STRING_RW") == "ABCDEF", "MELSEC even ASCII conversion failed.");
        melsec.WriteString("STRING_RW", "");
        Assert(melsec.ReadString("STRING_RW") == "", "MELSEC empty ASCII conversion failed.");
        melsec.WriteString("STRING_RW", "A\0B");
        Assert(melsec.ReadString("STRING_RW") == "A\0B", "MELSEC null character conversion failed.");

        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.AutoEcho);
        long beforeReadCycle = melsec.ReadCycleNo;
        int wordRequest = melsec.QueueWriteWord("WORD_WRITE", 4660, "WORD_READ", 100, 1);
        ST_MELSEC_WRITE_STATUS wordStatus = WaitForMelsecWrite(melsec, wordRequest, 2000);
        Assert(wordStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed,
            "MELSEC Word write-confirm did not complete.");
        Assert(wordStatus.ConfirmReadCycle >= wordStatus.MinimumReadCycle,
            "MELSEC Word confirmed without the required new read cycle.");
        Assert(wordStatus.ConfirmReadCycle > beforeReadCycle,
            "MELSEC Word used an old read cycle for confirmation.");

        int bitOnRequest = melsec.QueueWriteBit("BIT_WRITE", true, "BIT_READ", 100, 0);
        ST_MELSEC_WRITE_STATUS bitOnStatus = WaitForMelsecWrite(melsec, bitOnRequest, 2000);
        Assert(bitOnStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed && bitOnStatus.ActualValue == "1",
            "MELSEC Busy ON style write-confirm failed.");
        int bitOffRequest = melsec.QueueWriteBit("BIT_WRITE", false, "BIT_READ", 100, 0);
        ST_MELSEC_WRITE_STATUS bitOffStatus = WaitForMelsecWrite(melsec, bitOffRequest, 2000);
        Assert(bitOffStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed && bitOffStatus.ActualValue == "0",
            "MELSEC Busy OFF style write-confirm failed.");

        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.HoldValue);
        int timeoutRequest = melsec.QueueWriteWord("WORD_WRITE", 77, "WORD_READ", 30, 0);
        int duplicateRequest = melsec.QueueWriteWord("WORD_WRITE", 77, "WORD_READ", 30, 0);
        Assert(duplicateRequest == timeoutRequest,
            "MELSEC accepted an unintended duplicate write request.");
        ST_MELSEC_WRITE_STATUS timeoutStatus = WaitForMelsecWrite(melsec, timeoutRequest, 2000);
        Assert(timeoutStatus.Result == EN_MELSEC_WRITE_RESULT.Timeout,
            "MELSEC mismatched readback advanced instead of timing out.");

        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.FailFirstAttempt);
        int retryRequest = melsec.QueueWriteWord("WORD_WRITE", 88, "WORD_READ", 30, 1);
        ST_MELSEC_WRITE_STATUS retryStatus = WaitForMelsecWrite(melsec, retryRequest, 2000);
        Assert(retryStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed && retryStatus.CurrentRetryCount == 1,
            "MELSEC retry did not confirm after one failed attempt.");

        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.CommunicationError);
        int errorRequest = melsec.QueueWriteWord("WORD_WRITE", 99, "WORD_READ", 30, 1);
        ST_MELSEC_WRITE_STATUS errorStatus = WaitForMelsecWrite(melsec, errorRequest, 2000);
        Assert(errorStatus.Result == EN_MELSEC_WRITE_RESULT.CommunicationError &&
            errorStatus.CurrentRetryCount == 1,
            "MELSEC communication error retry policy failed.");

        int invalidRequest = melsec.QueueWriteWord("WORD_WRITE", 100, "UNKNOWN_READBACK", 30, 0);
        ST_MELSEC_WRITE_STATUS? invalidStatus = melsec.GetWriteStatus(invalidRequest);
        Assert(invalidStatus != null && invalidStatus.Result == EN_MELSEC_WRITE_RESULT.InvalidParameter,
            "MELSEC invalid readback ID was accepted.");

        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.HoldValue);
        int cancelledRequest = melsec.QueueWriteWord("WORD_WRITE", 111, "WORD_READ", 5000, 0);
        Thread.Sleep(20);
        melsec.DeInitialize();
        ST_MELSEC_WRITE_STATUS? cancelledStatus = melsec.GetWriteStatus(cancelledRequest);
        Assert(cancelledStatus != null && cancelledStatus.Result == EN_MELSEC_WRITE_RESULT.Cancelled,
            "MELSEC active request was not cancelled during Stop.");
        Assert(!melsec.IsRunning, "MELSEC control thread remained alive after Stop.");

        melsec.Initialize();
        melsec.Initialize();
        melsec.SetSimulationReadbackMode(EN_MELSEC_SIMULATION_READBACK.AutoEcho);
        int restartRequest = melsec.QueueWriteWord("WORD_WRITE", 222, "WORD_READ", 100, 0);
        ST_MELSEC_WRITE_STATUS restartStatus = WaitForMelsecWrite(melsec, restartRequest, 2000);
        Assert(restartStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed,
            "MELSEC control did not restart after Stop.");
        manager.Destroy();
        Assert(!melsec.IsRunning, "MELSEC control thread remained alive after manager Destroy.");

        CInterfaceManager offlineManager = new CInterfaceManager(false, null, null, null, map);
        offlineManager.Register(CreateSimulatedInterface(
            EN_INTERFACE_TYPE.MelsecNet,
            EN_EQP_MODULE.Melsec,
            0,
            "MELSEC_OFFLINE_TEST"));
        int offlineRequest = offlineManager.Melsec.QueueWriteWord(
            "WORD_WRITE",
            333,
            "WORD_READ",
            30,
            0);
        ST_MELSEC_WRITE_STATUS? offlineStatus = offlineManager.Melsec.GetWriteStatus(offlineRequest);
        Assert(offlineStatus != null &&
            offlineStatus.Result == EN_MELSEC_WRITE_RESULT.CommunicationError,
            "MELSEC accepted a live write while communication was offline.");
        Assert(!offlineManager.Melsec.IsRunning,
            "MELSEC started its control thread for an offline rejected write.");
        offlineManager.Initialize();
        Assert(!offlineManager.IsConnect(EN_EQP_MODULE.Melsec, 0),
            "MELSEC invalid live endpoint was reported online.");
        Assert(!offlineManager.Melsec.IsCommunicationAvailable,
            "MELSEC invalid live endpoint was reported communication available.");
        offlineManager.Destroy();
        Assert(!offlineManager.Melsec.IsRunning,
            "MELSEC failed-open control thread remained after Destroy.");
    }

    private static void RunConfiguredMelsecMapWriteConfirmFlow()
    {
        string repositoryRoot = FindRepositoryRoot();
        CMelsecMapFile mapFile = new CMelsecMapFile(Path.Combine(repositoryRoot, "Config"));
        IReadOnlyList<ST_MELSEC_MAP_DATA> map = mapFile.LoadAll();
        CInterfaceManager manager = new CInterfaceManager(true, null, null, null, map);
        manager.Register(CreateSimulatedInterface(
            EN_INTERFACE_TYPE.MelsecNet,
            EN_EQP_MODULE.Melsec,
            0,
            "MELSEC_CONFIGURED_MAP_TEST"));
        manager.Initialize();

        int confirmedCount = 0;
        foreach (ST_MELSEC_MAP_DATA data in map)
        {
            if (data.Access == EN_MELSEC_ACCESS.Read ||
                data.Direction == EN_MELSEC_DIRECTION.In ||
                !data.Id.EndsWith("_WRITE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int requestNo;
            switch (data.DataType)
            {
                case EN_MELSEC_DATA_TYPE.Bit:
                    requestNo = manager.Melsec.QueueWriteBit(data.Id, true, "", 100, 0);
                    break;
                case EN_MELSEC_DATA_TYPE.Word:
                case EN_MELSEC_DATA_TYPE.DWord:
                    requestNo = manager.Melsec.QueueWriteWord(data.Id, 123, "", 100, 0);
                    break;
                case EN_MELSEC_DATA_TYPE.Double:
                case EN_MELSEC_DATA_TYPE.Float:
                    requestNo = manager.Melsec.QueueWriteDouble(data.Id, 1.234, "", 100, 0);
                    break;
                case EN_MELSEC_DATA_TYPE.String:
                    requestNo = manager.Melsec.QueueWriteString(data.Id, "TEST", "", 100, 0);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Configured MELSEC write type is not supported: " + data.Id);
            }

            ST_MELSEC_WRITE_STATUS status = WaitForMelsecWrite(manager.Melsec, requestNo, 3000);
            Assert(status.Result == EN_MELSEC_WRITE_RESULT.Confirmed,
                "Configured MELSEC write/readback pair failed: " + data.Id +
                " / " + status.ReadbackId + " / " + status.ErrorMessage);
            confirmedCount++;
        }

        manager.Destroy();
        Assert(confirmedCount == 8,
            "Configured MELSEC write/readback pair count changed: " + confirmedCount);
        Assert(!manager.Melsec.IsRunning,
            "Configured MELSEC map test left its control thread running.");
    }

    private static void RunMelsecNetInterfaceConfigFlow(string testRoot)
    {
        string repositoryRoot = FindRepositoryRoot();
        string configRoot = Path.Combine(testRoot, "MelsecNetInterfaceConfig");
        Directory.CreateDirectory(configRoot);
        System.IO.File.Copy(
            Path.Combine(repositoryRoot, "Config", "JHMI_INTERFACE.csv"),
            Path.Combine(configRoot, "JHMI_INTERFACE.csv"),
            true);

        CInterfaceFile interfaceFile = new CInterfaceFile(configRoot);
        IReadOnlyList<ST_INTERFACE_DATA> rows = interfaceFile.LoadAll();
        ST_INTERFACE_DATA? melsecRow = null;
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Device == EN_EQP_MODULE.Melsec)
            {
                melsecRow = rows[index];
                break;
            }
        }

        if (melsecRow == null)
        {
            throw new InvalidOperationException("JHMI_INTERFACE does not contain a MELSEC row.");
        }
        Assert(melsecRow.InterfaceType == EN_INTERFACE_TYPE.MelsecNet,
            "JHMI_INTERFACE MELSEC row is not configured for MELSEC_NET.");
        Assert(melsecRow.IsSimulation,
            "Repository MELSEC_NET configuration unexpectedly enables live hardware.");

        interfaceFile.SaveAll(rows);
        IReadOnlyList<ST_INTERFACE_DATA> savedRows = interfaceFile.LoadAll();
        ST_INTERFACE_DATA? savedMelsecRow = null;
        for (int index = 0; index < savedRows.Count; index++)
        {
            if (savedRows[index].Device == EN_EQP_MODULE.Melsec)
            {
                savedMelsecRow = savedRows[index];
                break;
            }
        }
        Assert(savedMelsecRow != null &&
            savedMelsecRow.InterfaceType == EN_INTERFACE_TYPE.MelsecNet,
            "MELSEC_NET interface type did not survive save and reload.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "Drilling.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Drilling.sln repository root was not found.");
    }

    private static void RunMelsecMapValidationFlow(string testRoot)
    {
        string configRoot = Path.Combine(testRoot, "MelsecMapValidation");
        Directory.CreateDirectory(configRoot);
        string mapPath = Path.Combine(configRoot, "JHMI_MELSEC_MAP.csv");
        string header = "ID,USE,GROUP,NAME,DEVICE NO,ADDRESS,DATA TYPE,DIRECTION,ACCESS,SCALE,LENGTH,POLL_MS,DESCRIPTION";
        CMelsecMapFile mapFile = new CMelsecMapFile(configRoot);

        System.IO.File.WriteAllText(
            mapPath,
            header + Environment.NewLine +
            "# COMMENT,,,,,,,,,,,," + Environment.NewLine +
            "VALID_BIT,1,TEST,Valid Bit,0,W100.F,BIT,IN,R,1,1,10,valid",
            new UTF8Encoding(false));
        IReadOnlyList<ST_MELSEC_MAP_DATA> validRows = mapFile.LoadAll();
        Assert(validRows.Count == 1 && validRows[0].Id == "VALID_BIT",
            "MELSEC map comment or Bit 15 validation failed.");

        System.IO.File.WriteAllText(
            mapPath,
            header + Environment.NewLine +
            "INVALID_BIT,1,TEST,Invalid Bit,0,W100.10,BIT,IN,R,1,1,10,invalid",
            new UTF8Encoding(false));
        bool invalidBitRejected = false;
        try
        {
            mapFile.LoadAll();
        }
        catch (InvalidDataException)
        {
            invalidBitRejected = true;
        }
        Assert(invalidBitRejected, "MELSEC map accepted Bit 16.");

        System.IO.File.WriteAllText(
            mapPath,
            header + Environment.NewLine +
            "DUPLICATE,1,TEST,First,0,D100,WORD,IN,R,1,1,10,first" + Environment.NewLine +
            "DUPLICATE,1,TEST,Second,0,D101,WORD,IN,R,1,1,10,second",
            new UTF8Encoding(false));
        bool duplicateRejected = false;
        try
        {
            mapFile.LoadAll();
        }
        catch (InvalidDataException)
        {
            duplicateRejected = true;
        }
        Assert(duplicateRejected, "MELSEC map accepted a duplicate ID.");

        System.IO.File.WriteAllText(mapPath, "", new UTF8Encoding(false));
        bool emptyFileRejected = false;
        try
        {
            mapFile.LoadAll();
        }
        catch (InvalidDataException)
        {
            emptyFileRejected = true;
        }
        Assert(emptyFileRejected, "MELSEC map accepted an empty file.");

        System.IO.File.WriteAllText(
            mapPath,
            "ID,USE,GROUP" + Environment.NewLine + "SHORT,1,TEST",
            new UTF8Encoding(false));
        bool missingColumnRejected = false;
        try
        {
            mapFile.LoadAll();
        }
        catch (InvalidDataException)
        {
            missingColumnRejected = true;
        }
        Assert(missingColumnRejected, "MELSEC map accepted missing required columns.");
    }

    private static void RunMelsecNetApiFlow(IReadOnlyList<ST_MELSEC_MAP_DATA> map)
    {
        CTestMelsecNetApi api = new CTestMelsecNetApi();
        CInterfaceManager manager = new CInterfaceManager(false, null, null, null, map, api);
        manager.Register(CreateLiveMelsecNetInterface());

        try
        {
            manager.Initialize();
            Assert(manager.IsConnect(EN_EQP_MODULE.Melsec, 0),
                "MELSECNET mdOpen connection did not become online.");
            Assert(manager.Melsec.ReadCycleNo > 0,
                "MELSECNET connection became online before the initial mdReceiveEx read.");
            Assert(api.OpenCount == 1 && api.ReceiveCount > 0,
                "MELSECNET mdOpen/mdReceiveEx call path was not used.");

            int confirmedRequest = manager.Melsec.QueueWriteWord(
                "WORD_WRITE",
                4321,
                "WORD_READ",
                200,
                0);
            ST_MELSEC_WRITE_STATUS confirmedStatus = WaitForMelsecWrite(
                manager.Melsec,
                confirmedRequest,
                3000);
            Assert(confirmedStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed,
                "MELSECNET mdSendEx write-confirm failed.");
            Assert(api.ReceiveCount >= 2 && api.SendCount >= 1,
                "MELSECNET API did not receive the expected read/write sequence.");

            int bitOnRequest = manager.Melsec.QueueWriteBit(
                "BIT_WRITE",
                true,
                "BIT_READ",
                200,
                0);
            ST_MELSEC_WRITE_STATUS bitOnStatus = WaitForMelsecWrite(
                manager.Melsec,
                bitOnRequest,
                3000);
            Assert(bitOnStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed &&
                bitOnStatus.ActualValue == "1",
                "MELSECNET Bit ON write-confirm failed.");
            int bitOffRequest = manager.Melsec.QueueWriteBit(
                "BIT_WRITE",
                false,
                "BIT_READ",
                200,
                0);
            ST_MELSEC_WRITE_STATUS bitOffStatus = WaitForMelsecWrite(
                manager.Melsec,
                bitOffRequest,
                3000);
            Assert(bitOffStatus.Result == EN_MELSEC_WRITE_RESULT.Confirmed &&
                bitOffStatus.ActualValue == "0",
                "MELSECNET Bit OFF write-confirm failed.");

            api.SetEchoReadback(false);
            int mismatchRequest = manager.Melsec.QueueWriteWord(
                "WORD_WRITE",
                5432,
                "WORD_READ",
                40,
                0);
            ST_MELSEC_WRITE_STATUS mismatchStatus = WaitForMelsecWrite(
                manager.Melsec,
                mismatchRequest,
                3000);
            Assert(mismatchStatus.Result == EN_MELSEC_WRITE_RESULT.Timeout,
                "MELSECNET mismatched readback did not time out.");
            Assert(manager.IsConnect(EN_EQP_MODULE.Melsec, 0),
                "MELSECNET readback mismatch incorrectly dropped the connection.");

            api.SetEchoReadback(true);
            api.SetNextReturnCode(0xC051);
            int apiErrorRequest = manager.Melsec.QueueWriteWord(
                "WORD_WRITE",
                6543,
                "WORD_READ",
                100,
                0);
            ST_MELSEC_WRITE_STATUS apiErrorStatus = WaitForMelsecWrite(
                manager.Melsec,
                apiErrorRequest,
                3000);
            Assert(apiErrorStatus.Result == EN_MELSEC_WRITE_RESULT.CommunicationError,
                "MELSECNET SDK return code did not stop the write.");
            Assert(!manager.IsConnect(EN_EQP_MODULE.Melsec, 0),
                "MELSECNET SDK return code did not mark communication offline.");

            manager.Reconnect(EN_EQP_MODULE.Melsec, 0);
            Assert(manager.IsConnect(EN_EQP_MODULE.Melsec, 0),
                "MELSECNET reconnection did not restore communication.");
        }
        finally
        {
            manager.Destroy();
        }

        Assert(!manager.Melsec.IsRunning,
            "MELSECNET control thread remained after Destroy.");
        Assert(api.CloseCount >= 1,
            "MELSECNET mdClose was not called during disconnect or shutdown.");
        Assert(!api.ThreadViolation,
            "MELSECNET SDK functions were called from more than one thread.");

        CTestMelsecNetApi openFailureApi = new CTestMelsecNetApi();
        openFailureApi.SetOpenReturnCode(-20);
        CInterfaceManager openFailureManager =
            new CInterfaceManager(false, null, null, null, map, openFailureApi);
        openFailureManager.Register(CreateLiveMelsecNetInterface());
        openFailureManager.Initialize();
        Assert(!openFailureManager.IsConnect(EN_EQP_MODULE.Melsec, 0),
            "MELSECNET mdOpen failure was reported online.");
        Assert(openFailureApi.OpenCount == 1 &&
            openFailureApi.SendCount == 0 &&
            openFailureApi.ReceiveCount == 0,
            "MELSECNET performed I/O after mdOpen failure.");
        openFailureManager.Destroy();
        Assert(!openFailureManager.Melsec.IsRunning,
            "MELSECNET mdOpen failure left the control thread running.");
    }

    private static ST_INTERFACE_DATA CreateLiveMelsecNetInterface()
    {
        return new ST_INTERFACE_DATA(
            EN_INTERFACE_TYPE.MelsecNet,
            EN_EQP_MODULE.Melsec,
            0,
            "MELSEC_LIVE_TEST",
            "PLC",
            true,
            false,
            new[]
            {
                "51",
                "1",
                "1",
                "500",
                "1"
            });
    }

    private static ST_MELSEC_MAP_DATA MelsecMap(
        string id,
        string address,
        EN_MELSEC_DATA_TYPE dataType,
        EN_MELSEC_DIRECTION direction,
        EN_MELSEC_ACCESS access,
        double scale,
        int length)
    {
        return new ST_MELSEC_MAP_DATA(
            id,
            true,
            "REGRESSION",
            id,
            0,
            address,
            dataType,
            direction,
            access,
            scale,
            length,
            2,
            "MELSEC handshake regression");
    }

    private static ST_MELSEC_WRITE_STATUS WaitForMelsecWrite(
        CMelsec melsec,
        int requestNo,
        int timeoutMsec)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsec);
        while (DateTime.UtcNow < deadline)
        {
            ST_MELSEC_WRITE_STATUS? status = melsec.GetWriteStatus(requestNo);
            if (status != null && IsTerminalMelsecWriteResult(status.Result))
            {
                return status;
            }

            Thread.Sleep(2);
        }

        ST_MELSEC_WRITE_STATUS? timeoutStatus = melsec.GetWriteStatus(requestNo);
        throw new InvalidOperationException(
            "MELSEC write request did not reach terminal state: " + requestNo +
            " / " + (timeoutStatus == null ? "UNKNOWN" : timeoutStatus.Result.ToString()));
    }

    private static bool IsTerminalMelsecWriteResult(EN_MELSEC_WRITE_RESULT result)
    {
        return result == EN_MELSEC_WRITE_RESULT.Confirmed ||
            result == EN_MELSEC_WRITE_RESULT.Timeout ||
            result == EN_MELSEC_WRITE_RESULT.CommunicationError ||
            result == EN_MELSEC_WRITE_RESULT.InvalidParameter ||
            result == EN_MELSEC_WRITE_RESULT.Cancelled;
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

    private sealed class CTestMelsecNetApi : CMelsecNetApi
    {
        private const int TestPath = 7001;
        private readonly object mobjLock = new object();
        private readonly Dictionary<string, short> mobjWords =
            new Dictionary<string, short>(StringComparer.Ordinal);
        private bool mblnEchoReadback = true;
        private int mintNextReturnCode;
        private int mintOpenReturnCode;
        private int mintOwnerThreadId;
        private int mintOpenCount;
        private int mintCloseCount;
        private int mintSendCount;
        private int mintReceiveCount;
        private bool mblnThreadViolation;

        public int OpenCount
        {
            get
            {
                lock (mobjLock)
                {
                    return mintOpenCount;
                }
            }
        }

        public int CloseCount
        {
            get
            {
                lock (mobjLock)
                {
                    return mintCloseCount;
                }
            }
        }

        public int SendCount
        {
            get
            {
                lock (mobjLock)
                {
                    return mintSendCount;
                }
            }
        }

        public int ReceiveCount
        {
            get
            {
                lock (mobjLock)
                {
                    return mintReceiveCount;
                }
            }
        }

        public bool ThreadViolation
        {
            get
            {
                lock (mobjLock)
                {
                    return mblnThreadViolation;
                }
            }
        }

        public void SetEchoReadback(bool enabled)
        {
            lock (mobjLock)
            {
                mblnEchoReadback = enabled;
            }
        }

        public void SetNextReturnCode(int returnCode)
        {
            lock (mobjLock)
            {
                mintNextReturnCode = returnCode;
            }
        }

        public void SetOpenReturnCode(int returnCode)
        {
            lock (mobjLock)
            {
                mintOpenReturnCode = returnCode;
            }
        }

        public override int Open(short channelNo, out int path)
        {
            lock (mobjLock)
            {
                RegisterThreadAccess();
                mintOpenCount++;
                path = TestPath;
                if (channelNo != 51)
                {
                    return -10;
                }
                int returnCode = mintOpenReturnCode;
                mintOpenReturnCode = 0;
                return returnCode;
            }
        }

        public override int Close(int path)
        {
            lock (mobjLock)
            {
                RegisterThreadAccess();
                mintCloseCount++;
                int returnCode = path == TestPath ? TakeNextReturnCode() : -11;
                mintOwnerThreadId = 0;
                return returnCode;
            }
        }

        public override int SendEx(
            int path,
            int networkNo,
            int stationNo,
            int deviceType,
            int deviceNo,
            ref int size,
            short[] data)
        {
            lock (mobjLock)
            {
                RegisterThreadAccess();
                mintSendCount++;
                int returnCode = ValidateCall(path, networkNo, stationNo, size, data.Length);
                if (returnCode != 0)
                {
                    return returnCode;
                }

                returnCode = TakeNextReturnCode();
                if (returnCode != 0)
                {
                    return returnCode;
                }

                int wordCount = size / sizeof(short);
                for (int index = 0; index < wordCount; index++)
                {
                    short value = data[index];
                    mobjWords[CreateWordKey(deviceType, deviceNo + index)] = value;
                    if (mblnEchoReadback)
                    {
                        int readbackNumber = ResolveReadbackNumber(deviceType, deviceNo + index);
                        mobjWords[CreateWordKey(deviceType, readbackNumber)] = value;
                    }
                }
                return 0;
            }
        }

        public override int ReceiveEx(
            int path,
            int networkNo,
            int stationNo,
            int deviceType,
            int deviceNo,
            ref int size,
            short[] data)
        {
            lock (mobjLock)
            {
                RegisterThreadAccess();
                mintReceiveCount++;
                int returnCode = ValidateCall(path, networkNo, stationNo, size, data.Length);
                if (returnCode != 0)
                {
                    return returnCode;
                }

                returnCode = TakeNextReturnCode();
                if (returnCode != 0)
                {
                    return returnCode;
                }

                int wordCount = size / sizeof(short);
                for (int index = 0; index < wordCount; index++)
                {
                    mobjWords.TryGetValue(
                        CreateWordKey(deviceType, deviceNo + index),
                        out short value);
                    data[index] = value;
                }
                return 0;
            }
        }

        private void RegisterThreadAccess()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            if (mintOwnerThreadId == 0)
            {
                mintOwnerThreadId = currentThreadId;
                return;
            }

            if (mintOwnerThreadId != currentThreadId)
            {
                mblnThreadViolation = true;
            }
        }

        private static int ValidateCall(
            int path,
            int networkNo,
            int stationNo,
            int size,
            int dataLength)
        {
            if (path != TestPath ||
                networkNo != 1 ||
                stationNo != 1 ||
                size <= 0 ||
                size % sizeof(short) != 0 ||
                dataLength < size / sizeof(short))
            {
                return -12;
            }
            return 0;
        }

        private int TakeNextReturnCode()
        {
            int returnCode = mintNextReturnCode;
            mintNextReturnCode = 0;
            return returnCode;
        }

        private static int ResolveReadbackNumber(int deviceType, int writeNumber)
        {
            if (deviceType == 13 && writeNumber >= 100 && writeNumber < 200)
            {
                return writeNumber + 100;
            }

            if (deviceType == 24 && writeNumber >= 0x100 && writeNumber < 0x200)
            {
                return writeNumber + 0x100;
            }

            return writeNumber;
        }

        private static string CreateWordKey(int deviceType, int deviceNumber)
        {
            return deviceType.ToString(CultureInfo.InvariantCulture) + ":" +
                deviceNumber.ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed class CRegressionSettingFile : CSettingFileBase
    {
        public override IReadOnlyList<ST_SYSTEM_PARAMETER> Load(
            EN_SETTING_TAB section,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<ST_SYSTEM_PARAMETER>();
        }

        public override void Save(
            EN_SETTING_TAB section,
            IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        public override IReadOnlyList<ST_SETTING_HISTORY> LoadHistory(
            EN_SETTING_TAB section,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<ST_SETTING_HISTORY>();
        }
    }

    private sealed class CRegressionInterfaceFile : CInterfaceFileBase
    {
        public override IReadOnlyList<ST_INTERFACE_DATA> LoadAll(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<ST_INTERFACE_DATA>();
        }

        public override void SaveAll(
            IReadOnlyList<ST_INTERFACE_DATA> interfaces,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class CRegressionReviewResultFile : CReviewResultFileBase
    {
        public int SaveCount { get; private set; }

        public override string RootPath
        {
            get
            {
                return Path.GetTempPath();
            }
        }

        public override ST_REVIEW_RESULT_FILE_DATA Load(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ST_REVIEW_RESULT_FILE_DATA(
                path,
                Path.GetFileName(path),
                "",
                FixedTime,
                Array.Empty<ST_REVIEW_RESULT_FILE_ROW>());
        }

        public override void Save(
            ST_REVIEW_RESULT_DATA result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
        }
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
