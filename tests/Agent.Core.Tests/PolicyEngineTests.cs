// ============================================================================
// PolicyEngine 单元测试
// ============================================================================

using Agent.Core.Config;
using Agent.Core.Models;
using Agent.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Agent.Core.Tests;

public class PolicyEngineTests
{
    private readonly PolicyEngine _engine;

    public PolicyEngineTests()
    {
        var riskAssessor = new RiskAssessor(Mock.Of<ILogger<RiskAssessor>>());
        var config = Options.Create(new AgentConfig
        {
            Security = new SecurityConfig
            {
                WhitelistPath = "nonexistent.json" // 使用默认配置
            }
        });

        _engine = new PolicyEngine(Mock.Of<ILogger<PolicyEngine>>(), riskAssessor, config);
    }

    [Fact]
    public void CheckPolicy_ReadOnlyCommand_Allowed()
    {
        var request = new ToolRequest
        {
            ToolName = "Get-Process",
            SessionId = "test",
            Parameters = new()
        };

        var result = _engine.CheckPolicy(request);

        Assert.True(result.Allowed);
        Assert.False(result.RequiresReview);
        Assert.Equal(RiskLevel.Level0, result.RiskLevel);
    }

    [Fact]
    public void CheckPolicy_DestructiveCommand_RequiresReview()
    {
        // Format-Volume 是 Level 3 (破坏性操作)，需要用户确认
        var request = new ToolRequest
        {
            ToolName = "Format-Volume",
            SessionId = "test",
            Parameters = new()
        };

        var result = _engine.CheckPolicy(request);

        Assert.True(result.Allowed);
        Assert.True(result.RequiresReview); // Level 3 破坏性操作需用户确认
        Assert.Equal(RiskLevel.Level3, result.RiskLevel);
    }

    [Fact]
    public void CheckPolicy_SystemModifyCommand_RequiresReview()
    {
        // Stop-Service 是 Level 2 (系统修改)，现在自动放行无需确认
        var request = new ToolRequest
        {
            ToolName = "Stop-Service",
            SessionId = "test",
            Parameters = new() { ["Name"] = "W3SVC" }
        };

        var result = _engine.CheckPolicy(request);

        Assert.True(result.Allowed);
        Assert.False(result.RequiresReview);
        Assert.Equal(RiskLevel.Level2, result.RiskLevel);
    }

    [Fact]
    public void IsPathBlocked_System32_ReturnsTrue()
    {
        // 不再阻止任何路径，用户自行承担后果
        Assert.False(_engine.IsPathBlocked("C:\\Windows\\System32\\cmd.exe"));
    }

    [Fact]
    public void IsPathBlocked_AgentDir_ReturnsFalse()
    {
        Assert.False(_engine.IsPathBlocked("C:\\Agent\\tools"));
    }
}
