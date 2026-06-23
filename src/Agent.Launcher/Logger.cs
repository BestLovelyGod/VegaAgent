using System;
using System.IO;

namespace Agent.Launcher;

/// <summary>
/// 启动器日志记录器
/// </summary>
public static class Logger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vega",
        "logs");
    
    private static readonly string LogFile = Path.Combine(LogDirectory, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");
    private static readonly object _lock = new();
    
    /// <summary>
    /// 初始化日志目录
    /// </summary>
    static Logger()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }
        catch
        {
            // 忽略日志目录创建失败
        }
    }
    
    /// <summary>
    /// 记录信息日志
    /// </summary>
    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }
    
    /// <summary>
    /// 记录警告日志
    /// </summary>
    public static void Warn(string message)
    {
        WriteLog("WARN", message);
    }
    
    /// <summary>
    /// 记录错误日志
    /// </summary>
    public static void Error(string message, Exception? ex = null)
    {
        var logMessage = message;
        if (ex != null)
        {
            logMessage += $"\n{ex}";
        }
        WriteLog("ERROR", logMessage);
    }
    
    /// <summary>
    /// 记录调试日志
    /// </summary>
    public static void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }
    
    private static void WriteLog(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            
            lock (_lock)
            {
                File.AppendAllText(LogFile, logEntry + Environment.NewLine);
            }
        }
        catch
        {
            // 忽略日志写入失败
        }
    }
    
    /// <summary>
    /// 清理旧日志文件（保留最近7天）
    /// </summary>
    public static void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
                return;
            
            var cutoffDate = DateTime.Now.AddDays(-7);
            var logFiles = Directory.GetFiles(LogDirectory, "launcher-*.log");
            
            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoffDate)
                {
                    fileInfo.Delete();
                }
            }
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
