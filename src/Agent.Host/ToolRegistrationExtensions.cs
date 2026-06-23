// ============================================================================
// 工具注册扩展 — 内置工具注册 + tools/ 目录扫描
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Coding;
using Agent.Core.Config;
using Agent.Core.SDK;
using Agent.Core.Tools;
using Microsoft.Extensions.Options;
using Serilog;

namespace Agent.Host;

/// <summary>
/// 工具注册扩展
/// </summary>
public static class ToolRegistrationExtensions
{
    /// <summary>
    /// 注册内置工具 + 扫描 tools/ 目录 + 组装 GroupTool
    /// </summary>
    public static void RegisterTools(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<IToolRegistry>();

        // 注册独立保留的内置工具
        registry.Register(app.Services.GetRequiredService<PowerShellCommandTool>());
        registry.Register(app.Services.GetRequiredService<SaveMemoryTool>());
        registry.Register(app.Services.GetRequiredService<BrowserTool>());
        registry.Register(app.Services.GetRequiredService<DownloaderTool>());
        registry.Register(app.Services.GetRequiredService<ArchiveTool>());
        registry.Register(app.Services.GetRequiredService<RunProcessTool>());

        // 扫描 tools/ 目录自动注册脚本和 EXE 工具
        var toolsDir = ResolveToolsDir();

        var scanner = new ToolScanner(
            registry,
            app.Services.GetRequiredService<ILogger<ToolScanner>>(),
            app.Services.GetRequiredService<ILogger<PowerShellTool>>(),
            app.Services.GetRequiredService<ILogger<ExecutableTool>>(),
            toolsDir,
            app.Services.GetService<RuntimeCompiler>(),
            app.Services.GetService<DotnetSdkManager>(),
            app.Services.GetService<ILogger<DotnetScriptTool>>());
        scanner.ScanAndRegister();
        scanner.StartWatching();

        // 扫描后注册 SDK 和插件组工具 (扫描器会清除 Group 类型)
        registry.Register(new GroupTool(
            "sdk-ops",
            "SDK开发: compile(编译C#)/nuget-search(搜索包)/nuget-list(已装包)/run(运行程序,支持中文应用名如'微信','记事本')",
            [
                ("compile", app.Services.GetRequiredService<CompileCodeTool>()),
                ("nuget-search", app.Services.GetRequiredService<NuGetSearchTool>()),
                ("nuget-list", app.Services.GetRequiredService<NuGetListTool>()),
                ("run", app.Services.GetRequiredService<RunProcessTool>()),
            ]));

        registry.Register(new GroupTool(
            "plugin-ops",
            "插件管理: create(创建)/list(列表)/reload(重载)",
            [
                ("create", app.Services.GetRequiredService<CreatePluginTool>()),
                ("list", app.Services.GetRequiredService<ListPluginsTool>()),
                ("reload", app.Services.GetRequiredService<ReloadPluginsTool>()),
            ]));

        // 应用关闭时停止文件监控
        app.Lifetime.ApplicationStopping.Register(() => scanner.StopWatching());
    }

    /// <summary>
    /// 解析 tools/ 目录路径 (兼容发布模式和开发模式)
    /// </summary>
    private static string ResolveToolsDir()
    {
        // 发布模式: Agent.Host/ 同级的 tools/
        var publishDir = Path.Combine(AppContext.BaseDirectory, "..", "tools");
        if (Directory.Exists(publishDir))
            return publishDir;

        // 开发模式: bin/Debug/net10.0/ 上 5 级到项目根
        var devDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools"));
        if (Directory.Exists(devDir))
            return devDir;

        // 兜底
        return Path.Combine(AppContext.BaseDirectory, "tools");
    }
}
