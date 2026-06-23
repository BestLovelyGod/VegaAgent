using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Agent.Launcher;

public partial class MainForm : Form
{
    private Process? _guiProcess;
    private readonly string _baseDir;
    private const string ServiceName = "VegaAgent";
    private string NssmExe => Path.Combine(_baseDir, "nssm.exe");

    public MainForm()
    {
        InitializeComponent();
        _baseDir = FindBaseDirectory();
        Logger.CleanupOldLogs();
        Logger.Info("Vega 启动器已启动");
        Logger.Info($"工作目录: {_baseDir}");
        DetectEnvironment();
    }


    private string FindBaseDirectory()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            // 开发模式：找到 Agent.slnx
            if (File.Exists(Path.Combine(dir, "Agent.slnx")))
                return dir;
            // 发布模式：找到 tools 目录或 Agent.Host 目录
            if (Directory.Exists(Path.Combine(dir, "tools")) || Directory.Exists(Path.Combine(dir, "Agent.Host")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private string DotnetExe => Path.Combine(_baseDir, "Agent.Host", "sdk", "dotnet", "dotnet.exe");

    private string? FindFile(params string[] candidates)
        => candidates.Select(c => Path.Combine(_baseDir, c)).FirstOrDefault(File.Exists);

    private string? FindHostDll() => FindFile(
        "Agent.Host\\Agent.Host.dll",
        "publish\\release\\Agent.Host\\Agent.Host.dll",
        "src\\Agent.Host\\bin\\Release\\net10.0\\Agent.Host.dll");

    private string? FindGuiExe() => FindFile(
        "Agent.GUI\\Agent.GUI.exe",
        "publish\\release\\Agent.GUI\\Agent.GUI.exe",
        "src\\Agent.GUI\\bin\\Release\\net10.0-windows\\Agent.GUI.exe");

    private string? FindGuiDll() => FindFile(
        "Agent.GUI\\Agent.GUI.dll",
        "publish\\release\\Agent.GUI\\Agent.GUI.dll",
        "src\\Agent.GUI\\bin\\Release\\net10.0-windows\\Agent.GUI.dll");

    private string? FindScript(string name) => FindFile(name, $"publish\\release\\{name}");

    private static void KillProcessesByName(string processName)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { p.Kill(); p.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[Launcher] 终止 {processName} 进程失败: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Launcher] 枚举 {processName} 进程失败: {ex.Message}"); }
    }

    // ── NSSM 服务管理 ─────────────────────────────────────────────────

    private string NssmRun(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = NssmExe,
                Arguments = string.Join(" ", args),
                WorkingDirectory = _baseDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode,
                StandardErrorEncoding = System.Text.Encoding.Unicode,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var stderrTask = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(10000);
            var stderr = stderrTask.Result.Trim();
            if (p.ExitCode != 0)
                Log($"NSSM [{string.Join(" ", args)}] 失败 (exit={p.ExitCode}): {stderr}");
            return stdout;
        }
        catch (Exception ex) { Log($"NSSM 异常: {ex.Message}"); return ""; }
    }

    /// <summary>查询服务状态: "SERVICE_RUNNING", "SERVICE_STOPPED", "SERVICE_PAUSED", ""</summary>
    private string GetServiceStatus() => NssmRun("status", ServiceName);

    /// <summary>服务是否已注册</summary>
    private bool IsServiceRegistered()
    {
        var status = GetServiceStatus();
        return !string.IsNullOrEmpty(status) && !status.Contains("SERVICE_NOT_FOUND") && !status.Contains("Can't open service");
    }

    /// <summary>服务是否正在运行</summary>
    private bool IsServiceRunning() => GetServiceStatus().Contains("RUNNING");

    /// <summary>注册服务 (首次)</summary>
    private bool InstallService()
    {
        var dotnet = DotnetExe;
        var dll = FindHostDll();
        if (!File.Exists(dotnet) || dll == null)
        {
            Log("无法注册服务: SDK 或 Host 程序不存在");
            return false;
        }

        // 确保日志目录存在
        var logsDir = Path.Combine(_baseDir, "data", "logs");
        if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);

        Log("正在注册 Windows 服务...");
        NssmRun("install", ServiceName, $"\"{dotnet}\"", $"\"{dll}\"");
        NssmRun("set", ServiceName, "AppDirectory", $"\"{_baseDir}\"");
        NssmRun("set", ServiceName, "DisplayName", "Vega Agent Host");
        NssmRun("set", ServiceName, "Description", "Ignorant Vega 核心服务");
        NssmRun("set", ServiceName, "Start", "SERVICE_DEMAND_START");  // 手动启动
        NssmRun("set", ServiceName, "AppEnvironmentExtra", "ASPNETCORE_ENVIRONMENT=Production");
        NssmRun("set", ServiceName, "AppStdout", $"\"{_baseDir}\\data\\logs\\host-stdout.log\"");
        NssmRun("set", ServiceName, "AppStderr", $"\"{_baseDir}\\data\\logs\\host-stderr.log\"");
        NssmRun("set", ServiceName, "AppStdoutCreationDisposition", "4");  // 追加
        NssmRun("set", ServiceName, "AppStderrCreationDisposition", "4");

        // 确认注册成功
        if (IsServiceRegistered())
        {
            Log("服务注册成功");
            return true;
        }
        Log("服务注册失败");
        return false;
    }

    /// <summary>卸载服务</summary>
    private void RemoveService()
    {
        NssmRun("stop", ServiceName);
        NssmRun("remove", ServiceName, "confirm");
        Log("服务已卸载");
    }

    // ── 环境检测（只检测，不自动安装）────────────────────────────────

    private bool _sdkOk;
    private bool _wv2Ok;
    private bool _extOk;
    private bool _ivOk;

    private void DetectEnvironment()
    {
        Log("正在检查运行环境...");
        _sdkOk = File.Exists(DotnetExe);
        _wv2Ok = IsWebView2Installed();
        _extOk = IsExtensionInstalled();
        _ivOk = IsIVegaReady();

        SetStatus(lblSdk, _sdkOk);
        SetStatus(lblWebView, _wv2Ok);
        SetStatus(lblExtension, _extOk);
        SetStatus(lblIVega, _ivOk);

        var allOk = _sdkOk && _wv2Ok && _extOk && _ivOk;
        btnSetup.Visible = !allOk;

        if (allOk) Log("环境检查通过，一切就绪");
        else Log("环境缺失，请点击「安装环境」");

        RefreshButtons();
    }

    private async void btnSetup_Click(object? sender, EventArgs e)
    {
        btnSetup.Enabled = false;
        btnSetup.Text = "安装中...";

        if (!_sdkOk)
        {
            Log("正在安装 .NET SDK...");
            await RunScriptAsync("bootstrap-sdk.ps1", "SDK");
            // 以实际文件为准，不依赖脚本退出码
            _sdkOk = File.Exists(DotnetExe);
            SetStatus(lblSdk, _sdkOk);
            if (!_sdkOk) Log("SDK 安装后仍未找到 dotnet.exe");
        }

        if (!_wv2Ok)
        {
            Log("正在安装 WebView2 Runtime...");
            await RunScriptAsync("webview-bootstrap.ps1", "WebView2");
            // 重新检测，不依赖退出码
            _wv2Ok = IsWebView2Installed();
            SetStatus(lblWebView, _wv2Ok);
            if (!_wv2Ok) Log("WebView2 安装后仍未检测到");
        }

        if (!_extOk)
        {
            Log("正在安装浏览器插件...");
            _extOk = await RunScriptAsync("tools\\browser-extension\\install.ps1", "Extension");
            SetStatus(lblExtension, _extOk);
        }

        if (!_ivOk)
        {
            Log("正在创建 IVega 提权账户...");
            _ivOk = await CreateIVegaAsync();
            SetStatus(lblIVega, _ivOk);
        }

        var allOk = _sdkOk && _wv2Ok && _extOk && _ivOk;
        btnSetup.Visible = !allOk;
        btnSetup.Enabled = true;
        btnSetup.Text = "⚙  安装环境";

        if (allOk) Log("环境安装完成");
        else Log("部分环境安装失败，请检查日志");

        RefreshButtons();
    }

    private bool IsWebView2Installed()
    {
        try
        {
            // 1. 注册表: HKLM 64 位原生路径
            using var k0 = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}");
            if (k0 != null) return true;
            // 2. 注册表: HKLM 32 位兼容路径
            using var k1 = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}");
            if (k1 != null) return true;
            // 3. 注册表: HKCU 当前用户
            using var k2 = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}");
            if (k2 != null) return true;
            // 4. 可执行文件检测 (引导安装器异步安装时注册表可能延迟)
            var webView2Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "Microsoft-Edge-WebView", "msedgewebview2.exe");
            if (File.Exists(webView2Exe)) return true;
            // 5. Edge WebView2 安装目录检测
            var webView2Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "EdgeWebView");
            if (Directory.Exists(webView2Dir) && Directory.GetDirectories(webView2Dir).Length > 0) return true;
            return false;
        }
        catch (Exception ex) { Debug.WriteLine($"[Launcher] WebView2 检测失败: {ex.Message}"); return false; }
    }

    private bool IsExtensionInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.ignorantvega.browser");
            return key != null;
        }
        catch (Exception ex) { Debug.WriteLine($"[Launcher] 扩展检测失败: {ex.Message}"); return false; }
    }

    private bool IsIVegaReady()
    {
        try
        {
            var script = FindScript("tools\\scripts\\system\\Create-IVegaUser.ps1");
            if (script == null) return false;

            // 使用 -Command 代替 -File，通过 Out-String -Stream 过滤掉 Write-Host 的信息流
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -NoLogo -Command \"& '{script}' -Action Check 2>$null | Out-String\"",
                WorkingDirectory = _baseDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);

            // 从混合输出中提取 JSON 对象 (Write-Host 可能泄漏到 stdout)
            var jsonStart = output.IndexOf('{');
            var jsonEnd = output.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                Logger.Warn($"IVega Check 输出无有效 JSON: [{output.Trim()}] stderr=[{stderr.Trim()}]");
                return false;
            }
            var json = output.Substring(jsonStart, jsonEnd - jsonStart + 1);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var userExists = root.GetProperty("UserExists").GetBoolean();
            var enabled = root.TryGetProperty("Enabled", out var enProp) && enProp.GetBoolean();
            var inAdmin = root.GetProperty("InAdminGroup").GetBoolean();
            var credStored = root.GetProperty("CredentialStored").GetBoolean();

            Logger.Debug($"IVega Check: UserExists={userExists}, Enabled={enabled}, InAdminGroup={inAdmin}, CredentialStored={credStored}");
            return userExists && enabled && inAdmin && credStored;
        }
        catch (Exception ex)
        {
            Logger.Warn($"IVega Check 异常: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CreateIVegaAsync()
    {
        var script = FindScript("tools\\scripts\\system\\Create-IVegaUser.ps1");
        if (script == null) { Log("错误: 找不到 Create-IVegaUser.ps1"); return false; }

        Log($"IVega 脚本路径: {script}");

        // Create 需要管理员权限，用 RunAs 启动
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Action Create -Force",
                WorkingDirectory = _baseDir,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            Log($"IVega 脚本退出码: {p.ExitCode}");

            if (p.ExitCode != 0)
            {
                Log("IVega 创建失败，脚本返回非零退出码");
                return false;
            }

            // 等待系统注册新用户 (SAM 数据库同步需要时间)
            await Task.Delay(1500);

            // 验证是否创建成功 (最多重试 3 次)
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var ready = IsIVegaReady();
                Log($"IVega 状态验证 (第{attempt}次): {(ready ? "已就绪" : "未就绪")}");
                if (ready) return true;
                if (attempt < 3) await Task.Delay(1000);
            }
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log("IVega 创建已取消（用户拒绝提权）");
            return false;
        }
        catch (Exception ex)
        {
            Log($"IVega 创建失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RunScriptAsync(string scriptName, string tag)
    {
        var path = FindScript(scriptName);
        if (path == null) { Log($"错误: 找不到 {scriptName}"); return false; }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
                WorkingDirectory = _baseDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Log($"  [{tag}] {e.Data}"); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log($"  [{tag}] {e.Data}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Exception ex) { Log($"[{tag}] 异常: {ex.Message}"); return false; }
    }

    // ── 核心服务 (通过 NSSM 管理) ────────────────────────────────────

    private async void btnService_Click(object? sender, EventArgs e)
    {
        if (IsServiceRunning())
        {
            btnService.Enabled = false;
            btnService.Text = "停止中...";
            Log("正在停止核心服务...");
            await Task.Run(() => NssmRun("stop", ServiceName));
            await Task.Delay(1000);
            btnService.Text = "▶  启动核心服务";
            btnService.Enabled = true;
            Log("核心服务已停止");
            return;
        }

        // 环境未就绪时提示用户
        if (!_sdkOk)
        {
            Log("请先点击「安装环境」安装 .NET SDK");
            MessageBox.Show("请先点击「安装环境」安装 .NET SDK", "环境缺失",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnService.Enabled = false;
        btnService.Text = "启动中...";

        // 首次使用时注册服务
        if (!IsServiceRegistered())
        {
            if (!InstallService())
            {
                btnService.Text = "▶  启动核心服务";
                btnService.Enabled = true;
                return;
            }
        }

        Log("正在启动核心服务...");
        var startResult = await Task.Run(() => NssmRun("start", ServiceName));
        Log($"NSSM start 输出: [{startResult}]");
        await Task.Delay(3000);

        var status = GetServiceStatus();
        Log($"服务状态: [{status}]");

        if (status.Contains("RUNNING"))
        {
            Log("核心服务已启动");
            btnService.Text = "■  停止核心服务";
        }
        else
        {
            Log("核心服务启动失败，请查看日志");
            btnService.Text = "▶  启动核心服务";
        }
        btnService.Enabled = true;
    }

    // ── 对话界面 ──────────────────────────────────────────────────────

    private async void btnChat_Click(object? sender, EventArgs e)
    {
        if (IsProcessRunning("Agent.GUI"))
        {
            KillProcessesByName("Agent.GUI");
            Log("对话界面已关闭");
            btnChat.Text = "💬  打开对话界面";
            return;
        }

        btnChat.Enabled = false;
        btnChat.Text = "启动中...";

        // 优先用内置 SDK + DLL 启动 (无需系统安装 .NET Runtime)
        var dll = FindGuiDll();
        var exe = FindGuiExe();
        var hasSdk = File.Exists(DotnetExe);

        ProcessStartInfo guiStartInfo;
        if (dll != null && hasSdk)
        {
            Log("使用内置 SDK 启动对话界面...");
            guiStartInfo = new ProcessStartInfo
            {
                FileName = DotnetExe,
                Arguments = $"\"{dll}\"",
                WorkingDirectory = _baseDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else if (exe != null)
        {
            guiStartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = _baseDir,
                UseShellExecute = true,
                CreateNoWindow = false
            };
        }
        else
        {
            Log("未找到对话界面程序，请先发布项目");
            btnChat.Text = "💬  打开对话界面";
            btnChat.Enabled = true;
            return;
        }

        try
        {
            _guiProcess = Process.Start(guiStartInfo);

            // 监听进程退出，自动刷新按钮
            if (_guiProcess != null)
            {
                _guiProcess.EnableRaisingEvents = true;
                _guiProcess.Exited += (_, _) =>
                {
                    try
                    {
                        if (btnChat.InvokeRequired)
                            btnChat.Invoke(() => { btnChat.Text = "💬  打开对话界面"; });
                        else
                            btnChat.Text = "💬  打开对话界面";
                        Log("对话界面已退出");
                    }
                    catch (Exception ex) { Debug.WriteLine($"[Launcher] GUI Exited 事件处理失败: {ex.Message}"); }
                };
            }

            await Task.Delay(1500);

            if (_guiProcess is { HasExited: false })
            {
                Log("对话界面已启动");
                btnChat.Text = "■  关闭对话界面";
            }
            else { Log("对话界面启动失败"); btnChat.Text = "💬  打开对话界面"; }
        }
        catch (Exception ex) { Log($"启动失败: {ex.Message}"); btnChat.Text = "💬  打开对话界面"; }
        btnChat.Enabled = true;
    }

    // ── 设置 ─────────────────────────────────────────────────────────

    private void btnSettings_Click(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(new LauncherConfig(), _baseDir);
        form.ShowDialog(this);
    }

    // ── 工具方法 ──────────────────────────────────────────────────────

    private static bool IsProcessRunning(string n) => Process.GetProcessesByName(n).Length > 0;

    private void RefreshButtons()
    {
        btnService.Enabled = File.Exists(DotnetExe) || IsServiceRegistered();
        btnService.Text = IsServiceRunning() ? "■  停止核心服务" : "▶  启动核心服务";
        btnChat.Enabled = FindGuiDll() != null || FindGuiExe() != null;
        btnChat.Text = IsProcessRunning("Agent.GUI") ? "■  关闭对话界面" : "💬  打开对话界面";
    }

    private void SetStatus(Label lbl, bool ok)
    {
        void Do() { lbl.Text = ok ? "✅ 已就绪" : "❌ 未安装"; lbl.ForeColor = ok ? System.Drawing.Color.Green : System.Drawing.Color.Red; }
        if (lbl.InvokeRequired) lbl.Invoke(Do); else Do();
    }

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        if (txtLog.InvokeRequired) txtLog.Invoke(() => { txtLog.AppendText(line + Environment.NewLine); txtLog.ScrollToCaret(); });
        else { txtLog.AppendText(line + Environment.NewLine); txtLog.ScrollToCaret(); }
        Logger.Info(msg);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        using var dialog = new ExitDialog();
        dialog.InitServiceState(IsServiceRunning());
        var r = dialog.ShowDialog(this);
        if (r == DialogResult.Cancel) { e.Cancel = true; return; }

        if (!dialog.KeepServiceRunning)
        {
            // 通过 NSSM 停止核心服务
            if (IsServiceRunning()) NssmRun("stop", ServiceName);
        }
        else
        {
            Log("核心服务保持后台运行");
        }

        // 关闭 GUI 进程
        KillProcessesByName("Agent.GUI");
        Logger.Info("启动器已退出");
        base.OnFormClosing(e);
    }
}
