# Program

> 命名空间: (全局)

## 职责

Agent.Host 主机进程入口 — 配置加载、服务注册、中间件管道、端点映射、插件加载。

## 中间件管道

````
UseCors → UseWebSockets → UseSerilogRequestLogging → UseSwagger (Dev)
```

## WebSocket 集成

- `builder.Services.AddSingleton<VegaWebSocketHub>()` — Hub 注册为单例
- `app.Map("/ws", ...)` — WebSocket 端点接受升级请求
- `app.Lifetime.ApplicationStarted` 回调中启动心跳
- `app.Lifetime.ApplicationStopping` 回调中调 `hub.StopAsync()`

## 端点映射

| 端点组 | 路径前缀 |
|--------|----------|
| Health | `/health` |
| Tasks | `/api/tasks` |
| Config | `/api/config` |
| Tools | `/api/tools` |
| Audit | `/api/audit` |
| OpenAI | `/v1` |
| Plugins | `/api/plugins` |
| Prompts | `/api/prompts` |
| Browser | `/api/browser` |
| Sessions | `/api/sessions` |
| LLM Config | `/api/llm-config` |
| Knowledge | `/api/knowledge` |
| WebSocket | `/ws` |

