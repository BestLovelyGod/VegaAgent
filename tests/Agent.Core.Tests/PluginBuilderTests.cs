// ============================================================================
// PluginBuilder 单元测试
// ============================================================================

using Agent.Core.Plugins;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class PluginBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PluginBuilder _builder;

    public PluginBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"plugin-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _builder = new PluginBuilder(
            Mock.Of<ILogger<PluginBuilder>>(),
            _tempDir);
    }

    [Fact]
    public async Task BuildAndDeployAsync_ValidPlugin_CreatesFiles()
    {
        var sourceCode = """
            using Agent.Core.Plugins;

            public class TestPlugin : IPlugin
            {
                public string Name => "test";
                public string Version => "1.0.0";
                public string Description => "Test plugin";
                public async Task InitializeAsync(IPluginContext ctx, CancellationToken ct) => await Task.CompletedTask;
                public async Task ShutdownAsync(CancellationToken ct) => await Task.CompletedTask;
            }
            """;

        var result = await _builder.BuildAndDeployAsync(
            "test-plugin", "1.0.0", "Test plugin", sourceCode);

        Assert.True(result.Success, result.Message);
        Assert.Equal("test-plugin", result.PluginName);
        Assert.Equal("1.0.0", result.Version);
        Assert.NotNull(result.DllPath);
        Assert.NotNull(result.PluginDir);
        Assert.True(File.Exists(result.DllPath));
        Assert.True(File.Exists(Path.Combine(result.PluginDir, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(result.PluginDir, "test-plugin.cs")));
    }

    [Fact]
    public async Task BuildAndDeployAsync_InvalidCode_ReturnsError()
    {
        var sourceCode = "this is not valid C# code!!!";

        var result = await _builder.BuildAndDeployAsync(
            "bad-plugin", "1.0.0", "Bad plugin", sourceCode);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task BuildAndDeployAsync_NoIPlugin_ReturnsError()
    {
        var sourceCode = """
            public class NotAPlugin
            {
                public string Name => "not-a-plugin";
            }
            """;

        var result = await _builder.BuildAndDeployAsync(
            "no-interface", "1.0.0", "No interface", sourceCode);

        Assert.False(result.Success);
        Assert.Contains("验证失败", result.Message);
    }

    [Fact]
    public async Task BuildAndDeployAsync_WithEndpoints_SetsManifest()
    {
        var sourceCode = """
            using Agent.Core.Plugins;

            public class EndpointPlugin : IPlugin
            {
                public string Name => "endpoint";
                public string Version => "1.0.0";
                public string Description => "Endpoint plugin";
                public async Task InitializeAsync(IPluginContext ctx, CancellationToken ct) => await Task.CompletedTask;
                public async Task ShutdownAsync(CancellationToken ct) => await Task.CompletedTask;
            }
            """;

        var result = await _builder.BuildAndDeployAsync(
            "endpoint-plugin", "1.0.0", "Endpoint plugin", sourceCode,
            ["/api/test", "/api/test2"]);

        Assert.True(result.Success);

        var manifestPath = Path.Combine(result.PluginDir!, "plugin.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains("/api/test", manifestJson);
        Assert.Contains("/api/test2", manifestJson);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
