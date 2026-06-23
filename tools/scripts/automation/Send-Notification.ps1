# Send-Notification.ps1
# 通知发送: Windows 桌面通知 / 系统托盘通知
# 参数: Title(标题), Message(消息), Type(类型), Duration(显示秒数)

param(
    [Parameter(Mandatory)]
    [string]$Title,

    [Parameter(Mandatory)]
    [string]$Message,

    [ValidateSet("Info", "Success", "Warning", "Error")]
    [string]$Type = "Info",

    [int]$Duration = 5
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Title = $Title
    Message = $Message
    Type = $Type
    Status = $null
    Error = $null
}

try {
    # 方式 1: 使用 BurntToast 模块 (如果安装)
    if (Get-Module -ListAvailable -Name BurntToast -ErrorAction SilentlyContinue) {
        Import-Module BurntToast
        $icon = switch ($Type) {
            "Success" { "✅" }
            "Warning" { "⚠️" }
            "Error" { "❌" }
            default { "ℹ️" }
        }
        New-BurntToastNotification -Text "$icon $Title", $Message -ExpirationDuration (New-TimeSpan -Seconds $Duration)
        $result.Status = "已发送 (BurntToast)"
        $result | ConvertTo-Json -Depth 3
        return
    }

    # 方式 2: 使用 Windows Forms 通知气球
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop

    $notifyIcon = New-Object System.Windows.Forms.NotifyIcon
    $notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
    $notifyIcon.Visible = $true
    $notifyIcon.BalloonTipTitle = $Title
    $notifyIcon.BalloonTipText = $Message
    $notifyIcon.BalloonTipIcon = switch ($Type) {
        "Success" { [System.Windows.Forms.ToolTipIcon]::Info }
        "Warning" { [System.Windows.Forms.ToolTipIcon]::Warning }
        "Error" { [System.Windows.Forms.ToolTipIcon]::Error }
        default { [System.Windows.Forms.ToolTipIcon]::Info }
    }

    $notifyIcon.ShowBalloonTip($Duration * 1000)

    # 等待显示后清理
    Start-Sleep -Seconds ($Duration + 1)
    $notifyIcon.Dispose()

    $result.Status = "已发送 (BalloonTip)"
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 3
