# Manage-Service.ps1
# 系统服务完全管理: Agent 全面接管 Windows 服务控制
# 支持查询/详情/启动/停止/重启/暂停/恢复/创建/删除/配置/依赖/历史

param(
    [string]$Name = "*",

    [ValidateSet("Get", "GetDetail", "ListRunning", "ListAutomatic", "ListDisabled", "ListDelayed",
        "Start", "Stop", "Restart", "Pause", "Resume",
        "Create", "Delete",
        "SetStartType", "SetLogonAccount", "SetDescription",
        "GetDependent", "GetDependency", "GetConfig",
        "GetHistory")]
    [string]$Action = "Get",

    [string]$DisplayName = "",
    [string]$BinaryPathName = "",
    [ValidateSet("Automatic", "Manual", "Disabled", "Boot", "System")]
    [string]$StartType = "",
    [string]$LogonAccount = "",
    [string]$LogonPassword = "",
    [string]$Description = "",
    [ValidateSet("Normal", "BelowNormal", "AboveNormal", "High", "RealTime")]
    [string]$Priority = "Normal",
    [int]$MaxEvents = 50,
    [int]$MaxResults = 0
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Action = $Action
    ServiceName = $Name
    Services = @()
    Error = $null
}

# 辅助函数: 获取服务详细信息
function Get-ServiceDetail {
    param($Svc)
    $svcObj = if ($Svc -is [string]) { Get-Service -Name $Svc -ErrorAction SilentlyContinue } else { $Svc }
    if (-not $svcObj) { return $null }

    # 获取 WMI 配置信息
    $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($svcObj.Name)'" -ErrorAction SilentlyContinue

    # 获取依赖关系
    $dependsOn = @()
    try {
        $deps = $svcObj.DependentServices
        if ($deps) { $dependsOn = @($deps | ForEach-Object { $_.Name }) }
    } catch {}

    $dependencies = @()
    try {
        $reqs = $svcObj.RequiredServices
        if ($reqs) { $dependencies = @($reqs | ForEach-Object { $_.Name }) }
    } catch {}

    [ordered]@{
        Name = $svcObj.Name
        DisplayName = $svcObj.DisplayName
        Status = $svcObj.Status.ToString()
        StartType = if ($wmi) { $wmi.StartMode } else { "Unknown" }
        Description = if ($wmi) { $wmi.Description } else { "" }
        PathName = if ($wmi) { $wmi.PathName } else { "" }
        LogonAccount = if ($wmi) { $wmi.StartName } else { "" }
        ProcessId = if ($wmi) { $wmi.ProcessId } else { 0 }
        CanPauseAndContinue = $svcObj.CanPauseAndContinue
        CanShutdown = $svcObj.CanShutdown
        CanStop = $svcObj.CanStop
        ServiceType = if ($wmi) { $wmi.ServiceType } else { "" }
        DependentServices = $dependsOn
        DependentCount = $dependsOn.Count
        RequiredServices = $dependencies
        RequiredCount = $dependencies.Count
        ExitCode = if ($wmi) { $wmi.ExitCode } else { 0 }
        ServiceSpecificExitCode = if ($wmi) { $wmi.ServiceSpecificExitCode } else { 0 }
        Started = if ($wmi) { $wmi.Started } else { $false }
    }
}

# 辅助函数: 获取服务列表 (带过滤, 批量WMI查询优化)
function Get-ServiceList {
    param(
        [string]$Filter = "*",
        [string]$StatusFilter = "",
        [string]$StartTypeFilter = "",
        [int]$Limit = 0
    )

    # 一次性批量获取所有 WMI 服务数据
    $wmiAll = @{}
    Get-CimInstance -ClassName Win32_Service -ErrorAction SilentlyContinue | ForEach-Object {
        $wmiAll[$_.Name] = $_
    }

    $services = @(Get-Service -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like $Filter -or $_.DisplayName -like $Filter
    })

    if ($StatusFilter -eq "Running") {
        $services = @($services | Where-Object { $_.Status -eq 'Running' })
    } elseif ($StatusFilter -eq "Stopped") {
        $services = @($services | Where-Object { $_.Status -eq 'Stopped' })
    }

    if ($StartTypeFilter -eq "Disabled") {
        $services = @($services | Where-Object {
            $wmiAll[$_.Name].StartMode -eq 'Disabled'
        })
    } elseif ($StartTypeFilter -eq "Automatic") {
        $services = @($services | Where-Object {
            $wmiAll[$_.Name].StartMode -eq 'Auto'
        })
    } elseif ($StartTypeFilter -eq "Delayed") {
        $services = @($services | Where-Object {
            $wmiAll[$_.Name].StartMode -eq 'Auto' -and
            (Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$($_.Name)" -Name "DelayedAutostart" -ErrorAction SilentlyContinue).DelayedAutostart -eq 1
        })
    }

    if ($Limit -gt 0) {
        $services = @($services | Select-Object -First $Limit)
    }

    $result.TotalCount = $services.Count
    @($services | ForEach-Object {
        $wmi = $wmiAll[$_.Name]
        [ordered]@{
            Name = $_.Name
            DisplayName = $_.DisplayName
            Status = $_.Status.ToString()
            StartType = if ($wmi) { $wmi.StartMode } else { "Unknown" }
            ProcessId = if ($wmi) { $wmi.ProcessId } else { 0 }
            LogonAccount = if ($wmi) { $wmi.StartName } else { "" }
        }
    })
}

try {
    switch ($Action) {
        # ═══ 查询类 ═══════════════════════════════════════

        "Get" {
            $limit = if ($MaxResults -gt 0) { $MaxResults } else { 100 }
            $result.Services = @(Get-ServiceList -Filter $Name -Limit $limit)
        }

        "GetDetail" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "GetDetail 需要指定具体的服务名称"
            }
            $detail = Get-ServiceDetail -Svc $Name
            if ($detail) {
                $result.Services = @($detail)
            } else {
                $result.Error = "服务不存在: $Name"
            }
        }

        "ListRunning" {
            $limit = if ($MaxResults -gt 0) { $MaxResults } else { 0 }
            $result.Services = @(Get-ServiceList -Filter $Name -StatusFilter "Running" -Limit $limit)
        }

        "ListAutomatic" {
            $limit = if ($MaxResults -gt 0) { $MaxResults } else { 0 }
            $result.Services = @(Get-ServiceList -Filter $Name -StartTypeFilter "Automatic" -Limit $limit)
        }

        "ListDisabled" {
            $limit = if ($MaxResults -gt 0) { $MaxResults } else { 0 }
            $result.Services = @(Get-ServiceList -Filter $Name -StartTypeFilter "Disabled" -Limit $limit)
        }

        "ListDelayed" {
            $limit = if ($MaxResults -gt 0) { $MaxResults } else { 0 }
            $result.Services = @(Get-ServiceList -Filter $Name -StartTypeFilter "Delayed" -Limit $limit)
        }

        "GetConfig" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "GetConfig 需要指定具体的服务名称"
            }
            $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
            $reg = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$Name" -ErrorAction SilentlyContinue
            $result.Services = @([ordered]@{
                Name = $wmi.Name
                DisplayName = $wmi.DisplayName
                Description = $wmi.Description
                PathName = $wmi.PathName
                ServiceType = $wmi.ServiceType
                StartMode = $wmi.StartMode
                StartName = $wmi.StartName
                State = $wmi.State
                ProcessId = $wmi.ProcessId
                AcceptPause = $wmi.AcceptPause
                AcceptStop = $wmi.AcceptStop
                DesktopInteract = $wmi.DesktopInteract
                ErrorControl = $wmi.ErrorControl
                ExitCode = $wmi.ExitCode
                ServiceSpecificExitCode = $wmi.ServiceSpecificExitCode
                TagId = $wmi.TagId
                WaitHint = $wmi.WaitHint
                CheckPoint = $wmi.CheckPoint
                DelayedAutoStart = if ($reg) { $reg.DelayedAutostart } else { $null }
                Group = if ($reg) { $reg.Group } else { $null }
                DependOnService = if ($reg) { $reg.DependOnService } else { @() }
                DependOnGroup = if ($reg) { $reg.DependOnGroup } else { @() }
                ObjectName = $wmi.StartName
            })
        }

        "GetDependent" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "GetDependent 需要指定具体的服务名称"
            }
            $svc = Get-Service -Name $Name -ErrorAction Stop
            $dependents = @($svc.DependentServices | ForEach-Object {
                $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($_.Name)'" -ErrorAction SilentlyContinue
                [ordered]@{
                    Name = $_.Name
                    DisplayName = $_.DisplayName
                    Status = $_.Status.ToString()
                    StartType = if ($wmi) { $wmi.StartMode } else { "Unknown" }
                }
            })
            $result.ServiceName = $Name
            $result.TotalCount = $dependents.Count
            $result.DependentServices = @($dependents)
        }

        "GetDependency" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "GetDependency 需要指定具体的服务名称"
            }
            $svc = Get-Service -Name $Name -ErrorAction Stop
            $dependencies = @($svc.RequiredServices | ForEach-Object {
                $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($_.Name)'" -ErrorAction SilentlyContinue
                [ordered]@{
                    Name = $_.Name
                    DisplayName = $_.DisplayName
                    Status = $_.Status.ToString()
                    StartType = if ($wmi) { $wmi.StartMode } else { "Unknown" }
                }
            })
            $result.ServiceName = $Name
            $result.TotalCount = $dependencies.Count
            $result.RequiredServices = @($dependencies)
        }

        "GetHistory" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "GetHistory 需要指定具体的服务名称"
            }
            $displayName = (Get-Service -Name $Name -ErrorAction SilentlyContinue).DisplayName
            $events = @(Get-WinEvent -FilterHashtable @{
                LogName = 'System'
                ProviderName = 'Service Control Manager'
            } -MaxEvents ($MaxEvents * 5) -ErrorAction SilentlyContinue | Where-Object {
                $_.Message -like "*$Name*" -or ($displayName -and $_.Message -like "*$displayName*")
            } | Select-Object -First $MaxEvents | ForEach-Object {
                [ordered]@{
                    Time = $_.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
                    Id = $_.Id
                    Level = $_.LevelDisplayName
                    Message = if ($_.Message.Length -gt 300) { $_.Message.Substring(0, 300) + "..." } else { $_.Message }
                }
            })
            $result.TotalEvents = $events.Count
            $result.Events = @($events)
            $detail = Get-ServiceDetail -Svc $Name
            if ($detail) { $result.ServiceInfo = $detail }
        }

        # ═══ 控制类 ═══════════════════════════════════════

        "Start" {
            Start-Service -Name $Name -ErrorAction Stop
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "Stop" {
            Stop-Service -Name $Name -ErrorAction Stop -Force
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "Restart" {
            Restart-Service -Name $Name -ErrorAction Stop -Force
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "Pause" {
            Suspend-Service -Name $Name -ErrorAction Stop
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "Resume" {
            Resume-Service -Name $Name -ErrorAction Stop
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        # ═══ 创建/删除 ═══════════════════════════════════

        "Create" {
            if ([string]::IsNullOrEmpty($Name)) {
                throw "创建服务时必须指定 Name"
            }
            if ([string]::IsNullOrEmpty($BinaryPathName)) {
                throw "创建服务时必须指定 BinaryPathName (可执行文件路径)"
            }

            $createParams = @{
                Name = $Name
                BinaryPathName = $BinaryPathName
                ErrorAction = "Stop"
            }
            if (-not [string]::IsNullOrEmpty($DisplayName)) { $createParams["DisplayName"] = $DisplayName }
            if (-not [string]::IsNullOrEmpty($StartType)) { $createParams["StartupType"] = $StartType }
            if (-not [string]::IsNullOrEmpty($Description)) { $createParams["Description"] = $Description }
            if (-not [string]::IsNullOrEmpty($LogonAccount)) { $createParams["Credential"] = New-Object System.Management.Automation.PSCredential($LogonAccount, (ConvertTo-SecureString $LogonPassword -AsPlainText -Force)) }

            New-Service @createParams | Out-Null

            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "Delete" {
            $svc = Get-Service -Name $Name -ErrorAction Stop
            if ($svc.Status -eq 'Running') {
                Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
            }
            # 使用 sc.exe 删除服务 (PowerShell 没有 Remove-Service)
            $output = & sc.exe delete $Name 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "删除服务失败: $output"
            }
            $result.Services = @([ordered]@{
                Name = $Name
                State = "已删除"
            })
        }

        # ═══ 配置类 ═══════════════════════════════════════

        "SetStartType" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "SetStartType 需要指定具体的服务名称"
            }
            if ([string]::IsNullOrEmpty($StartType)) {
                throw "SetStartType 需要指定 StartType (Automatic/Manual/Disabled)"
            }
            Set-Service -Name $Name -StartupType $StartType -ErrorAction Stop
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "SetLogonAccount" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "SetLogonAccount 需要指定具体的服务名称"
            }
            if ([string]::IsNullOrEmpty($LogonAccount)) {
                throw "SetLogonAccount 需要指定 LogonAccount"
            }
            $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
            if ([string]::IsNullOrEmpty($LogonPassword)) {
                $null = Invoke-CimMethod -InputObject $wmi -MethodName Change -Arguments @{
                    StartName = $LogonAccount
                    StartPassword = ""
                } -ErrorAction Stop
            } else {
                $null = Invoke-CimMethod -InputObject $wmi -MethodName Change -Arguments @{
                    StartName = $LogonAccount
                    StartPassword = $LogonPassword
                } -ErrorAction Stop
            }
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }

        "SetDescription" {
            if ([string]::IsNullOrEmpty($Name) -or $Name -eq "*") {
                throw "SetDescription 需要指定具体的服务名称"
            }
            if ([string]::IsNullOrEmpty($Description)) {
                throw "SetDescription 需要指定 Description"
            }
            Set-Service -Name $Name -Description $Description -ErrorAction Stop
            $detail = Get-ServiceDetail -Svc $Name
            $result.Services = @($detail)
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
