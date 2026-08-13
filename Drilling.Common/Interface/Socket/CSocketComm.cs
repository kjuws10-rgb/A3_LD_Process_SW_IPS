using System.IO;
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

    public override async Task Connect(CancellationToken cancellationToken = default)
    {
        await DisconnectSocket();

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
                var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Math.Max(100, Option.TimeoutMs));

                var connectTask = Task.Run(
                    () => client.Connect(Option.RemoteAddress, Option.Port),
                    cancellationToken);

                if (await Task.WhenAny(connectTask, Task.Delay(Math.Max(100, Option.TimeoutMs), timeout.Token)) != connectTask)
                {
                    client.Dispose();
                    throw new TimeoutException("Socket connection timeout.");
                }

                await connectTask;

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

    public override async Task Disconnect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectSocket();
        SetState(EN_COMM_STATE.Offline);
    }

    public override async Task<string> Execute(
        string function,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_client is null || !_client.Connected)
            {
                await Connect(cancellationToken);
            }

            if (_client is null || !_client.Connected)
            {
                return "";
            }

            var stream = _client.GetStream();
            var sendBytes = Encoding.UTF8.GetBytes(function);
            await stream.WriteAsync(sendBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            LastSent = function;
            LastReceived = "";
            LastError = "";
            LastReceived = await ReadResponse(stream, cancellationToken);

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
            await DisconnectSocket();
            SetError(ex);
            return "";
        }
    }

    private Task DisconnectSocket()
    {
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    private async Task<string> ReadResponse(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(100, Option.TimeoutMs));

        try
        {
            var readCount = await stream.ReadAsync(buffer, timeout.Token);

            if (readCount == 0)
            {
                throw new IOException("Socket closed by remote.");
            }

            return Encoding.UTF8.GetString(buffer, 0, readCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LastError = "Socket response timeout.";
            return "";
        }
    }
}


