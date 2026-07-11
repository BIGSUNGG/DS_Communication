using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Server;

public sealed class TCPListener : IDisposable
{
    private readonly Socket _listenerSocket;
    private readonly IPEndPoint _endPoint;
    private readonly ConcurrentBag<SocketAsyncEventArgs> _acceptArgsPool = new();
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
            EventHandler<SocketAsyncEventArgs>? completedHandler = null;
            try
            {
                acceptEventArgs = RentAcceptArgs();
                var tcs = new TaskCompletionSource<SocketAsyncEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                completedHandler = (_, e) => tcs.TrySetResult(e);
                acceptEventArgs.Completed += completedHandler;

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
                            break;
                        }
                    }
                }

                if (acceptEventArgs.SocketError != SocketError.Success)
                {
                    if (!_isListening || token.IsCancellationRequested)
                        break;
                    continue;
                }

                var clientSocket = acceptEventArgs.AcceptSocket;
                acceptEventArgs.AcceptSocket = null;

                if (clientSocket != null && !token.IsCancellationRequested)
                {
                    _ = onClientAccepted(clientSocket);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (!_isListening)
                    break;
            }
            finally
            {
                if (acceptEventArgs != null)
                {
                    if (completedHandler != null)
                        acceptEventArgs.Completed -= completedHandler;
                    ReturnAcceptArgs(acceptEventArgs);
                }
            }
        }
    }

    private SocketAsyncEventArgs RentAcceptArgs()
    {
        if (_acceptArgsPool.TryTake(out var args))
        {
            args.AcceptSocket = null;
            return args;
        }

        return new SocketAsyncEventArgs();
    }

    private void ReturnAcceptArgs(SocketAsyncEventArgs args)
    {
        if (_disposed)
        {
            args.Dispose();
            return;
        }

        args.AcceptSocket = null;
        _acceptArgsPool.Add(args);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _listenerSocket?.Dispose();

        while (_acceptArgsPool.TryTake(out var args))
        {
            args.Dispose();
        }
    }
}
