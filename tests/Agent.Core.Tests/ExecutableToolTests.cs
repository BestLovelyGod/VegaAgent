// ============================================================================
// ExecutableTool 单元测试
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class ExecutableToolTests : IDisposable
{
    private readonly string _tempDir;

    public ExecutableToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"exe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var exePath = Path.Combine(_tempDir, "test.exe");
        File.WriteAllText(exePath, "");

        var tool = new ExecutableTool(exePath);

        Assert.Equal("test", tool.Name);
        Assert.Contains("test.exe", tool.Description);
        Assert.Equal(ToolCategory.Executable, tool.Category);
        Assert.Equal(RiskLevel.Level1, tool.RiskLevel);
    }

    [Fact]
    public void Constructor_CustomName_OverridesDefault()
    {
        var exePath = Path.Combine(_tempDir, "test.exe");
        File.WriteAllText(exePath, "");

        var tool = new ExecutableTool(exePath, name: "custom-name", description: "Custom description");

        Assert.Equal("custom-name", tool.Name);
        Assert.Equal("Custom description", tool.Description);
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsFailed()
    {
        var tool = new ExecutableTool("C:\\nonexistent\\fake.exe");

        var request = new ToolRequest
        {
            ToolName = "fake",
            SessionId = "test",
            Parameters = new()
        };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("不存在", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NullByteInArgs_ReturnsBlocked()
    {
        var exePath = Path.Combine(_tempDir, "test.exe");
        File.WriteAllText(exePath, "");

        var tool = new ExecutableTool(exePath);

        var request = new ToolRequest
        {
            ToolName = "test",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["Args"] = "arg1\0malicious"
            }
        };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Blocked, result.Status);
        Assert.Contains("非法字符", result.Error);
    }

    [Fact]
    public void GetParameters_ReturnsDefaultParameters()
    {
        var exePath = Path.Combine(_tempDir, "test.exe");
        File.WriteAllText(exePath, "");

        var tool = new ExecutableTool(exePath);
        var parameters = tool.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Contains(parameters, p => p.Name == "Args");
        Assert.Contains(parameters, p => p.Name == "WorkingDirectory");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
