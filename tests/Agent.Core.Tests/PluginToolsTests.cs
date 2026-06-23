// ============================================================================
// PluginTools 单元测试
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Plugins;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class PluginToolsTests
{
    private readonly Mock<PluginBuilder> _builderMock;
    private readonly Mock<PluginLoader> _loaderMock;

    public PluginToolsTests()
    {
        _builderMock = new Mock<PluginBuilder>(
            Mock.Of<ILogger<PluginBuilder>>(),
            (string?)null);

        _loaderMock = new Mock<PluginLoader>(
            Mock.Of<ILogger<PluginLoader>>(),
            Mock.Of<IServiceProvider>(),
            (string?)null);
    }

    [Fact]
    public async Task CreatePlugin_Success_ReturnsSuccessResult()
    {
        var buildResult = new PluginBuildResult
        {
            Success = true,
            PluginName = "test-plugin",
            Version = "1.0.0",
            DllPath = "/path/to/test-plugin.dll",
            PluginDir = "/path/to/dir",
            Message = "成功",
            TypeName = "TestPlugin"
        };

        _builderMock.Setup(b => b.BuildAndDeployAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]?>(),
            It.IsAny<string[]?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(buildResult);

        _loaderMock.Setup(l => l.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var tool = new CreatePluginTool(
            _builderMock.Object,
            _loaderMock.Object,
            Mock.Of<ILogger<CreatePluginTool>>());

        var request = new ToolRequest
        {
            ToolName = "create-plugin",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["Name"] = "test-plugin",
                ["Version"] = "1.0.0",
                ["Description"] = "Test",
                ["SourceCode"] = "code"
            }
        };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("test-plugin", result.Output);
    }

    [Fact]
    public async Task CreatePlugin_Failed_ReturnsError()
    {
        var buildResult = new PluginBuildResult
        {
            Success = false,
            Message = "编译失败",
            Errors = ["CS0001: 错误"]
        };

        _builderMock.Setup(b => b.BuildAndDeployAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]?>(),
            It.IsAny<string[]?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(buildResult);

        var tool = new CreatePluginTool(
            _builderMock.Object,
            _loaderMock.Object,
            Mock.Of<ILogger<CreatePluginTool>>());

        var request = new ToolRequest
        {
            ToolName = "create-plugin",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["Name"] = "bad-plugin",
                ["Description"] = "Bad",
                ["SourceCode"] = "bad code"
            }
        };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("编译失败", result.Output);
    }

    [Fact]
    public async Task CreatePlugin_MissingName_ReturnsError()
    {
        var tool = new CreatePluginTool(
            _builderMock.Object,
            _loaderMock.Object,
            Mock.Of<ILogger<CreatePluginTool>>());

        var request = new ToolRequest
        {
            ToolName = "create-plugin",
            SessionId = "test",
            Parameters = new Dictionary<string, object>
            {
                ["Description"] = "Test",
                ["SourceCode"] = "code"
            }
        };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Failed, result.Status);
        Assert.Contains("Name", result.Error);
    }

    [Fact]
    public async Task ListPlugins_ReturnsLoadedPlugins()
    {
        _loaderMock.Setup(l => l.LoadedPlugins).Returns(
            new Dictionary<string, LoadedPlugin>
            {
                ["test"] = new LoadedPlugin
                {
                    Manifest = new PluginManifest { Name = "test", Version = "1.0.0", Description = "Test" },
                    Context = new PluginContext("/data"),
                    Directory = "/path"
                }
            });

        var tool = new ListPluginsTool(_loaderMock.Object);
        var request = new ToolRequest { ToolName = "list-plugins", SessionId = "test", Parameters = new() };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("test", result.Output);
    }

    [Fact]
    public async Task ReloadPlugins_ReturnsCount()
    {
        _loaderMock.Setup(l => l.ReloadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var tool = new ReloadPluginsTool(_loaderMock.Object);
        var request = new ToolRequest { ToolName = "reload-plugins", SessionId = "test", Parameters = new() };

        var result = await tool.ExecuteAsync(request);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("3", result.Output);
    }
}
