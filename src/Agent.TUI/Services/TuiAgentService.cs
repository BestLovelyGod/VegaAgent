// ============================================================================
// Agent API 客户端 — TUI 与 Agent.Host 通信
// ============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using Agent.Core.Config;

namespace Agent.TUI.Services;

public sealed class TuiAgentService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TuiAgentService(string? baseUrl = null)
    {
        _baseUrl = (baseUrl ?? AppConstants.DefaultBaseUrl).TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // 读取 API Key: 环境变量 > 配置文件
        _apiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            try
            {
                var configPath = FindConfigFile();
                if (configPath is not null)
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    _apiKey = doc.RootElement
                        .GetProperty("Agent")
                        .GetProperty("Llm")
                        .GetProperty("ApiKey")
                        .GetString();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TuiAgent] 配置读取失败: {ex.Message}"); }
        }
    }

    private static string? FindConfigFile()
    {
        // 从 TUI 可执行文件位置向上查找项目根目录
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var path = Path.Combine(dir, "data", "config.json");
            if (File.Exists(path)) return path;
            dir = Path.GetDirectoryName(dir)!;
            if (dir is null) break;
        }
        // 也检查当前工作目录
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "config.json");
        return File.Exists(cwdPath) ? cwdPath : null;
    }

    private string Url(string path) => string.IsNullOrWhiteSpace(_apiKey)
        ? $"{_baseUrl}{path}"
        : $"{_baseUrl}{path}{(path.Contains('?') ? '&' : '?')}api-key={_apiKey}";

    public async Task<TaskInfo?> SubmitTaskAsync(string message, string sessionId = "tui", CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(Url("/api/tasks"),
                new { message, sessionId }, JsonOptions, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TaskInfo>(JsonOptions, ct);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<List<TaskInfo>> GetTasksAsync(CancellationToken ct = default)
    {
        try
        {
            var tasks = await _http.GetFromJsonAsync<List<TaskInfo>>(Url("/api/tasks"), JsonOptions, ct);
            return tasks ?? [];
        }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<TaskDetail?> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<TaskDetail>(Url($"/api/tasks/{taskId}"), JsonOptions, ct); }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        try { return (await _http.DeleteAsync(Url($"/api/tasks/{taskId}"), ct)).IsSuccessStatusCode; }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public async Task<List<ToolInfo>> GetToolsAsync(CancellationToken ct = default)
    {
        try
        {
            var tools = await _http.GetFromJsonAsync<List<ToolInfo>>(Url("/api/tools"), JsonOptions, ct);
            return tools ?? [];
        }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<bool> IsOnlineAsync(CancellationToken ct = default)
    {
        try { return (await _http.GetAsync($"{_baseUrl}/health", ct)).IsSuccessStatusCode; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TUI] 健康检查失败: {ex.Message}"); return false; }
    }

    /// <summary>流式读取任务输出 (SSE)</summary>
    public async Task StreamTaskAsync(string taskId, Action<string> onChunk, CancellationToken ct = default)
    {
        try
        {
            var url = Url($"/api/tasks/{taskId}/stream");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break; // 流结束
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString();
                        if (text is not null)
                            onChunk(text);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TuiAgent] SSE 块解析失败: {ex.Message}"); }
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public string? LastError { get; private set; }
    public void Dispose() => _http.Dispose();
}

// ── API 数据模型 ──

public sealed class TaskInfo
{
    public string TaskId { get; init; } = "";
    public string Message { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int Iterations { get; init; }
    public int TotalTokens { get; init; }
    public int ToolCalls { get; init; }
}

public sealed class TaskDetail
{
    public string TaskId { get; init; } = "";
    public string Message { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Result { get; init; }
    public string? Error { get; init; }
    public DateTime CreatedAt { get; init; }
    public int Iterations { get; init; }
    public int TotalTokens { get; init; }
    public List<ToolCallInfo> ToolCalls { get; init; } = [];
}

public sealed class ToolCallInfo
{
    public string ToolName { get; init; } = "";
    public bool Success { get; init; }
    public double Duration { get; init; }
    public string? Output { get; init; }
}

public sealed class ToolInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string RiskLevel { get; init; } = "";
}
