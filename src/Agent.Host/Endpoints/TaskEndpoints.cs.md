# TaskEndpoints

> 命名空间: `Agent.Host.Endpoints`

## 职责

任务 API — 提交、查询、取消 Agent 任务，支持 SSE 流式输出。

## 公共方法

| 方法 | 说明 |
|------|------|
| `MapTaskEndpoints(app)` | 注册 `/api/tasks` CRUD + `/{id}/stream` 流式输出 |

## 双通道推送

`GET /api/tasks/{id}/stream` 同时通过 SSE 和 WebSocket 推送任务流式内容。

## 注意事项

- 任务流式输出通过 `VegaWebSocketHub.PushTaskUpdateAsync` 同步推送
- `PushWsSafe` 包装确保 WS 推送异常不影响 SSE 流

