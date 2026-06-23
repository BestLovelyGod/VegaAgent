// ============================================================================
// Browser Extension API 端点
// 提供 Agent ↔ 浏览器扩展的通信桥梁
// ============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Host.Endpoints;

/// <summary>
/// 浏览器扩展 API 端点
/// 
/// 通信模式:
///   1. Agent → 扩展: POST /api/browser/command → 命令入队 → 扩展轮询获取
///   2. 扩展 → Agent: POST /api/browser/response → 结果返回给等待中的 Agent 调用
///   3. 扩展轮询: GET /api/browser/pending → 获取待执行命令
/// </summary>
public static class BrowserEndpoints
{
    // 待发送给扩展的命令队列
    private static readonly ConcurrentQueue<BrowserCommand> _pendingCommands = new();
    
    // 等待扩展响应的 Agent 调用
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserResponse>> _waitingCalls = new();
    
    // 最近的网络请求记录
    private static readonly ConcurrentDictionary<int, List<NetworkRequest>> _networkRequests = new();

    public static void MapBrowserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/browser")
            .WithTags("Browser Extension")
            .WithDescription("浏览器扩展通信 API");

        // ── Agent 调用 → 发送命令给扩展 ──

        group.MapPost("/command", async (BrowserCommandRequest request, CancellationToken ct) =>
        {
            var commandId = Guid.NewGuid().ToString("N")[..12];
            var command = new BrowserCommand
            {
                CommandId = commandId,
                Type = "agent_command",
                Action = request.Action,
                Params = request.Params
            };

            // 创建等待响应的 TCS
            var tcs = new TaskCompletionSource<BrowserResponse>();
            _waitingCalls[commandId] = tcs;

            // 入队命令
            _pendingCommands.Enqueue(command);

            // 等待扩展响应（最多 60 秒）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                var response = await tcs.Task.WaitAsync(cts.Token);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                _waitingCalls.TryRemove(commandId, out _);
                return Results.Json(
                    new { error = "浏览器命令超时 (60s)", commandId },
                    statusCode: 408);
            }
        })
        .WithName("SendBrowserCommand")
        .WithDescription("发送命令到浏览器扩展");

        // ── 扩展轮询 → 获取待执行命令 ──

        group.MapGet("/pending", () =>
        {
            var commands = new List<BrowserCommand>();
            while (_pendingCommands.TryDequeue(out var cmd))
            {
                commands.Add(cmd);
            }
            return Results.Ok(commands);
        })
        .WithName("GetPendingCommands")
        .WithDescription("获取待执行的浏览器命令（扩展轮询用）");

        // ── 扩应回复 → 返回结果给 Agent ──

        group.MapPost("/response", (BrowserResponse response) =>
        {
            if (!string.IsNullOrEmpty(response.CommandId) &&
                _waitingCalls.TryRemove(response.CommandId, out var tcs))
            {
                tcs.TrySetResult(response);
            }
            return Results.Ok();
        })
        .WithName("ReceiveBrowserResponse")
        .WithDescription("接收浏览器扩展的命令响应");

        // ── 浏览器状态查询 ──

        group.MapGet("/status", async (CancellationToken ct) =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                // 检查 Edge 调试端口
                var resp = await http.GetAsync("http://localhost:9222/json", ct);
                var tabs = JsonSerializer.Deserialize<List<JsonElement>>(
                    await resp.Content.ReadAsStringAsync(ct));
                
                return Results.Ok(new
                {
                    edgeRunning = true,
                    debugPort = 9222,
                    tabs = tabs?.Count ?? 0,
                    extensionConnected = !_pendingCommands.IsEmpty || _waitingCalls.IsEmpty
                });
            }
            catch
            {
                return Results.Ok(new
                {
                    edgeRunning = false,
                    debugPort = 9222,
                    tabs = 0,
                    extensionConnected = false
                });
            }
        })
        .WithName("GetBrowserStatus")
        .WithDescription("获取浏览器状态");

        // ── 获取标签页列表 ──

        group.MapGet("/tabs", async (CancellationToken ct) =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var resp = await http.GetAsync("http://localhost:9222/json", ct);
                var tabs = JsonSerializer.Deserialize<List<JsonElement>>(
                    await resp.Content.ReadAsStringAsync(ct));
                return Results.Ok(tabs);
            }
            catch
            {
                return Results.Ok(Array.Empty<object>());
            }
        })
        .WithName("GetBrowserTabs")
        .WithDescription("获取浏览器标签页列表");

        // ── 网络请求记录 ──

        group.MapPost("/network-request", (NetworkRequest request) =>
        {
            if (!_networkRequests.ContainsKey(request.TabId))
                _networkRequests[request.TabId] = new();
            
            var list = _networkRequests[request.TabId];
            list.Add(request);
            if (list.Count > 200) list.RemoveRange(0, list.Count - 200);
            
            return Results.Ok();
        })
        .WithName("RecordNetworkRequest")
        .WithDescription("记录网络请求");

        group.MapGet("/network-requests/{tabId:int}", (int tabId) =>
        {
            return Results.Ok(_networkRequests.GetValueOrDefault(tabId, new()));
        })
        .WithName("GetNetworkRequests")
        .WithDescription("获取指定标签页的网络请求记录");
    }
}

// ── 数据模型 ────────────────────────────────────────────────────────────────

public record BrowserCommandRequest
{
    public string Action { get; init; } = "";
    public JsonElement? Params { get; init; }
}

public record BrowserCommand
{
    public string CommandId { get; init; } = "";
    public string Type { get; init; } = "agent_command";
    public string Action { get; init; } = "";
    public JsonElement? Params { get; init; }
}

public record BrowserResponse
{
    public string? CommandId { get; init; }
    public bool Success { get; init; }
    public JsonElement? Data { get; init; }
    public string? Error { get; init; }
}

public record NetworkRequest
{
    public int TabId { get; init; }
    public string Url { get; init; } = "";
    public string Method { get; init; } = "GET";
    public int Status { get; init; }
    public string Type { get; init; } = "";
    public long Timestamp { get; init; }
}
