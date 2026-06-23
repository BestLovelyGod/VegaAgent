# Manage-ScheduledTask.ps1
# 计划任务完全管理: Agent 定时执行器 + 系统计划任务全生命周期管理
# Agent 创建的任务统一放在 \IgnorantVega\ 路径下
# 可管理所有系统计划任务 (查询/创建/修改/删除/导出/导入/历史)

param(
    [string]$TaskName = "*",

    [ValidateSet("Get", "GetDetail", "ListAgent", "ListRunning",
        "Create", "Modify", "Enable", "Disable", "Run", "Stop", "Delete",
        "Export", "Import", "GetHistory")]
    [string]$Action = "Get",

    [string]$TaskPath = "\",

    # ── 创建/修改 参数 ──
    [string]$Execute = "",
    [string]$Arguments = "",
    [string]$WorkingDirectory = "",
    [ValidateSet("Daily", "Weekly", "Monthly", "Once", "AtStartup", "AtLogon")]
    [string]$TriggerType = "Daily",
    [string]$TriggerTime = "12:00AM",
    # 重复间隔 (分钟), 0=不重复
    [int]$RepeatInterval = 0,
    # 重复持续时间 (分钟), 0=无限
    [int]$RepeatDuration = 0,
    # 周任务: 逗号分隔, 如 "Monday,Wednesday,Friday"
    [string]$WeekDays = "",
    # 月任务: 逗号分隔日期, 如 "1,15" 或 "Last"
    [string]$MonthDays = "",
    # 月任务: 逗号分隔月份, 如 "January,July"
    [string]$Months = "",
    [string]$Description = "",
    [string]$RunAsAccount = "",
    [ValidateSet("Normal", "Highest")]
    [string]$RunLevel = "Normal",
    # 超时 (分钟), -1=不修改, 0=不限
    [int]$ExecutionTimeLimit = -1,
    # 条件: 仅交流电时运行
    [bool]$RunOnlyOnAC = $false,
    # 条件: 网络可用时运行
    [bool]$RunIfNetworkAvailable = $false,
    # 失败后重试间隔 (分钟), -1=不修改, 0=不重试
    [int]$RestartInterval = -1,
    # 最大重试次数, -1=不修改
    [int]$RestartCount = -1,

    # ── 导出/导入 参数 ──
    [string]$XmlPath = "",

    # ── 历史参数 ──
    [int]$MaxEvents = 50
)

# Agent 专用任务路径
$AgentTaskPath = "\IgnorantVega\"

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Action = $Action
    TaskName = $TaskName
    Tasks = @()
    Error = $null
}

# ── 辅助函数 ──────────────────────────────────────────────

function Format-TriggerInfo {
    param($Trigger)
    $info = [ordered]@{
        Enabled = $Trigger.Enabled
        Type = $Trigger.CimClass.CimClassName -replace 'MSFT_Task.*Trigger_', ''
    }
    if ($Trigger.Repetition.Interval -and $Trigger.Repetition.Interval -ne 'PT0S') {
        $info.RepeatInterval = $Trigger.Repetition.Interval
        $info.RepeatDuration = $Trigger.Repetition.Duration
        $info.StopAtDurationEnd = $Trigger.Repetition.StopAtDurationEnd
    }
    if ($Trigger.StartBoundary) {
        $info.StartTime = $Trigger.StartBoundary
    }
    if ($Trigger.EndBoundary) {
        $info.EndTime = $Trigger.EndBoundary
    }
    # 周触发器
    if ($Trigger.DaysOfWeek) {
        $info.DaysOfWeek = $Trigger.DaysOfWeek
    }
    # 月触发器
    if ($Trigger.DaysOfMonth) {
        $info.DaysOfMonth = $Trigger.DaysOfMonth
    }
    if ($Trigger.MonthsOfYear) {
        $info.MonthsOfYear = $Trigger.MonthsOfYear
    }
    return $info
}

function Format-ActionInfo {
    param($Action)
    $info = [ordered]@{
        Type = $Action.CimClass.CimClassName -replace 'MSFT_Task.*Action_', ''
    }
    if ($Action.Execute) { $info.Execute = $Action.Execute }
    if ($Action.Arguments) { $info.Arguments = $Action.Arguments }
    if ($Action.WorkingDirectory) { $info.WorkingDirectory = $Action.WorkingDirectory }
    return $info
}

function Get-TaskDetail {
    param($Task)
    $info = Get-ScheduledTaskInfo -TaskName $Task.TaskName -TaskPath $Task.TaskPath -ErrorAction SilentlyContinue

    $detail = [ordered]@{
        TaskName = $Task.TaskName
        TaskPath = $Task.TaskPath
        State = $Task.State.ToString()
        Enabled = $Task.Settings.Enabled
        IsAgent = $Task.TaskPath -like "$AgentTaskPath*"
        Description = $Task.Description
        Author = $Task.Author
        # 运行信息
        LastRunTime = if ($info -and $info.LastRunTime) { $info.LastRunTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }
        NextRunTime = if ($info -and $info.NextRunTime -and $info.NextRunTime.Year -gt 2000) { $info.NextRunTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }
        LastResult = if ($info) { "0x{0:X}" -f $info.LastTaskResult } else { "N/A" }
        NumberOfMissedRuns = if ($info) { $info.NumberOfMissedRuns } else { 0 }
        # 触发器
        Triggers = @($Task.Triggers | ForEach-Object { Format-TriggerInfo $_ })
        # 操作
        Actions = @($Task.Actions | ForEach-Object { Format-ActionInfo $_ })
        # 设置
        Settings = [ordered]@{
            AllowStartIfOnBatteries = $Task.Settings.AllowStartIfOnBatteries
            DontStopIfGoingOnBatteries = $Task.Settings.DontStopIfGoingOnBatteries
            ExecutionTimeLimit = $Task.Settings.ExecutionTimeLimit
            RestartInterval = $Task.Settings.RestartInterval
            RestartCount = $Task.Settings.RestartCount
            MultipleInstances = $Task.Settings.MultipleInstances.ToString()
            Priority = $Task.Settings.Priority
            RunOnlyIfNetworkAvailable = $Task.Settings.RunOnlyIfNetworkAvailable
            StartWhenAvailable = $Task.Settings.StartWhenAvailable
            DisallowStartIfOnBatteries = $Task.Settings.DisallowStartIfOnBatteries
            StopIfGoingOnBatteries = $Task.Settings.StopIfGoingOnBatteries
        }
        # 运行主体
        Principal = [ordered]@{
            UserId = $Task.Principal.UserId
            LogonType = $Task.Principal.LogonType.ToString()
            RunLevel = $Task.Principal.RunLevel.ToString()
        }
    }
    return $detail
}

# ── 主逻辑 ────────────────────────────────────────────────

try {
    switch ($Action) {
        # ═══ 查询类 ═══════════════════════════════════════

        "Get" {
            $tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
                $_.TaskName -like $TaskName -and $_.TaskPath -like $TaskPath
            })
            $result.TotalCount = $tasks.Count
            $result.Tasks = @($tasks | ForEach-Object {
                $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
                $isAgent = $_.TaskPath -like "$AgentTaskPath*"

                [ordered]@{
                    TaskName = $_.TaskName
                    TaskPath = $_.TaskPath
                    State = $_.State.ToString()
                    Enabled = $_.Settings.Enabled
                    IsAgent = $isAgent
                    TriggerCount = @($_.Triggers).Count
                    ActionCount = @($_.Actions).Count
                    LastRunTime = if ($info -and $info.LastRunTime) { $info.LastRunTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }
                    NextRunTime = if ($info -and $info.NextRunTime -and $info.NextRunTime.Year -gt 2000) { $info.NextRunTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }
                    LastResult = if ($info) { "0x{0:X}" -f $info.LastTaskResult } else { "N/A" }
                    Description = if ($_.Description) { $_.Description.Substring(0, [Math]::Min(100, $_.Description.Length)) } else { "" }
                }
            })
        }

        "GetDetail" {
            if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                throw "GetDetail 需要指定具体的 TaskName"
            }
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }

        "ListAgent" {
            $tasks = @(Get-ScheduledTask -TaskPath $AgentTaskPath -ErrorAction SilentlyContinue | Where-Object {
                $_.TaskName -like $TaskName
            })
            $result.TotalCount = $tasks.Count
            $result.Tasks = @($tasks | ForEach-Object {
                $detail = Get-TaskDetail $_
                $detail
            })
        }

        "ListRunning" {
            $tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
                $_.State -eq 'Running' -and $_.TaskName -like $TaskName
            })
            $result.TotalCount = $tasks.Count
            $result.Tasks = @($tasks | ForEach-Object {
                $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
                [ordered]@{
                    TaskName = $_.TaskName
                    TaskPath = $_.TaskPath
                    State = "Running"
                    LastRunTime = if ($info -and $info.LastRunTime) { $info.LastRunTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }
                    Description = $_.Description
                }
            })
        }

        "GetHistory" {
            if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                throw "GetHistory 需要指定具体的 TaskName"
            }
            # 从事件日志中获取计划任务历史
            $events = @(Get-WinEvent -FilterHashtable @{
                LogName = 'Microsoft-Windows-TaskScheduler/Operational'
            } -MaxEvents ($MaxEvents * 3) -ErrorAction SilentlyContinue | Where-Object {
                $_.Message -like "*$TaskName*"
            } | Select-Object -First $MaxEvents | ForEach-Object {
                [ordered]@{
                    Time = $_.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
                    Id = $_.Id
                    Level = $_.LevelDisplayName
                    Message = if ($_.Message.Length -gt 200) { $_.Message.Substring(0, 200) + "..." } else { $_.Message }
                }
            })
            $result.TotalEvents = $events.Count
            $result.Events = @($events)
            # 同时返回任务基本信息
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction SilentlyContinue
            if ($task) {
                $result.TaskInfo = Get-TaskDetail $task
            }
        }

        # ═══ 创建类 ═══════════════════════════════════════

        "Create" {
            if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                throw "创建任务时必须指定 TaskName"
            }
            if ([string]::IsNullOrEmpty($Execute)) {
                throw "创建任务时必须指定 Execute (执行程序路径)"
            }

            # 构建执行操作
            $actionParams = @{ Execute = $Execute }
            if (-not [string]::IsNullOrEmpty($Arguments)) { $actionParams["Argument"] = $Arguments }
            if (-not [string]::IsNullOrEmpty($WorkingDirectory)) { $actionParams["WorkingDirectory"] = $WorkingDirectory }
            $taskAction = New-ScheduledTaskAction @actionParams

            # 构建触发器
            switch ($TriggerType) {
                "Daily" {
                    $triggerParams = @{ Daily = $true; At = $TriggerTime }
                    $taskTrigger = New-ScheduledTaskTrigger @triggerParams
                }
                "Weekly" {
                    $triggerParams = @{ Weekly = $true; At = $TriggerTime }
                    if (-not [string]::IsNullOrEmpty($WeekDays)) {
                        $days = $WeekDays -split ',' | ForEach-Object { $_.Trim() }
                        $triggerParams["DaysOfWeek"] = $days
                    } else {
                        $triggerParams["DaysOfWeek"] = "Monday"
                    }
                    $taskTrigger = New-ScheduledTaskTrigger @triggerParams
                }
                "Monthly" {
                    $triggerParams = @{ Monthly = $true; At = $TriggerTime }
                    if (-not [string]::IsNullOrEmpty($MonthDays)) {
                        if ($MonthDays -eq "Last") {
                            $triggerParams["DaysOfMonth"] = 31
                        } else {
                            $days = $MonthDays -split ',' | ForEach-Object { [int]$_.Trim() }
                            $triggerParams["DaysOfMonth"] = $days
                        }
                    } else {
                        $triggerParams["DaysOfMonth"] = 1
                    }
                    if (-not [string]::IsNullOrEmpty($Months)) {
                        $monthList = $Months -split ',' | ForEach-Object { $_.Trim() }
                        $triggerParams["MonthsOfYear"] = $monthList
                    }
                    $taskTrigger = New-ScheduledTaskTrigger @triggerParams
                }
                "Once" {
                    $taskTrigger = New-ScheduledTaskTrigger -Once -At $TriggerTime
                }
                "AtStartup" {
                    $taskTrigger = New-ScheduledTaskTrigger -AtStartup
                }
                "AtLogon" {
                    $taskTrigger = New-ScheduledTaskTrigger -AtLogon
                }
            }

            # 设置重复间隔
            if ($RepeatInterval -gt 0) {
                $interval = "PT${RepeatInterval}M"
                $duration = if ($RepeatDuration -gt 0) { "PT${RepeatDuration}M" } else { "P1D" }
                $taskTrigger.Repetition = (New-CimInstance -CimClass (Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler -ClassName MSFT_TaskRepetitionPattern) -ClientOnly -Property @{
                    Interval = $interval
                    Duration = $duration
                    StopAtDurationEnd = ($RepeatDuration -gt 0)
                })
            }

            # 构建任务主体
            $taskPrincipal = $null
            if (-not [string]::IsNullOrEmpty($RunAsAccount)) {
                $principalParams = @{
                    UserId = $RunAsAccount
                    LogonType = "S4U"
                }
                if ($RunLevel -eq "Highest") {
                    $principalParams["RunLevel"] = "Highest"
                }
                $taskPrincipal = New-ScheduledTaskPrincipal @principalParams
            }

            # 构建任务设置
            $settingsParams = @{
                AllowStartIfOnBatteries = (-not $RunOnlyOnAC)
                DontStopIfGoingOnBatteries = (-not $RunOnlyOnAC)
            }
            if ($ExecutionTimeLimit -ge 0) {
                $settingsParams["ExecutionTimeLimit"] = if ($ExecutionTimeLimit -gt 0) { [TimeSpan]::FromMinutes($ExecutionTimeLimit) } else { [TimeSpan]::FromHours(72) }
            }
            if ($RestartInterval -gt 0) {
                $settingsParams["RestartInterval"] = [TimeSpan]::FromMinutes($RestartInterval)
            }
            if ($RestartCount -gt 0) {
                $settingsParams["RestartCount"] = $RestartCount
            }
            if ($RunIfNetworkAvailable) {
                $settingsParams["RunOnlyIfNetworkAvailable"] = $true
            }
            $taskSettings = New-ScheduledTaskSettingsSet @settingsParams

            # 注册计划任务
            $registerParams = @{
                TaskName = $TaskName
                TaskPath = $AgentTaskPath
                Action = $taskAction
                Trigger = $taskTrigger
                Settings = $taskSettings
                ErrorAction = "Stop"
            }
            if (-not [string]::IsNullOrEmpty($Description)) {
                $registerParams["Description"] = $Description
            }
            if ($taskPrincipal) {
                $registerParams["Principal"] = $taskPrincipal
            }

            Register-ScheduledTask @registerParams

            # 返回创建的任务详情
            $created = Get-ScheduledTask -TaskName $TaskName -TaskPath $AgentTaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $created)
        }

        # ═══ 修改类 ═══════════════════════════════════════

        "Modify" {
            if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                throw "修改任务时必须指定 TaskName"
            }

            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop

            # 修改执行操作 (如果提供了新值)
            if (-not [string]::IsNullOrEmpty($Execute)) {
                $actionParams = @{ Execute = $Execute }
                if (-not [string]::IsNullOrEmpty($Arguments)) { $actionParams["Argument"] = $Arguments }
                if (-not [string]::IsNullOrEmpty($WorkingDirectory)) { $actionParams["WorkingDirectory"] = $WorkingDirectory }
                $task.Actions[0] = New-ScheduledTaskAction @actionParams
            } elseif (-not [string]::IsNullOrEmpty($Arguments)) {
                # 只修改参数
                $task.Actions[0].Arguments = $Arguments
            }

            # 修改触发器 (如果提供了新值)
            if (-not [string]::IsNullOrEmpty($TriggerType)) {
                switch ($TriggerType) {
                    "Daily" { $task.Triggers[0] = New-ScheduledTaskTrigger -Daily -At $TriggerTime }
                    "Weekly" {
                        $triggerParams = @{ Weekly = $true; At = $TriggerTime }
                        if (-not [string]::IsNullOrEmpty($WeekDays)) {
                            $triggerParams["DaysOfWeek"] = ($WeekDays -split ',' | ForEach-Object { $_.Trim() })
                        }
                        $task.Triggers[0] = New-ScheduledTaskTrigger @triggerParams
                    }
                    "Monthly" {
                        $triggerParams = @{ Monthly = $true; At = $TriggerTime }
                        if (-not [string]::IsNullOrEmpty($MonthDays)) {
                            $triggerParams["DaysOfMonth"] = ($MonthDays -split ',' | ForEach-Object { [int]$_.Trim() })
                        }
                        if (-not [string]::IsNullOrEmpty($Months)) {
                            $triggerParams["MonthsOfYear"] = ($Months -split ',' | ForEach-Object { $_.Trim() })
                        }
                        $task.Triggers[0] = New-ScheduledTaskTrigger @triggerParams
                    }
                    "Once" { $task.Triggers[0] = New-ScheduledTaskTrigger -Once -At $TriggerTime }
                    "AtStartup" { $task.Triggers[0] = New-ScheduledTaskTrigger -AtStartup }
                    "AtLogon" { $task.Triggers[0] = New-ScheduledTaskTrigger -AtLogon }
                }
            }

            # 修改描述
            if (-not [string]::IsNullOrEmpty($Description)) {
                $task.Description = $Description
            }

            # 修改设置
            if ($ExecutionTimeLimit -ge 0) {
                $task.Settings.ExecutionTimeLimit = if ($ExecutionTimeLimit -gt 0) { [TimeSpan]::FromMinutes($ExecutionTimeLimit) } else { [TimeSpan]::Zero }
            }
            if ($RestartInterval -ge 0) {
                $task.Settings.RestartInterval = if ($RestartInterval -gt 0) { [TimeSpan]::FromMinutes($RestartInterval) } else { [TimeSpan]::Zero }
            }
            if ($RestartCount -ge 0) {
                $task.Settings.RestartCount = $RestartCount
            }
            if ($RunOnlyOnAC) {
                $task.Settings.AllowStartIfOnBatteries = $false
                $task.Settings.DisallowStartIfOnBatteries = $true
                $task.Settings.StopIfGoingOnBatteries = $true
            }
            if ($RunIfNetworkAvailable) {
                $task.Settings.RunOnlyIfNetworkAvailable = $true
            }

            # 应用修改
            $task | Set-ScheduledTask -ErrorAction Stop | Out-Null

            # 返回修改后的详情
            $updated = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $updated)
        }

        # ═══ 状态控制 ═══════════════════════════════════════

        "Enable" {
            Enable-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop | Out-Null
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }

        "Disable" {
            Disable-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop | Out-Null
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }

        "Run" {
            Start-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop | Out-Null
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }

        "Stop" {
            Stop-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop | Out-Null
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }

        "Delete" {
            Unregister-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Confirm:$false -ErrorAction Stop
            $result.Tasks = @([ordered]@{
                TaskName = $TaskName
                TaskPath = $TaskPath
                State = "已删除"
            })
        }

        # ═══ 导入导出 ═══════════════════════════════════════

        "Export" {
            if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                throw "导出时必须指定 TaskName"
            }
            if ([string]::IsNullOrEmpty($XmlPath)) {
                $XmlPath = "$env:TEMP\$TaskName.xml"
            }
            $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop
            $xml = [xml](Export-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -ErrorAction Stop)
            $xml.Save($XmlPath)
            $result.Tasks = @([ordered]@{
                TaskName = $TaskName
                TaskPath = $TaskPath
                XmlPath = $XmlPath
                State = "已导出"
            })
        }

        "Import" {
            if ([string]::IsNullOrEmpty($XmlPath)) {
                throw "导入时必须指定 XmlPath"
            }
            if (-not (Test-Path $XmlPath)) {
                throw "XML 文件不存在: $XmlPath"
            }
            $targetName = if ([string]::IsNullOrEmpty($TaskName) -or $TaskName -eq "*") {
                [System.IO.Path]::GetFileNameWithoutExtension($XmlPath)
            } else {
                $TaskName
            }
            Register-ScheduledTask -TaskName $targetName -TaskPath $AgentTaskPath -Xml (Get-Content $XmlPath -Raw) -Force -ErrorAction Stop | Out-Null
            $task = Get-ScheduledTask -TaskName $targetName -TaskPath $AgentTaskPath -ErrorAction Stop
            $result.Tasks = @(Get-TaskDetail $task)
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
