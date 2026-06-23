// ============================================================================
// 配置管理端点 — 运行时查看/修改配置
// ============================================================================

using Agent.Core.Config;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Agent.Host.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config")
            .WithTags("Config")
            .WithDescription("配置管理");

        // GET /api/config — 查看当前配置 (脱敏)
        group.MapGet("/", (IOptions<AgentConfig> config) =>
        {
            var cfg = config.Value;
            return Results.Ok(new
            {
                llm = new
                {
                    endpoint = cfg.Llm.Endpoint,
                    apiKey = MaskApiKey(cfg.Llm.ApiKey),
                    defaultModel = cfg.Llm.DefaultModel,
                },
                service = new { port = cfg.Service.Port },
            });
        })
        .WithName("GetConfig")
        .WithDescription("查看当前配置 (API Key 脱敏)");

        // PUT /api/config/apikey — 设置 API Key
        group.MapPut("/apikey", async (HttpContext http, IConfiguration configuration) =>
        {
            var body = await http.Request.ReadFromJsonAsync<SetApiKeyRequest>();
            if (body is null || string.IsNullOrWhiteSpace(body.ApiKey))
                return Results.BadRequest(new { error = "ApiKey 不能为空" });

            // 写入 data/config.json (优先项目根目录)
            var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"));
            if (!File.Exists(configPath))
                configPath = Path.Combine(AppContext.BaseDirectory, "data", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            Dictionary<string, object> config;
            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                config = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>();
            }
            else
            {
                config = new Dictionary<string, object>();
            }

            // 更新 Agent.Llm.ApiKey
            if (!config.ContainsKey("Agent"))
                config["Agent"] = new Dictionary<string, object>();

            var agent = (JsonElement)config["Agent"];
            var agentDict = JsonSerializer.Deserialize<Dictionary<string, object>>(agent.GetRawText())
                ?? new Dictionary<string, object>();

            if (!agentDict.ContainsKey("Llm"))
                agentDict["Llm"] = new Dictionary<string, object>();

            var llm = (JsonElement)agentDict["Llm"];
            var llmDict = JsonSerializer.Deserialize<Dictionary<string, object>>(llm.GetRawText())
                ?? new Dictionary<string, object>();

            llmDict["ApiKey"] = body.ApiKey;
            agentDict["Llm"] = llmDict;
            config["Agent"] = agentDict;

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, options));

            return Results.Ok(new { message = "API Key 已保存到 data/config.json", masked = MaskApiKey(body.ApiKey) });
        })
        .WithName("SetApiKey")
        .WithDescription("设置 LLM API Key");

        // GET /api/config/apikey — 检查 API Key 状态
        group.MapGet("/apikey", (IOptions<AgentConfig> config) =>
        {
            var key = config.Value.Llm.ApiKey;
            return Results.Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(key),
                masked = MaskApiKey(key),
            });
        })
        .WithName("GetApiKeyStatus")
        .WithDescription("检查 API Key 状态");
    }

    private static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "(未设置)";
        if (key.Length <= 8) return "***";
        return $"{key[..4]}...{key[^4..]}";
    }

    private sealed record SetApiKeyRequest(string ApiKey);
}
