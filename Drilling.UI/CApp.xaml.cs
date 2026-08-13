using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Drilling.Common.Log;

namespace Drilling.UI;

public partial class CApp : Application
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private readonly DispatcherTimer _monitorFitTimer;
    private bool _isFittingMainWindow;

    public CApp()
    {
        _monitorFitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterExceptionHandlers();

        try
        {
            base.OnStartup(e);

            CProgramOpenLog.Write("PROGRAM_OPEN", "Application startup started.");

            var window = (Window)LoadComponent(new Uri("CRootView.xaml", UriKind.Relative));
            window.DataContext = CAppStartup.CreateMainViewModel();
            ConfigureMainWindowBounds(window);
            MainWindow = window;
            window.Show();

            CProgramOpenLog.Write("PROGRAM_OPEN", "Main window opened.");
        }
        catch (Exception exception)
        {
            CProgramOpenLog.Write("PROGRAM_OPEN_FAILED", exception);
            MessageBox.Show(
                $"Program startup failed.{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}Log: {CProgramOpenLog.LogPath}",
                "Laser Drilling",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CProgramOpenLog.Write("DISPATCHER_UNHANDLED", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            CProgramOpenLog.Write(
                "APPDOMAIN_UNHANDLED",
                exception?.ToString() ?? args.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CProgramOpenLog.Write("TASK_UNOBSERVED", args.Exception);
        };
    }

    private void ConfigureMainWindowBounds(Window window)
    {
        window.ResizeMode = ResizeMode.CanMinimize;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.WindowState = WindowState.Maximized;

        _monitorFitTimer.Tick += (_, _) =>
        {
            _monitorFitTimer.Stop();

            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                ScheduleMainWindowFit(window);
                return;
            }

            FitMainWindowToCurrentMonitor(window);
        };

        window.SourceInitialized += (_, _) => FitMainWindowToCurrentMonitor(window);
        window.LocationChanged += (_, _) => ScheduleMainWindowFit(window);
        window.StateChanged += (_, _) => ScheduleMainWindowFit(window);
    }

    private void ScheduleMainWindowFit(Window window)
    {
        if (_isFittingMainWindow || window.WindowState == WindowState.Minimized)
        {
            return;
        }

        _monitorFitTimer.Stop();
        _monitorFitTimer.Start();
    }

    private void FitMainWindowToCurrentMonitor(Window window)
    {
        if (_isFittingMainWindow || window.WindowState == WindowState.Minimized)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        if (transform is null)
        {
            return;
        }

        var topLeft = transform.Value.Transform(new Point(monitorInfo.rcWork.Left, monitorInfo.rcWork.Top));
        var bottomRight = transform.Value.Transform(new Point(monitorInfo.rcWork.Right, monitorInfo.rcWork.Bottom));

        try
        {
            _isFittingMainWindow = true;
            window.WindowState = WindowState.Normal;
            window.Left = topLeft.X;
            window.Top = topLeft.Y;
            window.Width = Math.Max(1, bottomRight.X - topLeft.X);
            window.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
            window.WindowState = WindowState.Maximized;
        }
        finally
        {
            _isFittingMainWindow = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}


