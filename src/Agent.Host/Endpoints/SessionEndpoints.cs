// ============================================================================
// 会话管理 API — CRUD 操作和消息历史
// ============================================================================

using Agent.Core.Models;
using System.Text.Json;

namespace Agent.Host.Endpoints;

public static class SessionEndpoints
{
    private static readonly string SessionsFilePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sessions.json"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions")
            .WithTags("Sessions")
            .WithDescription("会话管理");

        // GET /api/sessions — 会话列表 (置顶优先)
        group.MapGet("/", async () =>
        {
            var sessions = await LoadSessionsAsync();
            var summary = sessions.Select(s => new
            {
                s.Id,
                s.Title,
                s.CreatedAt,
                s.UpdatedAt,
                s.Pinned,
                MessageCount = s.Messages.Count
            }).OrderByDescending(s => s.Pinned).ThenByDescending(s => s.UpdatedAt);

            return Results.Ok(summary);
        })
        .WithName("GetSessions")
        .WithDescription("获取会话列表");

        // POST /api/sessions — 创建会话
        group.MapPost("/", async (CreateSessionRequest? request) =>
        {
            var sessions = await LoadSessionsAsync();
            
            var session = new Session
            {
                Title = request?.Title ?? "新对话",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            sessions.Add(session);
            await SaveSessionsAsync(sessions);

            return Results.Created($"/api/sessions/{session.Id}", new
            {
                sessionId = session.Id,
                session.Title
            });
        })
        .WithName("CreateSession")
        .WithDescription("创建新会话");

        // GET /api/sessions/{id} — 会话详情
        group.MapGet("/{id}", async (string id) =>
        {
            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            return Results.Ok(new
            {
                session.Id,
                session.Title,
                session.CreatedAt,
                session.UpdatedAt,
                MessageCount = session.Messages.Count
            });
        })
        .WithName("GetSession")
        .WithDescription("获取会话详情");

        // DELETE /api/sessions/{id} — 删除会话
        group.MapDelete("/{id}", async (string id) =>
        {
            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            sessions.Remove(session);
            await SaveSessionsAsync(sessions);

            return Results.Ok(new { message = "已删除" });
        })
        .WithName("DeleteSession")
        .WithDescription("删除会话");

        // GET /api/sessions/{id}/messages — 消息历史
        group.MapGet("/{id}/messages", async (string id) =>
        {
            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            return Results.Ok(session.Messages);
        })
        .WithName("GetSessionMessages")
        .WithDescription("获取会话消息历史");

        // PUT /api/sessions/{id}/pin — 切换置顶
        group.MapPut("/{id}/pin", async (string id) =>
        {
            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            session.Pinned = !session.Pinned;
            session.UpdatedAt = DateTime.UtcNow;
            await SaveSessionsAsync(sessions);

            return Results.Ok(new { message = session.Pinned ? "已置顶" : "已取消置顶", pinned = session.Pinned });
        })
        .WithName("TogglePinSession")
        .WithDescription("切换会话置顶状态");

        // PUT /api/sessions/{id}/rename — 重命名
        group.MapPut("/{id}/rename", async (string id, RenameSessionRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "标题不能为空" });

            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            session.Title = request.Title;
            session.UpdatedAt = DateTime.UtcNow;
            await SaveSessionsAsync(sessions);

            return Results.Ok(new { message = "已重命名", session.Title });
        })
        .WithName("RenameSession")
        .WithDescription("重命名会话");

        // POST /api/sessions/batch-delete — 批量删除
        group.MapPost("/batch-delete", async (BatchDeleteRequest request) =>
        {
            if (request.Ids is null || request.Ids.Count == 0)
                return Results.BadRequest(new { error = "未选择会话" });

            var sessions = await LoadSessionsAsync();
            var removed = 0;
            foreach (var id in request.Ids)
            {
                var session = sessions.FirstOrDefault(s => s.Id == id);
                if (session is not null)
                {
                    sessions.Remove(session);
                    removed++;
                }
            }
            await SaveSessionsAsync(sessions);

            return Results.Ok(new { message = $"已删除 {removed} 个会话", removed });
        })
        .WithName("BatchDeleteSessions")
        .WithDescription("批量删除会话");

        // POST /api/sessions/{id}/messages — 添加消息
        group.MapPost("/{id}/messages", async (string id, AddMessageRequest request) =>
        {
            var sessions = await LoadSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == id);

            if (session is null)
                return Results.NotFound(new { error = "会话不存在" });

            var message = new ChatMessage
            {
                Role = request.Role,
                Content = request.Content,
                Timestamp = DateTime.UtcNow,
                ReasoningContent = request.ReasoningContent,
                ToolCalls = request.ToolCalls?.Select(tc => new ToolCall
                {
                    Name = tc.Name,
                    Arguments = tc.Arguments
                }).ToList()
            };

            session.Messages.Add(message);
            session.UpdatedAt = DateTime.UtcNow;
            await SaveSessionsAsync(sessions);

            return Results.Ok(message);
        })
        .WithName("AddSessionMessage")
        .WithDescription("添加消息到会话");
    }

    private static async Task<List<Session>> LoadSessionsAsync()
    {
        try
        {
            if (!File.Exists(SessionsFilePath))
                return new List<Session>();

            var json = await File.ReadAllTextAsync(SessionsFilePath);
            return JsonSerializer.Deserialize<List<Session>>(json, JsonOptions) ?? new List<Session>();
        }
        catch
        {
            return new List<Session>();
        }
    }

    private static async Task SaveSessionsAsync(List<Session> sessions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionsFilePath)!);
        var json = JsonSerializer.Serialize(sessions, JsonOptions);
        await File.WriteAllTextAsync(SessionsFilePath, json);
    }
}

public sealed record CreateSessionRequest(string? Title);
public sealed record RenameSessionRequest(string Title);
public sealed record BatchDeleteRequest(List<string> Ids);

public sealed record AddMessageRequest(
    string Role,
    string Content,
    string? ReasoningContent,
    List<ToolCallRequest>? ToolCalls);

public sealed record ToolCallRequest(string Name, string Arguments);