// EdgeBrowser.cs
// Edge 浏览器自动化工具 — 通过 CDP 协议控制 Edge 浏览器
//
// 用法:
//   EdgeBrowser <command> [args...]
//
// 命令:
//   setup              配置 Edge 调试端口 (修改注册表, 需管理员权限)
//   status             检查 Edge 调试端口状态
//   launch [url]       启动带调试端口的 Edge
//   navigate <url>     导航到 URL
//   screenshot [path]  截图 (默认保存到 %TEMP%)
//   click <selector>   点击元素
//   fill <selector> <value>  填写表单
//   evaluate <js>      执行 JavaScript
//   extract <selector> 提取元素文本
//   tabs               列出所有标签页
//   close              关闭调试连接
//
// 输出: JSON 格式结果

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// ── 参数解析 ──────────────────────────────────────────────────────────────

if (args.Length == 0)
{
    Console.Error.WriteLine("用法: EdgeBrowser <command> [args...]");
    Console.Error.WriteLine("  setup              配置 Edge 调试端口");
    Console.Error.WriteLine("  status             检查调试端口状态");
    Console.Error.WriteLine("  launch [url]       启动 Edge");
    Console.Error.WriteLine("  navigate <url>     导航到 URL");
    Console.Error.WriteLine("  screenshot [path]  截图");
    Console.Error.WriteLine("  click <selector>   点击元素");
    Console.Error.WriteLine("  fill <sel> <value> 填写表单");
    Console.Error.WriteLine("  evaluate <js>      执行 JS");
    Console.Error.WriteLine("  extract <selector> 提取文本");
    Console.Error.WriteLine("  tabs               列出标签页");
    Environment.Exit(1);
    return;
}

var command = args[0].ToLowerInvariant();
var debugPort = 9222;
var edgePath = FindEdgePath();
var userDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Microsoft", "Edge", "User Data");

var result = new BrowserResult
{
    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    Command = command
};

try
{
    switch (command)
    {
        case "setup":
            HandleSetup();
            break;
        case "status":
            HandleStatus();
            break;
        case "launch":
            HandleLaunch(args.Length > 1 ? args[1] : null);
            break;
        case "navigate":
            if (args.Length < 2) { result.Error = "缺少 URL 参数"; break; }
            await HandleNavigate(args[1]);
            break;
        case "screenshot":
            var path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), $"edge-ss-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await HandleScreenshot(path);
            break;
        case "click":
            if (args.Length < 2) { result.Error = "缺少选择器参数"; break; }
            await HandleClick(args[1]);
            break;
        case "fill":
            if (args.Length < 3) { result.Error = "用法: fill <selector> <value>"; break; }
            await HandleFill(args[1], args[2]);
            break;
        case "evaluate":
            if (args.Length < 2) { result.Error = "缺少 JS 代码"; break; }
            await HandleEvaluate(args[1]);
            break;
        case "extract":
            if (args.Length < 2) { result.Error = "缺少选择器参数"; break; }
            await HandleExtract(args[1]);
            break;
        case "tabs":
            await HandleTabs();
            break;
        default:
            result.Error = $"未知命令: {command}";
            break;
    }
}
catch (Exception ex)
{
    result.Error = $"操作失败: {ex.Message}";
}

OutputResult(result);

// ── 命令处理 ──────────────────────────────────────────────────────────────

void HandleSetup()
{
    if (!IsAdmin())
    {
        result.Error = "修改注册表需要管理员权限。请以管理员身份运行此脚本。";
        return;
    }

    // 检查是否已配置
    var currentPort = GetRegistryDebugPort();
    if (currentPort == debugPort.ToString())
    {
        result.Status = "已配置";
        Console.WriteLine($"[OK] Edge 调试端口已配置 (端口 {debugPort})");
        Console.WriteLine("[i] 如果 Edge 正在运行，请重启 Edge 使配置生效");
        return;
    }

    // 显示风险警告
    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("                    WARNING - Security Notice                   ");
    Console.WriteLine("================================================================");
    Console.WriteLine();
    Console.WriteLine("  即将修改 Edge 浏览器的启动参数，添加调试端口。");
    Console.WriteLine();
    Console.WriteLine("  [!] 风险提示:");
    Console.WriteLine("      1. 任何本地程序都可以通过此端口控制 Edge 浏览器");
    Console.WriteLine("      2. 恶意网站可能利用调试端口获取敏感信息");
    Console.WriteLine("      3. 浏览器的沙箱保护可能被绕过");
    Console.WriteLine("      4. 此修改会影响所有 Edge 实例");
    Console.WriteLine();
    Console.WriteLine("  [+] 安全建议:");
    Console.WriteLine("      1. 仅在可信网络环境中使用");
    Console.WriteLine("      2. 不使用时可运行 'EdgeBrowser setup --remove' 恢复");
    Console.WriteLine("      3. 配合防火墙限制端口访问");
    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine();

    // 检查是否要移除
    if (args.Length > 1 && args[1] == "--remove")
    {
        RemoveRegistryDebugPort();
        result.Status = "已移除";
        Console.WriteLine("[OK] 已移除 Edge 调试端口配置");
        Console.WriteLine("[i] 请重启 Edge 使更改生效");
        return;
    }

    // 修改注册表
    SetRegistryDebugPort(debugPort);
    result.Status = "已配置";
    Console.WriteLine($"[OK] 已配置 Edge 调试端口 (端口 {debugPort})");
    Console.WriteLine("[i] 请重启 Edge 使配置生效");
    Console.WriteLine("[i] 移除配置: EdgeBrowser setup --remove");
}

void HandleStatus()
{
    var portAvailable = CheckDebugPort();
    var registryConfigured = GetRegistryDebugPort() == debugPort.ToString();
    var edgeRunning = IsEdgeRunning();

    result.Data = new Dictionary<string, object>
    {
        ["debugPort"] = debugPort,
        ["portAvailable"] = portAvailable,
        ["registryConfigured"] = registryConfigured,
        ["edgeRunning"] = edgeRunning,
        ["edgePath"] = edgePath ?? "未找到"
    };

    Console.WriteLine($"Edge 调试端口状态:");
    Console.WriteLine($"  端口 {debugPort} 可用: {(portAvailable ? "是" : "否")}");
    Console.WriteLine($"  注册表已配置: {(registryConfigured ? "是" : "否")}");
    Console.WriteLine($"  Edge 正在运行: {(edgeRunning ? "是" : "否")}");

    if (!portAvailable && !registryConfigured)
    {
        Console.WriteLine();
        Console.WriteLine("[!] 未检测到调试端口。运行 'EdgeBrowser setup' 配置。");
    }
    else if (!portAvailable && registryConfigured && edgeRunning)
    {
        Console.WriteLine();
        Console.WriteLine("[!] 注册表已配置但端口不可用。请重启 Edge。");
    }
}

void HandleLaunch(string? url)
{
    if (string.IsNullOrEmpty(edgePath))
    {
        result.Error = "未找到 Edge 浏览器";
        return;
    }

    // 检查是否已有 Edge 带调试端口运行
    if (CheckDebugPort())
    {
        Console.WriteLine($"[i] Edge 调试端口 {debugPort} 已可用");
        result.Status = "已运行";
        return;
    }

    // 检查 Edge 是否在运行 (不带调试端口)
    if (IsEdgeRunning())
    {
        Console.WriteLine("[!] Edge 正在运行但未启用调试端口");
        Console.WriteLine("[i] 选项:");
        Console.WriteLine("    1. 运行 'EdgeBrowser setup' 配置注册表后重启 Edge");
        Console.WriteLine("    2. 手动关闭 Edge 后运行 'EdgeBrowser launch'");
        result.Error = "Edge 已运行但无调试端口";
        return;
    }

    // 启动 Edge 带调试端口
    var args = $"--remote-debugging-port={debugPort}";
    if (!string.IsNullOrEmpty(url))
        args += $" {url}";

    var psi = new ProcessStartInfo
    {
        FileName = edgePath,
        Arguments = args,
        UseShellExecute = true
    };

    Process.Start(psi);
    Console.WriteLine($"[OK] Edge 已启动 (调试端口: {debugPort})");

    // 等待端口可用
    for (int i = 0; i < 10; i++)
    {
        Thread.Sleep(500);
        if (CheckDebugPort())
        {
            Console.WriteLine("[OK] 调试端口已就绪");
            result.Status = "已启动";
            return;
        }
    }

    Console.WriteLine("[!] Edge 启动中，调试端口尚未就绪，请稍后重试");
    result.Status = "启动中";
}

async Task HandleNavigate(string url)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token);

    // 启用 Page 事件域
    await SendCdpCommand(ws, "Page.enable", timeoutMs: 5000);

    // 导航
    var navResult = await SendCdpCommand(ws, "Page.navigate", new { url }, timeoutMs: 10000);
    if (navResult is null)
    {
        result.Error = "导航命令超时";
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        return;
    }

    // 等待页面加载完成 (最多 30 秒)
    var loadCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        var buffer = new byte[1024 * 64];
        while (!loadCts.Token.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult recvResult;
            do
            {
                recvResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), loadCts.Token);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, recvResult.Count));
            } while (!recvResult.EndOfMessage);

            var msg = sb.ToString();
            if (msg.Contains("Page.loadEventFired") || msg.Contains("Page.frameStoppedLoading"))
                break;
        }
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("[WARN] 页面加载等待超时 (30s)，继续执行");
    }

    Console.WriteLine($"[OK] 已导航到: {url}");
    result.Status = "已导航";
    result.Data = new Dictionary<string, object> { ["url"] = url };
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleScreenshot(string savePath)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var resp = await SendCdpCommand(ws, "Page.captureScreenshot", new { format = "png" });
    if (resp.HasValue && resp.Value.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("data", out var dataEl))
    {
        var base64 = dataEl.GetString();
        if (base64 != null)
        {
            var bytes = Convert.FromBase64String(base64);
            await File.WriteAllBytesAsync(savePath, bytes);
            Console.WriteLine($"[OK] 截图已保存: {savePath}");
            result.Status = "已截图";
            result.Data = new Dictionary<string, object> { ["path"] = savePath, ["size"] = bytes.Length };
        }
    }
    else
    {
        result.Error = "截图失败";
    }
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleClick(string selector)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var js = $"document.querySelector('{selector}').click()";
    await SendCdpCommand(ws, "Runtime.evaluate", new { expression = js });
    Console.WriteLine($"[OK] 已点击: {selector}");
    result.Status = "已点击";
    result.Data = new Dictionary<string, object> { ["selector"] = selector };
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleFill(string selector, string value)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var js = $"document.querySelector('{selector}').value = '{value.Replace("'", "\\'")}'";
    await SendCdpCommand(ws, "Runtime.evaluate", new { expression = js });
    Console.WriteLine($"[OK] 已填写: {selector}");
    result.Status = "已填写";
    result.Data = new Dictionary<string, object> { ["selector"] = selector, ["value"] = value };
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleEvaluate(string js)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var resp = await SendCdpCommand(ws, "Runtime.evaluate", new { expression = js, returnByValue = true });
    string? value = null;
    if (resp.HasValue && resp.Value.TryGetProperty("result", out var resultEl))
    {
        if (resultEl.TryGetProperty("result", out var resEl))
        {
            value = resEl.TryGetProperty("value", out var valEl) ? valEl.ToString() :
                    resEl.TryGetProperty("description", out var descEl) ? descEl.ToString() : null;
        }
    }
    Console.WriteLine($"[OK] 执行结果: {value}");
    result.Status = "已执行";
    result.Data = new Dictionary<string, object> { ["expression"] = js, ["value"] = value ?? "null" };
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleExtract(string selector)
{
    var tab = await GetFirstTab();
    if (tab is not { } t) { result.Error = "无法获取标签页"; return; }

    var wsUrl = GetWebSocketUrl(t);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var js = $"JSON.stringify(Array.from(document.querySelectorAll('{selector}')).map(e => e.textContent.trim()))";
    var resp = await SendCdpCommand(ws, "Runtime.evaluate", new { expression = js, returnByValue = true });
    string? value = null;
    if (resp.HasValue && resp.Value.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("result", out var resEl))
    {
        value = resEl.TryGetProperty("value", out var valEl) ? valEl.GetString() : null;
    }
    Console.WriteLine($"[OK] 提取结果:");
    Console.WriteLine(value ?? "(空)");
    result.Status = "已提取";
    result.Data = new Dictionary<string, object> { ["selector"] = selector, ["data"] = value ?? "[]" };
    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

async Task HandleTabs()
{
    var tabs = await GetTabs();
    if (tabs == null || tabs.Count == 0)
    {
        Console.WriteLine("[i] 没有打开的标签页 (确保 Edge 调试端口已启用)");
        result.Status = "无标签页";
        return;
    }

    Console.WriteLine($"打开的标签页 ({tabs.Count} 个):");
    for (int i = 0; i < tabs.Count; i++)
    {
        var t = tabs[i];
        Console.WriteLine($"  [{i}] {t.title} - {t.url}");
    }
    result.Status = "已列出";
    result.Data = new Dictionary<string, object> { ["count"] = tabs.Count };
}

// ── CDP 通信 ──────────────────────────────────────────────────────────────

async Task<JsonElement?> SendCdpCommand(ClientWebSocket ws, string method, object? parameters = null, int timeoutMs = 15000)
{
    var id = Random.Shared.Next(1, 999999);
    var msg = new Dictionary<string, object>
    {
        ["id"] = id,
        ["method"] = method
    };
    if (parameters != null) msg["params"] = parameters;

    var json = JsonSerializer.Serialize(msg);
    var bytes = Encoding.UTF8.GetBytes(json);

    using var cts = new CancellationTokenSource(timeoutMs);
    try
    {
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);

        // 读取响应 (带超时)
        var buffer = new byte[1024 * 64];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        var doc = JsonDocument.Parse(sb.ToString());
        return doc.RootElement;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine($"[WARN] CDP 命令超时 ({timeoutMs}ms): {method}");
        return null;
    }
}

// ── 辅助方法 ──────────────────────────────────────────────────────────────

bool CheckDebugPort()
{
    try
    {
        using var client = new TcpClient();
        var task = client.ConnectAsync("127.0.0.1", debugPort);
        return task.Wait(1000) && client.Connected;
    }
    catch { return false; }
}

async Task<List<(string id, string title, string url)>?> GetTabs()
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var json = await http.GetStringAsync($"http://localhost:{debugPort}/json");
        var tabs = JsonSerializer.Deserialize<List<JsonElement>>(json);
        return tabs?
            .Where(t => t.GetProperty("type").GetString() == "page")
            .Select(t => (
                id: t.GetProperty("id").GetString() ?? "",
                title: t.GetProperty("title").GetString() ?? "",
                url: t.GetProperty("url").GetString() ?? ""
            ))
            .ToList();
    }
    catch { return null; }
}

async Task<(string id, string title, string url)?> GetFirstTab()
{
    var tabs = await GetTabs();
    return tabs?.FirstOrDefault(t => t.url.StartsWith("http") || t.url.StartsWith("file"));
}

string GetWebSocketUrl((string id, string title, string url) tab)
{
    return $"ws://localhost:{debugPort}/devtools/page/{tab.id}";
}

bool IsEdgeRunning()
{
    return Process.GetProcessesByName("msedge").Length > 0;
}

string? FindEdgePath()
{
    var paths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
    };
    return paths.FirstOrDefault(File.Exists);
}

string? GetRegistryDebugPort()
{
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge");
        return key?.GetValue("DeveloperToolsAvailability")?.ToString();
    }
    catch { return null; }
}

void SetRegistryDebugPort(int port)
{
    // 设置 Edge 策略允许远程调试
    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge");
    key.SetValue("DeveloperToolsAvailability", 0); // 0 = 允许
    // 注意: 实际的 --remote-debugging-port 参数需要通过快捷方式或启动参数传递
    // 这里设置策略允许开发者工具，然后通过 launch 命令启动带端口的 Edge
}

void RemoveRegistryDebugPort()
{
    try
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Policies\Microsoft\Edge", false);
    }
    catch { }
}

bool IsAdmin()
{
    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
    var principal = new System.Security.Principal.WindowsPrincipal(identity);
    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}

void OutputResult(BrowserResult r)
{
    var json = JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    Environment.Exit(r.Error != null ? 1 : 0);
}

// ── 数据模型 ──────────────────────────────────────────────────────────────

class BrowserResult
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("data")] public Dictionary<string, object>? Data { get; set; }
}
