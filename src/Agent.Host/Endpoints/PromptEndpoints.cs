// ============================================================================
// 提示词调试 API — 查看和测试 System Prompt
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Planning;

namespace Agent.Host.Endpoints;

public static class PromptEndpoints
{
    public static void MapPromptEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/prompt")
            .WithTags("Prompt")
            .WithDescription("提示词管理");

        // GET /api/prompt — 查看当前完整提示词
        group.MapGet("/", (SystemPromptBuilder builder, IToolRegistry registry) =>
        {
            var tools = registry.GetAllTools();
            var prompt = builder.BuildMessages(tools);
            var summary = builder.GetSummary(tools);

            return Results.Ok(new
            {
                summary,
                prompt
            });
        })
        .WithName("GetPrompt")
        .WithDescription("查看当前 System Prompt");

        // GET /api/prompt/summary — 提示词摘要
        group.MapGet("/summary", (SystemPromptBuilder builder, IToolRegistry registry) =>
        {
            var tools = registry.GetAllTools();
            var summary = builder.GetSummary(tools);
            return Results.Ok(summary);
        })
        .WithName("GetPromptSummary")
        .WithDescription("提示词摘要信息");

        // GET /api/prompt/agent — 查看 agent.md 内容
        group.MapGet("/agent", () =>
        {
            var path = FindConfigFile("agent.md");
            if (path is null)
                return Results.NotFound(new { error = "agent.md 不存在" });

            var content = File.ReadAllText(path);
            return Results.Ok(new { path, content, length = content.Length });
        })
        .WithName("GetAgentPrompt")
        .WithDescription("查看 agent.md 内容");

        // GET /api/prompt/memory — 查看 memory.md 内容
        group.MapGet("/memory", () =>
        {
            var path = FindConfigFile("memory.md");
            if (path is null)
                return Results.NotFound(new { error = "memory.md 不存在" });

            var content = File.ReadAllText(path);
            return Results.Ok(new { path, content, length = content.Length });
        })
        .WithName("GetMemoryPrompt")
        .WithDescription("查看 memory.md 内容");

        // PUT /api/prompt/agent — 更新 agent.md
        group.MapPut("/agent", async (HttpRequest request, CancellationToken ct) =>
        {
            var path = FindConfigFile("agent.md") ?? GetDefaultConfigPath("agent.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var reader = new StreamReader(request.Body);
            var content = await reader.ReadToEndAsync(ct);
            await File.WriteAllTextAsync(path, content, ct);

            return Results.Ok(new { message = "agent.md 已更新", path, length = content.Length });
        })
        .WithName("UpdateAgentPrompt")
        .WithDescription("更新 agent.md");

        // PUT /api/prompt/memory — 更新 memory.md
        group.MapPut("/memory", async (HttpRequest request, CancellationToken ct) =>
        {
            var path = FindConfigFile("memory.md") ?? GetDefaultConfigPath("memory.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var reader = new StreamReader(request.Body);
            var content = await reader.ReadToEndAsync(ct);
            await File.WriteAllTextAsync(path, content, ct);

            return Results.Ok(new { message = "memory.md 已更新", path, length = content.Length });
        })
        .WithName("UpdateMemoryPrompt")
        .WithDescription("更新 memory.md");

        // GET /api/prompt/user — 查看 user.md 内容
        group.MapGet("/user", () =>
        {
            var path = FindConfigFile("user.md");
            if (path is null)
                return Results.NotFound(new { error = "user.md 不存在" });

            var content = File.ReadAllText(path);
            return Results.Ok(new { path, content, length = content.Length });
        })
        .WithName("GetUserPrompt")
        .WithDescription("查看 user.md 内容");

        // PUT /api/prompt/user — 更新 user.md
        group.MapPut("/user", async (HttpRequest request, CancellationToken ct) =>
        {
            var path = FindConfigFile("user.md") ?? GetDefaultConfigPath("user.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var reader = new StreamReader(request.Body);
            var content = await reader.ReadToEndAsync(ct);
            await File.WriteAllTextAsync(path, content, ct);

            return Results.Ok(new { message = "user.md 已更新", path, length = content.Length });
        })
        .WithName("UpdateUserPrompt")
        .WithDescription("更新 user.md");
    }

    private static string? FindConfigFile(string fileName)
    {
        // 优先查找项目根目录
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config", fileName));
        if (File.Exists(projectRoot)) return projectRoot;

        // 回退到 bin 目录
        var binPath = Path.Combine(AppContext.BaseDirectory, "data", "config", fileName);
        if (File.Exists(binPath)) return binPath;

        return null;
    }

    private static string GetDefaultConfigPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config", fileName));
    }
}
