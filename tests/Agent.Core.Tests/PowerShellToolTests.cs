// ============================================================================
// PowerShell 工具单元测试
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class PowerShellCommandToolTests
{
    private readonly PowerShellCommandTool _tool = new(Mock.Of<ILogger<PowerShellCommandTool>>());

    [Fact]
    public void Properties_AreCorrect()
    {
        Assert.Equal("powershell", _tool.Name);
        Assert.Equal(ToolCategory.PowerShellCommand, _tool.Category);
        Assert.NotEmpty(_tool.Description);
    }

    [Fact]
    public void GetParameters_ReturnsCommandAndTimeout()
    {
        var parameters = _tool.GetParameters();
        Assert.Contains(parameters, p => p.Name == "Command");
        Assert.Contains(parameters, p => p.Name == "Timeout");
    }

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ReturnsFailed()
    {
        var request = new ToolRequest
        {
            ToolName = "powershell",
            SessionId = "test",
            Parameters = new()
        };

        var result = await _tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("Command", result.Error!);
    }

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput()
    {
        var request = new ToolRequest
        {
            ToolName = "powershell",
            SessionId = "test",
            Parameters = new() { ["Command"] = "Write-Output 'Hello Agent'" }
        };

        var result = await _tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("Hello Agent", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_GetDate_ReturnsDate()
    {
        var request = new ToolRequest
        {
            ToolName = "powershell",
            SessionId = "test",
            Parameters = new() { ["Command"] = "Get-Date -Format 'yyyy'" }
        };

        var result = await _tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains(DateTime.Now.Year.ToString(), result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_PipelineCommand_Works()
    {
        var request = new ToolRequest
        {
            ToolName = "powershell",
            SessionId = "test",
            Parameters = new() { ["Command"] = "1..5 | ForEach-Object { $_ * 2 }" }
        };

        var result = await _tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("2", result.Output);
        Assert.Contains("10", result.Output);
    }
}

public class PowerShellToolTests
{
    [Fact]
    public void ExtractDescription_ReadsCommentHeader()
    {
        var tempFile = Path.GetTempFileName() + ".ps1";
        try
        {
            File.WriteAllText(tempFile, "# 测试脚本\n# 描述信息\nparam([string]$Name)\nWrite-Output $Name");

            var desc = PowerShellTool.ExtractDescription(tempFile);

            Assert.Contains("测试脚本", desc);
            Assert.Contains("描述信息", desc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AnalyzeParameters_ExtractsFromScript()
    {
        var tempFile = Path.GetTempFileName() + ".ps1";
        try
        {
            File.WriteAllText(tempFile, "param(\n    [string]$Name,\n    [int]$Count,\n    [switch]$Force\n)");

            var tool = new PowerShellTool(tempFile, logger: Mock.Of<Microsoft.Extensions.Logging.Abstractions.NullLogger<PowerShellTool>>());
            var parameters = tool.GetParameters();

            Assert.Contains(parameters, p => p.Name == "Name" && p.Type == "string");
            Assert.Contains(parameters, p => p.Name == "Count" && p.Type == "number");
            Assert.Contains(parameters, p => p.Name == "Force" && p.Type == "boolean");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
