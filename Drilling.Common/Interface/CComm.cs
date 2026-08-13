using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;
using Drilling.Common.Threading;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Drilling.Common.Interface;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class CCommTypeAttribute(string interfaceType, params string[] deviceNames) : Attribute
{
    public string InterfaceType { get; } = NormalizeName(interfaceType);

    public IReadOnlyList<string> DeviceNames { get; } = CreateDeviceNames(deviceNames);

    private static IReadOnlyList<string> CreateDeviceNames(string[] deviceNames)
    {
        List<string> normalizedNames = new List<string>();
        HashSet<string> registeredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string deviceName in deviceNames)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                continue;
            }

            string normalizedName = NormalizeName(deviceName);
            if (registeredNames.Add(normalizedName))
            {
                normalizedNames.Add(normalizedName);
            }
        }

        return normalizedNames.ToArray();
    }

    public static string NormalizeName(string value)
    {
        return value
            .Trim()
            .ToUpperInvariant()
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record ST_COMM_RECEIVED_MESSAGE(
    string RemoteEndPoint,
    string Message,
    DateTimeOffset ReceivedAt);

internal static class CComm
{
    private static readonly IReadOnlyList<CCommRegistration> CommTypes = LoadCommTypes();

    public static CCommBase Create(
        ST_INTERFACE_DATA data,
        ST_INTERFACE_CONNECT_OPTION option)
    {
        var interfaceType = CCommTypeAttribute.NormalizeName(data.InterfaceType.ToString());
        var deviceName = CCommTypeAttribute.NormalizeName(data.Device.ToString());
        bool FilterItem1(CCommRegistration item)
        {
            return item.IsMatch(interfaceType, deviceName);
        }

        int GetItemSortKey2(CCommRegistration item)
        {
            return item.DeviceNames.Count;
        }

        var commType = CommTypes
            .Where(FilterItem1)
            .OrderByDescending(GetItemSortKey2)
            .FirstOrDefault();

        if (commType is null)
        {
            return new CReadyOnlyComm(data, option);
        }

        return Activator.CreateInstance(commType.CommType, data, option) as CCommBase
            ?? throw new InvalidOperationException($"Interface communication creation failed: {data.InterfaceType}/{data.Device}");
    }

    private static IReadOnlyList<CCommRegistration> LoadCommTypes()
    {
        bool FilterType3(Type type)
        {
            return !type.IsAbstract && typeof(CCommBase).IsAssignableFrom(type);
        }

        IEnumerable<CCommRegistration> SelectType4(Type type)
        {
            CCommRegistration SelectAttribute1(CCommTypeAttribute attribute)
            {
                return new CCommRegistration(
                                                attribute.InterfaceType,
                                                attribute.DeviceNames,
                                                type);
            }

            return type.GetCustomAttributes<CCommTypeAttribute>()
                            .Select(SelectAttribute1);
        }

        return typeof(CCommBase)
            .Assembly
            .GetTypes()
            .Where(FilterType3)
            .SelectMany(SelectType4)
            .ToArray();
    }

    private sealed record CCommRegistration(
        string InterfaceType,
        IReadOnlyList<string> DeviceNames,
        Type CommType)
    {
        public bool IsMatch(string interfaceType, string deviceName)
        {
            return InterfaceType.Equals(interfaceType, StringComparison.OrdinalIgnoreCase) &&
                (DeviceNames.Count == 0 || DeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase));
        }
    }
}

internal enum EN_COMM_THREAD_COMMAND
{
    Connect,
    Disconnect,
    Execute
}

internal sealed class CCommThreadRequest
{
    public CCommThreadRequest(
        EN_COMM_THREAD_COMMAND command,
        string function,
        CancellationToken cancellationToken)
    {
        Command = command;
        Function = function;
        CancellationToken = cancellationToken;
    }

    public EN_COMM_THREAD_COMMAND Command { get; }

    public string Function { get; }

    public CancellationToken CancellationToken { get; }

    public ManualResetEvent Completed { get; } = new ManualResetEvent(false);

    public string Result { get; set; } = "";

    public Exception? Error { get; set; }
}

internal abstract class CCommBase(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CtrlThread
{
    protected readonly ST_INTERFACE_DATA Data = data;
    protected readonly ST_INTERFACE_CONNECT_OPTION Option = option;
    private readonly object mobjCommandLock = new object();
    private readonly Queue<CCommThreadRequest> mobjCommandQueue = new Queue<CCommThreadRequest>();

    public EN_COMM_STATE ConnectionState { get; protected set; } = EN_COMM_STATE.Offline;

    public string Endpoint
    {
        get
        {
            return Option.Endpoint;
        }
    }

    public string LastSent { get; protected set; } = "";

    public string LastReceived { get; protected set; } = "";

    public string LastError { get; protected set; } = "";

    public DateTimeOffset? LastChangedAt { get; protected set; }

    public void Connect(CancellationToken cancellationToken = default)
    {
        CCommThreadRequest request = new CCommThreadRequest(
            EN_COMM_THREAD_COMMAND.Connect,
            "",
            cancellationToken);
        ExecuteRequest(request);
    }

    public void Disconnect(CancellationToken cancellationToken = default)
    {
        BeforeQueueDisconnect();
        if (!IsRunning)
        {
            DisconnectCore(cancellationToken);
            SetState(EN_COMM_STATE.Offline);
            return;
        }

        CCommThreadRequest request = new CCommThreadRequest(
            EN_COMM_THREAD_COMMAND.Disconnect,
            "",
            cancellationToken);
        ExecuteRequest(request);
        Stop();
    }

    public string Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        BeforeQueueExecute(function);
        CCommThreadRequest request = new CCommThreadRequest(
            EN_COMM_THREAD_COMMAND.Execute,
            function,
            cancellationToken);
        ExecuteRequest(request);
        return request.Result;
    }

    public override void Run()
    {
        CCommThreadRequest? request = GetCommand();
        if (request == null)
        {
            Poll();
            return;
        }

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            switch (request.Command)
            {
                case EN_COMM_THREAD_COMMAND.Connect:
                    ConnectCore(request.CancellationToken);
                    break;
                case EN_COMM_THREAD_COMMAND.Disconnect:
                    DisconnectCore(request.CancellationToken);
                    SetState(EN_COMM_STATE.Offline);
                    break;
                case EN_COMM_THREAD_COMMAND.Execute:
                    request.Result = ExecuteCore(
                        request.Function,
                        request.CancellationToken);
                    break;
            }
        }
        catch (Exception exception)
        {
            request.Error = exception;
        }
        finally
        {
            request.Completed.Set();
        }
    }

    protected abstract void ConnectCore(CancellationToken cancellationToken);

    protected virtual void DisconnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(EN_COMM_STATE.Offline);
    }

    protected abstract string ExecuteCore(
        string function,
        CancellationToken cancellationToken);

    protected virtual void Poll()
    {
    }

    protected virtual void BeforeQueueDisconnect()
    {
    }

    protected virtual void BeforeQueueExecute(string function)
    {
    }

    protected void SetState(EN_COMM_STATE state)
    {
        ConnectionState = state;
        LastChangedAt = DateTimeOffset.Now;
    }

    protected void SetError(Exception ex)
    {
        LastError = ex.Message;
        SetState(EN_COMM_STATE.Offline);
    }

    protected void SetError(string message)
    {
        LastError = message;
        SetState(EN_COMM_STATE.Offline);
    }

    private void ExecuteRequest(CCommThreadRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        Start(1, "COMM_" + Data.Number.ToString() + "_" + Data.Device.ToString());

        lock (mobjCommandLock)
        {
            mobjCommandQueue.Enqueue(request);
        }

        while (!request.Completed.WaitOne(20))
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning)
            {
                throw new InvalidOperationException(
                    "Communication thread stopped before command completion: " + Data.Device);
            }
        }

        if (request.Error != null)
        {
            ExceptionDispatchInfo.Capture(request.Error).Throw();
        }
    }

    private CCommThreadRequest? GetCommand()
    {
        lock (mobjCommandLock)
        {
            if (mobjCommandQueue.Count == 0)
            {
                return null;
            }

            return mobjCommandQueue.Dequeue();
        }
    }
}

internal sealed class CReadyOnlyComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    protected override void ConnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetError($"Live channel is not ready for {Data.InterfaceType}.");
    }

    protected override string ExecuteCore(
        string function,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSent = function;
        LastReceived = "";
        SetError($"Live command is not ready for {Data.InterfaceType}.");
        return "";
    }
}
