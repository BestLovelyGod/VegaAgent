namespace Agent.Launcher;

#nullable enable

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(460, 510);
        this.MinimumSize = new System.Drawing.Size(460, 510);
        this.Text = "Vega 织女星";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
        this.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);

        // ── 顶部标题 ──
        var lblTitle = new System.Windows.Forms.Label();
        lblTitle.Text = "Vega 织女星";
        lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 16F, System.Drawing.FontStyle.Bold);
        lblTitle.ForeColor = System.Drawing.Color.FromArgb(30, 30, 30);
        lblTitle.Location = new System.Drawing.Point(30, 15);
        lblTitle.AutoSize = true;

        var lblSub = new System.Windows.Forms.Label();
        lblSub.Text = "Windows 电脑管家 · AI 助手";
        lblSub.ForeColor = System.Drawing.Color.Gray;
        lblSub.Location = new System.Drawing.Point(32, 48);
        lblSub.AutoSize = true;

        // ── 环境状态区 (4 行) ──
        var panelEnv = new System.Windows.Forms.Panel();
        panelEnv.Location = new System.Drawing.Point(30, 80);
        panelEnv.Size = new System.Drawing.Size(400, 110);
        panelEnv.BackColor = System.Drawing.Color.White;
        panelEnv.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

        var lblSdkLabel = new System.Windows.Forms.Label();
        lblSdkLabel.Text = ".NET SDK:";
        lblSdkLabel.Location = new System.Drawing.Point(15, 8);
        lblSdkLabel.AutoSize = true;

        lblSdk = new System.Windows.Forms.Label();
        lblSdk.Text = "检查中...";
        lblSdk.Location = new System.Drawing.Point(110, 8);
        lblSdk.AutoSize = true;
        lblSdk.ForeColor = System.Drawing.Color.Gray;

        var lblWv2Label = new System.Windows.Forms.Label();
        lblWv2Label.Text = "WebView2:";
        lblWv2Label.Location = new System.Drawing.Point(15, 32);
        lblWv2Label.AutoSize = true;

        lblWebView = new System.Windows.Forms.Label();
        lblWebView.Text = "检查中...";
        lblWebView.Location = new System.Drawing.Point(110, 32);
        lblWebView.AutoSize = true;
        lblWebView.ForeColor = System.Drawing.Color.Gray;

        var lblExtLabel = new System.Windows.Forms.Label();
        lblExtLabel.Text = "浏览器插件:";
        lblExtLabel.Location = new System.Drawing.Point(15, 56);
        lblExtLabel.AutoSize = true;

        lblExtension = new System.Windows.Forms.Label();
        lblExtension.Text = "检查中...";
        lblExtension.Location = new System.Drawing.Point(110, 56);
        lblExtension.AutoSize = true;
        lblExtension.ForeColor = System.Drawing.Color.Gray;

        var lblIVegaLabel = new System.Windows.Forms.Label();
        lblIVegaLabel.Text = "IVega 账户:";
        lblIVegaLabel.Location = new System.Drawing.Point(15, 80);
        lblIVegaLabel.AutoSize = true;

        lblIVega = new System.Windows.Forms.Label();
        lblIVega.Text = "检查中...";
        lblIVega.Location = new System.Drawing.Point(110, 80);
        lblIVega.AutoSize = true;
        lblIVega.ForeColor = System.Drawing.Color.Gray;

        panelEnv.Controls.AddRange(new System.Windows.Forms.Control[] { lblSdkLabel, lblSdk, lblWv2Label, lblWebView, lblExtLabel, lblExtension, lblIVegaLabel, lblIVega });

        // ── 安装环境按钮（环境缺失时显示）──
        btnSetup = new System.Windows.Forms.Button();
        btnSetup.Text = "⚙  安装环境";
        btnSetup.Location = new System.Drawing.Point(30, 200);
        btnSetup.Size = new System.Drawing.Size(400, 32);
        btnSetup.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F, System.Drawing.FontStyle.Bold);
        btnSetup.BackColor = System.Drawing.Color.FromArgb(255, 152, 0);
        btnSetup.ForeColor = System.Drawing.Color.White;
        btnSetup.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnSetup.FlatAppearance.BorderSize = 0;
        btnSetup.Cursor = System.Windows.Forms.Cursors.Hand;
        btnSetup.Visible = false;
        btnSetup.Click += new System.EventHandler(this.btnSetup_Click);

        // ── 两个大按钮 ──
        btnService = new System.Windows.Forms.Button();
        btnService.Text = "▶  启动核心服务";
        btnService.Location = new System.Drawing.Point(30, 240);
        btnService.Size = new System.Drawing.Size(400, 65);
        btnService.Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold);
        btnService.BackColor = System.Drawing.Color.FromArgb(0, 120, 212);
        btnService.ForeColor = System.Drawing.Color.White;
        btnService.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnService.FlatAppearance.BorderSize = 0;
        btnService.Cursor = System.Windows.Forms.Cursors.Hand;
        btnService.Enabled = false;
        btnService.Click += new System.EventHandler(this.btnService_Click);

        btnChat = new System.Windows.Forms.Button();
        btnChat.Text = "💬  打开对话界面";
        btnChat.Location = new System.Drawing.Point(30, 315);
        btnChat.Size = new System.Drawing.Size(400, 65);
        btnChat.Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold);
        btnChat.BackColor = System.Drawing.Color.FromArgb(16, 124, 16);
        btnChat.ForeColor = System.Drawing.Color.White;
        btnChat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnChat.FlatAppearance.BorderSize = 0;
        btnChat.Cursor = System.Windows.Forms.Cursors.Hand;
        btnChat.Enabled = false;
        btnChat.Click += new System.EventHandler(this.btnChat_Click);

        // ── 设置按钮 ──
        btnSettings = new System.Windows.Forms.Button();
        btnSettings.Text = "⚙  设置";
        btnSettings.Location = new System.Drawing.Point(30, 390);
        btnSettings.Size = new System.Drawing.Size(195, 32);
        btnSettings.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F);
        btnSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnSettings.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
        btnSettings.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(200, 200, 200);
        btnSettings.Cursor = System.Windows.Forms.Cursors.Hand;
        btnSettings.Click += new System.EventHandler(this.btnSettings_Click);

        // ── 日志区 ──
        txtLog = new System.Windows.Forms.TextBox();
        txtLog.Location = new System.Drawing.Point(30, 430);
        txtLog.Size = new System.Drawing.Size(400, 65);
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        txtLog.Font = new System.Drawing.Font("Consolas", 8F);
        txtLog.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        txtLog.ForeColor = System.Drawing.Color.FromArgb(200, 200, 200);
        txtLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

        // ── 布局 ──
        this.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            lblTitle, lblSub, panelEnv, btnSetup, btnService, btnChat, btnSettings, txtLog
        });
    }

    private System.Windows.Forms.Label lblSdk = null!;
    private System.Windows.Forms.Label lblWebView = null!;
    private System.Windows.Forms.Label lblExtension = null!;
    private System.Windows.Forms.Label lblIVega = null!;
    private System.Windows.Forms.Button btnSetup = null!;
    private System.Windows.Forms.Button btnService = null!;
    private System.Windows.Forms.Button btnChat = null!;
    private System.Windows.Forms.Button btnSettings = null!;
    private System.Windows.Forms.TextBox txtLog = null!;
}
