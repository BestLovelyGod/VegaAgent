# WsMessage + WsMessageType

> 命名空间: `Agent.Host.Ws`

## 职责

定义 WebSocket 通信的消息协议：类型常量 (`WsMessageType`) 和消息结构 (`WsMessage`)。

## WsMessageType 常量

| 常量 | 值 | 方向 | 说明 |
|------|-----|------|------|
| `Ping` | `"ping"` | 双向 | 心跳探测 |
| `Pong` | `"pong"` | 双向 | 心跳响应 |
| `LlmStream` | `"llm.stream"` | S→C | LLM 流式增量数据 |
| `LlmStreamEnd` | `"llm.stream.end"` | S→C | LLM 流结束 |
| `LlmError` | `"llm.error"` | S→C | LLM 错误 |
| `TaskUpdate` | `"task.update"` | S→C | 任务状态更新 |
| `TaskComplete` | `"task.complete"` | S→C | 任务完成 |
| `TaskError` | `"task.error"` | S→C | 任务错误 |
| `HostStatus` | `"host.status"` | S→C | Host 状态通知 |
| `Ack` | `"ack"` | S→C | 连接确认 |
| `Error` | `"error"` | S→C | 通用错误 |

## WsMessage 结构

```json
{
  "type": "llm.stream",
  "data": { "sessionId": "...", "delta": { "content": "..." } },
  "requestId": "可选",
  "timestamp": 1782233730093
}
```

## 注意事项

- `PropertyNameCaseInsensitive = true` 反序列化，客户端发 `type` 或 `Type` 均可
- 序列化使用 `JsonNamingPolicy.CamelCase`
