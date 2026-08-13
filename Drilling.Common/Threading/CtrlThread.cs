using System.Diagnostics;
using System.Threading;

namespace Drilling.Common.Threading;

public enum EN_CTRL_THREAD_STATE
{
    Stopped,
    Running,
    Paused,
    Stopping,
    Error
}

public class CtrlThread
{
    private readonly object mobjThreadLock = new object();
    private readonly ManualResetEvent mobjPauseEvent = new ManualResetEvent(true);
    private readonly ManualResetEvent mobjStopEvent = new ManualResetEvent(false);
    private Thread? mobjThread;
    private volatile bool mblnStopThread;
    private int mintDelayTime = 1;
    private string mstrThreadName = "";
    private EN_CTRL_THREAD_STATE meThreadState = EN_CTRL_THREAD_STATE.Stopped;
    private Exception? mobjLastError;

    public bool IsRunning
    {
        get
        {
            lock (mobjThreadLock)
            {
                return mobjThread != null && mobjThread.IsAlive;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (mobjThreadLock)
            {
                return meThreadState == EN_CTRL_THREAD_STATE.Paused;
            }
        }
    }

    public EN_CTRL_THREAD_STATE ThreadState
    {
        get
        {
            lock (mobjThreadLock)
            {
                return meThreadState;
            }
        }
    }

    public Exception? LastThreadError
    {
        get
        {
            lock (mobjThreadLock)
            {
                return mobjLastError;
            }
        }
    }

    public void Start(int nDelayTime, string strName)
    {
        lock (mobjThreadLock)
        {
            if (mobjThread != null && mobjThread.IsAlive)
            {
                return;
            }

            mintDelayTime = Math.Max(0, nDelayTime);
            mstrThreadName = string.IsNullOrWhiteSpace(strName)
                ? GetType().Name
                : strName;
            mblnStopThread = false;
            mobjLastError = null;
            mobjStopEvent.Reset();
            mobjPauseEvent.Set();
            meThreadState = EN_CTRL_THREAD_STATE.Running;
            mobjThread = new Thread(new ThreadStart(ThreadProcess));
            mobjThread.IsBackground = true;
            mobjThread.Name = mstrThreadName;
            mobjThread.Start();
        }
    }

    public void Pause()
    {
        lock (mobjThreadLock)
        {
            if (mobjThread == null || !mobjThread.IsAlive || mblnStopThread)
            {
                return;
            }

            mobjPauseEvent.Reset();
            meThreadState = EN_CTRL_THREAD_STATE.Paused;
        }
    }

    public void Resume()
    {
        lock (mobjThreadLock)
        {
            if (mobjThread == null || !mobjThread.IsAlive || mblnStopThread)
            {
                return;
            }

            meThreadState = EN_CTRL_THREAD_STATE.Running;
            mobjPauseEvent.Set();
        }
    }

    public void Stop()
    {
        Thread? objThread;

        lock (mobjThreadLock)
        {
            mblnStopThread = true;
            meThreadState = EN_CTRL_THREAD_STATE.Stopping;
            mobjPauseEvent.Set();
            mobjStopEvent.Set();
            objThread = mobjThread;
        }

        if (objThread != null && objThread != Thread.CurrentThread)
        {
            objThread.Join(3000);
        }

        lock (mobjThreadLock)
        {
            if (mobjThread == null || !mobjThread.IsAlive)
            {
                mobjThread = null;
                meThreadState = EN_CTRL_THREAD_STATE.Stopped;
            }
            else
            {
                meThreadState = EN_CTRL_THREAD_STATE.Error;
                mobjLastError = new TimeoutException(
                    "Thread stop timeout: " + mstrThreadName);
                Debug.WriteLine(mobjLastError.ToString());
            }
        }
    }

    public virtual void Run()
    {
    }

    protected bool IsStopRequested()
    {
        return mblnStopThread;
    }

    protected virtual void OnThreadError(Exception exception)
    {
        Debug.WriteLine(
            "Thread Error : " + mstrThreadName + " / " + exception);
    }

    private void ThreadProcess()
    {
        try
        {
            while (!mblnStopThread)
            {
                mobjPauseEvent.WaitOne();
                if (mblnStopThread)
                {
                    break;
                }

                try
                {
                    Run();
                }
                catch (Exception exception)
                {
                    lock (mobjThreadLock)
                    {
                        mobjLastError = exception;
                    }

                    OnThreadError(exception);
                }

                if (mintDelayTime > 0 && mobjStopEvent.WaitOne(mintDelayTime))
                {
                    break;
                }
            }
        }
        finally
        {
            lock (mobjThreadLock)
            {
                if (mobjThread == Thread.CurrentThread)
                {
                    mobjThread = null;
                }

                meThreadState = EN_CTRL_THREAD_STATE.Stopped;
            }
        }
    }
}
