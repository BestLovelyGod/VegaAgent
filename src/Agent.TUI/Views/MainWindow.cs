// ============================================================================
// 主窗口 — TUI 顶层容器 (Terminal.Gui v2.4)
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Input;
using Agent.TUI.Services;

namespace Agent.TUI.Views;

/// <summary>
/// 主窗口 — 组合对话面板和任务列表，管理快捷键和状态刷新
/// 
/// 布局:
/// ┌─ Ignorant Vega ──────────────────────────────────────────────────┐
/// │ [F2]对话 [F3]任务 [F4]工具 [F10]退出                            │
/// ├────────────────────────────────────┬────────────────────────────┤
/// │  💬 对话                           │  📋 任务列表               │
/// │  (聊天消息 + 输入框)               │  (任务状态列表)            │
/// └────────────────────────────────────┴────────────────────────────┘
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TuiAgentService _agent;
    private readonly ChatView _chatView;
    private readonly TaskListView _taskListView;

    private readonly CancellationTokenSource _cts = new();
    private Task? _refreshLoop;
    private string? _currentTaskId;

    public MainWindow(TuiAgentService agent)
    {
        Title = "✨ Ignorant Vega — 织女星";
        _agent = agent;

        // ── 左侧: 对话面板 (70%) ──
        _chatView = new ChatView
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(70),
            Height = Dim.Fill(),
        };
        _chatView.OnTaskSubmitted += OnTaskSubmitted;
        _chatView.OnCancelRequested += OnCancelRequested;
        Add(_chatView);

        // ── 右侧: 任务列表 (30%) ──
        _taskListView = new TaskListView
        {
            X = Pos.Right(_chatView),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_taskListView);

        // ── 快捷键 (F2 对话, F4 工具, F10 退出) ──
        KeyDown += (_, key) =>
        {
            if (key == Key.F2) { _chatView.FocusInput(); key.Handled = true; }
            else if (key == Key.F10) { Application.RequestStop(); key.Handled = true; }
        };

        // ── 启动后台刷新 ──
        _refreshLoop = Task.Run(() => RefreshLoopAsync(_cts.Token));
    }

    // ── 任务提交 ──

    private async void OnTaskSubmitted(string message)
    {
        try
        {
            var result = await _agent.SubmitTaskAsync(message);
            if (result is null)
            {
                _chatView.AddSystemMessage($"❌ 提交失败: {_agent.LastError}");
                return;
            }

            // 显示等待提示 + 设置任务运行状态
            Application.Invoke(() =>
            {
                _chatView.AddSystemMessage("⏳ 正在思考... (Esc 取消)");
                _chatView.SetTaskRunning(true);
            });

            _currentTaskId = result.TaskId;
            _ = Task.Run(() => WaitForTaskStreamingAsync(result.TaskId));
        }
        catch (OperationCanceledException) { /* 用户取消 */ }
        catch (Exception ex)
        {
            _chatView.AddSystemMessage($"❌ 提交异常: {ex.Message}");
        }
    }

    private async Task WaitForTaskStreamingAsync(string taskId)
    {
        var maxWait = TimeSpan.FromMinutes(5);
        var start = DateTime.Now;
        var lastToolIndex = 0;
        var finalizedToolIndices = new HashSet<int>();

        // 并行: 轮询任务状态 + 流式读取最终回答
        var firstChunk = true;
        var streamTask = Task.Run(async () =>
        {
            // 等任务开始执行后再连接流式端点
            await Task.Delay(500, _cts.Token);

            await _agent.StreamTaskAsync(taskId, chunk =>
            {
                Application.Invoke(() =>
                {
                    // 第一个 chunk 到达时才创建流式消息 (确保排在工具调用之后)
                    if (firstChunk)
                    {
                        _chatView.BeginStreamMessage();
                        firstChunk = false;
                    }
                    _chatView.AppendStreamChunk(chunk);
                });
            }, _cts.Token);

            Application.Invoke(() => _chatView.EndStreamMessage());
        }, _cts.Token);

        // 轮询任务状态 (显示工具调用)
        while (DateTime.Now - start < maxWait)
        {
            await Task.Delay(800, _cts.Token);
            var detail = await _agent.GetTaskAsync(taskId, _cts.Token);
            if (detail is null) continue;

            // 显示新的工具调用 + 更新已完成的工具调用 (在右侧活动面板)
            if (detail.ToolCalls.Count > 0)
            {
                for (var i = 0; i < detail.ToolCalls.Count; i++)
                {
                    var tool = detail.ToolCalls[i];

                    // 新工具: 首次出现
                    if (i >= lastToolIndex)
                    {
                        _taskListView.AddToolActivity(tool.ToolName, tool.Output, tool.Success, tool.Duration);
                    }
                    // 已显示的工具，从 "执行中" 变为 "完成": 更新一次
                    else if (!finalizedToolIndices.Contains(i) && tool.Output != "执行中...")
                    {
                        finalizedToolIndices.Add(i);
                        _taskListView.AddToolActivity(tool.ToolName, tool.Output, tool.Success, tool.Duration);
                    }
                }
                lastToolIndex = Math.Max(lastToolIndex, detail.ToolCalls.Count);
            }

            // 任务完成
            if (detail.Status is "Completed" or "Failed" or "Cancelled")
            {
                // 等待流式任务完成
                try { await Task.Delay(2000, _cts.Token); } catch (OperationCanceledException) { }
                try { await streamTask; } catch (OperationCanceledException) { }

                Application.Invoke(() =>
                {
                    _chatView.SetTaskRunning(false);
                    _currentTaskId = null;
                    if (detail.Status == "Failed")
                        _chatView.AddSystemMessage($"❌ 任务失败: {detail.Error ?? "未知错误"}");
                    else if (detail.Status == "Cancelled")
                        _chatView.AddSystemMessage("⚠️ 任务已取消");
                    // Completed 状态下，流式回调已经展示了内容
                    // 追加统计信息
                    if (detail.Status == "Completed" && detail.ToolCalls.Count > 0)
                    {
                        var summary = $"\n\n({detail.ToolCalls.Count} 次工具调用, {detail.Iterations} 轮, {detail.TotalTokens} tokens)";
                        _chatView.AppendStreamChunk(summary);
                    }
                    // 清除工具活动面板
                    _taskListView.ClearToolActivities();
                });
                return;
            }
        }

        Application.Invoke(() => _chatView.AddSystemMessage("⏰ 等待超时 (5分钟)"));
    }

    private void OnCancelRequested()
    {
        if (string.IsNullOrEmpty(_currentTaskId)) return;

        var taskId = _currentTaskId;
        _ = Task.Run(async () =>
        {
            try
            {
                var cancelled = await _agent.CancelTaskAsync(taskId, _cts.Token);
                Application.Invoke(() =>
                {
                    _chatView.SetTaskRunning(false);
                    if (cancelled)
                        _chatView.AddSystemMessage("⚠️ 任务取消请求已发送");
                    else
                        _chatView.AddSystemMessage("❌ 取消失败: 任务不存在或已结束");
                });
            }
            catch (Exception ex)
            {
                Application.Invoke(() => _chatView.AddSystemMessage($"❌ 取消异常: {ex.Message}"));
            }
        });
    }

    private void OnTaskSelected(TaskInfo task)
    {
        _chatView.AddSystemMessage($"选中任务: {task.TaskId[..8]}... ({task.Status})");
    }

    // ── 工具浏览器 ──

    private void ShowToolBrowser()
    {
        try
        {
            var toolView = new ToolBrowserView(_agent);
            Application.Run(toolView);
            toolView.Dispose();
        }
        catch (Exception ex)
        {
            _chatView.AddSystemMessage($"工具浏览器错误: {ex.Message}");
        }
    }

    private void ShowLogView()
    {
        try
        {
            var logView = new LogView();
            Application.Run(logView);
            logView.Dispose();
        }
        catch (Exception ex)
        {
            _chatView.AddSystemMessage($"日志查看器错误: {ex.Message}");
        }
    }

    private void ShowReviewView()
    {
        try
        {
            var reviewView = new ReviewView(_agent);
            Application.Run(reviewView);
            reviewView.Dispose();
        }
        catch (Exception ex)
        {
            _chatView.AddSystemMessage($"审阅面板错误: {ex.Message}");
        }
    }

    // ── 后台刷新 ──

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, ct);
                var isOnline = await _agent.IsOnlineAsync(ct);

                if (isOnline)
                {
                    var tasks = await _agent.GetTasksAsync(ct);
                    Application.Invoke(() =>
                    {
                        _taskListView.RefreshTasks(tasks);
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] 任务轮询异常: {ex.Message}"); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cts.Cancel(); _cts.Dispose(); }
        base.Dispose(disposing);
    }
}
