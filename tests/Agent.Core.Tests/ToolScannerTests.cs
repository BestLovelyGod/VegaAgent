// ============================================================================
// ToolScanner 单元测试 — 目录扫描与工具注册
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class ToolScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IToolRegistry> _registryMock = new();

    public ToolScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vega-scanner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // 默认返回空工具列表 (ScanAndRegister 会先遍历清除旧工具)
        _registryMock.Setup(r => r.GetAllTools()).Returns(Array.Empty<ToolInfo>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ToolScanner CreateScanner(string? toolsDir = null)
    {
        return new ToolScanner(
            _registryMock.Object,
            Mock.Of<ILogger<ToolScanner>>(),
            Mock.Of<ILogger<PowerShellTool>>(),
            Mock.Of<ILogger<ExecutableTool>>(),
            toolsDir ?? _tempDir);
    }

    [Fact]
    public void ScanAndRegister_EmptyDirectory_ReturnsZero()
    {
        var scanner = CreateScanner();
        var count = scanner.ScanAndRegister();

        Assert.Equal(0, count);
    }

    [Fact]
    public void ScanAndRegister_NonExistentDirectory_ReturnsZero()
    {
        var scanner = CreateScanner(Path.Combine(_tempDir, "nonexistent"));
        var count = scanner.ScanAndRegister();

        Assert.Equal(0, count);
    }

    [Fact]
    public void ScanAndRegister_ClearsPreviouslyScannedTools()
    {
        // 模拟已有扫描类别的工具信息
        var existingInfo = new ToolInfo
        {
            Name = "old-script",
            Description = "旧工具",
            Category = ToolCategory.PowerShellScript,
            RiskLevel = RiskLevel.Level0,
            Parameters = []
        };

        _registryMock.Setup(r => r.GetAllTools()).Returns(new[] { existingInfo });
        _registryMock.Setup(r => r.Unregister("old-script")).Returns(true);

        var scanner = CreateScanner();
        scanner.ScanAndRegister();

        // 应该尝试清除旧工具
        _registryMock.Verify(r => r.Unregister("old-script"), Times.Once);
    }

    [Fact]
    public void ScanAndRegister_Ps1InScriptsDir_RegistersPowerShellTool()
    {
        // 创建 scripts/system/test-tool.ps1
        var scriptsDir = Path.Combine(_tempDir, "scripts", "system");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "test-tool.ps1"), "Write-Host 'test'");

        var scanner = CreateScanner();
        var count = scanner.ScanAndRegister();

        Assert.True(count > 0, "应至少注册 1 个工具");
        // 验证 Register 被调用 (GroupTool 或 PowerShellTool)
        _registryMock.Verify(r => r.Register(It.IsAny<ITool>()), Times.AtLeastOnce);
    }

    [Fact]
    public void ScanAndRegister_Ps1GroupsByDirectory()
    {
        // 创建两个目录的脚本
        var sysDir = Path.Combine(_tempDir, "scripts", "system");
        var fsDir = Path.Combine(_tempDir, "scripts", "filesystem");
        Directory.CreateDirectory(sysDir);
        Directory.CreateDirectory(fsDir);
        File.WriteAllText(Path.Combine(sysDir, "Get-Info.ps1"), "# desc\nWrite-Host 'info'");
        File.WriteAllText(Path.Combine(fsDir, "Copy-Item2.ps1"), "# desc\nCopy-Item");

        var scanner = CreateScanner();
        var count = scanner.ScanAndRegister();

        Assert.True(count >= 2, "应注册至少 2 个工具 (每个目录一个 GroupTool)");
    }

    [Fact]
    public void ScanAndRegister_IgnoresNonPs1Files()
    {
        // 创建非 .ps1 文件 (在 scripts/system/ 子目录下)
        var sysDir = Path.Combine(_tempDir, "scripts", "system");
        Directory.CreateDirectory(sysDir);
        File.WriteAllText(Path.Combine(sysDir, "readme.txt"), "not a tool");
        File.WriteAllText(Path.Combine(sysDir, "data.json"), "{}");

        var scanner = CreateScanner();
        var count = scanner.ScanAndRegister();

        Assert.Equal(0, count);
    }

    [Fact]
    public void StopWatching_NoWatcherStarted_DoesNotThrow()
    {
        var scanner = CreateScanner();

        var ex = Record.Exception(() => scanner.StopWatching());
        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_NonExistentDirectory_DoesNotThrow()
    {
        var scanner = CreateScanner(Path.Combine(_tempDir, "nonexistent"));

        var ex = Record.Exception(() => scanner.StartWatching());
        Assert.Null(ex);
    }
}
