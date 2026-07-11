namespace ClientGUI
{
    partial class ChatForm
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
            chatTextBox = new TextBox();
            InputTextBox = new TextBox();
            SendButton = new Button();
            SuspendLayout();
            // 
            // chatTextBox
            // 
            chatTextBox.Location = new Point(21, 24);
            chatTextBox.Margin = new Padding(5, 6, 5, 6);
            chatTextBox.Multiline = true;
            chatTextBox.Name = "chatTextBox";
            chatTextBox.ReadOnly = true;
            chatTextBox.ScrollBars = ScrollBars.Vertical;
            chatTextBox.Size = new Size(1327, 716);
            chatTextBox.TabIndex = 0;
            // 
            // InputTextBox
            // 
            InputTextBox.Location = new Point(21, 770);
            InputTextBox.Margin = new Padding(5, 6, 5, 6);
            InputTextBox.Name = "InputTextBox";
            InputTextBox.Size = new Size(1156, 35);
            InputTextBox.TabIndex = 1;
            InputTextBox.KeyDown += InputTextBox_KeyDown;
            // 
            // SendButton
            // 
            SendButton.Location = new Point(1190, 766);
            SendButton.Margin = new Padding(5, 6, 5, 6);
            SendButton.Name = "SendButton";
            SendButton.Size = new Size(161, 54);
            SendButton.TabIndex = 2;
            SendButton.Text = "전송";
            SendButton.UseVisualStyleBackColor = true;
            SendButton.Click += SendButton_Click;
            // 
            // ChatForm
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1371, 900);
            Controls.Add(SendButton);
            Controls.Add(InputTextBox);
            Controls.Add(chatTextBox);
            Margin = new Padding(5, 6, 5, 6);
            Name = "ChatForm";
            Text = "채팅";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox chatTextBox;
        private System.Windows.Forms.TextBox InputTextBox;
        private System.Windows.Forms.Button SendButton;
    }
}