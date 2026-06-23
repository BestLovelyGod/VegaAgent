// ============================================================================
// Ignorant Vega — Native Messaging Host
// 桥接浏览器扩展和 Agent HTTP API
//
// 安装:
//   1. 编译: dotnet build -c Release
//   2. 注册注册表项 (见下方 RegisterHost)
//   3. manifest.json 中的 path 指向编译后的 exe
// ============================================================================

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string AGENT_API = "http://localhost:7300";
const string HOST_NAME = "com.ignorantvega.browser";

var client = new HttpClient { BaseAddress = new Uri(AGENT_API), Timeout = TimeSpan.FromSeconds(120) };

// 检查是否是注册命令
if (args.Length > 0 && args[0] == "register")
{
    RegisterHost();
    return;
}

// ── Native Messaging 主循环 ─────────────────────────────────────────────────

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

while (true)
{
    try
    {
        // 读取消息 (Native Messaging 协议: 4字节长度 + JSON)
        var lengthBytes = new byte[4];
        if (stdin.Read(lengthBytes, 0, 4) != 4) break;
        
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > 1024 * 1024) break; // 最大 1MB
        
        var messageBytes = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var bytesRead = stdin.Read(messageBytes, totalRead, length - totalRead);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }
        
        var messageJson = Encoding.UTF8.GetString(messageBytes, 0, totalRead);
        var message = JsonSerializer.Deserialize<NativeMessage>(messageJson);
        
        if (message == null) continue;
        
        // 处理来自扩展的响应（commandId 存在表示是命令响应）
        if (!string.IsNullOrEmpty(message.CommandId) && message.Type != "agent_command")
        {
            // 这是扩展对 Agent 指令的响应，转发给 Agent
            await ForwardResponseToAgent(message);
            continue;
        }
        
        // 处理来自扩展的状态查询
        if (message.Type == "status")
        {
            WriteResponse(stdout, new { connected = true, agent = AGENT_API });
            continue;
        }
    }
    catch (Exception ex)
    {
        // 写入 stderr 日志（不影响 stdout 的 Native Messaging 协议）
        Console.Error.WriteLine($"[NativeHost] 错误: {ex.Message}");
    }
}

// ── 辅助方法 ────────────────────────────────────────────────────────────────

async Task ForwardResponseToAgent(NativeMessage message)
{
    try
    {
        await client.PostAsJsonAsync("/api/browser/response", message);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[NativeHost] 转发响应失败: {ex.Message}");
    }
}

static void WriteResponse(Stream stdout, object response)
{
    var json = JsonSerializer.Serialize(response);
    var bytes = Encoding.UTF8.GetBytes(json);
    var lengthBytes = BitConverter.GetBytes(bytes.Length);
    
    stdout.Write(lengthBytes, 0, 4);
    stdout.Write(bytes, 0, bytes.Length);
    stdout.Flush();
}

void RegisterHost()
{
    // 注册 Native Messaging Host 到 Windows 注册表
    var hostManifest = new
    {
        name = HOST_NAME,
        description = "Ignorant Vega Browser Bridge",
        path = System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"),
        type = "stdio",
        allowed_origins = new[] { "extension://*/" }
    };
    
    var manifestJson = JsonSerializer.Serialize(hostManifest, new JsonSerializerOptions { WriteIndented = true });
    var manifestPath = Path.Combine(AppContext.BaseDirectory, $"{HOST_NAME}-manifest.json");
    File.WriteAllText(manifestPath, manifestJson);
    
    // 写入注册表
    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
        $@"SOFTWARE\Google\Chrome\NativeMessagingHosts\{HOST_NAME}");
    key.SetValue("", manifestPath);
    
    // Edge 使用相同的注册表路径
    using var edgeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
        $@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts\{HOST_NAME}");
    edgeKey.SetValue("", manifestPath);
    
    Console.WriteLine($"Native Messaging Host 已注册:");
    Console.WriteLine($"  清单: {manifestPath}");
    Console.WriteLine($"  可执行文件: {hostManifest.path}");
}

// ── 数据模型 ────────────────────────────────────────────────────────────────

class NativeMessage
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("commandId")] public string? CommandId { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
