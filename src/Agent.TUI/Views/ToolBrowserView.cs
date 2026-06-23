// ============================================================================
// 工具浏览器 — F4 查看所有可用工具
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Agent.TUI.Services;

namespace Agent.TUI.Views;

/// <summary>
/// 工具浏览器 — 显示所有已注册工具，支持按类别过滤
/// </summary>
public sealed class ToolBrowserView : Window
{
    private readonly TuiAgentService _agent;
    private readonly ListView _listView;
    private readonly TextField _searchField;
    private readonly List<string> _displayItems = new();
    private readonly List<ToolInfo> _allTools = new();

    public ToolBrowserView(TuiAgentService agent)
    {
        Title = "📦 工具浏览器 — F10 返回";
        _agent = agent;

        // 搜索框
        var searchLabel = new Label { Text = "🔍", X = 0, Y = 0 };
        Add(searchLabel);

        _searchField = new TextField
        {
            X = Pos.Right(searchLabel) + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };
        _searchField.TextChanged += (_, _) => FilterTools();
        Add(_searchField);

        // 工具列表
        _listView = new ListView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_listView);

        // F10 返回
        KeyDown += (_, key) =>
        {
            if (key == Key.F10 || key == Key.Esc)
            {
                Application.RequestStop();
                key.Handled = true;
            }
        };

        // 加载工具
        _ = LoadToolsAsync();
    }

    private async Task LoadToolsAsync()
    {
        var tools = await _agent.GetToolsAsync();
        _allTools.Clear();
        _allTools.AddRange(tools);
        FilterTools();
    }

    private void FilterTools()
    {
        var filter = _searchField.Text?.ToString()?.ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(filter)
            ? _allTools
            : _allTools.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                   t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _displayItems.Clear();

        var grouped = filtered.GroupBy(t => t.Category);
        foreach (var group in grouped)
        {
            _displayItems.Add($"── {group.Key} ({group.Count()}) ──");
            foreach (var tool in group)
            {
                var risk = tool.RiskLevel switch
                {
                    "Level0" => "🟢", "Level1" => "🟡",
                    "Level2" => "🟠", "Level3" => "🔴", _ => "⚪"
                };
                _displayItems.Add($"  {risk} {tool.Name}");
                _displayItems.Add($"     {tool.Description}");
            }
            _displayItems.Add("");
        }

        _listView.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(_displayItems));
    }
}
