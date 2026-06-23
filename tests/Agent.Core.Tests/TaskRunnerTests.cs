// ============================================================================
// TaskQueue 单元测试
// ============================================================================

using Agent.Core.Engine;

namespace Agent.Core.Tests;

public class TaskQueueTests
{
    [Fact]
    public async Task EnqueueDequeue_WorksCorrectly()
    {
        var queue = new TaskQueue();

        var task = new AgentTask { UserMessage = "test" };
        await queue.EnqueueAsync(task);

        var dequeued = await queue.DequeueAsync();
        Assert.Equal(task.TaskId, dequeued.TaskId);
        Assert.Equal("test", dequeued.UserMessage);
    }

    [Fact]
    public async Task Count_ReturnsCorrectValue()
    {
        var queue = new TaskQueue();

        Assert.Equal(0, queue.Count);

        await queue.EnqueueAsync(new AgentTask { UserMessage = "task 1" });
        await queue.EnqueueAsync(new AgentTask { UserMessage = "task 2" });

        Assert.Equal(2, queue.Count);

        await queue.DequeueAsync();
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void AgentTask_DefaultValues_AreCorrect()
    {
        var task = new AgentTask { UserMessage = "hello" };

        Assert.NotEmpty(task.TaskId);
        Assert.Equal("hello", task.UserMessage);
        Assert.Equal("default", task.SessionId);
        Assert.Equal(Engine.TaskStatus.Pending, task.Status);
        Assert.Null(task.Result);
        Assert.Null(task.Error);
        Assert.Empty(task.ToolCalls);
    }

    [Fact]
    public async Task MultipleEnqueueDequeue_MaintainsOrder()
    {
        var queue = new TaskQueue();

        await queue.EnqueueAsync(new AgentTask { UserMessage = "first" });
        await queue.EnqueueAsync(new AgentTask { UserMessage = "second" });
        await queue.EnqueueAsync(new AgentTask { UserMessage = "third" });

        Assert.Equal("first", (await queue.DequeueAsync()).UserMessage);
        Assert.Equal("second", (await queue.DequeueAsync()).UserMessage);
        Assert.Equal("third", (await queue.DequeueAsync()).UserMessage);
    }
}
