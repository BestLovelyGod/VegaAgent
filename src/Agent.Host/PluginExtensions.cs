// ============================================================================
// 插件加载扩展 — 插件目录发现、加载、端点注册、仪表盘端点
// ============================================================================

using Agent.Core.Plugins;
using Serilog;

namespace Agent.Host;

/// <summary>
/// 插件加载扩展
/// </summary>
public static class PluginExtensions
{
    /// <summary>
    /// 加载插件、注册插件端点、启动文件监控
    /// </summary>
    public static async Task LoadPluginsAsync(this WebApplication app)
    {
        // 插件目录: 优先使用项目根目录的 plugins/
        var pluginsDir = ResolvePluginsDir();

        var pluginLoader = app.Services.GetRequiredService<PluginLoader>();
        pluginLoader.SetPluginsDir(pluginsDir);
        var pluginCount = await pluginLoader.LoadAllAsync();

        // 应用插件注册的端点
        foreach (var plugin in pluginLoader.LoadedPlugins.Values)
        {
            foreach (var registration in plugin.Context.EndpointRegistrations)
            {
                try { registration(app); }
                catch (Exception ex) { Log.Error(ex, "应用插件端点失败: {Plugin}", plugin.Manifest.Name); }
            }
        }

        // 内置插件端点注册 (基于 manifest 中的 endpoints 声明)
        RegisterBuiltinPluginEndpoints(app, pluginLoader);

        pluginLoader.StartWatching();
        app.Lifetime.ApplicationStopping.Register(() => pluginLoader.StopWatching());
    }

    /// <summary>
    /// 解析插件目录路径 (兼容发布模式和开发模式)
    /// </summary>
    private static string ResolvePluginsDir()
    {
        var publishDir = Path.Combine(AppContext.BaseDirectory, "..", "plugins");
        if (Directory.Exists(publishDir))
            return publishDir;

        var devDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins"));
        if (Directory.Exists(devDir))
            return devDir;

        return Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    /// <summary>
    /// 根据插件 manifest 中的 endpoints 声明注册内置处理器
    /// </summary>
    private static void RegisterBuiltinPluginEndpoints(WebApplication app, PluginLoader loader)
    {
        foreach (var (name, plugin) in loader.LoadedPlugins)
        {
            foreach (var endpoint in plugin.Manifest.Endpoints)
            {
                Log.Information("注册插件端点: {Plugin} → {Endpoint}", name, endpoint);

                if (endpoint == "/api/dashboard")
                {
                    RegisterDashboardEndpoints(app);
                }
            }
        }
    }

    /// <summary>
    /// 注册系统仪表盘端点
    /// </summary>
    private static void RegisterDashboardEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("System Dashboard")
            .WithDescription("系统仪表盘 (插件)");

        group.MapGet("/overview", () =>
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            return Results.Ok(new
            {
                timestamp = DateTime.Now,
                hostname = Environment.MachineName,
                username = Environment.UserName,
                os = Environment.OSVersion.ToString(),
                dotnet = Environment.Version.ToString(),
                cpuCores = Environment.ProcessorCount,
                uptime = new { days = uptime.Days, hours = uptime.Hours, minutes = uptime.Minutes, totalHours = Math.Round(uptime.TotalHours, 1) },
                process = new { pid = process.Id, name = process.ProcessName, memoryMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1), threads = process.Threads.Count }
            });
        })
        .WithName("DashboardOverview")
        .WithDescription("系统概览");

        group.MapGet("/memory", () =>
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();

            return Results.Ok(new
            {
                timestamp = DateTime.Now,
                process = new { workingSetMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1), privateMemoryMB = Math.Round(process.PrivateMemorySize64 / 1024.0 / 1024.0, 1), gcTotalMemoryMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1) },
                gc = new { gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2), heapSizeMB = Math.Round(gc.HeapSizeBytes / 1024.0 / 1024.0, 1) }
            });
        })
        .WithName("DashboardMemory")
        .WithDescription("内存详情");

        group.MapGet("/processes", (int? top) =>
        {
            var count = top ?? 20;
            var processes = System.Diagnostics.Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64)
                .Take(count)
                .Select(p => new { pid = p.Id, name = p.ProcessName, memoryMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1), threads = p.Threads.Count });

            return Results.Ok(new { timestamp = DateTime.Now, total = System.Diagnostics.Process.GetProcesses().Length, top = count, processes });
        })
        .WithName("DashboardProcesses")
        .WithDescription("进程列表");
    }
}
