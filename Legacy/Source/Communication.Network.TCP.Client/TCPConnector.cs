using System.Net;
using System.Net.Sockets;

namespace Communication.Network.TCP.Client;

public sealed class TCPConnector
{
    private readonly string _host;
    private readonly int _port;

    public TCPConnector(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> ConnectAsync(Func<TcpClient, Task> onConnected, CancellationToken cancellationToken = default)
    {
        try
        {
            var tcpClient = new TcpClient();
            using (cancellationToken.Register(() => { try { tcpClient.Close(); } catch { } }))
            {
                await tcpClient.ConnectAsync(_host, _port).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcpClient.Close();
                return false;
            }

            await onConnected(tcpClient).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            // 연결 실패
            return false;
        }
    }
}

