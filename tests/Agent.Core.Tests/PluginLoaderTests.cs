// ============================================================================
// PluginLoader 单元测试 — 插件发现与加载
// ============================================================================

using Agent.Core.Plugins;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class PluginLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public PluginLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vega-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PluginLoader CreateLoader(string? pluginsDir = null)
    {
        return new PluginLoader(
            Mock.Of<ILogger<PluginLoader>>(),
            Mock.Of<IServiceProvider>(),
            pluginsDir ?? _tempDir);
    }

    [Fact]
    public async Task LoadAllAsync_EmptyDirectory_ReturnsZero()
    {
        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task LoadAllAsync_NonExistentDirectory_ReturnsZero()
    {
        var loader = CreateLoader(Path.Combine(_tempDir, "nonexistent"));
        var count = await loader.LoadAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task LoadAllAsync_SubDirWithoutManifest_Skips()
    {
        // 创建子目录但没有 plugin.json
        Directory.CreateDirectory(Path.Combine(_tempDir, "my-plugin"));
        File.WriteAllText(Path.Combine(_tempDir, "my-plugin", "readme.txt"), "not a plugin");

        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(0, count);
        Assert.Empty(loader.LoadedPlugins);
    }

    [Fact]
    public async Task LoadAllAsync_InvalidManifestJson_ReturnsZero()
    {
        // 创建包含无效 JSON 的 plugin.json
        var pluginDir = Path.Combine(_tempDir, "bad-plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), "{ invalid json !!!");

        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task LoadAllAsync_ValidManifestNoDll_ParsesManifest()
    {
        // 创建有效的 plugin.json (无 DLL)
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """
        {
            "name": "test-plugin",
            "version": "1.0.0",
            "description": "测试插件",
            "author": "test"
        }
        """);

        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(1, count);
        Assert.Single(loader.LoadedPlugins);
        Assert.True(loader.LoadedPlugins.ContainsKey("test-plugin"));
        Assert.Equal("test-plugin", loader.LoadedPlugins["test-plugin"].Manifest.Name);
        Assert.Equal("1.0.0", loader.LoadedPlugins["test-plugin"].Manifest.Version);
    }

    [Fact]
    public async Task LoadAllAsync_MultiplePlugins_LoadsAll()
    {
        // 创建两个有效插件
        foreach (var name in new[] { "plugin-a", "plugin-b" })
        {
            var dir = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
                "name": "{{name}}",
                "version": "1.0.0",
                "description": "{{name}} 插件"
            }
            """);
        }

        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(2, count);
        Assert.Equal(2, loader.LoadedPlugins.Count);
    }

    [Fact]
    public void SetPluginsDir_ChangesDirectory()
    {
        var loader = CreateLoader();
        var newDir = Path.Combine(_tempDir, "new-plugins");
        Directory.CreateDirectory(newDir);

        // 不应抛异常
        loader.SetPluginsDir(newDir);
    }

    [Fact]
    public void LoadedPlugins_InitiallyEmpty()
    {
        var loader = CreateLoader();
        Assert.Empty(loader.LoadedPlugins);
    }

    [Fact]
    public async Task LoadAllAsync_MissingRequiredFields_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "incomplete");
        Directory.CreateDirectory(pluginDir);
        // Name 和 Version 是 required，缺失会导致反序列化返回 null
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """
        {
            "description": "缺少必填字段"
        }
        """);

        var loader = CreateLoader();
        var count = await loader.LoadAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public void StopWatching_NoWatcherStarted_DoesNotThrow()
    {
        var loader = CreateLoader();
        var ex = Record.Exception(() => loader.StopWatching());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var loader = CreateLoader();
        await loader.LoadAllAsync();

        var ex = await Record.ExceptionAsync(() => loader.DisposeAsync().AsTask());
        Assert.Null(ex);
    }
}
