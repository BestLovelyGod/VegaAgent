// ============================================================================
// AuditLogger 单元测试 — 审计日志记录与查询
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class AuditLoggerTests : IDisposable
{
    private readonly AuditLogger _logger;
    private readonly Mock<ILogger<AuditLogger>> _mockLogger = new();

    public AuditLoggerTests()
    {
        _logger = new AuditLogger(_mockLogger.Object);
    }

    public void Dispose()
    {
        _logger.Dispose();
    }

    private static AuditEntry CreateEntry(
        string tool = "TestTool",
        string command = "test-cmd",
        RiskLevel risk = RiskLevel.Level0,
        bool success = true,
        string? taskId = null,
        DateTime? timestamp = null) => new()
    {
        AuditId = Guid.NewGuid().ToString(),
        TaskId = taskId,
        Timestamp = timestamp ?? DateTime.UtcNow,
        RiskLevel = risk,
        ReviewStatus = ReviewStatus.AutoApproved,
        ToolName = tool,
        Command = command,
        Success = success,
        Duration = TimeSpan.FromMilliseconds(100)
    };

    [Fact]
    public async Task LogAsync_AddsEntry_RetrievableByQuery()
    {
        var entry = CreateEntry(taskId: "task-1");

        await _logger.LogAsync(entry);
        var results = await _logger.GetByTaskIdAsync("task-1");

        Assert.Single(results);
        Assert.Equal("TestTool", results[0].ToolName);
    }

    [Fact]
    public async Task QueryAsync_FilterByToolName()
    {
        await _logger.LogAsync(CreateEntry(tool: "PowerShell"));
        await _logger.LogAsync(CreateEntry(tool: "DotnetScript"));
        await _logger.LogAsync(CreateEntry(tool: "PowerShell"));

        var results = await _logger.QueryAsync(new AuditQuery { ToolName = "Power" });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("Power", r.ToolName));
    }

    [Fact]
    public async Task QueryAsync_FilterByMinRiskLevel()
    {
        await _logger.LogAsync(CreateEntry(risk: RiskLevel.Level0));
        await _logger.LogAsync(CreateEntry(risk: RiskLevel.Level2));
        await _logger.LogAsync(CreateEntry(risk: RiskLevel.Level3));

        var results = await _logger.QueryAsync(new AuditQuery { MinRiskLevel = RiskLevel.Level2 });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.RiskLevel >= RiskLevel.Level2));
    }

    [Fact]
    public async Task QueryAsync_FilterBySuccessOnly()
    {
        await _logger.LogAsync(CreateEntry(success: true));
        await _logger.LogAsync(CreateEntry(success: false));
        await _logger.LogAsync(CreateEntry(success: true));

        var results = await _logger.QueryAsync(new AuditQuery { SuccessOnly = true });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task QueryAsync_FilterByTimeRange()
    {
        var now = DateTime.UtcNow;
        await _logger.LogAsync(CreateEntry(timestamp: now.AddHours(-2)));
        await _logger.LogAsync(CreateEntry(timestamp: now.AddHours(-1)));
        await _logger.LogAsync(CreateEntry(timestamp: now));

        var results = await _logger.QueryAsync(new AuditQuery
        {
            From = now.AddMinutes(-90),
            To = now.AddMinutes(1)
        });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task QueryAsync_SkipAndTake_Pagination()
    {
        for (var i = 0; i < 10; i++)
            await _logger.LogAsync(CreateEntry(tool: $"Tool{i}"));

        var page1 = await _logger.QueryAsync(new AuditQuery { Skip = 0, Take = 3 });
        var page2 = await _logger.QueryAsync(new AuditQuery { Skip = 3, Take = 3 });

        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        // 确保不重叠
        Assert.DoesNotContain(page1[0].AuditId, page2.Select(p => p.AuditId));
    }

    [Fact]
    public async Task GetByTaskIdAsync_ReturnsEmptyForUnknownTask()
    {
        var results = await _logger.GetByTaskIdAsync("nonexistent");
        Assert.Empty(results);
    }
}
