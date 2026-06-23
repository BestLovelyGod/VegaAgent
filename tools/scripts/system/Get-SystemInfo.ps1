# Get-SystemInfo.ps1
# 采集系统信息: CPU、内存、磁盘、网络
# 输出 JSON 格式的系统状态报告

param(
    [switch]$IncludeNetwork,
    [switch]$IncludeDisks
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    ComputerName = $env:COMPUTERNAME
    UserName = $env:USERNAME
    OS = (Get-CimInstance Win32_OperatingSystem).Caption
    CPU = @()
    Memory = @{}
    Disks = @()
    Network = @()
}

# CPU 信息
$cpu = Get-CimInstance Win32_Processor
$result.CPU = $cpu | ForEach-Object {
    [ordered]@{
        Name = $_.Name
        Cores = $_.NumberOfCores
        LogicalProcessors = $_.NumberOfLogicalProcessors
        LoadPercentage = $_.LoadPercentage
    }
}

# 内存信息
$os = Get-CimInstance Win32_OperatingSystem
$totalMB = [math]::Round($os.TotalVisibleMemorySize / 1024)
$freeMB = [math]::Round($os.FreePhysicalMemory / 1024)
$usedMB = $totalMB - $freeMB
$result.Memory = [ordered]@{
    TotalMB = $totalMB
    UsedMB = $usedMB
    FreeMB = $freeMB
    UsedPercentage = [math]::Round(($usedMB / $totalMB) * 100, 1)
}

# 磁盘信息
if ($IncludeDisks) {
    $result.Disks = Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object {
        $totalGB = [math]::Round($_.Size / 1GB, 1)
        $freeGB = [math]::Round($_.FreeSpace / 1GB, 1)
        [ordered]@{
            DeviceID = $_.DeviceID
            VolumeName = $_.VolumeName
            TotalGB = $totalGB
            FreeGB = $freeGB
            UsedPercentage = [math]::Round((($totalGB - $freeGB) / $totalGB) * 100, 1)
        }
    }
}

# 网络信息
if ($IncludeNetwork) {
    $result.Network = Get-NetAdapter -Physical | Where-Object Status -eq "Up" | ForEach-Object {
        $ip = Get-NetIPAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue
        [ordered]@{
            Name = $_.Name
            Status = $_.Status
            MacAddress = $_.MacAddress
            LinkSpeed = $_.LinkSpeed
            IPAddress = ($ip | Select-Object -First 1).IPAddress
        }
    }
}

$result | ConvertTo-Json -Depth 5
