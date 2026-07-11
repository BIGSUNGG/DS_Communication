using System.Net;
using System.Net.Sockets;

namespace Communication.Network.TCP.Server;

public sealed class TCPListener
{
    private readonly TcpListener _listener;

    public TCPListener(IPAddress ipAddress, int port)
    {
        _listener = new TcpListener(ipAddress, port);
    }

    public void Start() => _listener.Start();

    public void Stop() => _listener.Stop();

    public async Task ListenAsync(Func<TcpClient, Task> onClientAccepted, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Task<TcpClient> acceptTask;
            try
            {
                acceptTask = _listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            Task cancelTask;
            try
            {
                cancelTask = Task.Delay(Timeout.Infinite, token);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var completed = await Task.WhenAny(acceptTask, cancelTask).ConfigureAwait(false);
            if (completed == cancelTask || token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var client = await acceptTask.ConfigureAwait(false);
                _ = onClientAccepted(client);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
