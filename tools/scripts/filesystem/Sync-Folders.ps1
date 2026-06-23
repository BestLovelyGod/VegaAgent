# Sync-Folders.ps1
# 文件夹同步: 使用 Robocopy 同步两个目录
# 参数: Source(源目录), Destination(目标目录), Mirror(镜像模式), Exclude(排除模式)

param(
    [Parameter(Mandatory)]
    [string]$Source,

    [Parameter(Mandatory)]
    [string]$Destination,

    [switch]$Mirror,

    [string[]]$Exclude = @(),

    [switch]$WhatIf
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Source = $Source
    Destination = $Destination
    Mirror = $Mirror.IsPresent
    WhatIf = $WhatIf.IsPresent
    Status = $null
    Output = ""
    Error = $null
    Stats = $null
}

if (-not (Test-Path $Source)) {
    $result.Error = "源目录不存在: $Source"
    $result | ConvertTo-Json -Depth 3
    return
}

# 构建 Robocopy 参数
$roboArgs = @($Source, $Destination, "/E", "/NP", "/NDL", "/NJH", "/NJS")

if ($Mirror) {
    $roboArgs += "/MIR"
}

if ($WhatIf) {
    $roboArgs += "/L"
}

foreach ($ex in $Exclude) {
    $roboArgs += "/XD"
    $roboArgs += $ex
}

try {
    $output = & robocopy @roboArgs 2>&1 | Out-String

    $result.Output = $output

    # 解析 Robocopy 退出码
    $exitCode = $LASTEXITCODE
    $result.Status = switch ($exitCode) {
        0 { "无变更，无需复制" }
        1 { "文件已成功复制" }
        2 { "目标有额外文件/目录" }
        3 { "复制了一些文件，目标有额外内容" }
        4 { "有不匹配的文件" }
        5 { "复制了一些文件，有不匹配" }
        6 { "有额外内容，有不匹配" }
        7 { "复制了一些文件，有额外内容和不匹配" }
        8 { "有复制失败的文件" }
        default { "完成 (退出码: $exitCode)" }
    }

    # 解析统计信息
    if ($output -match "Dirs\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)") {
        $result.Stats = [ordered]@{
            Dirs_Total = [int]$Matches[1]
            Dirs_Copied = [int]$Matches[2]
            Dirs_Skipped = [int]$Matches[3]
            Dirs_Failed = [int]$Matches[4]
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
