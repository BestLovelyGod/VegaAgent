using System;
using System.Drawing;
using System.Windows.Forms;

namespace Agent.Launcher;

/// <summary>
/// 退出确认对话框 — 支持选择是否保留后台服务
/// </summary>
public class ExitDialog : Form
{
    public bool KeepServiceRunning { get; private set; }

    private readonly CheckBox _chkKeepService;

    public ExitDialog()
    {
        Text = "退出";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 130);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var lblMsg = new Label
        {
            Text = "确定退出启动器吗？",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        };

        _chkKeepService = new CheckBox
        {
            Text = "保留核心服务在后台运行",
            Location = new Point(20, 55),
            AutoSize = true,
            Checked = true  // 默认保留服务
        };

        var btnOk = new Button
        {
            Text = "退出",
            DialogResult = DialogResult.OK,
            Location = new Point(140, 90),
            Size = new Size(100, 30),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(255, 90),
            Size = new Size(100, 30),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };

        Controls.AddRange(new Control[] { lblMsg, _chkKeepService, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // 如果服务没在运行，禁用选项
        // （外部设置，构造后调用 InitServiceState）
    }

    /// <summary>
    /// 根据服务状态初始化：服务未运行时禁用选项并取消勾选
    /// </summary>
    public void InitServiceState(bool serviceRunning)
    {
        if (!serviceRunning)
        {
            _chkKeepService.Checked = false;
            _chkKeepService.Enabled = false;
            _chkKeepService.Text = "保留核心服务在后台运行（当前未启动）";
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        KeepServiceRunning = _chkKeepService.Checked;
        base.OnFormClosing(e);
    }
}
