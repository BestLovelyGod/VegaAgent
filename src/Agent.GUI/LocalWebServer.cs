using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agent.GUI;

/// <summary>
/// 本地 Web 服务器，为 WebView2 提供 UI 内容和配置 API
/// 环境管理（Host 启停、SDK 下载）已移至启动器
/// </summary>
public class LocalWebServer
{
    private WebApplication? _app;
    private Task? _appTask;
    private readonly int _port;
    private readonly string _hostUrl;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public LocalWebServer(int port = 5100, string? hostUrl = null)
    {
        _port = port;
        _hostUrl = hostUrl ?? Agent.Core.Config.AppConstants.DefaultBaseUrl;
    }

    public string Url => $"http://localhost:{_port}";

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        builder.WebHost.UseUrls($"http://localhost:{_port}");

        _app = builder.Build();

        _app.UseCors();

        // 静态文件服务
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            // UseDefaultFiles 必须在 UseStaticFiles 之前，让 "/" 自动映射到 index.html
            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });
        }

        // ── 核心服务状态（只读，不管理进程）──
        var coreGroup = _app.MapGroup("/api/core").WithTags("Core");

        coreGroup.MapGet("/status", async () =>
        {
            try
            {
                var resp = await _http.GetAsync($"{_hostUrl}/health");
                return Results.Ok(new { status = resp.IsSuccessStatusCode ? "running" : "stopped" });
            }
            catch
            {
                return Results.Ok(new { status = "stopped" });
            }
        });

        // ── LLM 配置 API ──
        var configGroup = _app.MapGroup("/api/config/llm").WithTags("LLM Config");

        configGroup.MapGet("/", async () =>
        {
            var path = FindDataFile("llm-config.json");
            if (path is null || !File.Exists(path))
                return Results.Ok(new { activeProvider = "mimo", activeModel = "mimo-v2.5", providers = new { }, modelConfigs = new { } });
            var json = await File.ReadAllTextAsync(path);
            return Results.Content(json, "application/json");
        });

        configGroup.MapPut("/provider", async (HttpRequest request) =>
        {
            var body = await request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (body is null || !body.TryGetValue("provider", out var provider))
                return Results.BadRequest(new { error = "缺少 provider" });
            var config = await LoadJsonAsync("llm-config.json");
            config["activeProvider"] = JsonSerializer.SerializeToElement(provider);
            await SaveJsonAsync("llm-config.json", config);
            return Results.Ok(new { message = "已切换" });
        });

        configGroup.MapPut("/model", async (HttpRequest request) =>
        {
            var body = await request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (body is null || !body.TryGetValue("model", out var model))
                return Results.BadRequest(new { error = "缺少 model" });
            var config = await LoadJsonAsync("llm-config.json");
            config["activeModel"] = JsonSerializer.SerializeToElement(model);
            await SaveJsonAsync("llm-config.json", config);
            return Results.Ok(new { message = "已切换" });
        });

        configGroup.MapPut("/provider/{id}/apikey", async (string id, HttpRequest request) =>
        {
            var body = await request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (body is null || !body.TryGetValue("apiKey", out var apiKey))
                return Results.BadRequest(new { error = "缺少 apiKey" });
            var config = await LoadJsonAsync("llm-config.json");
            if (!config.ContainsKey("providers")) return Results.BadRequest(new { error = "配置格式错误" });
            var providers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(config["providers"].GetRawText());
            if (providers is null || !providers.ContainsKey(id))
                return Results.BadRequest(new { error = $"提供商 '{id}' 不存在" });
            var providerDict = JsonSerializer.Deserialize<Dictionary<string, object>>(providers[id].GetRawText())
                ?? new Dictionary<string, object>();
            providerDict["apiKey"] = apiKey;
            providers[id] = JsonSerializer.SerializeToElement(providerDict);
            config["providers"] = JsonSerializer.SerializeToElement(providers);
            await SaveJsonAsync("llm-config.json", config);
            return Results.Ok(new { message = "API Key 已更新" });
        });

        // ── 提示词文件 API ──
        var promptGroup = _app.MapGroup("/api/prompt").WithTags("Prompt");

        promptGroup.MapGet("/{type}", (string type) =>
        {
            if (type is not ("agent" or "memory" or "user"))
                return Results.BadRequest(new { error = "无效类型" });
            var path = FindConfigFile($"{type}.md");
            if (path is null)
                return Results.Ok(new { content = "" });
            var content = File.ReadAllText(path);
            return Results.Ok(new { content, path, length = content.Length });
        });

        promptGroup.MapPut("/{type}", async (string type, HttpRequest request) =>
        {
            if (type is not ("agent" or "memory" or "user"))
                return Results.BadRequest(new { error = "无效类型" });
            var path = FindConfigFile($"{type}.md") ?? GetDefaultConfigPath($"{type}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var reader = new StreamReader(request.Body);
            var content = await reader.ReadToEndAsync();
            await File.WriteAllTextAsync(path, content);
            return Results.Ok(new { message = $"{type}.md 已更新", path, length = content.Length });
        });

        // ── 管理员权限检查 API ──
        var systemGroup = _app.MapGroup("/api/system").WithTags("System");

        systemGroup.MapGet("/admin-status", () =>
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            var userName = identity.Name;
            return Results.Ok(new { isAdmin, userName });
        });

        // ── 代理转发 API（将 UI 的请求代理到 Host）──
        _app.Map("/api/proxy/{**path}", async (HttpContext http, string path) =>
        {
            try
            {
                var url = $"{_hostUrl}/{path}";
                var method = http.Request.Method;
                HttpResponseMessage resp;

                if (method == "GET")
                {
                    resp = await _http.GetAsync(url);
                }
                else if (method == "POST")
                {
                    using var reader = new StreamReader(http.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    resp = await _http.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                }
                else if (method == "PUT")
                {
                    using var reader = new StreamReader(http.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    resp = await _http.PutAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                }
                else if (method == "DELETE")
                {
                    resp = await _http.DeleteAsync(url);
                }
                else
                {
                    return Results.BadRequest(new { error = "不支持的 HTTP 方法" });
                }

                var respBody = await resp.Content.ReadAsStringAsync();
                return Results.Content(respBody, "application/json", statusCode: (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"代理请求失败: {ex.Message}" });
            }
        });

        _appTask = _app.StartAsync();
        await Task.Delay(500);
    }

    public void Stop()
    {
        try
        {
            // 限时关闭，防止阻塞 UI 线程
            var task = _app?.StopAsync();
            if (task != null)
                Task.WhenAny(task, Task.Delay(3000)).GetAwaiter().GetResult();
        }
        catch { /* 忽略关闭异常 */ }
        try { _http.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[WebServer] Dispose 失败: {ex.Message}"); }
    }

    // ── 辅助方法 ──

    private static string? FindDataFile(string name)
    {
        // 1. 同级 data 目录
        var p1 = Path.Combine(AppContext.BaseDirectory, "data", name);
        if (File.Exists(p1)) return p1;

        // 2. 项目根目录 data
        var p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data", name));
        if (File.Exists(p2)) return p2;

        return null;
    }

    private static string? FindConfigFile(string name)
    {
        // 1. data/config 目录
        var p1 = Path.Combine(AppContext.BaseDirectory, "data", "config", name);
        if (File.Exists(p1)) return p1;

        // 2. 项目根目录
        var p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data", "config", name));
        if (File.Exists(p2)) return p2;

        return null;
    }

    private static string GetDefaultConfigPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "data", "config", name);
    }

    private static async Task<Dictionary<string, JsonElement>> LoadJsonAsync(string name)
    {
        var path = FindDataFile(name);
        if (path is null || !File.Exists(path))
            return new Dictionary<string, JsonElement>();
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
    }

    private static async Task SaveJsonAsync(string name, Dictionary<string, JsonElement> data)
    {
        var path = FindDataFile(name) ?? Path.Combine(AppContext.BaseDirectory, "data", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
