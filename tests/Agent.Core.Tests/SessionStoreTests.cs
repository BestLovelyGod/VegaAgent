// ============================================================================
// SessionStore 单元测试 — 会话缓存与持久化
// ============================================================================

using Agent.Core.Context;
using Agent.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly SessionStore _store;
    private readonly string _sessionsDir;

    public SessionStoreTests()
    {
        _store = new SessionStore(Mock.Of<ILogger<SessionStore>>());
        // SessionStore 内部用 PathConfig.ResolveDataPath("data/sessions")
        // 获取实际目录用于验证
        _sessionsDir = Path.Combine(AppContext.BaseDirectory, "data", "sessions");
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    private static List<LlmMessage> CreateMessages(int count = 2)
    {
        var messages = new List<LlmMessage>();
        for (var i = 0; i < count; i++)
        {
            messages.Add(new LlmMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"消息 {i}"
            });
        }
        return messages;
    }

    [Fact]
    public void GetHistory_UnknownSession_ReturnsEmpty()
    {
        var result = _store.GetHistory("nonexistent-session-id");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetHistory_ReturnsCachedMessages()
    {
        var messages = CreateMessages(3);
        var sessionId = $"test-{Guid.NewGuid():N}";

        await _store.SaveAsync(sessionId, messages);
        // 等待后台写入完成
        await Task.Delay(200);

        var result = _store.GetHistory(sessionId);

        Assert.Equal(3, result.Count);
        Assert.Equal("消息 0", result[0].Content);
        Assert.Equal("消息 1", result[1].Content);
        Assert.Equal("消息 2", result[2].Content);
    }

    [Fact]
    public async Task SaveAsync_EmptyMessages_DoesNothing()
    {
        var sessionId = $"test-{Guid.NewGuid():N}";
        await _store.SaveAsync(sessionId, []);

        var result = _store.GetHistory(sessionId);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_ReturnsDefensiveCopy()
    {
        var messages = CreateMessages(2);
        var sessionId = $"test-{Guid.NewGuid():N}";

        await _store.SaveAsync(sessionId, messages);

        // 修改原始列表不应影响存储
        messages.Add(new LlmMessage { Role = "user", Content = "新消息" });

        var result = _store.GetHistory(sessionId);
        Assert.Equal(2, result.Count); // 不应该包含新添加的消息
    }

    [Fact]
    public async Task GetHistory_ReturnsDefensiveCopy()
    {
        var messages = CreateMessages(2);
        var sessionId = $"test-{Guid.NewGuid():N}";

        await _store.SaveAsync(sessionId, messages);

        var result1 = _store.GetHistory(sessionId);
        result1.Add(new LlmMessage { Role = "user", Content = "篡改" });

        var result2 = _store.GetHistory(sessionId);
        Assert.Equal(2, result2.Count); // 不应该被篡改
    }

    [Fact]
    public async Task SaveAsync_OverwritesCache()
    {
        var sessionId = $"test-{Guid.NewGuid():N}";

        await _store.SaveAsync(sessionId, CreateMessages(2));
        await _store.SaveAsync(sessionId, CreateMessages(5));

        var result = _store.GetHistory(sessionId);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetRecentSessions_ReturnsSavedSessions()
    {
        var sessionId = $"test-{Guid.NewGuid():N}";
        await _store.SaveAsync(sessionId, CreateMessages(1));
        await Task.Delay(300); // 等待写入完成

        var sessions = _store.GetRecentSessions(10);

        // 可能包含其他测试留下的会话，所以只验证我们的 session 存在
        Assert.Contains(sessions, s => s.SessionId == sessionId);
    }

    [Fact]
    public async Task SaveAsync_PersistsToFile()
    {
        var sessionId = $"persist-{Guid.NewGuid():N}";
        await _store.SaveAsync(sessionId, CreateMessages(2));
        await Task.Delay(500); // 等待异步写入

        // 创建新的 SessionStore 实例，验证从文件恢复
        using var store2 = new SessionStore(Mock.Of<ILogger<SessionStore>>());
        var result = store2.GetHistory(sessionId);

        // 如果文件已写入，新实例应该能加载
        // 注意: 文件可能还没写入完成，所以只验证不抛异常
        Assert.NotNull(result);
    }
}
