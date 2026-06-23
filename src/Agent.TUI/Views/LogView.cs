// ============================================================================
// 日志查看器 — F5 查看 Agent 日志
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Agent.TUI.Views;

/// <summary>
/// 日志查看器 — 实时显示日志条目
/// </summary>
public sealed class LogView : Window
{
    private readonly TextView _logText;
    private readonly List<string> _logEntries = new();
    private const int MaxEntries = 500;

    public LogView()
    {
        Title = "📋 日志 — F10 返回";

        _logText = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
        };
        Add(_logText);

        KeyDown += (_, key) =>
        {
            if (key == Key.F10 || key == Key.Esc)
            {
                Application.RequestStop();
                key.Handled = true;
            }
        };

        AddLog("ℹ️ 日志查看器已启动");
        AddLog("ℹ️ Agent.Host 日志会通过 API 推送到此");
        AddLog("");
    }

    public void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var entry = $"[{timestamp}] {message}";

        _logEntries.Add(entry);
        if (_logEntries.Count > MaxEntries)
            _logEntries.RemoveAt(0);

        _logText.Text = string.Join("\n", _logEntries);
    }
}
