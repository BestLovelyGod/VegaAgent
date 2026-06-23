// ============================================================================
// 健康检查端点
// ============================================================================

namespace Agent.Host.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "IgnorantAgent",
            version = "1.0.0",
            timestamp = DateTime.UtcNow
        }))
        .WithName("Health")
        .WithTags("System")
        .WithDescription("健康检查")
        .ExcludeFromDescription();
    }
}
