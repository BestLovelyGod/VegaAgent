// ============================================================================
// SaveMemoryTool 单元测试 — 记忆写入与 section 追加
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class SaveMemoryToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _memoryPath;
    private readonly SaveMemoryTool _tool;

    public SaveMemoryToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vega-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _memoryPath = Path.Combine(_tempDir, "memory.md");
        _tool = new SaveMemoryTool(Mock.Of<ILogger<SaveMemoryTool>>(), _memoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ToolRequest MakeRequest(string? section = null, string? content = null) => new()
    {
        ToolName = "save-memory",
        SessionId = "test",
        Parameters = new Dictionary<string, object>(
            new Dictionary<string, string?>
            {
                ["section"] = section,
                ["content"] = content
            }.Where(kv => kv.Value is not null)
             .Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value!))
        )
    };

    [Fact]
    public void Name_IsSaveMemory()
    {
        Assert.Equal("save-memory", _tool.Name);
    }

    [Fact]
    public void RiskLevel_IsLevel0()
    {
        Assert.Equal(RiskLevel.Level0, _tool.RiskLevel);
    }

    [Fact]
    public void GetParameters_HasSectionAndContent()
    {
        var parameters = _tool.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Contains(parameters, p => p.Name == "section" && p.Required);
        Assert.Contains(parameters, p => p.Name == "content" && p.Required);
    }

    [Fact]
    public async Task ExecuteAsync_MissingSection_ReturnsFailed()
    {
        var result = await _tool.ExecuteAsync(MakeRequest(content: "test content"));

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("section", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ReturnsFailed()
    {
        var result = await _tool.ExecuteAsync(MakeRequest(section: "用户偏好"));

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("content", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NewFile_CreatesWithDefaultSections()
    {
        var result = await _tool.ExecuteAsync(
            MakeRequest(section: "用户偏好", content: "- 喜欢用 PowerShell"));

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.True(File.Exists(_memoryPath));

        var content = await File.ReadAllTextAsync(_memoryPath);
        Assert.Contains("## 用户偏好", content);
        Assert.Contains("喜欢用 PowerShell", content);
        Assert.Contains("# 长期记忆", content); // 默认模板
    }

    [Fact]
    public async Task ExecuteAsync_AppendToExistingSection()
    {
        // 第一次写入
        await _tool.ExecuteAsync(
            MakeRequest(section: "用户偏好", content: "- 习惯 1"));

        // 第二次追加
        await _tool.ExecuteAsync(
            MakeRequest(section: "用户偏好", content: "- 习惯 2"));

        var content = await File.ReadAllTextAsync(_memoryPath);
        Assert.Contains("习惯 1", content);
        Assert.Contains("习惯 2", content);
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentSection_AutoCreates()
    {
        // 先写入默认模板
        await _tool.ExecuteAsync(
            MakeRequest(section: "用户偏好", content: "- 习惯 1"));

        // 写入新 section
        await _tool.ExecuteAsync(
            MakeRequest(section: "自定义分区", content: "- 自定义内容"));

        var content = await File.ReadAllTextAsync(_memoryPath);
        Assert.Contains("## 自定义分区", content);
        Assert.Contains("自定义内容", content);
    }

    [Fact]
    public async Task ExecuteAsync_UsesAtomicWrite()
    {
        await _tool.ExecuteAsync(
            MakeRequest(section: "用户偏好", content: "- 测试原子写入"));

        // .tmp 文件应该已被清理
        Assert.False(File.Exists(_memoryPath + ".tmp"));
        Assert.True(File.Exists(_memoryPath));
    }

    [Fact]
    public void Category_IsSDKIntegrated()
    {
        Assert.Equal(ToolCategory.SDKIntegrated, _tool.Category);
    }
}
