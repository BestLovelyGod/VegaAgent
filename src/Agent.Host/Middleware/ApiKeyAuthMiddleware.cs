// ============================================================================
// API Key 认证中间件
// ============================================================================

using Agent.Core.Config;

namespace Agent.Host.Middleware;

/// <summary>
/// API Key 认证中间件 — 通过请求头验证 API Key
/// 
/// 支持两种方式:
///   - Authorization: Bearer <key>
///   - api-key: <key>
/// 
/// 白名单路径不需要认证:
///   - /health
///   - /swagger
///   - /v1/models
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private readonly string? _apiKey;

    // 不需要认证的路径前缀
    private static readonly string[] PublicPaths =
    [
        "/health",
        "/swagger",
        "/v1/models",
        "/favicon"
    ];

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _apiKey = ApiKeyProvider.Resolve(config["Agent:LLM:ApiKey"]);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 公开路径不需要认证
        if (IsPublicPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // 如果没有配置 API Key，跳过认证 (开发模式)
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            await _next(context);
            return;
        }

        // 从请求头提取 API Key
        var requestKey = ExtractApiKey(context.Request);

        if (string.IsNullOrWhiteSpace(requestKey))
        {
            _logger.LogWarning("缺少 API Key: {Path} {Method}", context.Request.Path, context.Request.Method);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "缺少 API Key。请在请求头中设置 Authorization: Bearer <key> 或 api-key: <key>" });
            return;
        }

        if (!string.Equals(requestKey, _apiKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("API Key 验证失败: {Path} {Method}", context.Request.Path, context.Request.Method);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "API Key 无效" });
            return;
        }

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        return PublicPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractApiKey(HttpRequest request)
    {
        // 方式 1: Authorization: Bearer <key>
        var authHeader = request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        // 方式 2: api-key: <key>
        if (request.Headers.TryGetValue("api-key", out var apiKeyValues))
            return apiKeyValues.FirstOrDefault()?.Trim();

        // 方式 3: 查询参数 ?api-key=<key>
        if (request.Query.TryGetValue("api-key", out var queryValues))
            return queryValues.FirstOrDefault()?.Trim();

        return null;
    }
}
