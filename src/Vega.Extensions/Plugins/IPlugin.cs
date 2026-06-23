// ============================================================================
// 插件接口
// ============================================================================

namespace Agent.Core.Plugins;

/// <summary>
/// 插件接口 — 所有插件必须实现此接口
/// </summary>
public interface IPlugin
{
    /// <summary>插件名称</summary>
    string Name { get; }

    /// <summary>插件版本</summary>
    string Version { get; }

    /// <summary>插件描述</summary>
    string Description { get; }

    /// <summary>初始化插件</summary>
    Task InitializeAsync(IPluginContext context, CancellationToken ct = default);

    /// <summary>关闭插件</summary>
    Task ShutdownAsync(CancellationToken ct = default);
}

/// <summary>
/// 插件上下文 — 提供插件与宿主的交互能力
/// </summary>
public interface IPluginContext
{
    /// <summary>获取服务</summary>
    T GetService<T>() where T : notnull;

    /// <summary>插件数据目录</summary>
    string DataDir { get; }

    /// <summary>插件配置</summary>
    IDictionary<string, object> Config { get; }

    /// <summary>注册 API 端点</summary>
    void MapEndpoints(object app);

    /// <summary>注册 SignalR Hub</summary>
    void RegisterHub(object hubType);
}

/// <summary>
/// 插件清单 — 描述插件元数据和能力
/// </summary>
public sealed record PluginManifest
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string[] Dependencies { get; init; } = [];
    public string[] Endpoints { get; init; } = [];
    public string[] Hubs { get; init; } = [];
    public string EntryAssembly { get; init; } = "";
    public string[] Permissions { get; init; } = []; // 插件声明的权限: network, filesystem, system, tools
    public string[] PackageReferences { get; init; } = []; // NuGet 包引用 (格式: "PackageName" 或 "PackageName:Version")
}

/// <summary>
/// 插件权限常量
/// </summary>
public static class PluginPermissions
{
    public const string Network = "network";       // 访问网络 (HTTP 请求)
    public const string Filesystem = "filesystem"; // 读写文件
    public const string System = "system";         // 系统操作 (进程/服务/注册表)
    public const string Tools = "tools";           // 调用其他工具
    public const string DataDir = "datadir";       // 访问插件数据目录

    public static readonly string[] All = [Network, Filesystem, System, Tools, DataDir];

    public static bool IsValid(string permission) => All.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
