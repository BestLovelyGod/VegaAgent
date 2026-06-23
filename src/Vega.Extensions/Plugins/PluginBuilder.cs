// ============================================================================
// 插件构建器 — 编译 .cs 文件为 DLL 插件并部署
// ============================================================================

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Plugins;

public class PluginBuilder
{
    private readonly ILogger<PluginBuilder> _logger;
    private readonly string _pluginsDir;

    public PluginBuilder(ILogger<PluginBuilder> logger, string? pluginsDir = null)
    {
        _logger = logger;
        _pluginsDir = pluginsDir ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    public virtual async Task<PluginBuildResult> BuildAndDeployAsync(
        string pluginName, string version, string description,
        string sourceCode, string[]? additionalEndpoints = null,
        string[]? packageReferences = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("构建插件: {Name} v{Version}", pluginName, version);

        var compilation = await CompilePluginAsync(pluginName, sourceCode, packageReferences, ct);
        if (!compilation.Success)
        {
            _logger.LogWarning("编译失败: {Errors}", string.Join("; ", compilation.Errors));
            return new PluginBuildResult { Success = false, Errors = compilation.Errors, Message = $"编译失败: {compilation.Errors.Count} 个错误" };
        }

        var validation = ValidatePlugin(compilation.Assembly!);
        if (!validation.IsValid)
            return new PluginBuildResult { Success = false, Errors = [validation.Error ?? "未知错误"], Message = "插件验证失败" };

        var pluginDir = Path.Combine(_pluginsDir, pluginName);
        Directory.CreateDirectory(pluginDir);

        var dllPath = Path.Combine(pluginDir, $"{pluginName}.dll");
        await File.WriteAllBytesAsync(dllPath, compilation.DllBytes!, ct);

        var manifest = new PluginManifest { Name = pluginName, Version = version, Description = description, Author = "Ignorant Vega", EntryAssembly = $"{pluginName}.dll", Endpoints = additionalEndpoints ?? [], Dependencies = [], PackageReferences = packageReferences ?? [] };
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifestJson, ct);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, $"{pluginName}.cs"), sourceCode, ct);

        _logger.LogInformation("插件已部署: {Dir}", pluginDir);
        return new PluginBuildResult { Success = true, PluginName = pluginName, Version = version, DllPath = dllPath, PluginDir = pluginDir, Message = $"插件 {pluginName} v{version} 已成功构建并部署", TypeName = validation.TypeName };
    }

    private async Task<CompilationResult> CompilePluginAsync(string assemblyName, string sourceCode, string[]? packageReferences, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var agentCorePath = typeof(IPlugin).Assembly.Location;

            // 生成 PackageReference XML
            var packageRefsXml = "";
            if (packageReferences is { Length: > 0 })
            {
                var sb = new StringBuilder();
                foreach (var pkg in packageReferences)
                {
                    var parts = pkg.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var name = parts[0];
                    var version = parts.Length > 1 ? parts[1] : "*";
                    sb.Append($"<PackageReference Include=\"{name}\" Version=\"{version}\" />");
                }
                packageRefsXml = sb.ToString();
            }

            var csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">
<PropertyGroup>
  <OutputType>Library</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <AssemblyName>{assemblyName}</AssemblyName>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
<ItemGroup>
  <Reference Include=""Agent.Core""><HintPath>{agentCorePath}</HintPath></Reference>
</ItemGroup>
{(string.IsNullOrEmpty(packageRefsXml) ? "" : $"<ItemGroup>{packageRefsXml}</ItemGroup>")}
</Project>";
            await File.WriteAllTextAsync(Path.Combine(tempDir, $"{assemblyName}.csproj"), csproj, ct);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Plugin.cs"), sourceCode, ct);

            // 先 restore (下载 NuGet 包)
            if (packageReferences is { Length: > 0 })
            {
                _logger.LogInformation("还原 NuGet 包: {Packages}", string.Join(", ", packageReferences));
                var restorePsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"restore \"{tempDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var restoreProcess = System.Diagnostics.Process.Start(restorePsi);
                if (restoreProcess is not null)
                {
                    await restoreProcess.WaitForExitAsync(ct);
                    if (restoreProcess.ExitCode != 0)
                    {
                        var restoreErr = await restoreProcess.StandardError.ReadToEndAsync(ct);
                        return new CompilationResult { Success = false, Errors = [$"NuGet restore 失败: {restoreErr.Trim()}"] };
                    }
                }
            }

            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "dotnet", Arguments = $"build \"{tempDir}\" -c Release -o \"{Path.Combine(tempDir, "out")}\"", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return new CompilationResult { Success = false, Errors = ["无法启动 dotnet build"] };

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var errors = (stdout + "\n" + stderr).Split('\n').Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).ToList();
                return new CompilationResult { Success = false, Errors = errors.Count > 0 ? errors : ["编译失败", stdout, stderr] };
            }

            var dllPath = Path.Combine(tempDir, "out", $"{assemblyName}.dll");
            if (!File.Exists(dllPath)) return new CompilationResult { Success = false, Errors = [$"DLL 不存在: {dllPath}"] };

            var bytes = await File.ReadAllBytesAsync(dllPath, ct);
            return new CompilationResult { Success = true, DllBytes = bytes, Assembly = Assembly.Load(bytes) };
        }
        finally { try { Directory.Delete(tempDir, true); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PluginBuilder] 清理临时目录失败: {ex.Message}"); } }
    }

    private static PluginValidation ValidatePlugin(Assembly assembly)
    {
        var t = assembly.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);
        return t is null ? new PluginValidation { IsValid = false, Error = "未找到 IPlugin 实现" } : new PluginValidation { IsValid = true, TypeName = t.FullName };
    }

    private class CompilationResult { public bool Success; public byte[]? DllBytes; public Assembly? Assembly; public List<string> Errors = []; }
    private class PluginValidation { public bool IsValid; public string? Error; public string? TypeName; }
}

public sealed class PluginBuildResult
{
    public bool Success { get; init; }
    public string PluginName { get; init; } = "";
    public string Version { get; init; } = "";
    public string? DllPath { get; init; }
    public string? PluginDir { get; init; }
    public string Message { get; init; } = "";
    public string? TypeName { get; init; }
    public List<string> Errors { get; init; } = [];
}
