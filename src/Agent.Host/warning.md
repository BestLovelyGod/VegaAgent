# Agent.Host 风险点

## 必须遵循

1. **SystemPromptBuilder 构造函数**必须传入 `basePrompt`（身份提示词），不能传 agent.md 模板
2. **QuantumCore 数据目录**默认 `data/quantumcore/`，不要与其他存储路径冲突
3. **PaperCore 知识库端点**使用 `Results.Ok()` 而非 `Results.Created()`（中文 ID 在 Location header 会报错）
4. **端口配置**默认 7300，通过 `appsettings.json` 或环境变量 `AGENT_PORT` 覆盖
5. **CORS**仅允许 localhost，生产环境需收紧

## 风险点

- `onLlmChunk` 回调是同步的（`Action<LlmStreamChunk>`），不要在其中 await
- `BuildMessages` 返回 `IReadOnlyList<LlmMessage>` 而非 string
- MiMo API 的 `developer` 角色需始终注入身份提示词
