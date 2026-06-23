// ============================================================================
// 对话面板 — 左侧聊天区域 (Terminal.Gui v2.4)
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Input;

namespace Agent.TUI.Views;

/// <summary>
/// 对话面板 — 显示用户和助手的消息，支持输入新任务
/// </summary>
public sealed class ChatView : View
{
    private readonly TextView _chatDisplay;
    private readonly TextField _inputField;
    private readonly List<ChatMessage> _messages = new();

    public event Action<string>? OnTaskSubmitted;
    public event Action? OnCancelRequested;

    private bool _isTaskRunning;

    public ChatView()
    {
        CanFocus = true;

        // 聊天显示区域 (只读)
        _chatDisplay = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            CanFocus = false,
        };
        Add(_chatDisplay);

        // 输入框 (固定在底部最后一行)
        _inputField = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
        };
        _inputField.SetFocus();
        Add(_inputField);

        // Enter 键提交, Esc 键取消
        _inputField.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                SubmitMessage();
                key.Handled = true;
            }
            else if (key == Key.Esc && _isTaskRunning)
            {
                OnCancelRequested?.Invoke();
                key.Handled = true;
            }
        };

        AddAssistantMessage("欢迎使用 Ignorant Vega！\n输入任务描述，我将帮你完成。\n\n例如:\n• 查看系统状态\n• 清理临时文件\n• 列出运行中的服务");
    }

    public void AddUserMessage(string message)
    {
        _messages.Add(new ChatMessage { Role = "user", Content = message, Timestamp = DateTime.Now });
        RefreshDisplay();
    }

    public void AddAssistantMessage(string message)
    {
        _messages.Add(new ChatMessage { Role = "assistant", Content = message, Timestamp = DateTime.Now });
        RefreshDisplay();
    }

    /// <summary>开始流式消息 (创建空的 assistant 消息)</summary>
    public void BeginStreamMessage()
    {
        _messages.Add(new ChatMessage { Role = "assistant", Content = "", Timestamp = DateTime.Now, IsStreaming = true });
        RefreshDisplay();
    }

    /// <summary>追加流式文本块到当前流式消息</summary>
    public void AppendStreamChunk(string chunk)
    {
        var last = _messages.LastOrDefault(m => m is { Role: "assistant", IsStreaming: true });
        if (last is not null)
        {
            last.Content += chunk;
            RefreshDisplay();
        }
    }

    /// <summary>结束流式消息</summary>
    public void EndStreamMessage()
    {
        var last = _messages.LastOrDefault(m => m is { Role: "assistant", IsStreaming: true });
        if (last is not null)
        {
            last.IsStreaming = false;
            if (string.IsNullOrWhiteSpace(last.Content))
                last.Content = "(无内容)";
            RefreshDisplay();
        }
    }

    public void AddToolCallMessage(string toolName, string? output = null, bool success = true, double durationMs = 0)
    {
        // 如果最后一个工具消息是同名的 "执行中..."，更新它而非新增
        var lastTool = _messages.LastOrDefault(m => m.Role == "tool" && m.Content.Contains($"[{toolName}]"));
        if (lastTool is not null && lastTool.Content.Contains("执行中..."))
        {
            var icon = success ? "OK" : "!!";
            var duration = durationMs > 0 ? $" ({durationMs:F0}ms)" : "";
            lastTool.Content = $"{icon} [{toolName}]{duration}";
            if (!string.IsNullOrEmpty(output))
            {
                var truncated = output.Length > 500 ? output[..500] + "..." : output;
                lastTool.Content += $"\n{truncated}";
            }
        }
        else
        {
            var icon = success ? "OK" : (output == "执行中..." ? ".." : "!!");
            var duration = durationMs > 0 ? $" ({durationMs:F0}ms)" : "";
            var content = $"{icon} [{toolName}]{duration}";
            if (!string.IsNullOrEmpty(output) && output != "执行中...")
            {
                var truncated = output.Length > 500 ? output[..500] + "..." : output;
                content += $"\n{truncated}";
            }
            else if (output == "执行中...")
            {
                content += " 执行中...";
            }
            _messages.Add(new ChatMessage { Role = "tool", Content = content, Timestamp = DateTime.Now });
        }
        RefreshDisplay();
    }

    public void AddSystemMessage(string message)
    {
        _messages.Add(new ChatMessage { Role = "system", Content = message, Timestamp = DateTime.Now });
        RefreshDisplay();
    }

    public void Clear()
    {
        _messages.Clear();
        _chatDisplay.Text = "";
    }

    public void SetTaskRunning(bool running)
    {
        _isTaskRunning = running;
    }

    public void FocusInput()
    {
        _inputField.SetFocus();
    }

    private void SubmitMessage()
    {
        var text = _inputField.Text?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _inputField.Text = "";
        AddUserMessage(text);
        OnTaskSubmitted?.Invoke(text);
    }

    private void RefreshDisplay()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var msg in _messages)
        {
            var prefix = msg.Role switch
            {
                "user" => ">> ",
                "assistant" => "<< ",
                "tool" => ":: ",
                "system" => "** ",
                _ => ""
            };
            sb.AppendLine($"{prefix}{msg.Content}");
            sb.AppendLine();
        }

        _chatDisplay.Text = sb.ToString();

        // 滚动到底部
        try
        {
            var totalLines = sb.ToString().Split('\n').Length;
            _chatDisplay.ScrollTo(new System.Drawing.Point(0, Math.Max(0, totalLines)));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ChatView] ScrollTo 失败: {ex.Message}"); }

        _chatDisplay.SetNeedsDraw();
    }
}

internal sealed class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsStreaming { get; set; }
}
