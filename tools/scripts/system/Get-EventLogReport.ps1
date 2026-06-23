# Get-EventLogReport.ps1
# 事件日志分析: 查看 Windows 事件日志，支持按级别/来源/时间过滤
# 参数: LogName(日志名), Level(级别), Source(来源), Hours(最近N小时), MaxEntries(最大条数)

param(
    [ValidateSet("Application", "System", "Security", "Setup")]
    [string]$LogName = "Application",

    [ValidateSet("Error", "Warning", "Information", "Critical")]
    [string]$Level = "",

    [string]$Source = "",

    [int]$Hours = 24,

    [int]$MaxEntries = 100
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    LogName = $LogName
    Filter = [ordered]@{
        Level = if ($Level) { $Level } else { "All" }
        Source = if ($Source) { $Source } else { "All" }
        Hours = $Hours
    }
    TotalFound = 0
    Entries = @()
    Summary = [ordered]@{
        Errors = 0
        Warnings = 0
        Info = 0
        Critical = 0
    }
}

try {
    $startTime = (Get-Date).AddHours(-$Hours)

    $filter = @{
        LogName = $LogName
        StartTime = $startTime
    }

    if ($Level) {
        $filter["Level"] = switch ($Level) {
            "Critical" { 1 }
            "Error" { 2 }
            "Warning" { 3 }
            "Information" { 4 }
        }
    }

    $events = Get-WinEvent -FilterHashtable $filter -MaxEvents $MaxEntries -ErrorAction SilentlyContinue

    if ($Source) {
        $events = $events | Where-Object { $_.ProviderName -like "*$Source*" }
    }

    $result.TotalFound = ($events | Measure-Object).Count

    $result.Entries = $events | ForEach-Object {
        $levelText = switch ($_.LevelDisplayName) {
            "Critical" { $result.Summary.Critical++; "🔴 Critical" }
            "Error" { $result.Summary.Errors++; "❌ Error" }
            "Warning" { $result.Summary.Warnings++; "⚠️ Warning" }
            default { $result.Summary.Info++; "ℹ️ Information" }
        }

        [ordered]@{
            TimeCreated = $_.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
            Level = $levelText
            Source = $_.ProviderName
            EventId = $_.Id
            Message = $_.Message.Substring(0, [Math]::Min(200, $_.Message.Length))
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
