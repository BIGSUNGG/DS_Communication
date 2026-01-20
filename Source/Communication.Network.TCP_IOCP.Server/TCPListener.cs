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
            try
            {
                var acceptEventArgs = new SocketAsyncEventArgs();
                acceptEventArgs.Completed += (sender, e) => OnAcceptCompleted(e, onClientAccepted, token);

                if (!_listenerSocket.AcceptAsync(acceptEventArgs))
                {
                    // 동기적으로 완료된 경우
                    await ProcessAccept(acceptEventArgs, onClientAccepted, token);
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
        }
    }

    private void OnAcceptCompleted(SocketAsyncEventArgs e, Func<Socket, Task> onClientAccepted, CancellationToken token)
    {
        if (e.SocketError == SocketError.Success)
        {
            _ = Task.Run(async () => await ProcessAccept(e, onClientAccepted, token));
        }
        else
        {
            e.Dispose();
        }
    }

    private async Task ProcessAccept(SocketAsyncEventArgs e, Func<Socket, Task> onClientAccepted, CancellationToken token)
    {
        try
        {
            var clientSocket = e.AcceptSocket;
            e.AcceptSocket = null;

            if (clientSocket != null && !token.IsCancellationRequested)
            {
                await onClientAccepted(clientSocket);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing accept: {ex.Message}");
        }
        finally
        {
            e.Dispose();
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
