# Find-Files.ps1
# 文件搜索: 按名称/扩展名/大小搜索文件
# 参数: Path(搜索目录), Pattern(文件名模式), Extension(扩展名), MinSizeMB(最小大小MB)

param(
    [string]$Path = "$env:USERPROFILE",
    [string]$Pattern = "*",
    [string]$Extension = "",
    [double]$MinSizeMB = 0
)

$filter = if ($Extension) { "*.$($Extension.TrimStart('.'))" } else { $Pattern }

$searchParams = @{
    Path = $Path
    Filter = $filter
    Recurse = $true
    File = $true
    ErrorAction = "SilentlyContinue"
}

$files = Get-ChildItem @searchParams

if ($MinSizeMB -gt 0) {
    $files = $files | Where-Object { $_.Length -ge ($MinSizeMB * 1MB) }
}

$report = $files | Select-Object -First 500 | ForEach-Object {
    [ordered]@{
        Name = $_.Name
        Path = $_.FullName
        SizeMB = [math]::Round($_.Length / 1MB, 2)
        LastModified = $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        Extension = $_.Extension
    }
}

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SearchPath = $Path
    Pattern = $filter
    MinSizeMB = $MinSizeMB
    TotalFound = ($files | Measure-Object).Count
    ReturnedCount = ($report | Measure-Object).Count
    Files = @($report)
}

$result | ConvertTo-Json -Depth 5
