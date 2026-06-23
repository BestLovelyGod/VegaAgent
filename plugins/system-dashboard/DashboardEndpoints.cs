// ============================================================================
// 系统仪表盘插件 — 提供系统状态 API 端点
// ============================================================================
//
// 这是一个纯脚本插件示例 (无 entryAssembly)，通过 plugin.json 声明端点，
// 由 PluginLoader 发现并在宿主中注册对应的 API。
//
// 本插件的功能通过 Agent.Host 内置的 PluginEndpoints 实现，
// 展示了插件清单驱动的端点注册模式。

using System.Diagnostics;
using System.Management;

namespace Agent.Host.Plugins.SystemDashboard;

/// <summary>
/// 系统仪表盘端点注册器
/// 
/// 由 PluginLoader 在加载 plugin.json 后自动调用，
/// 将 /api/dashboard 端点注册到 WebApplication。
/// </summary>
public static class DashboardEndpoints
{
    public static void Register(WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("System Dashboard")
            .WithDescription("系统仪表盘 (插件)");

        // GET /api/dashboard/overview — 系统概览
        group.MapGet("/overview", () =>
        {
            var cpu = Environment.ProcessorCount;
            var process = Process.GetCurrentProcess();
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            return Results.Ok(new
            {
                timestamp = DateTime.Now,
                hostname = Environment.MachineName,
                username = Environment.UserName,
                os = Environment.OSVersion.ToString(),
                dotnet = Environment.Version.ToString(),
                cpuCores = cpu,
                uptime = new
                {
                    days = uptime.Days,
                    hours = uptime.Hours,
                    minutes = uptime.Minutes,
                    totalHours = Math.Round(uptime.TotalHours, 1)
                },
                process = new
                {
                    pid = process.Id,
                    name = process.ProcessName,
                    memoryMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
                    threads = process.Threads.Count,
                    startTime = process.StartTime
                }
            });
        })
        .WithName("DashboardOverview")
        .WithDescription("系统概览");

        // GET /api/dashboard/memory — 内存详情
        group.MapGet("/memory", () =>
        {
            var process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();

            return Results.Ok(new
            {
                timestamp = DateTime.Now,
                process = new
                {
                    workingSetMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
                    privateMemoryMB = Math.Round(process.PrivateMemorySize64 / 1024.0 / 1024.0, 1),
                    gcTotalMemoryMB = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1)
                },
                gc = new
                {
                    gen0Collections = GC.CollectionCount(0),
                    gen1Collections = GC.CollectionCount(1),
                    gen2Collections = GC.CollectionCount(2),
                    heapSizeMB = Math.Round(gc.HeapSizeBytes / 1024.0 / 1024.0, 1),
                    fragmentedMB = Math.Round(gc.FragmentedBytes / 1024.0 / 1024.0, 1)
                }
            });
        })
        .WithName("DashboardMemory")
        .WithDescription("内存详情");

        // GET /api/dashboard/processes — 进程列表 (Top N)
        group.MapGet("/processes", (int? top) =>
        {
            var count = top ?? 20;
            var processes = Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64)
                .Take(count)
                .Select(p => new
                {
                    pid = p.Id,
                    name = p.ProcessName,
                    memoryMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                    threads = p.Threads.Count
                });

            return Results.Ok(new
            {
                timestamp = DateTime.Now,
                total = Process.GetProcesses().Length,
                top = count,
                processes
            });
        })
        .WithName("DashboardProcesses")
        .WithDescription("进程列表");
    }
}
