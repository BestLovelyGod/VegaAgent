# Clean-TempFiles.ps1
# 清理临时文件: 删除指定目录下超过 N 天的文件
# 参数: Path(目录路径), Days(天数), WhatIf(预览模式)

param(
    [string]$Path = "$env:TEMP",
    [int]$Days = 7,
    [switch]$WhatIf
)

$cutoffDate = (Get-Date).AddDays(-$Days)
$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    TargetPath = $Path
    CutoffDays = $Days
    CutoffDate = $cutoffDate.ToString("yyyy-MM-dd HH:mm:ss")
    WhatIf = $WhatIf.IsPresent
    FilesFound = 0
    FilesDeleted = 0
    SpaceFreedMB = 0
    Errors = @()
}

if (-not (Test-Path $Path)) {
    $result.Errors += "目录不存在: $Path"
    $result | ConvertTo-Json -Depth 3
    return
}

$oldFiles = Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $cutoffDate }

$result.FilesFound = ($oldFiles | Measure-Object).Count

foreach ($file in $oldFiles) {
    try {
        $sizeMB = [math]::Round($file.Length / 1MB, 2)
        if ($WhatIf) {
            Write-Output "[WhatIf] 删除: $($file.FullName) ($sizeMB MB, $($file.LastWriteTime))"
        } else {
            Remove-Item $file.FullName -Force -ErrorAction Stop
            $result.FilesDeleted++
            $result.SpaceFreedMB += $sizeMB
        }
    } catch {
        $result.Errors += "删除失败: $($file.FullName) - $($_.Exception.Message)"
    }
}

if (-not $WhatIf) {
    $result.SpaceFreedMB = [math]::Round($result.SpaceFreedMB, 2)
}

$result | ConvertTo-Json -Depth 3
