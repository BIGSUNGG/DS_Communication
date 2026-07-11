using System.Net;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Server;

public sealed class TCPListener : IDisposable
{
    private readonly Socket _listenerSocket;
    private readonly IPEndPoint _endPoint;
    private bool _isListening;
    private bool _disposed;

    public TCPListener(IPAddress ipAddress, int port)
    {
        _endPoint = new IPEndPoint(ipAddress, port);
        _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public void Start()
    {
        if (_isListening)
            return;

        _listenerSocket.Bind(_endPoint);
        _listenerSocket.Listen(100);
        _isListening = true;
    }

    public void Stop()
    {
        if (!_isListening)
            return;

        _isListening = false;
        _listenerSocket.Close();
    }

    public async Task ListenAsync(Func<Socket, Task> onClientAccepted, CancellationToken token)
    {
        if (!_isListening)
            throw new InvalidOperationException("Listener is not started. Call Start() first.");

        while (!token.IsCancellationRequested && _isListening)
        {
            SocketAsyncEventArgs? acceptEventArgs = null;
            try
            {
                acceptEventArgs = new SocketAsyncEventArgs();
                var tcs = new TaskCompletionSource<SocketAsyncEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                acceptEventArgs.Completed += (_, e) => tcs.TrySetResult(e);

                bool pending = _listenerSocket.AcceptAsync(acceptEventArgs);
                if (pending)
                {
                    using (token.Register(() => tcs.TrySetCanceled(token)))
                    {
                        try
                        {
                            acceptEventArgs = await tcs.Task.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            acceptEventArgs?.Dispose();
                            break;
                        }
                    }
                }

                if (acceptEventArgs.SocketError != SocketError.Success)
                {
                    acceptEventArgs.Dispose();
                    if (!_isListening || token.IsCancellationRequested)
                        break;
                    continue;
                }

                var clientSocket = acceptEventArgs.AcceptSocket;
                acceptEventArgs.AcceptSocket = null;
                acceptEventArgs.Dispose();
                acceptEventArgs = null;

                if (clientSocket != null && !token.IsCancellationRequested)
                {
                    _ = onClientAccepted(clientSocket);
                }
            }
            catch (ObjectDisposedException)
            {
                acceptEventArgs?.Dispose();
                break;
            }
            catch (SocketException)
            {
                acceptEventArgs?.Dispose();
                if (!_isListening)
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _listenerSocket?.Dispose();
    }
}
