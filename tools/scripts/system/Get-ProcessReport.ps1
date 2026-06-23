# Get-ProcessReport.ps1
# 进程分析报告: 按 CPU/内存排序的进程列表
# 参数: Top(显示前N个), SortBy(CPU/Memory/Name)

param(
    [int]$Top = 20,
    [string]$SortBy = "Memory"
)

$processes = Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First $Top

$report = $processes | ForEach-Object {
    [ordered]@{
        Name = $_.ProcessName
        PID = $_.Id
        MemoryMB = [math]::Round($_.WorkingSet64 / 1MB, 1)
        Handles = $_.HandleCount
        Threads = $_.Threads.Count
    }
}

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SortBy = $SortBy
    TopN = $Top
    TotalProcesses = (Get-Process).Count
    Processes = @($report)
}

$result | ConvertTo-Json -Depth 5
