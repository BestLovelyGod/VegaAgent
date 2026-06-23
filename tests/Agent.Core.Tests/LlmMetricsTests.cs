// ============================================================================
// LlmMetrics 单元测试
// ============================================================================

using Agent.Core.Observability;

namespace Agent.Core.Tests;

public class LlmMetricsTests
{
    [Fact]
    public void RecordRequest_AccumulatesCorrectly()
    {
        var metrics = new LlmMetrics();

        metrics.RecordRequest(100, 50, 1000);
        metrics.RecordRequest(200, 100, 2000, isError: true);

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(2, snapshot.TotalRequests);
        Assert.Equal(1, snapshot.TotalErrors);
        Assert.Equal(300, snapshot.TotalPromptTokens);
        Assert.Equal(150, snapshot.TotalCompletionTokens);
        Assert.Equal(450, snapshot.TotalTokens);
        Assert.Equal(0.5, snapshot.ErrorRate);
    }

    [Fact]
    public void GetSnapshot_CalculatesLatencyPercentiles()
    {
        var metrics = new LlmMetrics();

        // 添加 10 个延迟样本
        for (int i = 1; i <= 10; i++)
            metrics.RecordRequest(10, 5, i * 100); // 100, 200, ..., 1000

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(550, snapshot.AvgLatencyMs, 0.1);
        Assert.True(snapshot.P50LatencyMs > 0);
        Assert.True(snapshot.P95LatencyMs >= snapshot.P50LatencyMs);
        Assert.True(snapshot.P99LatencyMs >= snapshot.P95LatencyMs);
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        var metrics = new LlmMetrics();

        metrics.RecordRequest(100, 50, 1000);
        metrics.Reset();

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(0, snapshot.TotalRequests);
        Assert.Equal(0, snapshot.TotalTokens);
        Assert.Equal(0, snapshot.AvgLatencyMs);
    }

    [Fact]
    public void GetSnapshot_EmptyMetrics_ReturnsZeros()
    {
        var metrics = new LlmMetrics();
        var snapshot = metrics.GetSnapshot();

        Assert.Equal(0, snapshot.TotalRequests);
        Assert.Equal(0, snapshot.ErrorRate);
        Assert.Equal(0, snapshot.AvgLatencyMs);
    }
}
