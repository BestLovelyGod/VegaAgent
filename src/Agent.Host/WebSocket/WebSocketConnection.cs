// ============================================================================
// WebSocket 连接信息
// ============================================================================

using System.Net.WebSockets;

namespace Agent.Host.Ws;

/// <summary>
/// 单个 WebSocket 连接的上下文信息
/// </summary>
public sealed class WebSocketConnection : IDisposable
{
    /// <summary>连接唯一标识</summary>
    public required string ConnectionId { get; init; }

    /// <summary>底层 WebSocket 套接字</summary>
    public required System.Net.WebSockets.WebSocket Socket { get; init; }

    /// <summary>连接建立时间 (UTC)</summary>
    public DateTime ConnectedAt { get; init; }

    /// <summary>最后一次收到消息的时间 (UTC)</summary>
    public DateTime LastActivity { get; set; }

    /// <summary>客户端 User-Agent 或自定义标识</summary>
    public string? ClientInfo { get; init; }

    /// <summary>并发写锁 — 保护同一 socket 的 SendAsync 不被交错</summary>
    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    /// <summary>释放 WriteLock 和 Socket 资源</summary>
    public void Dispose()
    {
        WriteLock.Dispose();
        Socket.Dispose();
    }
}
