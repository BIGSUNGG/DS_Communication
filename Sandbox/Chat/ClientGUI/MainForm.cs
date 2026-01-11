using Client;
using Client.Network;
using Communication.Network.TCP.Client;
using Communication.Shared.Messages;
using Communication.Shared.Messages.Receiver;
using Communication.Shared.Messages.Sender;
using Communication.Shared.Session;

namespace ClientGUI
{
    public partial class MainForm : UniqueForm
    {
        private readonly string _host;
        private readonly int _port;

        public MainForm(string host, int port)
        {
            _host = host;
            _port = port;

            InitializeComponent();
        }

        private async void ConnectBtn_Click(object sender, EventArgs e)
        {
            using var cts = new CancellationTokenSource();

            var connector = new TCPConnector(_host, _port);
            ConnectBtn.Enabled = false;
            ConnectBtn.Text = "연결 중...";

            var connected = await connector.ConnectAsync(async tcpClient =>
            {
                OnServerConnectSuccess();

                var session = new ServerSession(
                    tcpClient,
                    (Session s) => { return new MessageReceiver(tcpClient.GetStream(), new ServerMessageHandler(s)); },
                    (Session s) => { return new MessageSender(tcpClient.GetStream()); }
                );

                ServerSessionManager.Instance.Session = session;
            }, cts.Token);

            if (!connected)
            {
                OnServerConnectFail();
            }
        }

        private void OnServerConnectSuccess()
        {
            ConnectBtn.Enabled = true;
            ConnectBtn.Text = "연결";

            // 로그인 폼 열기
            var loginForm = new LoginForm(_host, _port);
            loginForm.FormClosed += (s, e) => this.Close();
            loginForm.Show();
            this.Hide();
        }

        private void OnServerConnectFail()
        {
            ConnectBtn.Enabled = true;
            ConnectBtn.Text = "연결";
            MessageBox.Show("서버 연결에 실패하였습니다.", "연결 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
