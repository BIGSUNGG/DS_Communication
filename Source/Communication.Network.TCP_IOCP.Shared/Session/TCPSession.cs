using Communication.Shared.Messages;
using Communication.Shared.Session;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Shared.Session;

public abstract class TCPSession : Session
{
    private readonly Socket _socket;

    public TCPSession(Socket socket, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(receiverCreater, senderCreater)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public override bool IsConnected()
    {
        return _socket != null && _socket.Connected;
    }

    protected override void OnDisconnected()
    {
        try
        {
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch { }
        finally
        {
            _socket.Close();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _socket?.Dispose();
    }
}
