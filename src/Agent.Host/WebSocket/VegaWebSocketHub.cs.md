# VegaWebSocketHub

> 命名空间: `Agent.Host.Ws`

## 职责

WebSocket 连接管理中心 — 负责连接生命周期、心跳检测、消息广播、LLM/任务流式推送。

## 公共方法

| 方法 | 说明 |
|------|------|
| `HandleConnectionAsync(socket, clientInfo)` | 处理新连接 — 阻塞直到客户端断开 |
| `BroadcastAsync(message)` | 向所有连接广播消息 (并发安全) |
| `SendToAsync(connectionId, message)` | 向指定连接发送消息 |
| `PushLlmChunkAsync(sessionId, delta, reasoning)` | 推送 LLM 流式增量 |
| `PushLlmStreamEndAsync(sessionId, finishReason)` | 推送 LLM 流结束 |
| `PushLlmErrorAsync(sessionId, error)` | 推送 LLM 错误 |
| `PushTaskUpdateAsync(taskId, status, data)` | 推送任务状态更新 |
| `StartHeartbeat()` | 启动心跳检测 (30s 间隔) |
| `StopAsync()` | 停止 Hub，关闭所有连接并释放资源 |

## 注册方式

```csharp
// Program.cs
builder.Services.AddSingleton<VegaWebSocketHub>();
app.Map("/ws", async (HttpContext context, VegaWebSocketHub hub) => {
    if (context.WebSockets.IsWebSocketRequest) {
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        await hub.HandleConnectionAsync(socket);
    }
});
```

## 注意事项

- 心跳间隔 30 秒，超时 2 分钟的连接自动清理
- `BroadcastAsync` 使用 per-connection `SemaphoreSlim` 保护并发写
- 大消息支持分片 (8KB buffer + `StringBuilder` 累积 + `EndOfMessage` 检查)
- `StopAsync` 释放 `_cts`、`_heartbeatTimer`、所有连接的 `WriteLock` 和 `Socket`
- 客户端发送 `llm.cancel` 会广播 `llm.error("用户取消")`
