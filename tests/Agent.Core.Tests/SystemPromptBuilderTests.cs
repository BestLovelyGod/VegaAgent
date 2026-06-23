// ============================================================================
// SystemPromptBuilder 单元测试
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Planning;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class SystemPromptBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SystemPromptBuilder _builder;

    /// <summary>将 BuildMessages 返回的消息列表合并为纯文本 (用于断言)</summary>
    private static string Flatten(IReadOnlyList<LlmMessage> messages)
        => string.Join("\n", messages.Select(m => m.Content ?? ""));

    public SystemPromptBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prompt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _builder = new SystemPromptBuilder(
            Mock.Of<ILogger<SystemPromptBuilder>>(),
            _tempDir);
    }

    [Fact]
    public void Build_IncludesCorePrompt()
    {
        var prompt = Flatten(_builder.BuildMessages([]));

        Assert.Contains("# 当前上下文", prompt);
        Assert.Contains("计算机名", prompt);
        Assert.Contains("操作系统", prompt);
    }

    [Fact]
    public void Build_IncludesToolsList()
    {
        var tools = new List<ToolInfo>
        {
            new()
            {
                Name = "test-tool",
                Description = "测试工具",
                Category = ToolCategory.SDKIntegrated,
                RiskLevel = RiskLevel.Level0,
                ExposeToLlm = true,
                Parameters =
                [
                    new() { Name = "arg1", Type = "string", Description = "参数1", Required = true },
                    new() { Name = "arg2", Type = "number", Description = "参数2", Required = false }
                ]
            }
        };

        var prompt = Flatten(_builder.BuildMessages(tools));

        // 工具详情通过 API 的 tools 参数传递，不在 prompt 中
        Assert.DoesNotContain("test-tool", prompt);
    }

    [Fact]
    public void Build_IncludesMemoryFile()
    {
        var memoryPath = Path.Combine(_tempDir, "memory.md");
        File.WriteAllText(memoryPath, "## 用户偏好\n\n语言: 中文\n风格: 简洁");

        // 先写文件，再构造 builder
        var builder = new SystemPromptBuilder(
            Mock.Of<ILogger<SystemPromptBuilder>>(),
            _tempDir);

        var prompt = Flatten(builder.BuildMessages([]));

        Assert.Contains("用户偏好", prompt);
        Assert.Contains("语言: 中文", prompt);
        builder.Dispose();
    }

    [Fact]
    public void Build_IncludesAgentFile()
    {
        var agentPath = Path.Combine(_tempDir, "agent.md");
        File.WriteAllText(agentPath, "# 自定义设定\n\n额外规则: 测试");

        var builder = new SystemPromptBuilder(
            Mock.Of<ILogger<SystemPromptBuilder>>(),
            _tempDir);

        var prompt = Flatten(builder.BuildMessages([]));

        Assert.Contains("自定义设定", prompt);
        Assert.Contains("额外规则: 测试", prompt);
        builder.Dispose();
    }

    [Fact]
    public void Build_IncludesContextInfo()
    {
        var prompt = Flatten(_builder.BuildMessages([]));

        Assert.Contains("当前时间", prompt);
        Assert.Contains("计算机名", prompt);
        Assert.Contains("用户", prompt);
        Assert.Contains("操作系统", prompt);
    }

    [Fact]
    public void Build_GroupsToolsByCategory()
    {
        var tools = new List<ToolInfo>
        {
            new() { Name = "tool1", Description = "工具1", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level0, ExposeToLlm = true, Parameters = [] },
            new() { Name = "tool2", Description = "工具2", Category = ToolCategory.PowerShellScript, RiskLevel = RiskLevel.Level0, ExposeToLlm = true, Parameters = [] },
            new() { Name = "tool3", Description = "工具3", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level1, ExposeToLlm = true, Parameters = [] },
        };

        var prompt = Flatten(_builder.BuildMessages(tools));

        // 系统提示词不再包含工具分组信息
        Assert.DoesNotContain("SDK 集成工具", prompt);
        Assert.DoesNotContain("PowerShell 脚本", prompt);
    }

    [Fact]
    public void Build_ShowsRiskIcons()
    {
        // 风险图标现在由 FunctionCallingBridge 处理，不在系统提示词中
        var tools = new List<ToolInfo>
        {
            new() { Name = "safe", Description = "安全", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level0, Parameters = [] },
            new() { Name = "medium", Description = "中等", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level1, Parameters = [] },
            new() { Name = "danger", Description = "危险", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level3, Parameters = [] },
        };

        var prompt = Flatten(_builder.BuildMessages(tools));

        // 系统提示词不再包含风险图标
        Assert.DoesNotContain("🟢", prompt);
        Assert.DoesNotContain("🟡", prompt);
        Assert.DoesNotContain("🔴", prompt);
    }

    [Fact]
    public void GetSummary_ReturnsCorrectCounts()
    {
        var tools = new List<ToolInfo>
        {
            new() { Name = "t1", Description = "d1", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level0, Parameters = [] },
            new() { Name = "t2", Description = "d2", Category = ToolCategory.SDKIntegrated, RiskLevel = RiskLevel.Level0, Parameters = [] },
        };

        var summary = _builder.GetSummary(tools);

        Assert.Equal(2, summary.ToolCount);
        Assert.True(summary.TotalLength > 0);
    }

    [Fact]
    public void Build_EmptyTools_StillContainsCore()
    {
        var prompt = Flatten(_builder.BuildMessages([]));

        Assert.Contains("# 当前上下文", prompt);
        Assert.Contains(".NET", prompt);
        // 空工具列表时，不应有工具分类标题
        Assert.DoesNotContain("SDK 集成工具", prompt);
    }

    public void Dispose()
    {
        _builder.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
