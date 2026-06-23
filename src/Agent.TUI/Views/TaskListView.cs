// ============================================================================
// 任务列表面板 — 右侧任务状态区域 (Terminal.Gui v2.4)
// ============================================================================

using Terminal.Gui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Agent.TUI.Services;

namespace Agent.TUI.Views;

/// <summary>
/// 任务列表 — 显示任务状态和实时工具调用活动
/// </summary>
public sealed class TaskListView : View
{
    private readonly ListView _listView;
    private readonly List<TaskInfo> _tasks = new();
    private readonly List<string> _displayItems = new();

    // 工具活动区 (实时工具调用状态)
    private readonly List<string> _toolActivities = new();

    public TaskListView()
    {
        _listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_listView);
    }

    /// <summary>添加或更新工具活动状态</summary>
    public void AddToolActivity(string toolName, string? output, bool success, double durationMs)
    {
        var icon = output == "执行中..." ? "⏳" : (success ? "✅" : "❌");
        var duration = durationMs > 0 ? $" {durationMs:F0}ms" : "";
        var line = $"{icon} {toolName}{duration}";

        // 查找已有的同名工具条目 (匹配 ⏳/✅/❌ 前缀 + 工具名)
        var existingIndex = -1;
        for (var i = 0; i < _toolActivities.Count; i++)
        {
            if ((_toolActivities[i].StartsWith("⏳ ") || _toolActivities[i].StartsWith("✅ ") || _toolActivities[i].StartsWith("❌ "))
                && _toolActivities[i].Contains(toolName))
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            _toolActivities[existingIndex] = line;
        }
        else
        {
            _toolActivities.Add(line);
            existingIndex = _toolActivities.Count - 1;
        }

        // 添加工具输出 (非空且非执行中)
        if (!string.IsNullOrWhiteSpace(output) && output != "执行中...")
        {
            // 截断但保留更多内容 (最多 3 行)
            var outputLines = output.Split('\n');
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < Math.Min(outputLines.Length, 3); i++)
            {
                var ol = outputLines[i].Trim();
                if (ol.Length > 80) ol = ol[..80] + "...";
                if (!string.IsNullOrEmpty(ol))
                    sb.AppendLine($"   {ol}");
            }
            if (outputLines.Length > 3)
                sb.AppendLine($"   ... (+{outputLines.Length - 3} 行)");

            var outputBlock = sb.ToString().TrimEnd();

            // 检查是否已有输出行紧跟其后
            if (existingIndex + 1 < _toolActivities.Count && _toolActivities[existingIndex + 1].StartsWith("   "))
            {
                _toolActivities[existingIndex + 1] = outputBlock;
            }
            else
            {
                _toolActivities.Insert(existingIndex + 1, outputBlock);
            }
        }

        RefreshDisplay();
    }

    /// <summary>清除工具活动</summary>
    public void ClearToolActivities()
    {
        _toolActivities.Clear();
        RefreshDisplay();
    }

    public void RefreshTasks(List<TaskInfo> tasks)
    {
        _tasks.Clear();
        _tasks.AddRange(tasks.OrderByDescending(t => t.CreatedAt));
        RefreshDisplay();
    }

    public void UpdateTask(TaskInfo task)
    {
        var index = _tasks.FindIndex(t => t.TaskId == task.TaskId);
        if (index >= 0)
            _tasks[index] = task;
        else
            _tasks.Insert(0, task);
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        _displayItems.Clear();

        // 工具活动区 (最新的在上面)
        if (_toolActivities.Count > 0)
        {
            _displayItems.Add("── 🔧 工具活动 ──");
            for (var i = _toolActivities.Count - 1; i >= 0; i--)
                _displayItems.Add($" {_toolActivities[i]}");
            _displayItems.Add("");
        }

        // 任务列表区
        if (_tasks.Count > 0)
        {
            _displayItems.Add("── 📋 任务 ──");
            foreach (var task in _tasks)
                _displayItems.Add($" {FormatTaskLine(task)}");
        }

        var source = new System.Collections.ObjectModel.ObservableCollection<string>(_displayItems);
        _listView.SetSource(source);
    }

    private static string FormatTaskLine(TaskInfo task)
    {
        var icon = task.Status switch
        {
            "Pending" => "⏳",
            "Running" => "🔄",
            "Completed" => "✅",
            "Failed" => "❌",
            "Cancelled" => "🚫",
            _ => "❓"
        };
        var message = task.Message.Length > 30 ? task.Message[..30] + "..." : task.Message;
        return $"{icon} {message}";
    }
}
