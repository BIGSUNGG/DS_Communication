using System.Net;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Client;

public sealed class TCPConnector : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private bool _disposed;

    public TCPConnector(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> ConnectAsync(Func<Socket, Task> onConnected, CancellationToken cancellationToken = default)
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connectEventArgs = new SocketAsyncEventArgs();
            var tcs = new TaskCompletionSource<bool>();

            connectEventArgs.RemoteEndPoint = new DnsEndPoint(_host, _port);
            connectEventArgs.Completed += (sender, e) =>
            {
                if (e.SocketError == SocketError.Success)
                {
                    tcs.SetResult(true);
                }
                else
                {
                    tcs.SetResult(false);
                }
            };

            if (socket.ConnectAsync(connectEventArgs))
            {
                // 비동기로 진행 중
                var connected = await tcs.Task;
                if (connected && !cancellationToken.IsCancellationRequested)
                {
                    await onConnected(socket);
                    return true;
                }
                else
                {
                    socket.Dispose();
                    return false;
                }
            }
            else
            {
                // 동기적으로 완료됨
                if (connectEventArgs.SocketError == SocketError.Success && !cancellationToken.IsCancellationRequested)
                {
                    await onConnected(socket);
                    return true;
                }
                else
                {
                    socket.Dispose();
                    return false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
