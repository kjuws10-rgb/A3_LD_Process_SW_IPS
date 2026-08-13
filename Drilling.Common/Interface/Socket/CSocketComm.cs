using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Interface;

[CCommType("SocketClient")]
[CCommType("ModbusTcp")]
internal sealed class CSocketComm(
    ST_INTERFACE_DATA data,
    ST_INTERFACE_CONNECT_OPTION option) : CCommBase(data, option)
{
    private TcpClient? _client;

    protected override void ConnectCore(CancellationToken cancellationToken)
    {
        DisconnectSocket();

        if (string.IsNullOrWhiteSpace(Option.RemoteAddress) || Option.Port <= 0)
        {
            SetError("Socket endpoint is invalid.");
            return;
        }

        var retryCount = Math.Max(1, Option.RetryCount);

        for (var tryNo = 0; tryNo < retryCount; tryNo++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                TcpClient client = ConnectSocket(cancellationToken);

                _client = client;
                LastError = "";
                SetState(EN_COMM_STATE.Online);
                return;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException or ArgumentException)
            {
                SetError(ex);
            }
        }
    }

    protected override void DisconnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisconnectSocket();
        SetState(EN_COMM_STATE.Offline);
    }

    protected override string ExecuteCore(
        string function,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_client is null || !_client.Connected)
            {
                ConnectCore(cancellationToken);
            }

            if (_client is null || !_client.Connected)
            {
                return "";
            }

            var stream = _client.GetStream();
            var sendBytes = Encoding.UTF8.GetBytes(function);
            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(sendBytes, 0, sendBytes.Length);
            stream.Flush();

            LastSent = function;
            LastReceived = "";
            LastError = "";
            LastReceived = ReadResponse(stream, cancellationToken);

            if (string.IsNullOrWhiteSpace(LastReceived))
            {
                SetError(string.IsNullOrWhiteSpace(LastError)
                    ? "Socket response timeout."
                    : LastError);
                return "";
            }

            SetState(EN_COMM_STATE.Online);
            return LastReceived;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            DisconnectSocket();
            SetError(ex);
            return "";
        }
    }

    private void DisconnectSocket()
    {
        _client?.Dispose();
        _client = null;
    }

    private string ReadResponse(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int readCount = stream.Read(buffer, 0, buffer.Length);

            if (readCount == 0)
            {
                throw new IOException("Socket closed by remote.");
            }

            return Encoding.UTF8.GetString(buffer, 0, readCount);
        }
        catch (IOException exception) when (
            exception.InnerException is SocketException socketException &&
            socketException.SocketErrorCode == SocketError.TimedOut)
        {
            LastError = "Socket response timeout.";
            return "";
        }
    }

    private TcpClient ConnectSocket(CancellationToken cancellationToken)
    {
        IPAddress address = ResolveRemoteAddress();
        TcpClient client = new TcpClient(address.AddressFamily);
        Socket socket = client.Client;
        int timeoutMsec = Math.Max(100, Option.TimeoutMs);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsec);
        socket.Blocking = false;

        try
        {
            try
            {
                socket.Connect(new IPEndPoint(address, Option.Port));
            }
            catch (SocketException exception) when (
                exception.SocketErrorCode == SocketError.WouldBlock ||
                exception.SocketErrorCode == SocketError.InProgress ||
                exception.SocketErrorCode == SocketError.AlreadyInProgress)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Socket connection is pending: " + exception.SocketErrorCode);
            }

            while (!socket.Connected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Socket connection timeout.");
                }

                if (socket.Poll(10_000, SelectMode.SelectError))
                {
                    object? socketError = socket.GetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.Error);
                    int errorCode = socketError is int value ? value : 0;
                    throw new SocketException(errorCode);
                }

                socket.Poll(10_000, SelectMode.SelectWrite);
            }

            socket.Blocking = true;
            client.ReceiveTimeout = timeoutMsec;
            client.SendTimeout = timeoutMsec;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private IPAddress ResolveRemoteAddress()
    {
        if (IPAddress.TryParse(Option.RemoteAddress, out IPAddress? address))
        {
            return address;
        }

        IPAddress[] addresses = Dns.GetHostAddresses(Option.RemoteAddress);
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        return addresses[0];
    }
}
