using System;
using System.IO;
using System.Text.Json;

namespace Agent.Launcher;

/// <summary>
/// 启动器配置管理
/// </summary>
public class LauncherConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vega",
        "launcher-config.json");
    
    /// <summary>
    /// 默认环境
    /// </summary>
    public string DefaultEnvironment { get; set; } = "Development";
    
    /// <summary>
    /// 自动启动Agent.Host
    /// </summary>
    public bool AutoStartHost { get; set; } = false;
    
    /// <summary>
    /// 自动启动Agent.GUI
    /// </summary>
    public bool AutoStartGui { get; set; } = false;
    
    /// <summary>
    /// 最小化到系统托盘
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;
    
    /// <summary>
    /// 开机自启动
    /// </summary>
    public bool StartWithWindows { get; set; } = false;
    
    /// <summary>
    /// 窗口位置
    /// </summary>
    public System.Drawing.Point? WindowLocation { get; set; }
    
    /// <summary>
    /// 窗口大小
    /// </summary>
    public System.Drawing.Size? WindowSize { get; set; }
    
    /// <summary>
    /// 最近使用的环境列表
    /// </summary>
    public string[] RecentEnvironments { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// 加载配置
    /// </summary>
    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载配置失败: {ex.Message}");
        }
        
        return new LauncherConfig();
    }
    
    /// <summary>
    /// 保存配置
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存配置失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 添加最近使用的环境
    /// </summary>
    public void AddRecentEnvironment(string environment)
    {
        var recent = new List<string>(RecentEnvironments);
        recent.Remove(environment); // 移除重复项
        recent.Insert(0, environment); // 添加到开头
        
        if (recent.Count > 5)
        {
            recent = recent.Take(5).ToList(); // 只保留最近5个
        }
        
        RecentEnvironments = recent.ToArray();
    }
}
