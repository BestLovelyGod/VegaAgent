using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Agent.Launcher;

/// <summary>
/// 启动器设置对话框 — 包含启动选项 + LLM 配置
/// </summary>
public partial class SettingsForm : Form
{
    private readonly LauncherConfig _config;
    private readonly string _llmConfigPath;
    private JsonObject? _llmConfig;
    private Dictionary<string, JsonObject> _providers = new();
    private string _activeProvider = "";
    private string _activeModel = "";

    public SettingsForm(LauncherConfig config, string baseDir)
    {
        _config = config;
        _llmConfigPath = FindLlmConfig(baseDir);
        InitializeComponent();
        LoadSettings();
        LoadLlmConfig();
    }

    private static string FindLlmConfig(string baseDir)
    {
        // 按优先级查找 llm-config.json
        var candidates = new[]
        {
            Path.Combine(baseDir, "data", "llm-config.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "llm-config.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "llm-config.json"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        // 默认路径
        return Path.GetFullPath(Path.Combine(baseDir, "data", "llm-config.json"));
    }

    private void LoadSettings()
    {
        chkAutoStartHost.Checked = _config.AutoStartHost;
        chkAutoStartGui.Checked = _config.AutoStartGui;
        chkMinimizeToTray.Checked = _config.MinimizeToTray;
        chkStartWithWindows.Checked = _config.StartWithWindows;
    }

    private void LoadLlmConfig()
    {
        try
        {
            if (!File.Exists(_llmConfigPath))
            {
                Log("llm-config.json 不存在，使用默认配置");
                _llmConfig = new JsonObject
                {
                    ["activeProvider"] = "mimo-token-plan",
                    ["activeModel"] = "mimo-v2.5",
                    ["providers"] = new JsonObject(),
                    ["modelConfigs"] = new JsonObject()
                };
                PopulateProviderCombo();
                return;
            }

            var json = File.ReadAllText(_llmConfigPath);
            _llmConfig = JsonNode.Parse(json) as JsonObject;
            if (_llmConfig == null) { Log("llm-config.json 解析失败"); return; }

            _activeProvider = _llmConfig["activeProvider"]?.GetValue<string>() ?? "";
            _activeModel = _llmConfig["activeModel"]?.GetValue<string>() ?? "";

            // 加载提供商列表
            if (_llmConfig["providers"] is JsonObject providers)
            {
                foreach (var kvp in providers)
                {
                    if (kvp.Value is JsonObject p)
                        _providers[kvp.Key] = p;
                }
            }

            PopulateProviderCombo();
            Log("LLM 配置已加载");
        }
        catch (Exception ex)
        {
            Log($"加载 LLM 配置失败: {ex.Message}");
        }
    }

    private void PopulateProviderCombo()
    {
        cmbProvider.Items.Clear();
        foreach (var kvp in _providers)
        {
            var name = kvp.Value["name"]?.GetValue<string>() ?? kvp.Key;
            cmbProvider.Items.Add(new ComboItem(kvp.Key, name));
        }

        // 选中当前活跃提供商
        for (int i = 0; i < cmbProvider.Items.Count; i++)
        {
            if (cmbProvider.Items[i] is ComboItem item && item.Value == _activeProvider)
            {
                cmbProvider.SelectedIndex = i;
                break;
            }
        }
        if (cmbProvider.SelectedIndex < 0 && cmbProvider.Items.Count > 0)
            cmbProvider.SelectedIndex = 0;
    }

    private void cmbProvider_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbProvider.SelectedItem is not ComboItem item) return;
        var providerId = item.Value;

        // 显示当前 API Key（掩码）
        if (_providers.TryGetValue(providerId, out var provider))
        {
            var key = provider["apiKey"]?.GetValue<string>() ?? "";
            txtApiKey.Text = key;
            txtApiKey.PlaceholderText = string.IsNullOrEmpty(key) ? "未设置" : "已设置 (输入新值可覆盖)";

            // 加载模型列表
            cmbModel.Items.Clear();
            if (provider["models"] is JsonArray models)
            {
                foreach (var m in models)
                {
                    var modelStr = m?.GetValue<string>() ?? "";
                    cmbModel.Items.Add(modelStr);
                }
            }
            // 选中当前模型
            var idx = cmbModel.Items.IndexOf(_activeModel);
            if (idx >= 0) cmbModel.SelectedIndex = idx;
            else if (cmbModel.Items.Count > 0) cmbModel.SelectedIndex = 0;
        }
    }

    private async void btnTest_Click(object? sender, EventArgs e)
    {
        if (cmbProvider.SelectedItem is not ComboItem item) return;
        var providerId = item.Value;
        if (!_providers.TryGetValue(providerId, out var provider)) return;

        var baseUrl = provider["baseUrl"]?.GetValue<string>() ?? "";
        var apiKey = txtApiKey.Text.Trim();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = provider["apiKey"]?.GetValue<string>() ?? "";

        if (string.IsNullOrEmpty(baseUrl))
        {
            Log("无法测试: 未配置 API 地址");
            return;
        }

        btnTest.Enabled = false;
        btnTest.Text = "测试中...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            var resp = await http.GetAsync($"{baseUrl}/models");
            if (resp.IsSuccessStatusCode)
            {
                Log($"✓ {provider["name"]?.GetValue<string>() ?? providerId} 连接成功");
                MessageBox.Show("连接成功！", "测试", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log($"✗ 连接失败: HTTP {(int)resp.StatusCode}");
                MessageBox.Show($"连接失败: HTTP {(int)resp.StatusCode}\n{resp.ReasonPhrase}", "测试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log($"✗ 连接异常: {ex.Message}");
            MessageBox.Show($"连接失败: {ex.Message}", "测试", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnTest.Text = "测试连接";
            btnTest.Enabled = true;
        }
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        // 保存启动选项
        _config.AutoStartHost = chkAutoStartHost.Checked;
        _config.AutoStartGui = chkAutoStartGui.Checked;
        _config.MinimizeToTray = chkMinimizeToTray.Checked;
        _config.StartWithWindows = chkStartWithWindows.Checked;
        _config.Save();

        // 保存 LLM 配置
        SaveLlmConfig();

        MessageBox.Show("设置已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        this.Close();
    }

    private void SaveLlmConfig()
    {
        try
        {
            if (_llmConfig == null) return;

            var providerItem = cmbProvider.SelectedItem as ComboItem;
            var providerId = providerItem?.Value ?? _activeProvider;
            var model = cmbModel.SelectedItem?.ToString() ?? _activeModel;

            _llmConfig["activeProvider"] = providerId;
            _llmConfig["activeModel"] = model;

            // 更新 API Key
            var newKey = txtApiKey.Text.Trim();
            if (!string.IsNullOrEmpty(newKey) && _providers.ContainsKey(providerId))
            {
                _providers[providerId]["apiKey"] = newKey;
            }

            // 写回 providers
            var providersObj = new JsonObject();
            foreach (var kvp in _providers)
                providersObj[kvp.Key] = kvp.Value.DeepClone();
            _llmConfig["providers"] = providersObj;

            // 确保目录存在
            var dir = Path.GetDirectoryName(_llmConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_llmConfigPath, _llmConfig.ToJsonString(options));
            Log("LLM 配置已保存");
        }
        catch (Exception ex)
        {
            Log($"保存 LLM 配置失败: {ex.Message}");
            MessageBox.Show($"保存 LLM 配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        this.Close();
    }

    private void Log(string msg)
    {
        txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    /// <summary>ComboBox 绑定项</summary>
    private record ComboItem(string Value, string Display)
    {
        public override string ToString() => Display;
    }
}
