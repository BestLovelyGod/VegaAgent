// ============================================================================
// RiskAssessor 单元测试
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class RiskAssessorTests
{
    private readonly RiskAssessor _assessor;

    public RiskAssessorTests()
    {
        _assessor = new RiskAssessor(Mock.Of<ILogger<RiskAssessor>>());
    }

    [Theory]
    [InlineData("Get-Process", RiskLevel.Level0)]
    [InlineData("Get-Service", RiskLevel.Level0)]
    [InlineData("Test-Connection", RiskLevel.Level0)]
    [InlineData("Find-Files", RiskLevel.Level0)]
    [InlineData("Select-Object", RiskLevel.Level0)]
    public void AssessCommandRisk_ReadOnlyCommands_ReturnsLevel0(string command, RiskLevel expected)
    {
        Assert.Equal(expected, _assessor.AssessCommandRisk(command));
    }

    [Theory]
    [InlineData("Stop-Service", RiskLevel.Level2)]
    [InlineData("Restart-Service", RiskLevel.Level2)]
    [InlineData("Set-ItemProperty", RiskLevel.Level2)]
    [InlineData("New-Item", RiskLevel.Level2)]
    [InlineData("Remove-Item", RiskLevel.Level2)]
    public void AssessCommandRisk_SystemModifyCommands_ReturnsLevel2(string command, RiskLevel expected)
    {
        Assert.Equal(expected, _assessor.AssessCommandRisk(command));
    }

    [Theory]
    [InlineData("Format-Volume", RiskLevel.Level3)]
    [InlineData("Remove-Partition", RiskLevel.Level3)]
    [InlineData("Stop-Computer", RiskLevel.Level3)]
    [InlineData("Restart-Computer", RiskLevel.Level3)]
    [InlineData("Clear-Disk", RiskLevel.Level3)]
    public void AssessCommandRisk_DangerousCommands_ReturnsLevel3(string command, RiskLevel expected)
    {
        Assert.Equal(expected, _assessor.AssessCommandRisk(command));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe", RiskLevel.Level0)]
    [InlineData("C:\\Program Files\\App\\test.exe", RiskLevel.Level0)]
    [InlineData("C:\\Boot\\bootmgr", RiskLevel.Level0)]
    public void AssessPathRisk_DangerousPaths_ReturnsLevel0(string path, RiskLevel expected)
    {
        // 路径不再影响风险等级，用户自行承担后果
        Assert.Equal(expected, _assessor.AssessPathRisk(path));
    }

    [Theory]
    [InlineData("C:\\Temp\\test.txt", RiskLevel.Level0)]
    [InlineData("C:\\Users\\me\\Documents\\file.txt", RiskLevel.Level0)]
    [InlineData("C:\\Agent\\tools\\script.ps1", RiskLevel.Level0)]
    public void AssessPathRisk_WritablePaths_ReturnsLevel0(string path, RiskLevel expected)
    {
        // 路径不再影响风险等级
        Assert.Equal(expected, _assessor.AssessPathRisk(path));
    }

    [Fact]
    public void AssessRisk_ToolRequestWithDangerousPath_ReturnsLevel0()
    {
        var request = new ToolRequest
        {
            ToolName = "Get-Content",
            SessionId = "test",
            Parameters = new() { ["Path"] = "C:\\Windows\\System32\\config\\SAM" }
        };

        // 路径不再影响风险等级
        Assert.Equal(RiskLevel.Level0, _assessor.AssessRisk(request));
    }

    [Fact]
    public void AssessRisk_ToolRequestWithSafeCommand_ReturnsLevel0()
    {
        var request = new ToolRequest
        {
            ToolName = "Get-Process",
            SessionId = "test",
            Parameters = new()
        };

        Assert.Equal(RiskLevel.Level0, _assessor.AssessRisk(request));
    }
}
