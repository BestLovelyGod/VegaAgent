// ============================================================================
// LLM 配置管理 API — 多提供商支持
// ============================================================================

using Agent.Core.Models;
using System.Text.Json;

namespace Agent.Host.Endpoints;

public static class LlmConfigEndpoints
{
    private static readonly string ConfigFilePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "llm-config.json"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapLlmConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config/llm")
            .WithTags("LLM Config")
            .WithDescription("LLM 配置管理");

        // GET /api/config/llm — 获取 LLM 配置（API Key 脱敏）
        group.MapGet("/", async () =>
        {
            var config = await LoadConfigAsync();
            var maskedConfig = MaskConfig(config);
            return Results.Ok(maskedConfig);
        })
        .WithName("GetLlmConfig")
        .WithDescription("获取 LLM 配置（API Key 脱敏）");

        // PUT /api/config/llm/provider — 切换当前 API 提供商
        group.MapPut("/provider", async (SwitchProviderRequest request) =>
        {
            var config = await LoadConfigAsync();
            
            if (!config.Providers.ContainsKey(request.Provider))
                return Results.BadRequest(new { error = $"提供商 '{request.Provider}' 不存在" });

            config = config with { ActiveProvider = request.Provider };
            await SaveConfigAsync(config);

            return Results.Ok(new { message = $"已切换到 {config.Providers[request.Provider].Name}" });
        })
        .WithName("SwitchProvider")
        .WithDescription("切换当前 API 提供商");

        // PUT /api/config/llm/model — 切换当前模型
        group.MapPut("/model", async (SwitchModelRequest request) =>
        {
            var config = await LoadConfigAsync();
            
            // 验证模型是否在当前提供商的可用模型列表中
            if (config.Providers.TryGetValue(config.ActiveProvider, out var provider) &&
                !provider.Models.Contains(request.Model))
            {
                return Results.BadRequest(new { error = $"模型 '{request.Model}' 不在当前提供商的可用模型列表中" });
            }

            config = config with { ActiveModel = request.Model };
            await SaveConfigAsync(config);

            return Results.Ok(new { message = $"已切换到 {request.Model}" });
        })
        .WithName("SwitchModel")
        .WithDescription("切换当前模型");

        // PUT /api/config/llm/provider/{id}/apikey — 更新指定 API 的 Key
        group.MapPut("/provider/{id}/apikey", async (string id, UpdateApiKeyRequest request) =>
        {
            var config = await LoadConfigAsync();
            
            if (!config.Providers.TryGetValue(id, out var provider))
                return Results.BadRequest(new { error = $"提供商 '{id}' 不存在" });

            var updatedProvider = provider with { ApiKey = request.ApiKey };
            var providers = new Dictionary<string, LlmProvider>(config.Providers)
            {
                [id] = updatedProvider
            };
            config = config with { Providers = providers };
            await SaveConfigAsync(config);

            return Results.Ok(new { message = "API Key 已更新" });
        })
        .WithName("UpdateApiKey")
        .WithDescription("更新指定 API 的 Key");

        // PUT /api/config/llm/model/{id}/params — 更新模型参数
        group.MapPut("/model/{id}/params", async (string id, UpdateModelParamsRequest request) =>
        {
            var config = await LoadConfigAsync();
            
            if (!config.ModelConfigs.ContainsKey(id))
            {
                // 如果模型配置不存在，创建默认配置
                config.ModelConfigs[id] = new ModelConfig();
            }

            var currentConfig = config.ModelConfigs[id];
            var updatedConfig = currentConfig with
            {
                Temperature = request.Temperature ?? currentConfig.Temperature,
                MaxTokens = request.MaxTokens ?? currentConfig.MaxTokens,
                TopP = request.TopP ?? currentConfig.TopP,
                Thinking = request.Thinking ?? currentConfig.Thinking
            };

            var modelConfigs = new Dictionary<string, ModelConfig>(config.ModelConfigs)
            {
                [id] = updatedConfig
            };
            config = config with { ModelConfigs = modelConfigs };
            await SaveConfigAsync(config);

            return Results.Ok(new { message = "模型参数已更新" });
        })
        .WithName("UpdateModelParams")
        .WithDescription("更新模型参数");

        // POST /api/config/llm/provider/{id}/test — 测试指定 API 连接
        group.MapPost("/provider/{id}/test", async (string id, IHttpClientFactory httpClientFactory) =>
        {
            var config = await LoadConfigAsync();
            
            if (!config.Providers.TryGetValue(id, out var provider))
                return Results.BadRequest(new { error = $"提供商 '{id}' 不存在" });

            // llm-config.json 中 Key 为空时，从 config.json 兜底读取
            var apiKey = provider.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = GetHostConfigApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { error = "API Key 未配置" });

            try
            {
                var startTime = DateTime.UtcNow;
                var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                // 发送简单的模型列表请求测试连接
                var request = new HttpRequestMessage(HttpMethod.Get, $"{provider.BaseUrl}/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                
                var response = await httpClient.SendAsync(request);
                var latency = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                if (response.IsSuccessStatusCode)
                {
                    return Results.Ok(new
                    {
                        status = "ok",
                        latency,
                        model = config.ActiveModel,
                        provider = provider.Name
                    });
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return Results.Ok(new
                    {
                        status = "error",
                        latency,
                        error = $"API 返回错误: {response.StatusCode}",
                        details = errorContent
                    });
                }
            }
            catch (TaskCanceledException)
            {
                return Results.Ok(new { status = "error", error = "连接超时" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { status = "error", error = ex.Message });
            }
        })
        .WithName("TestProviderConnection")
        .WithDescription("测试指定 API 连接");
    }

    private static async Task<LlmFullConfig> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return CreateDefaultConfig();
            }

            var json = await File.ReadAllTextAsync(ConfigFilePath);
            return JsonSerializer.Deserialize<LlmFullConfig>(json, JsonOptions) ?? CreateDefaultConfig();
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private static async Task SaveConfigAsync(LlmFullConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(ConfigFilePath, json);
    }

    private static LlmFullConfig CreateDefaultConfig()
    {
        return new LlmFullConfig
        {
            ActiveProvider = "mimo",
            ActiveModel = "mimo-v2.5-pro",
            Providers = new Dictionary<string, LlmProvider>
            {
                ["mimo"] = new()
                {
                    Name = "MiMo API",
                    BaseUrl = "https://api.xiaomimimo.com/v1",
                    Models = new List<string> { "mimo-v2.5", "mimo-v2.5-pro" }
                },
                ["mimo-token-plan"] = new()
                {
                    Name = "MiMo Token Plan",
                    BaseUrl = "https://token-plan-cn.xiaomimimo.com/v1",
                    Models = new List<string> { "mimo-v2.5", "mimo-v2.5-pro" }
                },
                ["deepseek"] = new()
                {
                    Name = "DeepSeek API",
                    BaseUrl = "https://api.deepseek.com/v1",
                    Models = new List<string> { "deepseek-chat", "deepseek-coder", "deepseek-reasoner" }
                }
            },
            ModelConfigs = new Dictionary<string, ModelConfig>
            {
                ["mimo-v2.5"] = new()
                {
                    Temperature = 1.0,
                    MaxTokens = 32768,
                    TopP = 0.95,
                    Thinking = true
                },
                ["mimo-v2.5-pro"] = new()
                {
                    Temperature = 1.0,
                    MaxTokens = 131072,
                    TopP = 0.95,
                    Thinking = true
                }
            }
        };
    }

    private static object MaskConfig(LlmFullConfig config)
    {
        return new
        {
            config.ActiveProvider,
            config.ActiveModel,
            Providers = config.Providers.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.Name,
                    kvp.Value.BaseUrl,
                    ApiKey = MaskApiKey(kvp.Value.ApiKey),
                    kvp.Value.Models
                }),
            config.ModelConfigs
        };
    }

    private static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "(未设置)";
        if (key.Length <= 8) return "***";
        return $"{key[..4]}...{key[^4..]}";
    }

    /// <summary>从 config.json 读取 Host 实际使用的 API Key（兜底）</summary>
    private static string? GetHostConfigApiKey()
    {
        try
        {
            var configPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"));
            if (!File.Exists(configPath)) return null;
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("Agent")
                .GetProperty("Llm")
                .GetProperty("ApiKey")
                .GetString();
        }
        catch
        {
            return null;
        }
    }
}

public sealed record SwitchProviderRequest(string Provider);
public sealed record SwitchModelRequest(string Model);
public sealed record UpdateApiKeyRequest(string ApiKey);
public sealed record UpdateModelParamsRequest(
    double? Temperature,
    int? MaxTokens,
    double? TopP,
    bool? Thinking);