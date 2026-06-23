// ============================================================================
// 审计 & 审阅 API 端点
// ============================================================================

using Agent.Core.Security;

namespace Agent.Host.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        // 审计日志端点
        var auditGroup = app.MapGroup("/api/audit")
            .WithTags("Security")
            .WithDescription("审计日志");

        auditGroup.MapGet("/", async (
            string? taskId,
            string? toolName,
            int skip,
            int take,
            IAuditLogger auditLogger,
            CancellationToken ct) =>
        {
            var query = new AuditQuery
            {
                TaskId = taskId,
                ToolName = toolName,
                Skip = skip,
                Take = Math.Min(take, 100)
            };
            var entries = await auditLogger.QueryAsync(query, ct);
            return Results.Ok(entries);
        })
        .WithName("QueryAudit")
        .WithDescription("查询审计日志");

        // 审阅端点
        var reviewGroup = app.MapGroup("/api/reviews")
            .WithTags("Reviews")
            .WithDescription("审阅管理");

        reviewGroup.MapGet("/", async (
            IReviewGate reviewGate,
            CancellationToken ct) =>
        {
            var reviews = await reviewGate.GetPendingReviewsAsync(ct);
            return Results.Ok(reviews);
        })
        .WithName("GetPendingReviews")
        .WithDescription("获取待审阅列表");

        reviewGroup.MapPost("/{id}/approve", async (
            string id,
            IReviewGate reviewGate,
            CancellationToken ct) =>
        {
            await reviewGate.ApproveAsync(id, ct: ct);
            return Results.Ok(new { status = "approved" });
        })
        .WithName("ApproveReview")
        .WithDescription("批准审阅");

        reviewGroup.MapPost("/{id}/reject", async (
            string id,
            IReviewGate reviewGate,
            CancellationToken ct) =>
        {
            await reviewGate.RejectAsync(id, ct: ct);
            return Results.Ok(new { status = "rejected" });
        })
        .WithName("RejectReview")
        .WithDescription("拒绝审阅");
    }
}
