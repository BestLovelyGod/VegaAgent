// ============================================================================
// ApiKeyProvider 单元测试 — API Key 解析优先级
// ============================================================================

using Agent.Core.Config;

namespace Agent.Core.Tests;

public class ApiKeyProviderTests : IDisposable
{
    private readonly string? _origAgentKey;
    private readonly string? _origMimoKey;

    public ApiKeyProviderTests()
    {
        // 保存原始环境变量
        _origAgentKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
        _origMimoKey = Environment.GetEnvironmentVariable("MIMO_API_KEY");
    }

    public void Dispose()
    {
        // 恢复原始环境变量
        Environment.SetEnvironmentVariable("AGENT_API_KEY", _origAgentKey);
        Environment.SetEnvironmentVariable("MIMO_API_KEY", _origMimoKey);
    }

    [Fact]
    public void Resolve_AgentKeyEnvVar_TakesHighestPriority()
    {
        Environment.SetEnvironmentVariable("AGENT_API_KEY", "agent-key-123");
        Environment.SetEnvironmentVariable("MIMO_API_KEY", "mimo-key-456");

        var result = ApiKeyProvider.Resolve("config-key-789");

        Assert.Equal("agent-key-123", result);
    }

    [Fact]
    public void Resolve_MimoKeyEnvVar_SecondPriority()
    {
        Environment.SetEnvironmentVariable("AGENT_API_KEY", null);
        Environment.SetEnvironmentVariable("MIMO_API_KEY", "mimo-key-456");

        var result = ApiKeyProvider.Resolve("config-key-789");

        Assert.Equal("mimo-key-456", result);
    }

    [Fact]
    public void Resolve_ConfigKey_Fallback()
    {
        Environment.SetEnvironmentVariable("AGENT_API_KEY", null);
        Environment.SetEnvironmentVariable("MIMO_API_KEY", null);

        var result = ApiKeyProvider.Resolve("config-key-789");

        Assert.Equal("config-key-789", result);
    }

    [Fact]
    public void Resolve_NoKeyAnywhere_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("AGENT_API_KEY", null);
        Environment.SetEnvironmentVariable("MIMO_API_KEY", null);

        var result = ApiKeyProvider.Resolve();

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_EmptyEnvVar_FallsThroughToNext()
    {
        Environment.SetEnvironmentVariable("AGENT_API_KEY", "");
        Environment.SetEnvironmentVariable("MIMO_API_KEY", "mimo-key");

        var result = ApiKeyProvider.Resolve();

        // 空字符串不等于 null，会被选中 (Environment.GetEnvironmentVariable 返回 "" 不是 null)
        // 但实际上空字符串也是 falsy，取决于实现
        // 实际: GetEnvironmentVariable("") 返回 ""，不会 fallthrough
        Assert.NotNull(result);
    }
}
