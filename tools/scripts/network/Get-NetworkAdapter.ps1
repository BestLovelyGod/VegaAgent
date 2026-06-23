# Get-NetworkAdapter.ps1
# 网络适配器: 查看网络适配器详细信息，包括 IP、DNS、速度
# 参数: Name(适配器名), IncludeDisabled(包含禁用的)

param(
    [string]$Name = "*",

    [switch]$IncludeDisabled
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    ComputerName = $env:COMPUTERNAME
    Adapters = @()
    Summary = [ordered]@{
        Total = 0
        Up = 0
        Down = 0
    }
}

try {
    $adapters = Get-NetAdapter -Name $Name -ErrorAction SilentlyContinue

    if (-not $IncludeDisabled) {
        $adapters = $adapters | Where-Object { $_.Status -ne "Not Present" }
    }

    $result.Summary.Total = ($adapters | Measure-Object).Count
    $result.Summary.Up = ($adapters | Where-Object { $_.Status -eq "Up" } | Measure-Object).Count
    $result.Summary.Down = ($adapters | Where-Object { $_.Status -ne "Up" } | Measure-Object).Count

    $result.Adapters = $adapters | ForEach-Object {
        $ipConfig = Get-NetIPAddress -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue
        $dnsConfig = Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue
        $dhcpEnabled = Get-NetIPInterface -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue

        $ipv4 = $ipConfig | Where-Object { $_.AddressFamily -eq 2 } | Select-Object -First 1
        $ipv6 = $ipConfig | Where-Object { $_.AddressFamily -eq 23 } | Select-Object -First 1

        [ordered]@{
            Name = $_.Name
            Description = $_.InterfaceDescription
            Status = $_.Status.ToString()
            MacAddress = $_.MacAddress
            LinkSpeed = $_.LinkSpeed.ToString()
            IPv4Address = if ($ipv4) { $ipv4.IPAddress } else { "N/A" }
            IPv4Subnet = if ($ipv4) { $ipv4.PrefixLength } else { 0 }
            IPv6Address = if ($ipv6) { $ipv6.IPAddress } else { "N/A" }
            DHCPEnabled = if ($dhcpEnabled) { $dhcpEnabled.Dhcp.ToString() } else { "Unknown" }
            DNSServers = @($dnsConfig | Where-Object { $_.AddressFamily -eq 2 } | ForEach-Object { $_.ServerAddresses })
            Gateway = @((Get-NetRoute -InterfaceIndex $_.ifIndex -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue).NextHop)
            BytesSent = $_.ifAlias
            MTU = (Get-NetIPInterface -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)?.NlMtu
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
