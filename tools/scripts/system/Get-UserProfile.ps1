# Get-UserProfile.ps1
# 返回当前用户的常用目录路径 (Desktop/Documents/Downloads 等)
# 解决 LLM 将 agent 目录误认为桌面的问题

$profile = [ordered]@{
    Timestamp  = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    UserName   = $env:USERNAME
    UserProfile = $env:USERPROFILE
    HomeDrive  = $env:HOMEDRIVE
    HomePath   = $env:HOMEPATH
    UserFolders = [ordered]@{
        Desktop    = [System.IO.Path]::Combine($env:USERPROFILE, "Desktop")
        Documents  = [System.IO.Path]::Combine($env:USERPROFILE, "Documents")
        Downloads  = [System.IO.Path]::Combine($env:USERPROFILE, "Downloads")
        Pictures   = [System.IO.Path]::Combine($env:USERPROFILE, "Pictures")
        Music      = [System.IO.Path]::Combine($env:USERPROFILE, "Music")
        Videos     = [System.IO.Path]::Combine($env:USERPROFILE, "Videos")
        Favorites  = [System.IO.Path]::Combine($env:USERPROFILE, "Favorites")
        AppData    = $env:APPDATA
        LocalAppData = $env:LOCALAPPDATA
    }
    SystemInfo = [ordered]@{
        ComputerName = $env:COMPUTERNAME
        OSVersion    = [System.Environment]::OSVersion.VersionString
        DotNetVersion = [System.Environment]::Version.ToString()
    }
}

# 中文桌面名兼容 (部分系统桌面文件夹名为中文)
$desktopPath = $profile.UserFolders.Desktop
if (-not (Test-Path $desktopPath)) {
    # 尝试常见中文名
    $zhDesktop = [System.IO.Path]::Combine($env:USERPROFILE, "桌面")
    if (Test-Path $zhDesktop) {
        $profile.UserFolders.Desktop = $zhDesktop
    }
}

$profile | ConvertTo-Json -Depth 5
