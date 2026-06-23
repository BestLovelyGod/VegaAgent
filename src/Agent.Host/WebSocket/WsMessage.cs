// ============================================================================
// WebSocket 消息类型定义 + 消息结构
// ============================================================================

namespace Agent.Host.Ws;

/// <summary>
/// WebSocket 消息类型常量
/// </summary>
public static class WsMessageType
{
    // ── 心跳 ──
    /// <summary>客户端/服务端心跳 ping</summary>
    public const string Ping = "ping";
    /// <summary>心跳 pong 响应</summary>
    public const string Pong = "pong";

    // ── LLM 流式输出 ──
    /// <summary>LLM 流式增量数据 (广播)</summary>
    public const string LlmStream = "llm.stream";
    /// <summary>LLM 流式输出结束</summary>
    public const string LlmStreamEnd = "llm.stream.end";
    /// <summary>LLM 流式输出错误</summary>
    public const string LlmError = "llm.error";

    // ── 任务状态 ──
    /// <summary>任务状态更新</summary>
    public const string TaskUpdate = "task.update";
    /// <summary>任务完成</summary>
    public const string TaskComplete = "task.complete";
    /// <summary>任务错误</summary>
    public const string TaskError = "task.error";

    // ── Host 状态 ──
    /// <summary>Host 状态通知</summary>
    public const string HostStatus = "host.status";

    // ── 通用 ──
    /// <summary>错误消息</summary>
    public const string Error = "error";
    /// <summary>连接确认 / 操作确认</summary>
    public const string Ack = "ack";
}

/// <summary>
/// WebSocket 消息结构 — 客户端和服务端之间传递的 JSON 格式
/// </summary>
public sealed class WsMessage
{
    /// <summary>消息类型，对应 <see cref="WsMessageType"/> 常量</summary>
    public required string Type { get; set; }

    /// <summary>消息负载 (可选)</summary>
    public object? Data { get; set; }

    /// <summary>请求 ID — 用于匹配请求和响应 (可选)</summary>
    public string? RequestId { get; set; }

    /// <summary>消息时间戳 (Unix ms)</summary>
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
