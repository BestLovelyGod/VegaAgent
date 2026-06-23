# Get-Performance.ps1
# 性能监控: CPU、内存、磁盘、网络实时计数器
# 参数: Category(类别), Duration(采样秒数), Interval(间隔秒数)

param(
    [ValidateSet("CPU", "Memory", "Disk", "Network", "All")]
    [string]$Category = "All",

    [int]$Duration = 5,

    [int]$Interval = 1
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Duration = $Duration
    Interval = $Interval
    CPU = @()
    Memory = $null
    Disk = @()
    Network = @()
}

# CPU 使用率采样
if ($Category -in @("CPU", "All")) {
    $cpuSamples = @()
    $samples = [Math]::Floor($Duration / $Interval)

    for ($i = 0; $i -lt $samples; $i++) {
        $cpu = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
        $cpuSamples += $cpu
        if ($i -lt $samples - 1) { Start-Sleep -Seconds $Interval }
    }

    $result.CPU = [ordered]@{
        Samples = $cpuSamples
        Average = [math]::Round(($cpuSamples | Measure-Object -Average).Average, 1)
        Min = ($cpuSamples | Measure-Object -Minimum).Minimum
        Max = ($cpuSamples | Measure-Object -Maximum).Maximum
        Current = $cpuSamples[-1]
    }
}

# 内存使用
if ($Category -in @("Memory", "All")) {
    $os = Get-CimInstance Win32_OperatingSystem
    $totalMB = [math]::Round($os.TotalVisibleMemorySize / 1024)
    $freeMB = [math]::Round($os.FreePhysicalMemory / 1024)
    $usedMB = $totalMB - $freeMB

    # 页面文件
    $pageFile = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue | Select-Object -First 1

    $result.Memory = [ordered]@{
        TotalMB = $totalMB
        UsedMB = $usedMB
        FreeMB = $freeMB
        UsedPercentage = [math]::Round(($usedMB / $totalMB) * 100, 1)
        PageFileTotalMB = if ($pageFile) { [math]::Round($pageFile.AllocatedBaseSize) } else { 0 }
        PageFileUsedMB = if ($pageFile) { [math]::Round($pageFile.CurrentUsage) } else { 0 }
    }
}

# 磁盘性能
if ($Category -in @("Disk", "All")) {
    $result.Disk = Get-CimInstance Win32_PerfFormattedData_PerfDisk_LogicalDisk -Filter "DriveType=3" -ErrorAction SilentlyContinue | ForEach-Object {
        [ordered]@{
            Drive = $_.Name
            ReadBytesPerSec = [math]::Round($_.DiskReadBytesPersec / 1KB, 1)
            WriteBytesPerSec = [math]::Round($_.DiskWriteBytesPersec / 1KB, 1)
            QueueLength = $_.CurrentDiskQueueLength
            IdlePercentage = $_.PercentIdleTime
        }
    }
}

# 网络性能
if ($Category -in @("Network", "All")) {
    $result.Network = Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface -ErrorAction SilentlyContinue |
        Where-Object { $_.BytesTotalPersec -gt 0 } |
        Select-Object -First 5 |
        ForEach-Object {
            [ordered]@{
                Name = $_.Name
                BytesSentPerSec = [math]::Round($_.BytesSentPersec / 1KB, 1)
                BytesReceivedPerSec = [math]::Round($_.BytesReceivedPersec / 1KB, 1)
                TotalBytesPerSec = [math]::Round($_.BytesTotalPersec / 1KB, 1)
                PacketsPerSec = $_.PacketsPersec
            }
        }
}

$result | ConvertTo-Json -Depth 5
