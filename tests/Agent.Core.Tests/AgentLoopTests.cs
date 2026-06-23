// ============================================================================
// AgentLoop 单元测试 — ReAct 循环核心逻辑
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Config;
using Agent.Core.LLM;
using Agent.Core.Models;
using Agent.Core.Planning;
using Agent.Core.Security;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Agent.Core.Tests;

public class AgentLoopTests
{
    private readonly Mock<LlmConnector> _llmMock;
    private readonly Mock<IToolRegistry> _registryMock = new();
    private readonly Mock<ToolExecutor> _executorMock;
    private readonly Mock<SystemPromptBuilder> _promptBuilderMock;
    private readonly AgentLoop _agentLoop;
    private readonly AgentConfig _config;

    public AgentLoopTests()
    {
        _config = new AgentConfig
        {
            Llm = new LlmConfig
            {
                DefaultModel = "test-model",
                MaxLoopIterations = 5,
                MaxLoopMessages = 50,
                MaxLoopTotalTokens = 50000
            }
        };

        var httpClient = new HttpClient();
        var optionsMonitor = new Mock<IOptionsMonitor<AgentConfig>>();
        optionsMonitor.Setup(o => o.CurrentValue).Returns(_config);
        _llmMock = new Mock<LlmConnector>(
            httpClient,
            Mock.Of<ILogger<LlmConnector>>(),
            optionsMonitor.Object,
            false);

        _executorMock = new Mock<ToolExecutor>(
            _registryMock.Object,
            Mock.Of<IPolicyEngine>(),
            Mock.Of<IReviewGate>(),
            Mock.Of<IAuditLogger>(),
            new Mock<CredentialHelper>(Mock.Of<ILogger<CredentialHelper>>()).Object,
            new ElevationAuditLogger(Mock.Of<ILogger<ElevationAuditLogger>>()),
            Mock.Of<ILogger<ToolExecutor>>(),
            Options.Create(_config),
            (ToolResultCache?)null);

        _promptBuilderMock = new Mock<SystemPromptBuilder>(
            Mock.Of<ILogger<SystemPromptBuilder>>(),
            (string?)null);

        _agentLoop = new AgentLoop(
            _llmMock.Object,
            _registryMock.Object,
            _executorMock.Object,
            _promptBuilderMock.Object,
            Mock.Of<ILogger<AgentLoop>>(),
            Options.Create(_config));
    }

    [Fact]
    public async Task RunAsync_TextResponse_ReturnsDirectly()
    {
        var response = new LlmResponse
        {
            Content = "这是一个测试回复",
            ToolCalls = null,
            PromptTokens = 100,
            CompletionTokens = 50
        };

        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        var result = await _agentLoop.RunAsync("你好");

        Assert.Equal("这是一个测试回复", result.Content);
        Assert.Empty(result.ToolCalls);
        Assert.Equal(1, result.Iterations);
    }

    [Fact]
    public async Task RunAsync_ToolCall_ExecutesAndContinues()
    {
        var toolCallResponse = new LlmResponse
        {
            Content = null,
            ToolCalls =
            [
                new LlmToolCall
                {
                    Id = "call_001",
                    Type = "function",
                    Function = new LlmFunctionCall
                    {
                        Name = "test-tool",
                        Arguments = "{}"
                    }
                }
            ],
            PromptTokens = 100,
            CompletionTokens = 50
        };

        var finalResponse = new LlmResponse
        {
            Content = "工具执行完成",
            ToolCalls = null,
            PromptTokens = 200,
            CompletionTokens = 30
        };

        var callCount = 0;
        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? toolCallResponse : finalResponse;
            });

        var toolInfo = new ToolInfo
        {
            Name = "test-tool",
            Description = "测试工具",
            Category = ToolCategory.SDKIntegrated,
            RiskLevel = RiskLevel.Level0,
            Parameters = []
        };
        _registryMock.Setup(r => r.GetAllTools()).Returns([toolInfo]);

        var toolResult = new ToolResult
        {
            RequestId = "test",
            Status = ToolResultStatus.Success,
            Output = "工具输出",
            ToolName = "test-tool"
        };
        _executorMock.Setup(e => e.ExecuteAsync(
            It.IsAny<ToolRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var result = await _agentLoop.RunAsync("执行测试工具");

        Assert.Equal("工具执行完成", result.Content);
        Assert.Single(result.ToolCalls);
        Assert.Equal(2, result.Iterations);
    }

    [Fact]
    public async Task RunAsync_MaxIterations_StopsLoop()
    {
        var toolCallResponse = new LlmResponse
        {
            Content = null,
            ToolCalls =
            [
                new LlmToolCall
                {
                    Id = "call_001",
                    Type = "function",
                    Function = new LlmFunctionCall
                    {
                        Name = "test-tool",
                        Arguments = "{}"
                    }
                }
            ],
            PromptTokens = 100,
            CompletionTokens = 50
        };

        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolCallResponse);

        var toolInfo = new ToolInfo
        {
            Name = "test-tool",
            Description = "测试工具",
            Category = ToolCategory.SDKIntegrated,
            RiskLevel = RiskLevel.Level0,
            Parameters = []
        };
        _registryMock.Setup(r => r.GetAllTools()).Returns([toolInfo]);

        var toolResult = new ToolResult
        {
            RequestId = "test",
            Status = ToolResultStatus.Success,
            Output = "工具输出",
            ToolName = "test-tool"
        };
        _executorMock.Setup(e => e.ExecuteAsync(
            It.IsAny<ToolRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var result = await _agentLoop.RunAsync("无限循环测试", maxIterations: 3);

        Assert.Equal(3, result.Iterations);
        Assert.Contains("达到最大推理轮次", result.Error);
    }

    [Fact]
    public async Task RunAsync_LlmException_ReturnsError()
    {
        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmException(500, "服务不可用"));

        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        var result = await _agentLoop.RunAsync("测试错误");

        Assert.NotNull(result.Error);
        Assert.Contains("服务不可用", result.Error);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        try
        {
            var result = await _agentLoop.RunAsync("取消测试", ct: cts.Token);
            Assert.True(!result.Success || result.Error is not null);
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task RunAsync_ToolExecutionFailed_ReportsToLlm()
    {
        var toolCallResponse = new LlmResponse
        {
            Content = null,
            ToolCalls =
            [
                new LlmToolCall
                {
                    Id = "call_001",
                    Type = "function",
                    Function = new LlmFunctionCall
                    {
                        Name = "failing-tool",
                        Arguments = "{}"
                    }
                }
            ],
            PromptTokens = 100,
            CompletionTokens = 50
        };

        var finalResponse = new LlmResponse
        {
            Content = "工具执行失败，但我会告诉你原因",
            ToolCalls = null,
            PromptTokens = 200,
            CompletionTokens = 30
        };

        var callCount = 0;
        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? toolCallResponse : finalResponse;
            });

        var toolInfo = new ToolInfo
        {
            Name = "failing-tool",
            Description = "会失败的工具",
            Category = ToolCategory.SDKIntegrated,
            RiskLevel = RiskLevel.Level0,
            Parameters = []
        };
        _registryMock.Setup(r => r.GetAllTools()).Returns([toolInfo]);

        var toolResult = new ToolResult
        {
            RequestId = "test",
            Status = ToolResultStatus.Failed,
            Error = "权限不足",
            ToolName = "failing-tool"
        };
        _executorMock.Setup(e => e.ExecuteAsync(
            It.IsAny<ToolRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolResult);

        var result = await _agentLoop.RunAsync("测试失败工具");

        Assert.Equal("工具执行失败，但我会告诉你原因", result.Content);
        Assert.Equal(2, result.Iterations);
        Assert.Single(result.ToolCalls);
        Assert.False(result.ToolCalls[0].Success);
    }

    [Fact]
    public async Task RunAsync_NullMessage_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _agentLoop.RunAsync(null!));
    }

    [Fact]
    public async Task RunAsync_EmptyHistory_StartsFresh()
    {
        var response = new LlmResponse
        {
            Content = "新对话",
            ToolCalls = null,
            PromptTokens = 50,
            CompletionTokens = 20
        };

        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        var result = await _agentLoop.RunAsync("新对话测试", history: []);

        Assert.Equal("新对话", result.Content);
    }

    [Fact]
    public async Task RunAsync_WithHistory_PreservesContext()
    {
        var response = new LlmResponse
        {
            Content = "继续对话",
            ToolCalls = null,
            PromptTokens = 100,
            CompletionTokens = 20
        };

        LlmMessage[]? capturedMessages = null;
        _llmMock.Setup(l => l.ChatCompletionAsync(
            It.IsAny<LlmMessage[]>(),
            It.IsAny<string?>(),
            It.IsAny<LlmToolDefinition[]?>(),
            It.IsAny<float?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .Callback<LlmMessage[], string?, LlmToolDefinition[]?, float?, int?, CancellationToken>(
                (msgs, _, _, _, _, _) => capturedMessages = msgs)
            .ReturnsAsync(response);

        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        var history = new List<LlmMessage>
        {
            new() { Role = "user", Content = "之前的消息" },
            new() { Role = "assistant", Content = "之前的回复" }
        };

        var result = await _agentLoop.RunAsync("继续", history: history);

        Assert.NotNull(capturedMessages);
        Assert.True(capturedMessages!.Length >= 4);
        Assert.Equal("继续对话", result.Content);
    }
}
