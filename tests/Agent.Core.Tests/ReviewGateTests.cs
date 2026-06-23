// ============================================================================
// ReviewGate 单元测试
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class ReviewGateTests
{
    private readonly ReviewGate _gate;

    public ReviewGateTests()
    {
        _gate = new ReviewGate(Mock.Of<ILogger<ReviewGate>>());
    }

    [Theory]
    [InlineData(RiskLevel.Level0, true)]
    [InlineData(RiskLevel.Level1, true)]
    [InlineData(RiskLevel.Level2, true)]
    [InlineData(RiskLevel.Level3, false)]
    public void ShouldAutoApprove_CorrectLevels(RiskLevel level, bool expected)
    {
        // Level 0-2 全部自动批准，仅 Level 3 (破坏性操作) 需要用户确认
        Assert.Equal(expected, _gate.ShouldAutoApprove(level));
    }

    [Fact]
    public async Task SubmitForReview_Level0_AutoApproved()
    {
        var request = new ReviewRequest
        {
            TaskId = "t1",
            ToolName = "Get-Process",
            Command = "Get-Process",
            RiskLevel = RiskLevel.Level0
        };

        var response = await _gate.SubmitForReviewAsync(request);

        Assert.Equal(ReviewStatus.AutoApproved, response.Status);
    }

    [Fact]
    public async Task SubmitForReview_Level2_Pending()
    {
        // Level 2 现在自动批准，无需用户确认
        var request = new ReviewRequest
        {
            TaskId = "t2",
            ToolName = "Stop-Service",
            Command = "Stop-Service W3SVC",
            RiskLevel = RiskLevel.Level2
        };

        var response = await _gate.SubmitForReviewAsync(request);

        Assert.Equal(ReviewStatus.AutoApproved, response.Status);
    }

    [Fact]
    public async Task SubmitForReview_Level3_CanReject()
    {
        var request = new ReviewRequest
        {
            TaskId = "t3",
            ToolName = "Format-Volume",
            Command = "Format-Volume -DriveLetter D",
            RiskLevel = RiskLevel.Level3
        };

        var task = _gate.SubmitForReviewAsync(request, CancellationToken.None);

        // 拒绝
        await _gate.RejectAsync(request.ReviewId, "太危险了");

        var response = await task;
        Assert.Equal(ReviewStatus.Rejected, response.Status);
    }
}
