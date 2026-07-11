namespace ClientGUI
{
    partial class LoginForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            UserIdLabel = new Label();
            PasswordLabel = new Label();
            UserIdTextBox = new TextBox();
            PasswordTextBox = new TextBox();
            LoginButton = new Button();
            RegisterButton = new Button();
            SuspendLayout();
            // 
            // UserIdLabel
            // 
            UserIdLabel.AutoSize = true;
            UserIdLabel.Font = new Font("맑은 고딕", 12F, FontStyle.Regular, GraphicsUnit.Point, 129);
            UserIdLabel.Location = new Point(50, 50);
            UserIdLabel.Name = "UserIdLabel";
            UserIdLabel.Size = new Size(107, 38);
            UserIdLabel.TabIndex = 0;
            UserIdLabel.Text = "아이디:";
            // 
            // PasswordLabel
            // 
            PasswordLabel.AutoSize = true;
            PasswordLabel.Font = new Font("맑은 고딕", 12F, FontStyle.Regular, GraphicsUnit.Point, 129);
            PasswordLabel.Location = new Point(50, 120);
            PasswordLabel.Name = "PasswordLabel";
            PasswordLabel.Size = new Size(135, 38);
            PasswordLabel.TabIndex = 1;
            PasswordLabel.Text = "비밀번호:";
            // 
            // UserIdTextBox
            // 
            UserIdTextBox.Font = new Font("맑은 고딕", 12F, FontStyle.Regular, GraphicsUnit.Point, 129);
            UserIdTextBox.Location = new Point(185, 47);
            UserIdTextBox.Name = "UserIdTextBox";
            UserIdTextBox.Size = new Size(300, 45);
            UserIdTextBox.TabIndex = 2;
            // 
            // PasswordTextBox
            // 
            PasswordTextBox.Font = new Font("맑은 고딕", 12F, FontStyle.Regular, GraphicsUnit.Point, 129);
            PasswordTextBox.Location = new Point(185, 117);
            PasswordTextBox.Name = "PasswordTextBox";
            PasswordTextBox.PasswordChar = '*';
            PasswordTextBox.Size = new Size(300, 45);
            PasswordTextBox.TabIndex = 3;
            PasswordTextBox.KeyDown += PasswordTextBox_KeyDown;
            // 
            // LoginButton
            // 
            LoginButton.Font = new Font("맑은 고딕", 12F, FontStyle.Bold, GraphicsUnit.Point, 129);
            LoginButton.Location = new Point(185, 200);
            LoginButton.Name = "LoginButton";
            LoginButton.Size = new Size(140, 50);
            LoginButton.TabIndex = 4;
            LoginButton.Text = "로그인";
            LoginButton.UseVisualStyleBackColor = true;
            LoginButton.Click += LoginButton_Click;
            // 
            // RegisterButton
            // 
            RegisterButton.Font = new Font("맑은 고딕", 12F, FontStyle.Bold, GraphicsUnit.Point, 129);
            RegisterButton.Location = new Point(345, 200);
            RegisterButton.Name = "RegisterButton";
            RegisterButton.Size = new Size(140, 50);
            RegisterButton.TabIndex = 5;
            RegisterButton.Text = "회원가입";
            RegisterButton.UseVisualStyleBackColor = true;
            RegisterButton.Click += RegisterButton_Click;
            // 
            // LoginForm
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(566, 300);
            Controls.Add(RegisterButton);
            Controls.Add(LoginButton);
            Controls.Add(PasswordTextBox);
            Controls.Add(UserIdTextBox);
            Controls.Add(PasswordLabel);
            Controls.Add(UserIdLabel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LoginForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "로그인";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label UserIdLabel;
        private Label PasswordLabel;
        private TextBox UserIdTextBox;
        private TextBox PasswordTextBox;
        private Button LoginButton;
        private Button RegisterButton;
    }
}

