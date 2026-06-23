# OpenAiEndpoints

> 命名空间: `Agent.Host.Endpoints`

## 职责

OpenAI 兼容 API 端点 — 聊天补全、模型列表、对话历史管理。所有请求统一走 AgentLoop，自动注入已注册工具。

## 公共方法

| 方法 | 说明 |
|------|------|
| `MapOpenAiEndpoints(app)` | 注册 `/v1/chat/completions`、`/v1/models`、`/v1/conversations/{id}` |

## 双通道推送

流式输出同时通过两个通道推送：
1. **SSE** (保持向后兼容): `text/event-stream` 格式
2. **WebSocket** (新通道): 通过 `VegaWebSocketHub.PushLlmChunkAsync` 广播

## 注意事项

- 对话历史限制 50 条，自动淘汰最早的
- WebSocket 推送使用 `PushWsSafe` 包装，异常不会影响 SSE
- `onLlmChunk` 回调是同步 `Action<LlmStreamChunk>`，WS 推送用 fire-and-forget

