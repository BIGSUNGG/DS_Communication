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
            var acceptTask = _listener.AcceptTcpClientAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, token);
            var completed = await Task.WhenAny(acceptTask, cancelTask).ConfigureAwait(false);
            if (completed == cancelTask)
            {
                break;
            }

            var client = await acceptTask.ConfigureAwait(false);
            _ = Task.Run(() => onClientAccepted(client), token);
        }
    }
}

