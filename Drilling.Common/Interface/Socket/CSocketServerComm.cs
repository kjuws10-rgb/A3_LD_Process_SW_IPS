using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Drilling.Common.Interface;

[CCommType("SocketServer")]
internal sealed class CSocketServerComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private const int MaxMessageLength = 8192;

    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _acceptTask;
    private int _clientSequence;

    public event Func<ST_COMM_RECEIVED_MESSAGE, CancellationToken, Task<string>>? MessageReceived;

    public override async Task Connect(CancellationToken cancellationToken = default)
    {
        await _serverLock.WaitAsync(cancellationToken);

        try
        {
            if (_listener is not null &&
                _serverCts is not null &&
                !_serverCts.IsCancellationRequested)
            {
                LastError = "";
                SetState(EN_COMM_STATE.Online);
                return;
            }

            await StopServer();

            if (Option.Port <= 0)
            {
                SetError("Socket server port is invalid.");
                return;
            }

            var bindAddress = ResolveBindAddress(Option.LocalAddress);
            var listener = new TcpListener(bindAddress, Option.Port);
            listener.Start();

            _listener = listener;
            _serverCts = new CancellationTokenSource();
            Task? RunTask1()
            {
                return AcceptLoop(listener, _serverCts.Token);
            }

            _acceptTask = Task.Run(
RunTask1,
                CancellationToken.None);

            LastSent = "";
            LastReceived = "";
            LastError = "";
            SetState(EN_COMM_STATE.Online);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or InvalidOperationException)
        {
            await StopServer();
            SetError(ex);
        }
        finally
        {
            _serverLock.Release();
        }
    }

    public override async Task Disconnect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _serverLock.WaitAsync(cancellationToken);

        try
        {
            await StopServer();
            SetState(EN_COMM_STATE.Offline);
        }
        finally
        {
            _serverLock.Release();
        }
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        if (_listener is null)
        {
            await Connect(cancellationToken);
        }

        if (_listener is null)
        {
            return "";
        }

        var command = function.Trim();

        if (command.Equals("STATUS", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("CLIENTS", StringComparison.OrdinalIgnoreCase))
        {
            LastSent = command;
            LastReceived = $"OK:SOCKET_SERVER:CLIENTS:{_clients.Count}";
            LastError = "";
            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }

        if (command.Equals("CLOSE_CLIENTS", StringComparison.OrdinalIgnoreCase))
        {
            CloseAllClients();
            LastSent = command;
            LastReceived = "OK:SOCKET_SERVER:CLOSE_CLIENTS";
            LastError = "";
            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }

        LastSent = command;
        LastReceived = "";
        SetError($"Socket server command is not supported: {command}");
        return "";
    }

    private async Task AcceptLoop(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);

                if (!IsAllowedRemote(client))
                {
                    client.Close();
                    continue;
                }

                var maxClientCount = Math.Max(1, Option.MaxClientCount);
                if (_clients.Count >= maxClientCount)
                {
                    LastError = $"Socket server max client count reached: {maxClientCount}";
                    LastChangedAt = DateTimeOffset.Now;
                    client.Close();
                    continue;
                }

                client.NoDelay = true;
                var clientId = Interlocked.Increment(ref _clientSequence);
                _clients[clientId] = client;
                LastError = "";
                SetState(EN_COMM_STATE.Online);
                Task? RunTask2()
                {
                    return ReceiveLoop(clientId, client, cancellationToken);
                }

                var receiveTask = Task.Run(
RunTask2,
                    CancellationToken.None);
                _clientTasks[clientId] = receiveTask;
                void HandleValue3(Task task)
                {
                    CleanupClientTask(clientId, task);
                }

                _ = receiveTask.ContinueWith(
HandleValue3,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested ||
                ex is ObjectDisposedException or SocketException or OperationCanceledException)
            {
                client?.Close();

                if (!cancellationToken.IsCancellationRequested)
                {
                    LastError = ex.Message;
                    LastChangedAt = DateTimeOffset.Now;
                }
            }
        }
    }

    private async Task ReceiveLoop(
        int clientId,
        TcpClient client,
        CancellationToken cancellationToken)
    {
        var remoteEndPoint = GetRemoteEndPoint(client);

        try
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 4096,
                    leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var line = await ReadProtocolLineWithTimeout(reader, cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    var message = NormalizeReceivedMessage(line);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    var response = await RaiseMessageReceived(
                        remoteEndPoint,
                        message,
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        var protocolResponse = NormalizeProtocolLine(response);
                        await writer.WriteLineAsync(protocolResponse.AsMemory(), cancellationToken);
                        LastSent = protocolResponse;
                        LastChangedAt = DateTimeOffset.Now;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LastError = ex.Message;
                LastChangedAt = DateTimeOffset.Now;
            }
        }
    }

    private void CleanupClientTask(
        int clientId,
        Task task)
    {
        if (task.IsFaulted)
        {
            LastError = task.Exception?.GetBaseException().Message ?? "Socket server receive task failed.";
            LastChangedAt = DateTimeOffset.Now;
        }

        _clients.TryRemove(clientId, out _);
        _clientTasks.TryRemove(clientId, out _);
    }

    private async Task<string> RaiseMessageReceived(
        string remoteEndPoint,
        string message,
        CancellationToken cancellationToken)
    {
        LastReceived = message;
        LastError = "";
        SetState(EN_COMM_STATE.Online);

        var handler = MessageReceived;

        if (handler is null)
        {
            return "ACK";
        }

        try
        {
            var response = await handler(
                new ST_COMM_RECEIVED_MESSAGE(
                    remoteEndPoint,
                    message,
                    DateTimeOffset.Now),
                cancellationToken);

            return string.IsNullOrWhiteSpace(response) ? "ACK" : response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastChangedAt = DateTimeOffset.Now;
            return $"NAK|{NormalizeProtocolLine(ex.Message)}";
        }
    }

    private async Task StopServer()
    {
        var cts = _serverCts;
        var listener = _listener;
        var acceptTask = _acceptTask;

        _serverCts = null;
        _listener = null;
        _acceptTask = null;

        cts?.Cancel();
        listener?.Stop();
        CloseAllClients();

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
            {
            }
        }

        var clientTasks = _clientTasks.Values.ToArray();
        if (clientTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(clientTasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or IOException or OperationCanceledException)
            {
                if (cts is not null && !cts.IsCancellationRequested)
                {
                    LastError = ex.Message;
                    LastChangedAt = DateTimeOffset.Now;
                }
            }
        }

        _clientTasks.Clear();
        cts?.Dispose();
    }

    private void CloseAllClients()
    {
        foreach (var pair in _clients.ToArray())
        {
            if (_clients.TryRemove(pair.Key, out var client))
            {
                client.Close();
            }
        }
    }

    private bool IsAllowedRemote(TcpClient client)
    {
        var allowedRemote = Option.RemoteAddress.Trim();

        if (string.IsNullOrWhiteSpace(allowedRemote) ||
            allowedRemote.Equals("*", StringComparison.OrdinalIgnoreCase) ||
            allowedRemote.Equals("ANY", StringComparison.OrdinalIgnoreCase) ||
            allowedRemote.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
        {
            return true;
        }

        if (IPAddress.TryParse(allowedRemote, out var allowedAddress))
        {
            return remoteEndPoint.Address.Equals(allowedAddress);
        }

        return remoteEndPoint.Address.ToString().Equals(allowedRemote, StringComparison.OrdinalIgnoreCase);
    }

    private static IPAddress ResolveBindAddress(string localAddress)
    {
        if (string.IsNullOrWhiteSpace(localAddress) ||
            localAddress.Equals("*", StringComparison.OrdinalIgnoreCase) ||
            localAddress.Equals("ANY", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        return IPAddress.TryParse(localAddress, out var address)
            ? address
            : throw new ArgumentException($"Socket server local address is invalid: {localAddress}");
    }

    private static async Task<string?> ReadProtocolLine(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1];
        var message = new StringBuilder();

        while (true)
        {
            var readCount = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);

            if (readCount == 0)
            {
                return message.Length == 0 ? null : message.ToString();
            }

            var ch = buffer[0];

            if (ch == '\n')
            {
                return message.ToString().TrimEnd('\r');
            }

            if (message.Length >= MaxMessageLength)
            {
                throw new InvalidDataException(
                    $"Socket server message exceeded {MaxMessageLength} characters.");
            }

            message.Append(ch);
        }
    }

    private async Task<string?> ReadProtocolLineWithTimeout(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, Option.TimeoutMs)));

        try
        {
            return await ReadProtocolLine(reader, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Socket server receive timeout after {Math.Max(1, Option.TimeoutMs)} ms.");
        }
    }

    private static string GetRemoteEndPoint(TcpClient client)
    {
        return client.Client.RemoteEndPoint?.ToString() ?? "UNKNOWN";
    }

    private static string NormalizeReceivedMessage(string message)
    {
        return message.TrimEnd('\0').Trim();
    }

    private static string NormalizeProtocolLine(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

}
