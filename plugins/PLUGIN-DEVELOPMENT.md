# 插件开发指南

> Ignorant Vega 插件系统 — 让你轻松扩展 Agent 能力

---

## 📋 目录

- [概述](#概述)
- [快速开始](#快速开始)
- [插件结构](#插件结构)
- [plugin.json 清单](#pluginjson-清单)
- [IPlugin 接口](#iplugin-接口)
- [IPluginContext 上下文](#iplugincontext-上下文)
- [端点注册](#端点注册)
- [Hub 注册 (实时通信)](#hub-注册-实时通信)
- [插件示例](#插件示例)
- [热重载](#热重载)
- [安全约束](#安全约束)
- [最佳实践](#最佳实践)

---

## 概述

Ignorant Vega 的插件系统支持两种模式：

| 模式 | 说明 | 适用场景 |
|------|------|----------|
| **声明式插件** | 只有 `plugin.json`，端点由宿主内置实现 | 简单功能、脚本扩展 |
| **程序集插件** | `plugin.json` + `EntryAssembly` DLL | 复杂功能、独立服务 |

**核心特性：**
- ✅ `plugin.json` 清单驱动
- ✅ `AssemblyLoadContext` 隔离加载（插件异常不影响主进程）
- ✅ `FileSystemWatcher` 热重载（文件变更自动重新加载）
- ✅ 错误隔离（插件崩溃不会导致 Agent 崩溃）
- ✅ **NuGet 包支持** — 创建插件时可指定 `PackageReferences`
- ✅ **插件启停控制** — `manage-plugin` 工具支持 enable/disable/uninstall
- ✅ **插件权限声明** — manifest 中声明所需权限
- ✅ **插件配置文件** — 自动加载 `plugin.config.json`

---

## 快速开始

### 1. 创建插件目录

```bash
mkdir plugins/my-plugin
```

### 2. 创建 plugin.json

```json
{
  "name": "my-plugin",
  "version": "1.0.0",
  "description": "我的第一个插件",
  "author": "Your Name",
  "dependencies": [],
  "endpoints": ["/api/my-plugin"],
  "hubs": [],
  "entryAssembly": ""
}
```

### 3. 重启 Agent.Host

插件会自动被发现和加载。

### 4. 验证

```bash
curl http://localhost:7300/api/plugins
```

---

## 插件结构

### 声明式插件（最简单）

```
plugins/
└── my-plugin/
    └── plugin.json          # 唯一必需文件
```

### 程序集插件（完整）

```
plugins/
└── my-plugin/
    ├── plugin.json          # 插件清单
    ├── MyPlugin.dll         # 插件程序集 (实现 IPlugin)
    ├── config.json          # 插件配置 (可选)
    └── wwwroot/             # 静态文件 (可选)
```

---

## plugin.json 清单

```json
{
  "name": "string",              // 插件名称 (必需，唯一标识)
  "version": "string",           // 版本号 (必需，语义化版本)
  "description": "string",       // 插件描述
  "author": "string",            // 作者
  "dependencies": ["string"],    // 依赖的其他插件名称
  "endpoints": ["string"],       // 声明的 API 端点路径
  "hubs": ["string"],            // 声明的 SignalR Hub
  "entryAssembly": "string",     // 入口程序集文件名 (空 = 声明式插件)
  "permissions": ["string"],     // 插件声明的权限: network, filesystem, system, tools, datadir
  "packageReferences": ["string"] // NuGet 包引用 (格式: "Name" 或 "Name:Version")
}
```

### 字段说明

| 字段 | 必需 | 说明 |
|------|------|------|
| `name` | ✅ | 插件唯一标识，建议用小写加连字符 |
| `version` | ✅ | 语义化版本 (如 `1.0.0`) |
| `description` | ❌ | 插件功能描述 |
| `author` | ❌ | 作者信息 |
| `dependencies` | ❌ | 依赖的其他插件名称列表 |
| `endpoints` | ❌ | 插件提供的 API 端点路径列表 |
| `hubs` | ❌ | 插件提供的 SignalR Hub 名称列表 |
| `entryAssembly` | ❌ | 入口 DLL 文件名，空表示声明式插件 |
| `permissions` | ❌ | 插件声明的权限列表: `network` (网络), `filesystem` (文件), `system` (系统), `tools` (工具调用), `datadir` (数据目录) |
| `packageReferences` | ❌ | NuGet 包引用列表，格式: `"PackageName"` 或 `"PackageName:1.0.0"` |

---

## IPlugin 接口

程序集插件必须实现 `IPlugin` 接口：

```csharp
using Agent.Core.Plugins;

public class MyPlugin : IPlugin
{
    public string Name => "my-plugin";
    public string Version => "1.0.0";
    public string Description => "我的插件";

    public async Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        // 插件初始化逻辑
        // - 注册端点
        // - 注册 Hub
        // - 初始化服务
        // - 加载配置

        context.AddEndpointRegistration(app =>
        {
            // 注册 API 端点
            var group = ((WebApplication)app).MapGroup("/api/my-plugin");
            group.MapGet("/", () => Results.Ok(new { message = "Hello from my plugin!" }));
        });
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        // 清理资源
        // - 关闭连接
        // - 保存状态
        // - 释放锁
    }
}
```

---

## IPluginContext 上下文

插件通过 `IPluginContext` 与宿主交互：

```csharp
public interface IPluginContext
{
    // 获取依赖注入的服务
    T GetService<T>() where T : notnull;

    // 插件专属数据目录 (用于持久化存储)
    string DataDir { get; }

    // 插件配置 (从 config.json 加载)
    IDictionary<string, object> Config { get; }

    // 注册端点 (延迟到所有插件加载完成后统一执行)
    void MapEndpoints(object app);

    // 注册 SignalR Hub
    void RegisterHub(object hubType);

    // 注册端点配置回调
    void AddEndpointRegistration(Action<object> registration);
}
```

### 常用操作

```csharp
public async Task InitializeAsync(IPluginContext context, CancellationToken ct)
{
    // 1. 获取服务
    var logger = context.GetService<ILogger<MyPlugin>>();
    var toolRegistry = context.GetService<IToolRegistry>();

    // 2. 读取配置
    var apiKey = context.Config.GetValueOrDefault("apiKey")?.ToString();

    // 3. 获取数据目录
    var dataFile = Path.Combine(context.DataDir, "cache.json");

    // 4. 注册端点
    context.AddEndpointRegistration(app => { /* ... */ });
}
```

---

## 端点注册

### 方式 1: 声明式（推荐简单场景）

在 `plugin.json` 中声明端点，由宿主内置实现：

```json
{
  "endpoints": ["/api/dashboard"]
}
```

宿主根据端点路径自动注册对应的处理器。

**当前支持的声明式端点：**
- `/api/dashboard` — 系统仪表盘 (overview/memory/processes)

### 方式 2: 程序集注册（推荐复杂场景）

在 `InitializeAsync` 中注册端点：

```csharp
context.AddEndpointRegistration(app =>
{
    var webApp = (WebApplication)app;
    var group = webApp.MapGroup("/api/my-plugin")
        .WithTags("My Plugin")
        .WithDescription("我的插件");

    // GET /api/my-plugin/status
    group.MapGet("/status", () => Results.Ok(new
    {
        status = "running",
        timestamp = DateTime.Now
    }))
    .WithName("MyPluginStatus")
    .WithDescription("插件状态");

    // POST /api/my-plugin/action
    group.MapPost("/action", async (ActionRequest request, CancellationToken ct) =>
    {
        // 处理请求
        return Results.Ok(new { result = "done" });
    })
    .WithName("MyPluginAction")
    .WithDescription("执行操作");
});
```

### 端点命名规范

- 使用 `.WithName("UniqueName")` 给端点命名（必须全局唯一）
- 命名格式建议：`{PluginName}{Action}`（如 `DashboardOverview`、`MyPluginStatus`）

---

## Hub 注册 (实时通信)

注册 SignalR Hub 用于实时推送：

```csharp
public async Task InitializeAsync(IPluginContext context, CancellationToken ct)
{
    context.RegisterHub(typeof(MyPluginHub));
}

// Hub 实现
public class MyPluginHub : Hub
{
    public async Task SendMessage(string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
}
```

> ⚠️ SignalR 支持尚未完全集成，当前版本仅记录日志。

---

## 插件示例

### 示例 1: 系统仪表盘插件

```
plugins/
└── system-dashboard/
    └── plugin.json
```

plugin.json:
```json
{
  "name": "system-dashboard",
  "version": "1.0.0",
  "description": "系统仪表盘 — 实时显示 CPU、内存、磁盘、网络状态",
  "author": "Ignorant Vega",
  "dependencies": [],
  "endpoints": ["/api/dashboard"],
  "hubs": [],
  "entryAssembly": ""
}
```

宿主自动注册以下端点：
- `GET /api/dashboard/overview` — 系统概览
- `GET /api/dashboard/memory` — 内存详情
- `GET /api/dashboard/processes` — 进程列表

### 示例 2: 快捷命令插件

```
plugins/
└── quick-commands/
    └── plugin.json
```

> 当前为声明式插件，端点 `/api/quick-commands` 尚未实现内置处理器。

---

## 热重载

插件系统支持文件监控热重载：

1. **新增插件**: 在 `plugins/` 目录创建新文件夹 + `plugin.json`，自动加载
2. **修改插件**: 修改 `plugin.json` 或 DLL，自动重新加载
3. **删除插件**: 删除插件文件夹，自动卸载

热重载通过 `FileSystemWatcher` 实现，延迟 500ms 防抖。

---

## 安全约束

| 约束 | 说明 |
|------|------|
| **进程隔离** | 插件在同一进程内，但通过 `AssemblyLoadContext` 隔离 |
| **异常捕获** | 插件异常不会导致主进程崩溃 |
| **权限声明** | 插件需在 manifest 中声明所需权限 (`permissions` 字段) |
| **端点冲突** | 端点名称必须全局唯一，否则注册失败 |
| **启停控制** | 可通过 `manage-plugin` 工具启用/禁用/卸载插件 |

### 权限类型

| 权限 | 说明 |
|------|------|
| `network` | 访问网络 (HTTP 请求、API 调用) |
| `filesystem` | 读写文件系统 |
| `system` | 系统操作 (进程、服务、注册表) |
| `tools` | 调用其他工具 |
| `datadir` | 访问插件数据目录 |

---

## 最佳实践

1. **命名规范**: 插件名使用小写加连字符 (如 `my-plugin`)
2. **版本管理**: 使用语义化版本 (如 `1.0.0`)
3. **配置外置**: 敏感配置放在 `plugin.config.json`，不要硬编码
4. **日志记录**: 使用 `ILogger` 记录关键操作
5. **优雅关闭**: 在 `ShutdownAsync` 中清理资源
6. **错误处理**: 捕获异常并返回友好的错误信息
7. **权限最小化**: 只声明插件实际需要的权限
8. **NuGet 依赖**: 通过 `PackageReferences` 引入外部库，不要手动打包 DLL

---

## 参考

- [插件开发文档](plugins/PLUGIN-DEVELOPMENT.md)
- [示例插件: system-dashboard](plugins/system-dashboard/)
- [示例插件: quick-commands](plugins/quick-commands/)
