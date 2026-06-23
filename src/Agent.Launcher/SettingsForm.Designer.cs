#nullable enable

namespace Agent.Launcher;

partial class SettingsForm
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(480, 520);
        this.Text = "设置";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        this.SuspendLayout();

        // ══════════════════════════════════════════════
        // LLM 配置区
        // ══════════════════════════════════════════════
        var groupBoxLlm = new System.Windows.Forms.GroupBox();
        groupBoxLlm.Text = "LLM 模型配置";
        groupBoxLlm.Location = new System.Drawing.Point(20, 15);
        groupBoxLlm.Size = new System.Drawing.Size(440, 230);
        groupBoxLlm.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F, System.Drawing.FontStyle.Bold);

        var lblProvider = new System.Windows.Forms.Label();
        lblProvider.Text = "提供商:";
        lblProvider.Location = new System.Drawing.Point(20, 30);
        lblProvider.AutoSize = true;
        lblProvider.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        cmbProvider = new System.Windows.Forms.ComboBox();
        cmbProvider.Location = new System.Drawing.Point(90, 27);
        cmbProvider.Size = new System.Drawing.Size(200, 25);
        cmbProvider.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbProvider.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
        cmbProvider.SelectedIndexChanged += new System.EventHandler(this.cmbProvider_SelectedIndexChanged);

        var lblModel = new System.Windows.Forms.Label();
        lblModel.Text = "模型:";
        lblModel.Location = new System.Drawing.Point(20, 65);
        lblModel.AutoSize = true;
        lblModel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        cmbModel = new System.Windows.Forms.ComboBox();
        cmbModel.Location = new System.Drawing.Point(90, 62);
        cmbModel.Size = new System.Drawing.Size(200, 25);
        cmbModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbModel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        var lblApiKey = new System.Windows.Forms.Label();
        lblApiKey.Text = "API Key:";
        lblApiKey.Location = new System.Drawing.Point(20, 100);
        lblApiKey.AutoSize = true;
        lblApiKey.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        txtApiKey = new System.Windows.Forms.TextBox();
        txtApiKey.Location = new System.Drawing.Point(90, 97);
        txtApiKey.Size = new System.Drawing.Size(280, 25);
        txtApiKey.Font = new System.Drawing.Font("Consolas", 9F);
        txtApiKey.UseSystemPasswordChar = true;

        btnToggleKey = new System.Windows.Forms.Button();
        btnToggleKey.Text = "Show";
        btnToggleKey.Location = new System.Drawing.Point(375, 96);
        btnToggleKey.Size = new System.Drawing.Size(40, 27);
        btnToggleKey.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnToggleKey.FlatAppearance.BorderSize = 0;
        btnToggleKey.Cursor = System.Windows.Forms.Cursors.Hand;
        btnToggleKey.Font = new System.Drawing.Font("Microsoft YaHei UI", 7.5F);
        btnToggleKey.Click += (_, _) =>
        {
            txtApiKey.UseSystemPasswordChar = !txtApiKey.UseSystemPasswordChar;
            btnToggleKey.Text = txtApiKey.UseSystemPasswordChar ? "Show" : "Hide";
        };

        btnTest = new System.Windows.Forms.Button();
        btnTest.Text = "测试连接";
        btnTest.Location = new System.Drawing.Point(90, 135);
        btnTest.Size = new System.Drawing.Size(100, 28);
        btnTest.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnTest.Cursor = System.Windows.Forms.Cursors.Hand;
        btnTest.Click += new System.EventHandler(this.btnTest_Click);

        txtStatus = new System.Windows.Forms.TextBox();
        txtStatus.Location = new System.Drawing.Point(20, 175);
        txtStatus.Size = new System.Drawing.Size(400, 45);
        txtStatus.Multiline = true;
        txtStatus.ReadOnly = true;
        txtStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        txtStatus.Font = new System.Drawing.Font("Consolas", 8F);
        txtStatus.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        txtStatus.ForeColor = System.Drawing.Color.FromArgb(200, 200, 200);
        txtStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

        groupBoxLlm.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            lblProvider, cmbProvider, lblModel, cmbModel,
            lblApiKey, txtApiKey, btnToggleKey, btnTest, txtStatus
        });

        // ══════════════════════════════════════════════
        // 启动选项区
        // ══════════════════════════════════════════════
        var groupBoxStartup = new System.Windows.Forms.GroupBox();
        groupBoxStartup.Text = "启动选项";
        groupBoxStartup.Location = new System.Drawing.Point(20, 260);
        groupBoxStartup.Size = new System.Drawing.Size(440, 120);
        groupBoxStartup.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F, System.Drawing.FontStyle.Bold);

        chkAutoStartHost = new System.Windows.Forms.CheckBox();
        chkAutoStartHost.Text = "自动启动 Agent.Host";
        chkAutoStartHost.Location = new System.Drawing.Point(20, 30);
        chkAutoStartHost.AutoSize = true;
        chkAutoStartHost.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        chkAutoStartGui = new System.Windows.Forms.CheckBox();
        chkAutoStartGui.Text = "自动启动 Agent.GUI";
        chkAutoStartGui.Location = new System.Drawing.Point(20, 58);
        chkAutoStartGui.AutoSize = true;
        chkAutoStartGui.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        chkStartWithWindows = new System.Windows.Forms.CheckBox();
        chkStartWithWindows.Text = "开机自启动";
        chkStartWithWindows.Location = new System.Drawing.Point(20, 86);
        chkStartWithWindows.AutoSize = true;
        chkStartWithWindows.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        groupBoxStartup.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            chkAutoStartHost, chkAutoStartGui, chkStartWithWindows
        });

        // ══════════════════════════════════════════════
        // 界面选项区
        // ══════════════════════════════════════════════
        var groupBoxUI = new System.Windows.Forms.GroupBox();
        groupBoxUI.Text = "界面选项";
        groupBoxUI.Location = new System.Drawing.Point(20, 395);
        groupBoxUI.Size = new System.Drawing.Size(440, 55);
        groupBoxUI.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.5F, System.Drawing.FontStyle.Bold);

        chkMinimizeToTray = new System.Windows.Forms.CheckBox();
        chkMinimizeToTray.Text = "关闭时最小化到系统托盘";
        chkMinimizeToTray.Location = new System.Drawing.Point(20, 25);
        chkMinimizeToTray.AutoSize = true;
        chkMinimizeToTray.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        groupBoxUI.Controls.Add(chkMinimizeToTray);

        // ══════════════════════════════════════════════
        // 底部按钮
        // ══════════════════════════════════════════════
        btnSave = new System.Windows.Forms.Button();
        btnSave.Text = "保存";
        btnSave.Location = new System.Drawing.Point(280, 470);
        btnSave.Size = new System.Drawing.Size(85, 32);
        btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnSave.BackColor = System.Drawing.Color.FromArgb(0, 120, 212);
        btnSave.ForeColor = System.Drawing.Color.White;
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Cursor = System.Windows.Forms.Cursors.Hand;
        btnSave.Click += new System.EventHandler(this.btnSave_Click);

        btnCancel = new System.Windows.Forms.Button();
        btnCancel.Text = "取消";
        btnCancel.Location = new System.Drawing.Point(375, 470);
        btnCancel.Size = new System.Drawing.Size(85, 32);
        btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
        btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

        this.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            groupBoxLlm, groupBoxStartup, groupBoxUI, btnSave, btnCancel
        });

        this.ResumeLayout(false);
    }

    #endregion

    // LLM 配置
    private System.Windows.Forms.ComboBox cmbProvider = null!;
    private System.Windows.Forms.ComboBox cmbModel = null!;
    private System.Windows.Forms.TextBox txtApiKey = null!;
    private System.Windows.Forms.Button btnToggleKey = null!;
    private System.Windows.Forms.Button btnTest = null!;
    private System.Windows.Forms.TextBox txtStatus = null!;

    // 启动选项
    private System.Windows.Forms.CheckBox chkAutoStartHost = null!;
    private System.Windows.Forms.CheckBox chkAutoStartGui = null!;
    private System.Windows.Forms.CheckBox chkMinimizeToTray = null!;
    private System.Windows.Forms.CheckBox chkStartWithWindows = null!;
    private System.Windows.Forms.Button btnSave = null!;
    private System.Windows.Forms.Button btnCancel = null!;
}
