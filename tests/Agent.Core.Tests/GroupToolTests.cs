// ============================================================================
// GroupTool 单元测试 — 组工具路由与参数合并
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class GroupToolTests
{
    private static Mock<ITool> CreateMockChild(string name, RiskLevel risk = RiskLevel.Level0,
        ToolParameter[]? parameters = null)
    {
        var mock = new Mock<ITool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.Description).Returns($"desc-{name}");
        mock.Setup(t => t.Category).Returns(ToolCategory.PowerShellScript);
        mock.Setup(t => t.RiskLevel).Returns(risk);
        mock.Setup(t => t.GetParameters()).Returns(parameters ?? []);
        mock.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                RequestId = "test",
                Status = ToolResultStatus.Success,
                Output = $"executed-{name}",
                ToolName = name
            });
        return mock;
    }

    [Fact]
    public void Name_ReturnsConstructorName()
    {
        var group = new GroupTool("my-group", "desc", []);
        Assert.Equal("my-group", group.Name);
    }

    [Fact]
    public void RiskLevel_TakesMaxFromChildren()
    {
        var child1 = CreateMockChild("c1", RiskLevel.Level0);
        var child2 = CreateMockChild("c2", RiskLevel.Level2);
        var child3 = CreateMockChild("c3", RiskLevel.Level1);

        var group = new GroupTool("g", "desc",
        [
            ("action1", child1.Object),
            ("action2", child2.Object),
            ("action3", child3.Object)
        ]);

        Assert.Equal(RiskLevel.Level2, group.RiskLevel);
    }

    [Fact]
    public void RiskLevel_EmptyChildren_UsesConstructorDefault()
    {
        var group = new GroupTool("g", "desc", [], riskLevel: RiskLevel.Level3);
        Assert.Equal(RiskLevel.Level3, group.RiskLevel);
    }

    [Fact]
    public void GetParameters_IncludesActionEnum()
    {
        var child1 = CreateMockChild("c1");
        var child2 = CreateMockChild("c2");

        var group = new GroupTool("g", "desc",
        [
            ("run", child1.Object),
            ("stop", child2.Object)
        ]);

        var parameters = group.GetParameters();
        var actionParam = parameters.FirstOrDefault(p => p.Name == "action");

        Assert.NotNull(actionParam);
        Assert.True(actionParam.Required);
        Assert.Contains("run", actionParam.Enum!.Select(e => e.ToString()));
        Assert.Contains("stop", actionParam.Enum!.Select(e => e.ToString()));
    }

    [Fact]
    public void GetParameters_MergesChildParameters()
    {
        var child1 = CreateMockChild("c1", parameters:
        [
            new ToolParameter { Name = "path", Type = "string", Required = true },
            new ToolParameter { Name = "verbose", Type = "boolean" }
        ]);
        var child2 = CreateMockChild("c2", parameters:
        [
            new ToolParameter { Name = "path", Type = "string" },  // duplicate
            new ToolParameter { Name = "timeout", Type = "number" }
        ]);

        var group = new GroupTool("g", "desc",
        [
            ("a1", child1.Object),
            ("a2", child2.Object)
        ]);

        var paramNames = group.GetParameters().Select(p => p.Name).ToList();

        Assert.Contains("action", paramNames);
        Assert.Contains("path", paramNames);
        Assert.Contains("verbose", paramNames);
        Assert.Contains("timeout", paramNames);
        // "path" 应该只出现一次 (去重)
        Assert.Equal(1, paramNames.Count(n => n == "path"));
    }

    [Fact]
    public async Task ExecuteAsync_ValidAction_RoutesToChild()
    {
        var child = CreateMockChild("child-tool");

        var group = new GroupTool("g", "desc", [("run", child.Object)]);

        var request = new ToolRequest
        {
            ToolName = "g",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["action"] = "run",
                ["path"] = @"C:\test"
            }
        };

        var result = await group.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Equal("executed-child-tool", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsParametersExcludingAction()
    {
        ToolRequest? capturedRequest = null;
        var child = new Mock<ITool>();
        child.Setup(t => t.Name).Returns("child");
        child.Setup(t => t.RiskLevel).Returns(RiskLevel.Level0);
        child.Setup(t => t.GetParameters()).Returns([]);
        child.Setup(t => t.ExecuteAsync(It.IsAny<ToolRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ToolRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ToolResult { RequestId = "test", Status = ToolResultStatus.Success, ToolName = "child" });

        var group = new GroupTool("g", "desc", [("run", child.Object)]);

        var request = new ToolRequest
        {
            ToolName = "g",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["action"] = "run",
                ["path"] = @"C:\test",
                ["timeout"] = 30
            }
        };

        await group.ExecuteAsync(request);

        Assert.NotNull(capturedRequest);
        Assert.False(capturedRequest.Parameters.ContainsKey("action"));
        Assert.Equal(@"C:\test", capturedRequest.Parameters["path"]);
        Assert.Equal(30, capturedRequest.Parameters["timeout"]);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsFailed()
    {
        var child = CreateMockChild("child");
        var group = new GroupTool("g", "desc", [("run", child.Object)]);

        var request = new ToolRequest
        {
            ToolName = "g",
            SessionId = "test",
            Parameters = new Dictionary<string, object> { ["action"] = "invalid" }
        };

        var result = await group.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("invalid", result.Error);
        Assert.Contains("run", result.Error); // 应列出可用 action
    }

    [Fact]
    public async Task ExecuteAsync_MissingAction_ReturnsFailed()
    {
        var child = CreateMockChild("child");
        var group = new GroupTool("g", "desc", [("run", child.Object)]);

        var request = new ToolRequest
        {
            ToolName = "g",
            SessionId = "test",
            Parameters = new Dictionary<string, object>()
        };

        var result = await group.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ActionIsCaseInsensitive()
    {
        var child = CreateMockChild("child");
        var group = new GroupTool("g", "desc", [("Run", child.Object)]);

        var request = new ToolRequest
        {
            ToolName = "g",
            SessionId = "test",
            Parameters = new Dictionary<string, object> { ["action"] = "run" }
        };

        var result = await group.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
    }
}
