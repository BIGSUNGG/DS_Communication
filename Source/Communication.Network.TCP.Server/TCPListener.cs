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
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(() => onClientAccepted(client), token);
        }
    }
}

