# Test-Network.ps1
# 网络诊断: 连接测试、DNS 解析、网络适配器状态
# 参数: Target(测试目标, 默认 bing.com), Count(测试次数)

param(
    [string]$Target = "bing.com",
    [int]$Count = 4
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Target = $Target
    Connectivity = @{}
    DNS = @{}
    Adapters = @()
}

# 连接测试
try {
    $ping = Test-Connection -ComputerName $Target -Count $Count -ErrorAction Stop
    $result.Connectivity = [ordered]@{
        Status = "OK"
        Target = $Target
        PacketsSent = $Count
        PacketsReceived = ($ping | Measure-Object).Count
        AvgLatencyMS = [math]::Round(($ping | Measure-Object -Property Latency -Average).Average, 1)
        MinLatencyMS = ($ping | Measure-Object -Property Latency -Minimum).Minimum
        MaxLatencyMS = ($ping | Measure-Object -Property Latency -Maximum).Maximum
    }
} catch {
    $result.Connectivity = [ordered]@{
        Status = "FAILED"
        Target = $Target
        Error = $_.Exception.Message
    }
}

# DNS 解析
try {
    $dns = Resolve-DnsName -Name $Target -ErrorAction Stop
    $result.DNS = [ordered]@{
        Status = "OK"
        Target = $Target
        Addresses = @($dns | Where-Object Type -eq "A" | ForEach-Object { $_.IPAddress })
    }
} catch {
    $result.DNS = [ordered]@{
        Status = "FAILED"
        Target = $Target
        Error = $_.Exception.Message
    }
}

# 网络适配器
$result.Adapters = Get-NetAdapter -Physical | ForEach-Object {
    $ip = Get-NetIPAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue
    [ordered]@{
        Name = $_.Name
        Status = $_.Status
        MacAddress = $_.MacAddress
        LinkSpeed = $_.LinkSpeed
        IPAddress = ($ip | Select-Object -First 1).IPAddress
    }
}

$result | ConvertTo-Json -Depth 5
