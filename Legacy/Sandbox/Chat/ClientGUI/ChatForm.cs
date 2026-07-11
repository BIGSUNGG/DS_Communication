using Client.Network;
using Communication.Shared.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClientGUI
{
    public partial class ChatForm : UniqueForm
    {
        public ChatForm()
        {
            InitializeComponent();
        }

        public void OnReceiveChatMessage(S_ChatNotifyMessage chatMessage)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    chatTextBox.AppendText($"{chatMessage.SenderName} : {chatMessage.Content}" + Environment.NewLine);
                }));
            }
            else
            {
                chatTextBox.AppendText($"{chatMessage.SenderName} : {chatMessage.Content}" + Environment.NewLine);
            }
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            await SendChatMessage();
        }

        private async Task SendChatMessage()
        {
            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
                return;

            if (ServerSessionManager.Instance.Session == null)
            {
                MessageBox.Show("서버에 연결되지 않았습니다.", "연결 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            C_ChatSendMessage chatMessage = new(InputTextBox.Text);
            InputTextBox.Clear();
            await ServerSessionManager.Instance.Session.SendAsync(chatMessage);
        }

        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await SendChatMessage();
            }
        }
    }
}
