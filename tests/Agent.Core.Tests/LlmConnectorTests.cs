// ============================================================================
// LlmConnector 单元测试 — 请求构建、响应解析、重试逻辑
// ============================================================================

using System.Net;
using System.Text;
using System.Text.Json;
using Agent.Core.Config;
using Agent.Core.LLM;
using Agent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Agent.Core.Tests;

public class LlmConnectorTests : IDisposable
{
    private readonly List<MockHttpHandler> _handlers = new();

    public void Dispose()
    {
        // 清理环境变量 (防止泄漏到其他测试类)
        Environment.SetEnvironmentVariable("AGENT_API_KEY", null);
        Environment.SetEnvironmentVariable("MIMO_API_KEY", null);
    }

    /// <summary>
    /// 创建 LlmConnector 实例，使用 mock HTTP handler
    /// </summary>
    private (LlmConnector connector, MockHttpHandler handler) CreateConnector(
        string? apiKey = "test-key",
        string endpoint = "https://api.test.com",
        string defaultModel = "test-model")
    {
        // apiKey=null 时清除环境变量 (防止并行测试类干扰)
        if (apiKey is null)
        {
            Environment.SetEnvironmentVariable("AGENT_API_KEY", null);
            Environment.SetEnvironmentVariable("MIMO_API_KEY", null);
        }

        // 通过配置传递 Key (不依赖环境变量，避免并行测试竞争)
        var config = new AgentConfig
        {
            Llm = new LlmConfig
            {
                Endpoint = endpoint,
                ApiKey = apiKey,  // ApiKeyProvider.Resolve 会优先用这个
                DefaultModel = defaultModel,
                TimeoutSeconds = 10
            }
        };

        var handler = new MockHttpHandler();
        _handlers.Add(handler);
        var httpClient = new HttpClient(handler);

        var connector = new LlmConnector(
            httpClient,
            Mock.Of<ILogger<LlmConnector>>(),
            new SimpleOptionsMonitor<AgentConfig>(config),
            disableFileFallback: apiKey is null);

        return (connector, handler);
    }

    private static LlmMessage[] SimpleMessages() =>
    [
        new LlmMessage { Role = "user", Content = "你好" }
    ];

    private static string BuildChoicesResponse(string? content = null, string? reasoning = null,
        object[]? toolCalls = null, int promptTokens = 10, int completionTokens = 5)
    {
        var message = new Dictionary<string, object?>();
        if (content is not null) message["content"] = content;
        if (reasoning is not null) message["reasoning_content"] = reasoning;
        if (toolCalls is not null) message["tool_calls"] = toolCalls;

        var response = new
        {
            id = "chatcmpl-test",
            model = "test-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message,
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ─── 请求构建测试 ────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_SendsCorrectEndpoint()
    {
        var (connector, handler) = CreateConnector(endpoint: "https://custom.api.com");
        handler.Response = BuildChoicesResponse(content: "回复");

        await connector.ChatCompletionAsync(SimpleMessages());

        Assert.Contains("https://custom.api.com/chat/completions", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ChatCompletionAsync_SendsAuthHeader()
    {
        var (connector, handler) = CreateConnector(apiKey: "my-secret-key");
        handler.Response = BuildChoicesResponse(content: "回复");

        await connector.ChatCompletionAsync(SimpleMessages());

        Assert.NotNull(handler.LastAuthHeader);
        Assert.StartsWith("Bearer ", handler.LastAuthHeader);
    }

    [Fact]
    public async Task ChatCompletionAsync_IncludesModelInBody()
    {
        var (connector, handler) = CreateConnector(defaultModel: "mimo-test");
        handler.Response = BuildChoicesResponse(content: "回复");

        await connector.ChatCompletionAsync(SimpleMessages());

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("mimo-test", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ChatCompletionAsync_CustomModelOverridesDefault()
    {
        var (connector, handler) = CreateConnector(defaultModel: "default");
        handler.Response = BuildChoicesResponse(content: "回复");

        await connector.ChatCompletionAsync(SimpleMessages(), model: "custom-model");

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("custom-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ChatCompletionAsync_IncludesThinkingConfig()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(content: "回复");

        await connector.ChatCompletionAsync(SimpleMessages());

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var thinking = body.RootElement.GetProperty("thinking");
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ChatCompletionAsync_IncludesTools()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(content: "回复");

        var tools = new[]
        {
            new LlmToolDefinition
            {
                Function = new LlmFunctionDefinition
                {
                    Name = "test-tool",
                    Description = "测试工具",
                    Parameters = new { type = "object", properties = new { } }
                }
            }
        };

        await connector.ChatCompletionAsync(SimpleMessages(), tools: tools);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(body.RootElement.TryGetProperty("tools", out var toolsArr));
        Assert.Equal(1, toolsArr.GetArrayLength());
    }

    // ─── 响应解析测试 ────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_ParsesContentResponse()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(content: "你好！有什么可以帮助你的？");

        var result = await connector.ChatCompletionAsync(SimpleMessages());

        Assert.Equal("你好！有什么可以帮助你的？", result.Content);
        Assert.Null(result.ToolCalls);
    }

    [Fact]
    public async Task ChatCompletionAsync_ParsesReasoningContent()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(content: "答案", reasoning: "让我想想...");

        var result = await connector.ChatCompletionAsync(SimpleMessages());

        Assert.Equal("答案", result.Content);
        Assert.Equal("让我想想...", result.ReasoningContent);
    }

    [Fact]
    public async Task ChatCompletionAsync_ParsesToolCalls()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(toolCalls:
        [
            new
            {
                id = "call_123",
                type = "function",
                function = new { name = "run-script", arguments = "{\"path\":\"test.ps1\"}" }
            }
        ]);

        var result = await connector.ChatCompletionAsync(SimpleMessages());

        Assert.True(result.HasToolCalls);
        Assert.Single(result.ToolCalls!);
        Assert.Equal("call_123", result.ToolCalls![0].Id);
        Assert.Equal("run-script", result.ToolCalls[0].Function.Name);
        Assert.Equal("{\"path\":\"test.ps1\"}", result.ToolCalls[0].Function.Arguments);
    }

    [Fact]
    public async Task ChatCompletionAsync_ParsesUsage()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = BuildChoicesResponse(content: "回复", promptTokens: 150, completionTokens: 80);

        var result = await connector.ChatCompletionAsync(SimpleMessages());

        Assert.Equal(150, result.PromptTokens);
        Assert.Equal(80, result.CompletionTokens);
    }

    [Fact]
    public async Task ChatCompletionAsync_EmptyChoices_ReturnsFallbackMessage()
    {
        var (connector, handler) = CreateConnector();
        handler.Response = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            choices = Array.Empty<object>(),
            usage = new { prompt_tokens = 0, completion_tokens = 0 }
        });

        var result = await connector.ChatCompletionAsync(SimpleMessages());

        Assert.Contains("空响应", result.Content);
    }

    // ─── 错误处理测试 ────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_NoApiKey_ThrowsLlmException()
    {
        var (connector, handler) = CreateConnector(apiKey: null);
        // handler won't be reached

        await Assert.ThrowsAsync<LlmException>(() =>
            connector.ChatCompletionAsync(SimpleMessages()));
    }

    [Fact]
    public async Task ChatCompletionAsync_HttpError_ThrowsLlmException()
    {
        var (connector, handler) = CreateConnector();
        handler.StatusCode = HttpStatusCode.InternalServerError;
        handler.Response = "服务不可用";

        await Assert.ThrowsAsync<LlmException>(() =>
            connector.ChatCompletionAsync(SimpleMessages()));
    }

    [Fact]
    public async Task ChatCompletionAsync_429_ThrowsLlmException()
    {
        var (connector, handler) = CreateConnector();
        handler.StatusCode = HttpStatusCode.TooManyRequests;
        handler.Response = "rate limited";

        // 用短超时取消重试延迟 (429 退避总延迟 ~93s，这里 2s 足够触发首次重试后取消)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // 429 会触发重试，被 CancellationToken 取消后抛出异常
        var exception = await Record.ExceptionAsync(() =>
            connector.ChatCompletionAsync(SimpleMessages(), ct: cts.Token));

        Assert.NotNull(exception);
        Assert.True(exception is LlmException or OperationCanceledException);
    }

    [Fact]
    public async Task ChatCompletionAsync_500_ThrowsLlmException()
    {
        var (connector, handler) = CreateConnector();
        handler.StatusCode = HttpStatusCode.InternalServerError;
        handler.Response = "server error";

        // 用短超时取消重试延迟 (500 退避总延迟 ~31s)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var exception = await Record.ExceptionAsync(() =>
            connector.ChatCompletionAsync(SimpleMessages(), ct: cts.Token));

        Assert.NotNull(exception);
        Assert.True(exception is LlmException or OperationCanceledException);
    }

    // ─── Mock HTTP Handler ──────────────────────────

    public class MockHttpHandler : HttpMessageHandler
    {
        public string? Response { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? LastRequestBody { get; private set; }
        public string? LastRequestUrl { get; private set; }
        public string? LastAuthHeader { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUrl = request.RequestUri?.ToString();
            LastAuthHeader = request.Headers.Authorization?.ToString();

            if (request.Content is not null)
            {
                LastRequestBody = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Response ?? "", Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}

/// <summary>测试用简易 IOptionsMonitor 实现</summary>
internal sealed class SimpleOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    private T _value;
    public SimpleOptionsMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();
    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
}
