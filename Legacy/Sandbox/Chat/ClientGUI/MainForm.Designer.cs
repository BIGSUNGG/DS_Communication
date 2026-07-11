namespace ClientGUI
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            ConnectBtn = new Button();
            SuspendLayout();
            // 
            // ConnectBtn
            // 
            ConnectBtn.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ConnectBtn.Font = new Font("맑은 고딕", 15.8571434F, FontStyle.Bold, GraphicsUnit.Point, 129);
            ConnectBtn.Location = new Point(280, 175);
            ConnectBtn.Name = "ConnectBtn";
            ConnectBtn.RightToLeft = RightToLeft.No;
            ConnectBtn.Size = new Size(240, 100);
            ConnectBtn.TabIndex = 0;
            ConnectBtn.Text = "연결";
            ConnectBtn.UseVisualStyleBackColor = true;
            ConnectBtn.Click += ConnectBtn_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(ConnectBtn);
            Name = "MainForm";
            Text = "연결";
            ResumeLayout(false);
        }

        #endregion

        private Button ConnectBtn;
    }
}
