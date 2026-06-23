// ============================================================================
// 工具 API 端点
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Tools;

namespace Agent.Host.Endpoints;

public static class ToolEndpoints
{
    public static void MapToolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools")
            .WithTags("Tools")
            .WithDescription("工具管理");

        // GET /api/tools — 列出所有可用工具
        group.MapGet("/", (IToolRegistry registry) =>
        {
            var tools = registry.GetAllTools();
            return Results.Ok(tools);
        })
        .WithName("GetAllTools")
        .WithDescription("列出所有可用工具");

        // GET /api/tools/{name} — 获取工具详情
        group.MapGet("/{name}", (string name, IToolRegistry registry) =>
        {
            var tool = registry.GetTool(name);
            return tool is not null
                ? Results.Ok(tool.GetInfo())
                : Results.NotFound(new { error = $"工具 '{name}' 未找到" });
        })
        .WithName("GetTool")
        .WithDescription("获取工具详情");

        // POST /api/tools/{name}/execute — 执行工具
        group.MapPost("/{name}/execute", async (
            string name,
            ToolExecuteRequest request,
            ToolExecutor executor,
            CancellationToken ct) =>
        {
            var toolRequest = new ToolRequest
            {
                ToolName = name,
                Parameters = request.Parameters ?? new(),
                SessionId = request.SessionId ?? "api-direct"
            };

            var result = await executor.ExecuteAsync(toolRequest, ct);

            return result.Status switch
            {
                ToolResultStatus.Success => Results.Ok(result),
                ToolResultStatus.Blocked => Results.Json(result, statusCode: 403),
                ToolResultStatus.Timeout => Results.Json(result, statusCode: 408),
                ToolResultStatus.Cancelled => Results.Json(result, statusCode: 499),
                _ => Results.Json(result, statusCode: 500)
            };
        })
        .WithName("ExecuteTool")
        .WithDescription("执行指定工具");
    }
}

/// <summary>工具执行请求体</summary>
public sealed record ToolExecuteRequest
{
    public Dictionary<string, object>? Parameters { get; init; }
    public string? SessionId { get; init; }
}
