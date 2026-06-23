// ============================================================================
// 插件管理 API 端点
// ============================================================================

using Agent.Core.Plugins;

namespace Agent.Host.Endpoints;

/// <summary>
/// 插件管理 API — 列出、加载、卸载插件
/// </summary>
public static class PluginEndpoints
{
    public static void MapPluginEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/plugins")
            .WithTags("Plugins")
            .WithDescription("插件管理");

        // GET /api/plugins — 列出所有插件
        group.MapGet("/", (PluginLoader loader) =>
        {
            var plugins = loader.LoadedPlugins.Values.Select(p => new
            {
                name = p.Manifest.Name,
                version = p.Manifest.Version,
                description = p.Manifest.Description,
                author = p.Manifest.Author,
                directory = p.Directory,
                hasAssembly = p.Plugin is not null,
                dependencies = p.Manifest.Dependencies,
                endpoints = p.Manifest.Endpoints,
                hubs = p.Manifest.Hubs
            });

            return Results.Ok(new
            {
                total = plugins.Count(),
                plugins
            });
        })
        .WithName("GetPlugins")
        .WithDescription("列出所有已加载的插件");

        // GET /api/plugins/{name} — 获取插件详情
        group.MapGet("/{name}", (string name, PluginLoader loader) =>
        {
            if (!loader.LoadedPlugins.TryGetValue(name, out var plugin))
                return Results.NotFound(new { error = $"插件 '{name}' 未加载" });

            return Results.Ok(new
            {
                name = plugin.Manifest.Name,
                version = plugin.Manifest.Version,
                description = plugin.Manifest.Description,
                author = plugin.Manifest.Author,
                directory = plugin.Directory,
                dataDir = plugin.Context.DataDir,
                hasAssembly = plugin.Plugin is not null,
                dependencies = plugin.Manifest.Dependencies,
                endpoints = plugin.Manifest.Endpoints,
                hubs = plugin.Manifest.Hubs
            });
        })
        .WithName("GetPlugin")
        .WithDescription("获取插件详情");

        // POST /api/plugins/reload — 重新加载所有插件
        group.MapPost("/reload", async (PluginLoader loader, CancellationToken ct) =>
        {
            var count = await loader.LoadAllAsync(ct);
            return Results.Ok(new
            {
                message = $"重新加载完成，共 {count} 个插件",
                count
            });
        })
        .WithName("ReloadPlugins")
        .WithDescription("重新加载所有插件");

        // GET /api/plugins/directory — 获取插件目录内容
        group.MapGet("/directory", (IConfiguration config) =>
        {
            var pluginsDir = config["Agent:Paths:PluginsDir"] ?? "plugins";
            var fullPath = Path.GetFullPath(pluginsDir);

            if (!Directory.Exists(fullPath))
                return Results.Ok(new { path = fullPath, exists = false, plugins = Array.Empty<object>() });

            var dirs = Directory.GetDirectories(fullPath).Select(d => new
            {
                name = Path.GetFileName(d),
                path = d,
                hasManifest = File.Exists(Path.Combine(d, "plugin.json")),
                files = Directory.GetFiles(d, "*", SearchOption.TopDirectoryOnly)
                    .Select(f => Path.GetFileName(f)).ToArray()
            });

            return Results.Ok(new { path = fullPath, exists = true, plugins = dirs });
        })
        .WithName("GetPluginDirectory")
        .WithDescription("查看插件目录内容");
    }
}
