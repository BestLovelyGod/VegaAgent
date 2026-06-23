# Vega.Extensions 风险点

## 必须遵循

1. **RootNamespace=Agent.Core**：Vega.Extensions 使用 `Agent.Core` 命名空间，避免与开源 Agent.Core 冲突
2. **Git 依赖 LibGit2Sharp**：仅 Vega.Extensions 引用，不要泄露到其他项目
3. **Plugins/PluginBuilder** 使用 Roslyn 编译：临时目录必须清理
4. **PluginLoader 支持热重载**：FileSystemWatcher 需防抖 (500ms)

## 风险点

- PluginBuilder.CompilePluginAsync 使用 `dotnet build` 子进程，需处理超时
- GitService.AutoCommitAsync 不要自动 push（除非用户明确要求）
- PluginLoader.UninstallPluginAsync 备份到 .bak，不要直接删除
