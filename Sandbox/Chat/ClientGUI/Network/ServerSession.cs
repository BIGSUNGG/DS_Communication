using Communication.Shared.Messages;
using Communication.Shared.Session;
using System;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Client;

public sealed class ServerSession : TCPSession
{
    public ServerSession(TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(tcpClient, receiverCreater, senderCreater) 
    {
    }

    protected override void OnDisconnected()
    {
        Application.OpenForms[0]?.Invoke(new Action(() =>
        {
            MessageBox.Show("서버와의 연결이 종료되었습니다.", "연결 종료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
        }));
    }
}

