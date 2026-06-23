// ============================================================================
// create-plugin 工具 — LLM 自主创建插件
// ============================================================================

using System.Text;
using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Tools;

/// <summary>
/// create-plugin 工具 — 让 LLM 自主创建、编译和部署插件
/// 
/// LLM 调用此工具时:
///   1. 传入插件名称、描述、C# 源码
///   2. 工具自动编译为 DLL
///   3. 部署到 plugins/ 目录
///   4. PluginLoader 热重载自动加载
///   5. 插件端点立即可用
/// </summary>
public sealed class CreatePluginTool : ITool
{
    private readonly PluginBuilder _builder;
    private readonly PluginLoader _loader;
    private readonly ILogger<CreatePluginTool> _logger;

    public string Name => "create-plugin";
    public string Description => "创建部署插件(需IPlugin源码)";
    public ToolCategory Category => ToolCategory.SDKIntegrated;
    public RiskLevel RiskLevel => RiskLevel.Level1;

    public CreatePluginTool(PluginBuilder builder, PluginLoader loader, ILogger<CreatePluginTool> logger)
    {
        _builder = builder;
        _loader = loader;
        _logger = logger;
    }

    public ToolParameter[] GetParameters() =>
    [
        new() { Name = "Name", Type = "string", Description = "插件名(小写连字符)", Required = true },
        new() { Name = "Version", Type = "string", Description = "版本号", Required = false, Default = "1.0.0" },
        new() { Name = "Description", Type = "string", Description = "描述", Required = true },
        new() { Name = "SourceCode", Type = "string", Description = "C#源码(需实现IPlugin)", Required = true },
        new() { Name = "Endpoints", Type = "string", Description = "端点路径(逗号分隔)", Required = false },
        new() { Name = "PackageReferences", Type = "string", Description = "NuGet包(逗号分隔,格式:Name:Version 或 Name)", Required = false },
    ];

    public async Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken ct = default)
    {
        var name = request.Parameters.GetValueOrDefault("Name")?.ToString();
        var version = request.Parameters.GetValueOrDefault("Version")?.ToString() ?? "1.0.0";
        var description = request.Parameters.GetValueOrDefault("Description")?.ToString() ?? "";
        var sourceCode = request.Parameters.GetValueOrDefault("SourceCode")?.ToString();
        var endpointsStr = request.Parameters.GetValueOrDefault("Endpoints")?.ToString();
        var pkgRefStr = request.Parameters.GetValueOrDefault("PackageReferences")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return Fail(request, "缺少必需参数: Name");

        if (string.IsNullOrWhiteSpace(sourceCode))
            return Fail(request, "缺少必需参数: SourceCode");

        // 解析端点
        var endpoints = string.IsNullOrWhiteSpace(endpointsStr)
            ? []
            : endpointsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 解析 NuGet 包引用
        var packageRefs = string.IsNullOrWhiteSpace(pkgRefStr)
            ? []
            : pkgRefStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("创建插件: {Name} v{Version}", name, version);

        // 1. 编译并部署
        var result = await _builder.BuildAndDeployAsync(
            name, version, description, sourceCode, endpoints, packageRefs, ct);

        if (!result.Success)
        {
            var errorSb = new StringBuilder();
            errorSb.AppendLine($"插件构建失败: {result.Message}");
            errorSb.AppendLine();
            if (result.Errors.Count > 0)
            {
                errorSb.AppendLine("编译错误:");
                foreach (var error in result.Errors)
                    errorSb.AppendLine($"  - {error}");
            }

            return new ToolResult
            {
                RequestId = request.RequestId,
                Status = ToolResultStatus.Failed,
                Output = errorSb.ToString(),
                Error = result.Message,
                ToolName = Name
            };
        }

        // 2. 触发热重载
        try
        {
            await _loader.LoadAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "插件热重载失败，但插件已部署");
        }

        // 3. 返回结果
        var sb = new StringBuilder();
        sb.AppendLine($"✅ 插件创建成功！");
        sb.AppendLine();
        sb.AppendLine($"- **名称**: {result.PluginName}");
        sb.AppendLine($"- **版本**: {result.Version}");
        sb.AppendLine($"- **DLL**: `{result.DllPath}`");
        sb.AppendLine($"- **目录**: `{result.PluginDir}`");
        sb.AppendLine($"- **类型**: {result.TypeName}");

        if (endpoints.Length > 0)
        {
            sb.AppendLine($"- **端点**: {string.Join(", ", endpoints)}");
        }
        if (packageRefs.Length > 0)
        {
            sb.AppendLine($"- **NuGet 包**: {string.Join(", ", packageRefs)}");
        }
        if (endpoints.Length > 0 || packageRefs.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ 注意: 新端点需要重启 Agent.Host 才能生效。");
        }

        sb.AppendLine();
        sb.AppendLine("插件已部署到 plugins/ 目录，下次启动时自动加载。");

        return new ToolResult
        {
            RequestId = request.RequestId,
            Status = ToolResultStatus.Success,
            Output = sb.ToString(),
            ToolName = Name
        };
    }

    private static ToolResult Fail(ToolRequest request, string error) => new()
    {
        RequestId = request.RequestId,
        Status = ToolResultStatus.Failed,
        Error = error,
        Output = $"❌ {error}",
        ToolName = "create-plugin"
    };
}

/// <summary>
/// list-plugins 工具 — 列出已加载的插件
/// </summary>
public sealed class ListPluginsTool : ITool
{
    private readonly PluginLoader _loader;

    public string Name => "list-plugins";
    public string Description => "列出已加载插件";
    public ToolCategory Category => ToolCategory.SDKIntegrated;
    public RiskLevel RiskLevel => RiskLevel.Level0;

    public ListPluginsTool(PluginLoader loader)
    {
        _loader = loader;
    }

    public ToolParameter[] GetParameters() => [];

    public Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken ct = default)
    {
        var plugins = _loader.LoadedPlugins;

        var sb = new StringBuilder();
        sb.AppendLine($"已加载 {plugins.Count} 个插件:");
        sb.AppendLine();

        foreach (var (_, plugin) in plugins)
        {
            var m = plugin.Manifest;
            sb.AppendLine($"📦 **{m.Name}** v{m.Version}");
            sb.AppendLine($"   {m.Description}");
            if (m.Endpoints.Length > 0)
                sb.AppendLine($"   端点: {string.Join(", ", m.Endpoints)}");
            sb.AppendLine();
        }

        if (plugins.Count == 0)
            sb.AppendLine("没有已加载的插件。");

        return Task.FromResult(new ToolResult
        {
            RequestId = request.RequestId,
            Status = ToolResultStatus.Success,
            Output = sb.ToString(),
            ToolName = Name
        });
    }
}

/// <summary>
/// reload-plugins 工具 — 重新加载所有插件
/// </summary>
public sealed class ReloadPluginsTool : ITool
{
    private readonly PluginLoader _loader;

    public string Name => "reload-plugins";
    public string Description => "重载所有插件";
    public ToolCategory Category => ToolCategory.SDKIntegrated;
    public RiskLevel RiskLevel => RiskLevel.Level0;

    public ReloadPluginsTool(PluginLoader loader)
    {
        _loader = loader;
    }

    public ToolParameter[] GetParameters() => [];

    public async Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken ct = default)
    {
        var count = await _loader.ReloadAllAsync(ct);

        return new ToolResult
        {
            RequestId = request.RequestId,
            Status = ToolResultStatus.Success,
            Output = $"✅ 已原子重载 {count} 个插件",
            ToolName = Name
        };
    }
}

/// <summary>
/// manage-plugin 工具 — 启用/禁用/卸载插件
/// </summary>
public sealed class ManagePluginTool : ITool
{
    private readonly PluginLoader _loader;

    public string Name => "manage-plugin";
    public string Description => "管理插件(启用/禁用/卸载)";
    public ToolCategory Category => ToolCategory.SDKIntegrated;
    public RiskLevel RiskLevel => RiskLevel.Level1;

    public ManagePluginTool(PluginLoader loader)
    {
        _loader = loader;
    }

    public ToolParameter[] GetParameters() =>
    [
        new() { Name = "action", Type = "string", Description = "enable/disable/uninstall", Required = true },
        new() { Name = "name", Type = "string", Description = "插件名称", Required = true },
    ];

    public async Task<ToolResult> ExecuteAsync(ToolRequest request, CancellationToken ct = default)
    {
        var action = request.Parameters.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant();
        var name = request.Parameters.GetValueOrDefault("name")?.ToString();

        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(name))
            return new ToolResult { RequestId = request.RequestId, Status = ToolResultStatus.Failed, Error = "缺少 action 或 name 参数", ToolName = Name };

        var success = action switch
        {
            "enable" => _loader.EnablePlugin(name),
            "disable" => _loader.DisablePlugin(name),
            "uninstall" => await _loader.UninstallPluginAsync(name, ct),
            _ => false
        };

        var actionText = action switch
        {
            "enable" => "启用",
            "disable" => "禁用",
            "uninstall" => "卸载",
            _ => action
        };

        return new ToolResult
        {
            RequestId = request.RequestId,
            Status = success ? ToolResultStatus.Success : ToolResultStatus.Failed,
            Output = success ? $"✅ 插件已{actionText}: {name}" : $"❌ 操作失败: 插件 {name} 不存在或操作无效",
            ToolName = Name
        };
    }
}
