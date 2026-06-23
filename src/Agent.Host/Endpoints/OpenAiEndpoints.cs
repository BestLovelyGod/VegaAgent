// ============================================================================
// OpenAI 兼容 API 端点 — 支持多轮对话
// ============================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Core.Abstractions;
using Agent.Core.LLM;
using Agent.Core.Models;
using Agent.Core.Planning;
using Agent.Host.Ws;

namespace Agent.Host.Endpoints;

public static class OpenAiEndpoints
{
    private static readonly ConcurrentDictionary<string, List<LlmMessage>> _conversations = new();
    private const int MaxHistoryMessages = 50;

    /// <summary>Fire-and-forget WebSocket 推送，捕获异常避免吞没</summary>
    private static async Task PushWsSafe(Ws.VegaWebSocketHub hub, string sessionId, Agent.Core.Models.LlmStreamChunk chunk)
    {
        try
        {
            await hub.PushLlmChunkAsync(sessionId, chunk.DeltaContent, chunk.DeltaReasoningContent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WS] LLM chunk 推送失败: {ex.Message}");
        }
    }

    public static void MapOpenAiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1")
            .WithTags("OpenAI Compatible")
            .WithDescription("OpenAI 兼容 API");

        group.MapGet("/models", () => Results.Ok(new
        {
            data = new[]
            {
                new { id = "mimo-v2.5", @object = "model", owned_by = "xiaomi" },
                new { id = "mimo-v2.5-pro", @object = "model", owned_by = "xiaomi" }
            }
        }))
        .WithName("ListModels")
        .WithDescription("列出可用模型");

        group.MapPost("/chat/completions", async (
            ChatCompletionRequest request,
            LlmConnector llm,
            AgentLoop agentLoop,
            IToolRegistry registry,
            VegaWebSocketHub wsHub,
            HttpContext http,
            CancellationToken ct) =>
        {
            var sessionId = http.Request.Headers["X-Session-Id"].FirstOrDefault()
                ?? $"openai-{request.Model ?? "default"}";

            var history = _conversations.GetOrAdd(sessionId, _ => new List<LlmMessage>());

            // 只添加新消息 (避免与 AgentLoop 中的 userMessage 重复)
            // 保留 reasoning_content + tool_calls — MiMo API 多轮会话必须回传
            var existingCount = history.Count;
            foreach (var msg in request.Messages)
            {
                history.Add(new LlmMessage
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    ReasoningContent = msg.ReasoningContent,
                    ToolCallId = msg.ToolCallId,
                    ToolCalls = msg.ToolCalls?.Select(tc => new LlmToolCall
                    {
                        Id = tc.Id ?? $"call-{Guid.NewGuid():N}",
                        Type = tc.Type ?? "function",
                        Function = new LlmFunctionCall
                        {
                            Name = tc.Function?.Name ?? "",
                            Arguments = tc.Function?.Arguments ?? "{}"
                        }
                    }).ToArray()
                });
            }

            while (history.Count > MaxHistoryMessages)
                history.RemoveAt(0);

            // 提取最新的用户消息 (AgentLoop 会用它作为输入，不重复添加)
            var lastUserMessage = history.LastOrDefault(m => m.Role == "user")?.Content ?? "";

            // 所有请求统一走 Agent Loop — 自动注入已注册工具 (SearchWeb, PowerShell 等)
            return await HandleAgentLoopRequest(request, agentLoop, lastUserMessage, history, sessionId, wsHub, http, ct);
        })
        .WithName("ChatCompletions")
        .WithDescription("聊天补全");

        group.MapDelete("/conversations/{sessionId}", (string sessionId) =>
        {
            var removed = _conversations.TryRemove(sessionId, out _);
            return Results.Ok(new { removed, sessionId });
        })
        .WithName("ClearConversation")
        .WithDescription("清除对话历史");
    }

    private static async Task<IResult> HandleAgentLoopRequest(
        ChatCompletionRequest request,
        AgentLoop agentLoop,
        string userMessage,
        List<LlmMessage> history,
        string sessionId,
        VegaWebSocketHub wsHub,
        HttpContext http,
        CancellationToken ct)
    {

        // 流式输出 — 通过 AgentLoop onStreamChunk 回调逐步推送 SSE
        if (request.Stream)
        {
            http.Response.ContentType = "text/event-stream";
            http.Response.StatusCode = 200;

            var fullContent = new StringBuilder();
            var streamModel = request.Model ?? "mimo-v2.5";

            try
            {
                var result = await agentLoop.RunAsync(
                    userMessage,
                    history: new List<LlmMessage>(history.GetRange(0, Math.Max(0, history.Count - 1))),
                    maxIterations: 100,
                    onLlmChunk: (chunk) =>
                    {
                        if (chunk.DeltaContent is not null) fullContent.Append(chunk.DeltaContent);
                        var delta = new Dictionary<string, object?>();
                        if (chunk.DeltaContent is not null) delta["content"] = chunk.DeltaContent;
                        if (chunk.DeltaReasoningContent is not null) delta["reasoning_content"] = chunk.DeltaReasoningContent;
                        var sseData = JsonSerializer.Serialize(new
                        {
                            id = $"chatcmpl-{Guid.NewGuid():N}",
                            @object = "chat.completion.chunk",
                            model = streamModel,
                            choices = new[] { new { index = 0, delta, finish_reason = (string?)null } }
                        });
                        
                        // SSE 推送（保持向后兼容）
                        http.Response.WriteAsync($"data: {sseData}\n\n", ct).GetAwaiter().GetResult();
                        http.Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
                        
                        // WebSocket 推送（新通道）- 使用 Fire-and-forget + 容错
                        _ = PushWsSafe(wsHub, sessionId, chunk);
                    },
                    ct: ct);

                // 写入结束标记
                // 如果没有流式内容（达到轮次上限/失败），发送错误信息
                if (fullContent.Length == 0 && !string.IsNullOrEmpty(result.Error))
                {
                    fullContent.Append(result.Error);
                    var errorChunk = JsonSerializer.Serialize(new
                    {
                        id = $"chatcmpl-{Guid.NewGuid():N}",
                        @object = "chat.completion.chunk",
                        model = streamModel,
                        choices = new[] { new { index = 0, delta = new { content = result.Error }, finish_reason = (string?)null } }
                    });
                    await http.Response.WriteAsync($"data: {errorChunk}\n\n", ct);
                }

                var doneData = JsonSerializer.Serialize(new
                {
                    id = $"chatcmpl-{Guid.NewGuid():N}",
                    @object = "chat.completion.chunk",
                    model = streamModel,
                    choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }
                });
                await http.Response.WriteAsync($"data: {doneData}\n\n", ct);
                await http.Response.WriteAsync("data: [DONE]\n\n", ct);

                // WebSocket 推送流结束
                _ = wsHub.PushLlmStreamEndAsync(sessionId);

                // 使用 AgentLoop 返回的完整消息历史 (保留 reasoning_content + tool_calls)
                if (result.Messages is { Count: > 0 })
                {
                    history.Clear();
                    history.AddRange(result.Messages.Where(m => m.Role != "system" && m.Role != "developer"));
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开，尝试发送 [DONE]
                try { await http.Response.WriteAsync("data: [DONE]\n\n", ct); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SSE] 发送 [DONE] 失败: {ex.Message}"); }
                _ = wsHub.PushLlmStreamEndAsync(sessionId);
            }
            catch (IOException)
            {
                // 连接丢失，尝试发送 [DONE]
                try { await http.Response.WriteAsync("data: [DONE]\n\n", ct); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SSE] 发送 [DONE] 失败: {ex.Message}"); }
                _ = wsHub.PushLlmStreamEndAsync(sessionId);
            }
            catch (Exception ex)
            {
                // 未预期异常，发送错误信息给客户端
                try
                {
                    var errorChunk = JsonSerializer.Serialize(new
                    {
                        id = $"chatcmpl-{Guid.NewGuid():N}",
                        @object = "chat.completion.chunk",
                        model = request.Model ?? "mimo-v2.5",
                        choices = new[] { new { index = 0, delta = new { content = $"服务器内部错误: {ex.Message}" }, finish_reason = (string?)null } }
                    });
                    await http.Response.WriteAsync($"data: {errorChunk}\n\n", ct);
                    await http.Response.WriteAsync("data: [DONE]\n\n", ct);
                }
                catch (Exception ex2) { System.Diagnostics.Debug.WriteLine($"[SSE] 发送错误响应失败: {ex2.Message}"); }
            }

            return Results.Empty;
        }

        // 非流式 — 直接返回完整结果
        var nonStreamResult = await agentLoop.RunAsync(userMessage, history: new List<LlmMessage>(history.GetRange(0, Math.Max(0, history.Count - 1))), maxIterations: 100, ct: ct);

        // 使用 AgentLoop 返回的完整消息历史 (保留 reasoning_content + tool_calls)
        if (nonStreamResult.Messages is { Count: > 0 })
        {
            history.Clear();
            history.AddRange(nonStreamResult.Messages.Where(m => m.Role != "system" && m.Role != "developer"));
        }

        // 构建包含 reasoning_content 的响应消息
        var lastAssistant = nonStreamResult.Messages?.LastOrDefault(m => m.Role == "assistant");
        var responseMessage = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = nonStreamResult.Content ?? nonStreamResult.Error
        };
        if (lastAssistant?.ReasoningContent is not null)
            responseMessage["reasoning_content"] = lastAssistant.ReasoningContent;

        return Results.Ok(new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion",
            model = request.Model ?? "mimo-v2.5",
            choices = new[] { new { index = 0, message = responseMessage, finish_reason = nonStreamResult.Success ? "stop" : "length" } },
            usage = new { prompt_tokens = 0, completion_tokens = nonStreamResult.TotalTokens, total_tokens = nonStreamResult.TotalTokens }
        });
    }

    private static async Task<IResult> HandleStreamRequest(
        ChatCompletionRequest request,
        LlmConnector llm,
        LlmMessage[] messages,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.StatusCode = 200;

        var fullContent = new StringBuilder();

        try
        {
            await foreach (var chunk in llm.ChatCompletionStreamAsync(messages, request.Model, ct: ct))
            {
                if (chunk.DeltaContent is not null)
                    fullContent.Append(chunk.DeltaContent);

                var sseData = JsonSerializer.Serialize(new
                {
                    id = chunk.Id ?? $"chatcmpl-{Guid.NewGuid():N}",
                    @object = "chat.completion.chunk",
                    model = request.Model ?? "mimo-v2.5",
                    choices = new[] { new { index = 0, delta = new { content = chunk.DeltaContent }, finish_reason = chunk.IsComplete ? "stop" : (string?)null } }
                });

                await http.Response.WriteAsync($"data: {sseData}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }

        var sessionId = http.Request.Headers["X-Session-Id"].FirstOrDefault() ?? $"openai-{request.Model ?? "default"}";
        if (_conversations.TryGetValue(sessionId, out var history))
            history.Add(new LlmMessage { Role = "assistant", Content = fullContent.ToString() });

        await http.Response.WriteAsync("data: [DONE]\n\n", ct);
        return Results.Empty;
    }

    private static object BuildChatResponse(LlmResponse response, string? model) => new
    {
        id = response.Id ?? $"chatcmpl-{Guid.NewGuid():N}",
        @object = "chat.completion",
        model = model ?? "mimo-v2.5",
        choices = new[] { new { index = 0, message = new { role = "assistant", content = response.Content }, finish_reason = "stop" } },
        usage = new { prompt_tokens = response.PromptTokens, completion_tokens = response.CompletionTokens, total_tokens = response.PromptTokens + response.CompletionTokens }
    };
}

public sealed class ChatCompletionRequest
{
    public string? Model { get; init; }
    public required ChatMessageDto[] Messages { get; init; }
    public bool Stream { get; init; }
    public int? MaxTokens { get; init; }
    public float? Temperature { get; init; }
    public ToolDto[]? Tools { get; init; }
}

public sealed class ChatMessageDto
{
    public required string Role { get; init; }
    public string? Content { get; init; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    [JsonPropertyName("tool_calls")]
    public ChatToolCallDto[]? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}

public sealed class ChatToolCallDto
{
    public string? Id { get; init; }
    public string? Type { get; init; }
    public ChatToolCallFunctionDto? Function { get; init; }
}

public sealed class ChatToolCallFunctionDto
{
    public string? Name { get; init; }
    public string? Arguments { get; init; }
}

public sealed class ToolDto
{
    public string Type { get; init; } = "function";
    public FunctionDto? Function { get; init; }
}

public sealed class FunctionDto
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public object? Parameters { get; init; }
}
