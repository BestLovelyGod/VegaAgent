// ============================================================================
// 审阅确认面板 — F6 查看待审阅任务
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Agent.TUI.Services;

namespace Agent.TUI.Views;

/// <summary>
/// 审阅确认面板 — 显示待审阅任务，支持批准/拒绝
/// </summary>
public sealed class ReviewView : Window
{
    private readonly TuiAgentService _agent;
    private readonly ListView _listView;
    private readonly List<string> _displayItems = new();

    public ReviewView(TuiAgentService agent)
    {
        Title = "⚠️ 审阅 — F10 返回";
        _agent = agent;

        _listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_listView);

        KeyDown += (_, key) =>
        {
            if (key == Key.F10 || key == Key.Esc)
            {
                Application.RequestStop();
                key.Handled = true;
            }
        };

        _displayItems.Add("暂无待审阅任务");
        _displayItems.Add("");
        _displayItems.Add("提示: Level 2-3 的操作需要审阅确认");
        _displayItems.Add("当 Agent 执行高风险操作时会显示在此");
        _listView.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(_displayItems));
    }
}
