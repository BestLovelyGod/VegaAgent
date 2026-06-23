# WebSocketConnection

> 命名空间: `Agent.Host.Ws`

## 职责

单个 WebSocket 连接的上下文信息，封装套接字、连接元数据和并发写锁。

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `ConnectionId` | `string` | 连接唯一标识 (8位hex) |
| `Socket` | `System.Net.WebSockets.WebSocket` | 底层 WebSocket 套接字 |
| `ConnectedAt` | `DateTime` | 连接建立时间 (UTC) |
| `LastActivity` | `DateTime` | 最后一次收到消息的时间 (UTC) |
| `ClientInfo` | `string?` | 客户端 User-Agent 或自定义标识 |
| `WriteLock` | `SemaphoreSlim` | 并发写锁，保护 SendAsync 不被交错 |

## 注意事项

- 实现 `IDisposable`，释放 `WriteLock` 和 `Socket`
- Hub 在连接移除时自动调 `Dispose()`
- `WriteLock` 是 per-connection 粒度，不同连接之间无锁竞争
