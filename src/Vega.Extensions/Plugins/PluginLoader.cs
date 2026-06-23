// ============================================================================
// 插件加载器 — 发现、加载、管理插件
// ============================================================================

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Plugins;

/// <summary>
/// 插件加载器 — 扫描 plugins/ 目录，加载插件
/// 
/// 支持:
///   - plugin.json 清单文件
///   - AssemblyLoadContext 隔离加载
///   - FileSystemWatcher 热重载
///   - 错误隔离 (插件异常不影响主进程)
/// </summary>
public class PluginLoader : IDisposable, IAsyncDisposable
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly IServiceProvider _serviceProvider;
    private string _pluginsDir;
    private readonly Dictionary<string, LoadedPlugin> _plugins = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private const int DebounceMs = 500;

    public PluginLoader(ILogger<PluginLoader> logger, IServiceProvider serviceProvider, string? pluginsDir = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _pluginsDir = pluginsDir ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    /// <summary>已加载的插件</summary>
    public virtual IReadOnlyDictionary<string, LoadedPlugin> LoadedPlugins => _plugins;

    /// <summary>设置插件目录</summary>
    public void SetPluginsDir(string pluginsDir)
    {
        _pluginsDir = pluginsDir;
    }

    /// <summary>
    /// 扫描并加载所有插件
    /// </summary>
    public virtual async Task<int> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_pluginsDir))
        {
            _logger.LogDebug("插件目录不存在: {Dir}", _pluginsDir);
            return 0;
        }

        var count = 0;
        foreach (var pluginDir in Directory.GetDirectories(_pluginsDir))
        {
            try
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath))
                    continue;

                var loaded = await LoadPluginAsync(pluginDir, ct);
                if (loaded) count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载插件失败: {Dir}", pluginDir);
            }
        }

        _logger.LogInformation("插件加载完成: {Count} 个", count);
        return count;
    }

    /// <summary>
    /// 加载单个插件
    /// </summary>
    private async Task<bool> LoadPluginAsync(string pluginDir, CancellationToken ct)
    {
        var manifestPath = Path.Combine(pluginDir, "plugin.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null)
        {
            _logger.LogWarning("插件清单解析失败: {Path}", manifestPath);
            return false;
        }

        _logger.LogInformation("加载插件: {Name} v{Version}", manifest.Name, manifest.Version);

        // 加载插件程序集 (如果有)
        IPlugin? plugin = null;
        AssemblyLoadContext? alc = null;

        if (!string.IsNullOrEmpty(manifest.EntryAssembly))
        {
            var assemblyPath = Path.Combine(pluginDir, manifest.EntryAssembly);
            if (File.Exists(assemblyPath))
            {
                alc = new AssemblyLoadContext(manifest.Name, isCollectible: true);
                var assembly = alc.LoadFromAssemblyPath(assemblyPath);

                // 查找 IPlugin 实现
                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                if (pluginType is not null)
                {
                    plugin = (IPlugin?)Activator.CreateInstance(pluginType);
                }
            }
        }

        // 创建插件上下文
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data", "plugins", manifest.Name);
        Directory.CreateDirectory(dataDir);

        var context = new PluginContext(dataDir, serviceProvider: _serviceProvider, logger: _logger);

        // 初始化插件
        if (plugin is not null)
        {
            await plugin.InitializeAsync(context, ct);
        }

        _plugins[manifest.Name] = new LoadedPlugin
        {
            Manifest = manifest,
            Plugin = plugin,
            Context = context,
            LoadContext = alc,
            Directory = pluginDir
        };

        return true;
    }

    /// <summary>
    /// 启动文件监控 (热重载)
    /// </summary>
    public void StartWatching()
    {
        if (!Directory.Exists(_pluginsDir)) return;

        _watcher = new FileSystemWatcher(_pluginsDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, e) =>
        {
            _logger.LogInformation("插件文件变更: {Path}", e.FullPath);
            // 防抖: 合并连续事件后单次重载 (M43)
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ =>
            {
                try { await LoadAllAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "插件热重载失败"); }
            }, null, DebounceMs, Timeout.Infinite);
        };

        _logger.LogInformation("插件热重载监控已启动");
    }

    /// <summary>停止文件监控</summary>
    public void StopWatching()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    // ── 插件管理 API ──

    /// <summary>启用插件</summary>
    public bool EnablePlugin(string name)
    {
        if (_plugins.TryGetValue(name, out var plugin))
        {
            plugin.Enabled = true;
            _logger.LogInformation("插件已启用: {Name}", name);
            return true;
        }
        return false;
    }

    /// <summary>禁用插件 (不卸载，仅标记)</summary>
    public bool DisablePlugin(string name)
    {
        if (_plugins.TryGetValue(name, out var plugin))
        {
            plugin.Enabled = false;
            _logger.LogInformation("插件已禁用: {Name}", name);
            return true;
        }
        return false;
    }

    /// <summary>卸载并删除插件</summary>
    public async Task<bool> UninstallPluginAsync(string name, CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(name, out var plugin))
            return false;

        try
        {
            if (plugin.Plugin is not null)
                await plugin.Plugin.ShutdownAsync(ct);
            plugin.LoadContext?.Unload();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "卸载插件失败: {Name}", name);
        }

        _plugins.Remove(name);

        // 删除插件目录 (备份到 .bak)
        try
        {
            if (Directory.Exists(plugin.Directory))
            {
                var backupDir = plugin.Directory + ".bak";
                if (Directory.Exists(backupDir))
                    Directory.Delete(backupDir, true);
                Directory.Move(plugin.Directory, backupDir);
                _logger.LogInformation("插件目录已备份: {Dir} → {Backup}", plugin.Directory, backupDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除插件目录失败: {Dir}", plugin.Directory);
        }

        _logger.LogInformation("插件已卸载: {Name}", name);
        return true;
    }

    // ── 热重载原子替换 ──

    /// <summary>
    /// 原子重载: 先加载到临时集合验证，再替换已加载插件
    /// </summary>
    public virtual async Task<int> ReloadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_pluginsDir))
            return 0;

        var tempPlugins = new Dictionary<string, LoadedPlugin>();
        var count = 0;

        foreach (var pluginDir in Directory.GetDirectories(_pluginsDir))
        {
            try
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath))
                    continue;

                // 加载到临时集合 (不影响当前运行的插件)
                var loaded = await LoadPluginToDictAsync(pluginDir, tempPlugins, ct);
                if (loaded) count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重载插件失败: {Dir}", pluginDir);
            }
        }

        // 原子交换: 替换已加载插件字典
        var oldPlugins = new Dictionary<string, LoadedPlugin>(_plugins);
        _plugins.Clear();
        foreach (var kv in tempPlugins)
            _plugins[kv.Key] = kv.Value;

        // 卸载旧插件中不再存在的插件
        foreach (var (name, oldPlugin) in oldPlugins)
        {
            if (!_plugins.ContainsKey(name))
            {
                try
                {
                    if (oldPlugin.Plugin is not null)
                        await oldPlugin.Plugin.ShutdownAsync(ct);
                    oldPlugin.LoadContext?.Unload();
                    _logger.LogInformation("卸载旧插件: {Name}", name);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "卸载旧插件失败: {Name}", name); }
            }
        }

        _logger.LogInformation("插件原子重载完成: {Count} 个", count);
        return count;
    }

    private async Task<bool> LoadPluginToDictAsync(string pluginDir, Dictionary<string, LoadedPlugin> target, CancellationToken ct)
    {
        var manifestPath = Path.Combine(pluginDir, "plugin.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null) return false;

        // 检查权限合法性
        foreach (var perm in manifest.Permissions)
        {
            if (!PluginPermissions.IsValid(perm))
            {
                _logger.LogWarning("插件 {Name} 声明了无效权限: {Perm}", manifest.Name, perm);
            }
        }

        // 加载插件程序集
        IPlugin? plugin = null;
        AssemblyLoadContext? alc = null;

        if (!string.IsNullOrEmpty(manifest.EntryAssembly))
        {
            var assemblyPath = Path.Combine(pluginDir, manifest.EntryAssembly);
            if (File.Exists(assemblyPath))
            {
                alc = new AssemblyLoadContext(manifest.Name, isCollectible: true);
                var assembly = alc.LoadFromAssemblyPath(assemblyPath);
                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);
                if (pluginType is not null)
                    plugin = (IPlugin?)Activator.CreateInstance(pluginType);
            }
        }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data", "plugins", manifest.Name);
        Directory.CreateDirectory(dataDir);

        // P1: 自动加载 plugin.config.json
        var context = new PluginContext(dataDir, serviceProvider: _serviceProvider, logger: _logger);
        await context.LoadConfigAsync(pluginDir, ct);

        if (plugin is not null)
            await plugin.InitializeAsync(context, ct);

        target[manifest.Name] = new LoadedPlugin
        {
            Manifest = manifest,
            Plugin = plugin,
            Context = context,
            LoadContext = alc,
            Directory = pluginDir
        };

        return true;
    }

    public void Dispose()
    {
        StopWatching();
        // 同步清理: 只卸载 LoadContext (H38: 避免 sync-over-async)
        foreach (var loaded in _plugins.Values)
        {
            try { loaded.LoadContext?.Unload(); }
            catch (Exception ex) { _logger.LogWarning(ex, "关闭插件失败: {Name}", loaded.Manifest.Name); }
        }
        _plugins.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        StopWatching();
        // 异步清理: 正确等待 ShutdownAsync (H38)
        foreach (var loaded in _plugins.Values)
        {
            try
            {
                if (loaded.Plugin is not null)
                    await loaded.Plugin.ShutdownAsync();
                loaded.LoadContext?.Unload();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "关闭插件失败: {Name}", loaded.Manifest.Name); }
        }
        _plugins.Clear();
    }
}

/// <summary>已加载的插件</summary>
public sealed class LoadedPlugin
{
    public required PluginManifest Manifest { get; init; }
    public IPlugin? Plugin { get; init; }
    public required PluginContext Context { get; init; }
    public AssemblyLoadContext? LoadContext { get; init; }
    public required string Directory { get; init; }
    public bool Enabled { get; set; } = true;
    public DateTime LoadedAt { get; init; } = DateTime.Now;
}

/// <summary>插件上下文实现</summary>
public sealed class PluginContext : IPluginContext
{
    private readonly Dictionary<string, object> _config = new();
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger? _logger;
    private readonly List<Action<object>> _endpointRegistrations = new();
    private readonly List<Type> _hubTypes = new();
    private string? _pluginDir;

    public PluginContext(string dataDir, IServiceProvider? serviceProvider = null, ILogger? logger = null)
    {
        DataDir = dataDir;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string DataDir { get; }
    public IDictionary<string, object> Config => _config;

    /// <summary>已注册的端点配置</summary>
    public IReadOnlyList<Action<object>> EndpointRegistrations => _endpointRegistrations;

    /// <summary>已注册的 Hub 类型</summary>
    public IReadOnlyList<Type> HubTypes => _hubTypes;

    // ── P1: 插件配置文件支持 ──

    /// <summary>从 plugin.config.json 加载配置</summary>
    public async Task LoadConfigAsync(string pluginDir, CancellationToken ct = default)
    {
        _pluginDir = pluginDir;
        var configPath = Path.Combine(pluginDir, "plugin.config.json");
        if (!File.Exists(configPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                _config[prop.Name] = prop.Value.ToString();
            }
            _logger?.LogInformation("加载插件配置: {Config}", configPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载插件配置失败: {Config}", configPath);
        }
    }

    /// <summary>保存配置到 plugin.config.json</summary>
    public async Task SaveConfigAsync(CancellationToken ct = default)
    {
        if (_pluginDir is null) return;
        var configPath = Path.Combine(_pluginDir, "plugin.config.json");
        var json = System.Text.Json.JsonSerializer.Serialize(_config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json, ct);
        _logger?.LogInformation("保存插件配置: {Config}", configPath);
    }

    public T GetService<T>() where T : notnull
    {
        if (_serviceProvider is null)
            throw new InvalidOperationException("服务提供者未配置，无法解析服务");

        return (T?)_serviceProvider.GetService(typeof(T))
            ?? throw new InvalidOperationException($"服务 {typeof(T).Name} 未注册");
    }

    public void MapEndpoints(object app)
    {
        // 端点注册延迟到所有插件加载完成后统一执行
        _logger?.LogInformation("插件注册端点配置");
    }

    /// <summary>
    /// 注册端点配置回调 (插件在 InitializeAsync 中调用)
    /// </summary>
    public void AddEndpointRegistration(Action<object> registration)
    {
        _endpointRegistrations.Add(registration);
        _logger?.LogInformation("插件端点配置已注册");
    }

    public void RegisterHub(object hubType)
    {
        if (hubType is Type type)
        {
            _hubTypes.Add(type);
            _logger?.LogInformation("注册插件 Hub: {Type}", type.Name);
        }
    }
}
