using System.Diagnostics;
using Drilling.Common.Log;
using Drilling.Common.Threading;

namespace Drilling.UI.Threading;

internal sealed class CStationLogThread : CtrlThread
{
    private readonly CLogManager mobjLogManager;
    private readonly object mobjQueueLock = new object();
    private readonly Queue<CStationLogRequest> mobjQueue = new Queue<CStationLogRequest>();

    public CStationLogThread(CLogManager logManager)
    {
        mobjLogManager = logManager;
    }

    public void Start()
    {
        base.Start(5, "UIStationLog");
    }

    public void Enqueue(
        string stateName,
        string action,
        string detail)
    {
        lock (mobjQueueLock)
        {
            mobjQueue.Enqueue(new CStationLogRequest(stateName, action, detail));
        }
    }

    public void StopAndFlush()
    {
        Stop();

        while (TryGetRequest(out CStationLogRequest? request))
        {
            if (request is not null)
            {
                Write(request);
            }
        }
    }

    public override void Run()
    {
        if (TryGetRequest(out CStationLogRequest? request) && request is not null)
        {
            Write(request);
        }
    }

    private bool TryGetRequest(out CStationLogRequest? request)
    {
        lock (mobjQueueLock)
        {
            if (mobjQueue.Count == 0)
            {
                request = null;
                return false;
            }

            request = mobjQueue.Dequeue();
            return true;
        }
    }

    private void Write(CStationLogRequest request)
    {
        try
        {
            mobjLogManager.WriteStationState(
                "UI",
                request.StateName,
                request.Action,
                request.Detail);
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Station state log failed: " + exception);
        }
    }

    private sealed class CStationLogRequest
    {
        public CStationLogRequest(
            string stateName,
            string action,
            string detail)
        {
            StateName = stateName;
            Action = action;
            Detail = detail;
        }

        public string StateName { get; }
        public string Action { get; }
        public string Detail { get; }
    }
}
