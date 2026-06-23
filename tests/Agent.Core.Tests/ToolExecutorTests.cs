// ============================================================================
// ToolExecutor 单元测试
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Config;
using Agent.Core.Models;
using Agent.Core.Security;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Agent.Core.Tests;

public class ToolExecutorTests
{
    private readonly Mock<IToolRegistry> _registryMock = new();
    private readonly Mock<IPolicyEngine> _policyMock = new();
    private readonly Mock<IReviewGate> _reviewMock = new();
    private readonly Mock<IAuditLogger> _auditMock = new();
    private readonly Mock<CredentialHelper> _credentialMock = new(Mock.Of<ILogger<CredentialHelper>>());
    private readonly ToolExecutor _executor;

    public ToolExecutorTests()
    {
        var config = Options.Create(new AgentConfig());
        _executor = new ToolExecutor(
            _registryMock.Object,
            _policyMock.Object,
            _reviewMock.Object,
            _auditMock.Object,
            _credentialMock.Object,
            new ElevationAuditLogger(Mock.Of<ILogger<ElevationAuditLogger>>()),
            Mock.Of<ILogger<ToolExecutor>>(),
            config);
    }

    [Fact]
    public async Task ExecuteAsync_ToolNotFound_ReturnsFailed()
    {
        _registryMock.Setup(r => r.GetTool("missing")).Returns((ITool?)null);
        _registryMock.Setup(r => r.GetAllTools()).Returns([]);

        var request = new ToolRequest { ToolName = "missing", SessionId = "test" };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedByPolicy_ReturnsBlocked()
    {
        var tool = CreateMockTool("dangerous");
        _registryMock.Setup(r => r.GetTool("dangerous")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult { Allowed = false, Reason = "被阻止" });

        var request = new ToolRequest { ToolName = "dangerous", SessionId = "test" };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Blocked, result.Status);
        Assert.Contains("被阻止", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresReview_ReturnsReviewRequired()
    {
        var tool = CreateMockTool("stop-service");
        _registryMock.Setup(r => r.GetTool("stop-service")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult
            {
                Allowed = true,
                RequiresReview = true,
                RiskLevel = RiskLevel.Level3
            });

        var request = new ToolRequest { ToolName = "stop-service", SessionId = "test" };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.ReviewRequired, result.Status);
        Assert.Contains("确认", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresReview_WithConfirmed_ExecutesSuccessfully()
    {
        var tool = CreateMockTool("stop-service");
        tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                RequestId = "test",
                Status = ToolResultStatus.Success,
                Output = "服务已停止"
            });
        _registryMock.Setup(r => r.GetTool("stop-service")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult
            {
                Allowed = true,
                RequiresReview = true,
                RiskLevel = RiskLevel.Level3
            });

        var request = new ToolRequest
        {
            ToolName = "stop-service",
            SessionId = "test",
            Parameters = new() { ["confirmed"] = "true" }
        };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("服务已停止", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_AutoApproved_ExecutesSuccessfully()
    {
        var tool = CreateMockTool("get-info");
        tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                RequestId = "test",
                Status = ToolResultStatus.Success,
                Output = "CPU: 23%"
            });
        _registryMock.Setup(r => r.GetTool("get-info")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult { Allowed = true, RequiresReview = false });

        var request = new ToolRequest { ToolName = "get-info", SessionId = "test" };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Equal("CPU: 23%", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ToolTimeout_ReturnsTimeout()
    {
        var tool = CreateMockTool("slow-tool");
        tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _registryMock.Setup(r => r.GetTool("slow-tool")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult { Allowed = true, RequiresReview = false });

        var request = new ToolRequest { ToolName = "slow-tool", SessionId = "test" };
        var result = await _executor.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_AuditLogCalled()
    {
        var tool = CreateMockTool("test-tool");
        tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { RequestId = "test", Status = ToolResultStatus.Success, Output = "ok" });
        _registryMock.Setup(r => r.GetTool("test-tool")).Returns(tool.Object);
        _policyMock.Setup(p => p.CheckPolicy(It.IsAny<ToolRequest>()))
            .Returns(new PolicyResult { Allowed = true, RequiresReview = false });

        var request = new ToolRequest { ToolName = "test-tool", SessionId = "test" };
        await _executor.ExecuteAsync(request);

        _auditMock.Verify(a => a.LogAsync(
            It.Is<AuditEntry>(e => e.Success && e.ToolName == "test-tool"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ITool> CreateMockTool(string name)
    {
        var mock = new Mock<ITool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.RiskLevel).Returns(RiskLevel.Level0);
        return mock;
    }
}
