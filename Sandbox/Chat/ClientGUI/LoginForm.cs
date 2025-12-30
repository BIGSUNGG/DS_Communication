using Client.Network;
using DS.Communication.Shared.Messages;
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
    public partial class LoginForm : UniqueForm
    {
        private readonly string _host;
        private readonly int _port;
        private bool _isConnecting = false;

        public LoginForm(string host, int port)
        {
            _host = host;
            _port = port;
            InitializeComponent();
        }

        private async void LoginButton_Click(object sender, EventArgs e)
        {
            if (_isConnecting)
                return;

            string userId = UserIdTextBox.Text.Trim();
            string password = PasswordTextBox.Text;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("아이디와 비밀번호를 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 서버 연결 확인
            if (ServerSessionManager.Instance.Session == null)
            {
                MessageBox.Show("서버에 연결되지 않았습니다. 먼저 서버에 연결해주세요.", "연결 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _isConnecting = true;
            LoginButton.Enabled = false;
            RegisterButton.Enabled = false;

            // 로그인 요청 전송
            C_LoginRequestMessage loginMessage = new C_LoginRequestMessage(userId, password);
            await ServerSessionManager.Instance.Session.SendAsync(loginMessage);
        }

        private async void RegisterButton_Click(object sender, EventArgs e)
        {
            if (_isConnecting)
                return;

            string userId = UserIdTextBox.Text.Trim();
            string password = PasswordTextBox.Text;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("아이디와 비밀번호를 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 서버 연결 확인
            if (ServerSessionManager.Instance.Session == null)
            {
                MessageBox.Show("서버에 연결되지 않았습니다. 먼저 서버에 연결해주세요.", "연결 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _isConnecting = true;
            LoginButton.Enabled = false;
            RegisterButton.Enabled = false;

            // 회원가입 요청 전송
            C_RegisterRequestMessage registerMessage = new C_RegisterRequestMessage(userId, password);
            await ServerSessionManager.Instance.Session.SendAsync(registerMessage);
        }

        public void OnLoginResponse(S_LoginResponseMessage response)
        {
            _isConnecting = false;
            LoginButton.Enabled = true;
            RegisterButton.Enabled = true;

            if (response.IsSuccessful)
            {
                // UI 스레드에서 실행되도록 보장
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => ShowChatForm(response.Message)));
                }
                else
                {
                    ShowChatForm(response.Message);
                }
            }
            else
            {
                MessageBox.Show(response.Message, "로그인 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowChatForm(string message)
        {
            try
            {
                MessageBox.Show(message, "로그인 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                // 채팅 폼 생성 및 표시
                var chatForm = new ChatForm();
                chatForm.FormClosed += (s, e) => this.Close();
                chatForm.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"채팅 폼을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnRegisterResponse(S_RegisterResponseMessage response)
        {
            _isConnecting = false;
            LoginButton.Enabled = true;
            RegisterButton.Enabled = true;

            if (response.IsSuccessful)
            {
                MessageBox.Show(response.Message, "회원가입 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(response.Message, "회원가입 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PasswordTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }
    }
}

