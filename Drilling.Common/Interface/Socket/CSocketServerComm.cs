using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Drilling.Common.Interface;

internal delegate string CCommMessageReceivedHandler(
    ST_COMM_RECEIVED_MESSAGE message,
    CancellationToken cancellationToken);

[CCommType("SocketServer")]
internal sealed class CSocketServerComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private const int MaxMessageLength = 8192;
    private readonly object mobjClientLock = new object();
    private readonly Dictionary<int, CSocketClientSession> mobjClients =
        new Dictionary<int, CSocketClientSession>();
    private TcpListener? mobjListener;
    private int mintClientSequence;

    public event CCommMessageReceivedHandler? MessageReceived;

    protected override void ConnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mobjListener != null)
        {
            LastError = "";
            SetState(EN_COMM_STATE.Online);
            return;
        }

        StopServer();

        try
        {
            if (Option.Port <= 0)
            {
                SetError("Socket server port is invalid.");
                return;
            }

            IPAddress bindAddress = ResolveBindAddress(Option.LocalAddress);
            mobjListener = new TcpListener(bindAddress, Option.Port);
            mobjListener.Start();
            LastSent = "";
            LastReceived = "";
            LastError = "";
            SetState(EN_COMM_STATE.Online);
        }
        catch (Exception exception) when (
            exception is SocketException or ArgumentException or InvalidOperationException)
        {
            StopServer();
            SetError(exception);
        }
    }

    protected override void DisconnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopServer();
        SetState(EN_COMM_STATE.Offline);
    }

    protected override string ExecuteCore(
        string function,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mobjListener == null)
        {
            ConnectCore(cancellationToken);
        }

        if (mobjListener == null)
        {
            return "";
        }

        string command = function.Trim();
        if (command.Equals("STATUS", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("CLIENTS", StringComparison.OrdinalIgnoreCase))
        {
            LastSent = command;
            LastReceived = "OK:SOCKET_SERVER:CLIENTS:" + GetClientCount().ToString();
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
        SetError("Socket server command is not supported: " + command);
        return "";
    }

    protected override void Poll()
    {
        if (mobjListener == null)
        {
            return;
        }

        try
        {
            AcceptOneClient();
            PollClients();
        }
        catch (Exception exception) when (
            exception is SocketException or IOException or ObjectDisposedException)
        {
            LastError = exception.Message;
            LastChangedAt = DateTimeOffset.Now;
        }
    }

    private void AcceptOneClient()
    {
        if (mobjListener == null || !mobjListener.Pending())
        {
            return;
        }

        TcpClient client = mobjListener.AcceptTcpClient();
        if (!IsAllowedRemote(client))
        {
            client.Close();
            return;
        }

        int maxClientCount = Math.Max(1, Option.MaxClientCount);
        if (GetClientCount() >= maxClientCount)
        {
            LastError = "Socket server max client count reached: " + maxClientCount.ToString();
            LastChangedAt = DateTimeOffset.Now;
            client.Close();
            return;
        }

        client.NoDelay = true;
        int clientId = Interlocked.Increment(ref mintClientSequence);
        CSocketClientSession session = new CSocketClientSession(clientId, client);
        lock (mobjClientLock)
        {
            mobjClients.Add(clientId, session);
        }

        LastError = "";
        SetState(EN_COMM_STATE.Online);
    }

    private void PollClients()
    {
        List<CSocketClientSession> clients;
        lock (mobjClientLock)
        {
            clients = new List<CSocketClientSession>(mobjClients.Values);
        }

        foreach (CSocketClientSession client in clients)
        {
            try
            {
                if (IsReceiveTimeout(client))
                {
                    LastError = "Socket server receive timeout after " +
                        Math.Max(1, Option.TimeoutMs).ToString() + " ms.";
                    LastChangedAt = DateTimeOffset.Now;
                    RemoveClient(client.ClientId);
                    continue;
                }

                if (client.Client.Client.Poll(0, SelectMode.SelectRead) &&
                    client.Client.Available == 0)
                {
                    RemoveClient(client.ClientId);
                    continue;
                }

                if (client.Client.Available > 0)
                {
                    ReadClient(client);
                }
            }
            catch (Exception exception) when (
                exception is SocketException or IOException or ObjectDisposedException)
            {
                LastError = exception.Message;
                LastChangedAt = DateTimeOffset.Now;
                RemoveClient(client.ClientId);
            }
        }
    }

    private void ReadClient(CSocketClientSession client)
    {
        int readLength = Math.Min(4096, Math.Max(1, client.Client.Available));
        byte[] buffer = new byte[readLength];
        NetworkStream stream = client.Client.GetStream();
        int readCount = stream.Read(buffer, 0, buffer.Length);
        if (readCount <= 0)
        {
            RemoveClient(client.ClientId);
            return;
        }

        client.LastReceivedAt = DateTimeOffset.Now;
        client.ReceiveBuffer.Append(Encoding.UTF8.GetString(buffer, 0, readCount));
        if (client.ReceiveBuffer.Length > MaxMessageLength)
        {
            throw new InvalidDataException(
                "Socket server message exceeded " + MaxMessageLength.ToString() + " characters.");
        }

        ProcessCompleteLines(client, stream);
    }

    private void ProcessCompleteLines(CSocketClientSession client, NetworkStream stream)
    {
        while (true)
        {
            string buffer = client.ReceiveBuffer.ToString();
            int lineEnd = buffer.IndexOf('\n');
            if (lineEnd < 0)
            {
                return;
            }

            string line = buffer.Substring(0, lineEnd).TrimEnd('\r');
            client.ReceiveBuffer.Remove(0, lineEnd + 1);
            string message = NormalizeReceivedMessage(line);
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            string response = RaiseMessageReceived(client.RemoteEndPoint, message);
            if (string.IsNullOrWhiteSpace(response))
            {
                continue;
            }

            string protocolResponse = NormalizeProtocolLine(response);
            byte[] responseBytes = Encoding.UTF8.GetBytes(protocolResponse + "\n");
            stream.Write(responseBytes, 0, responseBytes.Length);
            stream.Flush();
            LastSent = protocolResponse;
            LastChangedAt = DateTimeOffset.Now;
        }
    }

    private string RaiseMessageReceived(string remoteEndPoint, string message)
    {
        LastReceived = message;
        LastError = "";
        SetState(EN_COMM_STATE.Online);
        CCommMessageReceivedHandler? handler = MessageReceived;
        if (handler == null)
        {
            return "ACK";
        }

        try
        {
            string response = handler(
                new ST_COMM_RECEIVED_MESSAGE(remoteEndPoint, message, DateTimeOffset.Now),
                CancellationToken.None);
            return string.IsNullOrWhiteSpace(response) ? "ACK" : response;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            LastChangedAt = DateTimeOffset.Now;
            return "NAK|" + NormalizeProtocolLine(exception.Message);
        }
    }

    private bool IsReceiveTimeout(CSocketClientSession client)
    {
        int timeoutMsec = Math.Max(1, Option.TimeoutMs);
        return DateTimeOffset.Now - client.LastReceivedAt > TimeSpan.FromMilliseconds(timeoutMsec);
    }

    private void StopServer()
    {
        TcpListener? listener = mobjListener;
        mobjListener = null;
        listener?.Stop();
        CloseAllClients();
    }

    private void CloseAllClients()
    {
        List<CSocketClientSession> clients;
        lock (mobjClientLock)
        {
            clients = new List<CSocketClientSession>(mobjClients.Values);
            mobjClients.Clear();
        }

        foreach (CSocketClientSession client in clients)
        {
            client.Client.Close();
        }
    }

    private void RemoveClient(int clientId)
    {
        CSocketClientSession? client = null;
        lock (mobjClientLock)
        {
            if (mobjClients.TryGetValue(clientId, out client))
            {
                mobjClients.Remove(clientId);
            }
        }

        client?.Client.Close();
    }

    private int GetClientCount()
    {
        lock (mobjClientLock)
        {
            return mobjClients.Count;
        }
    }

    private bool IsAllowedRemote(TcpClient client)
    {
        string allowedRemote = Option.RemoteAddress.Trim();
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

        if (IPAddress.TryParse(allowedRemote, out IPAddress? allowedAddress))
        {
            return remoteEndPoint.Address.Equals(allowedAddress);
        }

        return remoteEndPoint.Address.ToString().Equals(
            allowedRemote,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IPAddress ResolveBindAddress(string localAddress)
    {
        if (string.IsNullOrWhiteSpace(localAddress) ||
            localAddress.Equals("*", StringComparison.OrdinalIgnoreCase) ||
            localAddress.Equals("ANY", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(localAddress, out IPAddress? address))
        {
            return address;
        }

        throw new ArgumentException("Socket server local address is invalid: " + localAddress);
    }

    private static string NormalizeReceivedMessage(string message)
    {
        return message.TrimEnd('\0').Trim();
    }

    private static string NormalizeProtocolLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private sealed class CSocketClientSession
    {
        public CSocketClientSession(int clientId, TcpClient client)
        {
            ClientId = clientId;
            Client = client;
            RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "UNKNOWN";
            LastReceivedAt = DateTimeOffset.Now;
        }

        public int ClientId { get; }

        public TcpClient Client { get; }

        public string RemoteEndPoint { get; }

        public StringBuilder ReceiveBuffer { get; } = new StringBuilder();

        public DateTimeOffset LastReceivedAt { get; set; }
    }
}
