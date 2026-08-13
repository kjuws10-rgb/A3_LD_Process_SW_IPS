using Drilling.Common.Log;
using System.IO;
using System.Text;
using Drilling.Common.Managers;
using Drilling.File.JHMI;
using Drilling.File.Product;
using Drilling.File.ReviewResult;
using Drilling.File.Script;
using Drilling.Common.Threading;

namespace Drilling.UI;

public static class CAppStartup
{
    private static readonly object mobjInitializationLock = new object();
    private static CManagerInitializationThread? mobjInitializationThread;

    public static CRootView CreateMainViewModel()
    {
        var configRoot = GetConfigRoot();
        var settingFile = new CSettingFile(configRoot);
        var automationScriptFile = new CAutomation1ScriptFile(GetScriptDirectory(configRoot, settingFile));
        var manager = new CManager(
            configRoot,
            new CJhmiRecipeFile(configRoot),
            settingFile,
            new CManualScanFile(configRoot),
            new CInterfaceFile(configRoot),
            new CBETFile(configRoot),
            new CPowerMeterFile(configRoot),
            new CMotorFile(configRoot),
            new CIoFile(configRoot),
            new CMelsecMapFile(configRoot),
            new CProductFile(configRoot),
            new CReviewResultFile(configRoot),
            new CReviewRuleFile(configRoot),
            new CLogManager(configRoot),
            automationScriptFile,
            configStructureFile: new CConfigStructureFile(configRoot));

        var lastLoggedStartupOrder = WriteManagerStartupStatus(
            manager,
            "MANAGER_STARTUP_SEQUENCE",
            0);

        var initializationThread = new CManagerInitializationThread(
            manager,
            lastLoggedStartupOrder);
        lock (mobjInitializationLock)
        {
            mobjInitializationThread = initializationThread;
        }
        initializationThread.Start();

        return new CRootView(
            manager,
            manager.Station(),
            manager.Interface(),
            manager.Motion(),
            manager.Alarm(),
            manager.InterLock(),
            manager.ManualScanFile(),
            manager.Recipe(),
            manager.Setting(),
            manager.Product(),
            manager.Review(),
            manager.ReviewRuleFile(),
            automationScriptFile);
    }

    public static void StopInitialization()
    {
        CManagerInitializationThread? initializationThread;
        lock (mobjInitializationLock)
        {
            initializationThread = mobjInitializationThread;
            mobjInitializationThread = null;
        }

        if (initializationThread is null)
        {
            return;
        }

        initializationThread.Cancel();
        initializationThread.Stop();
        initializationThread.DisposeCancellationSource();
    }

    private static int WriteManagerStartupStatus(
        CManager manager,
        string title,
        int afterOrder)
    {
        var status = manager.ConfigStatus();
        bool FilterStep2(ST_MANAGER_STARTUP_STEP step)
        {
            return step.Order > afterOrder;
        }

        int GetStepSortKey3(ST_MANAGER_STARTUP_STEP step)
        {
            return step.Order;
        }

        var steps = status.StartupSteps
            .Where(FilterStep2)
            .OrderBy(GetStepSortKey3)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine($"ConfigRoot={status.ConfigRoot}");
        builder.AppendLine($"InterfaceCount={status.InterfaceCount}");
        builder.AppendLine($"MotorCount={status.MotorCount}");
        builder.AppendLine($"IoCount={status.IoCount}");
        builder.AppendLine($"MelsecMapCount={status.MelsecMapCount}");
        builder.AppendLine($"ActiveProductLoaded={status.ActiveProductLoaded}");

        if (steps.Length == 0)
        {
            builder.AppendLine("No additional manager startup step.");
        }
        else
        {
            foreach (var step in steps)
            {
                builder.AppendLine($"{step.Order:00} | {step.Result} | {step.StepName} | {step.Message}");
            }
        }

        if (status.StartupMessages.Count > 0)
        {
            builder.AppendLine("StartupMessages:");

            foreach (var message in status.StartupMessages)
            {
                builder.AppendLine($"- {message}");
            }
        }

        CProgramOpenLog.Write(title, builder.ToString().TrimEnd());
        int MaxStepCallback4(ST_MANAGER_STARTUP_STEP step)
        {
            return step.Order;
        }

        return status.StartupSteps.Count == 0
            ? afterOrder
            : status.StartupSteps.Max(MaxStepCallback4);
    }

    private static string GetConfigRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "Drilling.sln")))
            {
                return Path.Combine(directory.FullName, "Config");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, "Config");
    }

    private static string GetScriptDirectory(
        string configRoot,
        CSettingFile settingFile)
    {
        var projectRoot = Directory.GetParent(configRoot)?.FullName ?? configRoot;
        bool MatchParameter5(ST_SYSTEM_PARAMETER parameter)
        {
            return parameter.Key.Equals("LocalScriptPath", StringComparison.OrdinalIgnoreCase) ||
                            parameter.Name.Equals("LocalScriptPath", StringComparison.OrdinalIgnoreCase);
        }

        var scriptPath = settingFile
            .Load(EN_SETTING_TAB.Option)
            .FirstOrDefault(MatchParameter5)
            ?.Value;

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            scriptPath = Path.Combine("Data", "Script");
        }

        return Path.IsPathRooted(scriptPath)
            ? Path.GetFullPath(scriptPath)
            : Path.GetFullPath(Path.Combine(projectRoot, scriptPath));
    }

    private sealed class CManagerInitializationThread : CtrlThread
    {
        private readonly CManager mobjManager;
        private readonly int mintLastLoggedStartupOrder;
        private readonly CancellationTokenSource mobjCancellationSource = new CancellationTokenSource();
        private int mintExecuted;

        public CManagerInitializationThread(
            CManager manager,
            int lastLoggedStartupOrder)
        {
            mobjManager = manager;
            mintLastLoggedStartupOrder = lastLoggedStartupOrder;
        }

        public void Start()
        {
            base.Start(1, "ManagerInitialization");
        }

        public void Cancel()
        {
            mobjCancellationSource.Cancel();
        }

        public void DisposeCancellationSource()
        {
            mobjCancellationSource.Dispose();
        }

        public override void Run()
        {
            if (Interlocked.Exchange(ref mintExecuted, 1) != 0)
            {
                Pause();
                return;
            }

            try
            {
                mobjManager.Initialize(mobjCancellationSource.Token);
                WriteManagerStartupStatus(
                    mobjManager,
                    "MANAGER_INITIALIZE_SEQUENCE",
                    mintLastLoggedStartupOrder);
            }
            catch (OperationCanceledException) when (mobjCancellationSource.IsCancellationRequested)
            {
                CProgramOpenLog.Write(
                    "MANAGER_INITIALIZE_CANCELED",
                    "Manager initialization was canceled during program shutdown.");
            }
            catch (Exception exception)
            {
                CProgramOpenLog.Write("MANAGER_INITIALIZE_FAILED", exception);
                WriteManagerStartupStatus(
                    mobjManager,
                    "MANAGER_INITIALIZE_SEQUENCE",
                    mintLastLoggedStartupOrder);
            }
            finally
            {
                Stop();
            }
        }
    }
}
