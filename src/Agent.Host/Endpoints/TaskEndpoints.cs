// ============================================================================
// 任务 API 端点
// ============================================================================

using System.Text;
using System.Text.Json;
using Agent.Core.Engine;
using Agent.Host.Ws;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Host.Endpoints;

/// <summary>
/// 任务 API — 提交、查询、取消 Agent 任务
/// </summary>
public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tasks")
            .WithTags("Tasks")
            .WithDescription("任务管理");

        // POST /api/tasks — 提交任务
        group.MapPost("/", async (
            TaskSubmitRequest request,
            TaskRunner runner,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "消息不能为空" });

            var task = await runner.SubmitTaskAsync(
                request.Message,
                request.SessionId ?? "api",
                ct: ct);

            return Results.Ok(new
            {
                taskId = task.TaskId,
                status = task.Status.ToString(),
                message = task.UserMessage
            });
        })
        .WithName("SubmitTask")
        .WithDescription("提交新任务");

        // GET /api/tasks — 列出所有任务
        group.MapGet("/", (TaskRunner runner) =>
        {
            var tasks = runner.GetAllTasks().Values
                .OrderByDescending(t => t.CreatedAt)
                .Take(100)
                .Select(t => new
                {
                    taskId = t.TaskId,
                    message = t.UserMessage.Length > 100 ? t.UserMessage[..100] + "..." : t.UserMessage,
                    status = t.Status.ToString(),
                    createdAt = t.CreatedAt,
                    startedAt = t.StartedAt,
                    completedAt = t.CompletedAt,
                    iterations = t.Iterations,
                    totalTokens = t.TotalTokens,
                    toolCalls = t.ToolCalls.Count
                });

            return Results.Ok(tasks);
        })
        .WithName("GetAllTasks")
        .WithDescription("列出所有任务");

        // GET /api/tasks/{id} — 查询任务详情
        group.MapGet("/{id}", (string id, TaskRunner runner) =>
        {
            var task = runner.GetTask(id);
            if (task is null)
                return Results.NotFound(new { error = $"任务 {id} 不存在" });

            return Results.Ok(new
            {
                taskId = task.TaskId,
                message = task.UserMessage,
                status = task.Status.ToString(),
                result = task.Result,
                error = task.Error,
                createdAt = task.CreatedAt,
                startedAt = task.StartedAt,
                completedAt = task.CompletedAt,
                iterations = task.Iterations,
                totalTokens = task.TotalTokens,
                toolCalls = task.ToolCalls.Select(tc => new
                {
                    toolName = tc.ToolName,
                    success = tc.Success,
                    duration = tc.Duration.TotalMilliseconds,
                    output = tc.Output?.Length > 800 ? tc.Output[..800] + "..." : tc.Output
                })
            });
        })
        .WithName("GetTask")
        .WithDescription("查询任务详情");

        // DELETE /api/tasks/{id} — 取消任务
        group.MapDelete("/{id}", (string id, TaskRunner runner) =>
        {
            var cancelled = runner.CancelTask(id);
            return cancelled
                ? Results.Ok(new { status = "cancelled" })
                : Results.NotFound(new { error = $"任务 {id} 不存在或未在运行" });
        })
        .WithName("CancelTask")
        .WithDescription("取消任务");

        // GET /api/tasks/{id}/stream — SSE 流式输出任务的最终回答
        group.MapGet("/{id}/stream", async (string id, TaskRunner runner, VegaWebSocketHub wsHub, HttpContext http, CancellationToken ct) =>
        {
            var task = runner.GetTask(id);
            if (task is null)
                return Results.NotFound(new { error = $"任务 {id} 不存在" });

            http.Response.ContentType = "text/event-stream";
            http.Response.StatusCode = 200;

            try
            {
                await foreach (var chunk in task.StreamChannel.Reader.ReadAllAsync(ct))
                {
                    var sseData = JsonSerializer.Serialize(new { content = chunk });
                    await http.Response.WriteAsync($"data: {sseData}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    
                    // 同时通过 WS 推送
                    _ = PushWsSafe(wsHub, id, chunk);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }

            await http.Response.WriteAsync("data: [DONE]\n\n", ct);
            _ = wsHub.PushTaskUpdateAsync(id, "completed");
            return Results.Empty;
        })
        .WithName("StreamTask")
        .WithDescription("SSE 流式读取任务的最终回答");
    }

    private static async Task PushWsSafe(VegaWebSocketHub hub, string taskId, string chunk)
    {
        try
        {
            await hub.PushTaskUpdateAsync(taskId, "streaming", new { content = chunk });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WS] 任务流推送失败: {ex.Message}");
        }
    }
}

public sealed class TaskSubmitRequest
{
    public required string Message { get; init; }
    public string? SessionId { get; init; }
}
