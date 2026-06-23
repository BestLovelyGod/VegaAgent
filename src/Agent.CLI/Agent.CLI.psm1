# ============================================================================
# Ignorant Vega CLI 模块
# ============================================================================

$script:AgentBaseUrl = $env:AGENT_URL ?? "http://localhost:7300"

function Invoke-AgentTask {
    <#
    .SYNOPSIS
    提交任务到 Ignorant Vega
    .DESCRIPTION
    通过 REST API 提交任务，等待执行完成并返回结果
    .PARAMETER Message
    任务描述
    .PARAMETER Wait
    是否等待任务完成 (默认等待)
    .PARAMETER Timeout
    超时秒数 (默认 120)
    .EXAMPLE
    Invoke-AgentTask "查看系统内存使用情况"
    .EXAMPLE
    Invoke-AgentTask "清理 C:\Temp 下超过7天的文件" -Timeout 300
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Message,

        [switch]$Wait,
        [int]$Timeout = 120
    )

    # 提交任务
    $body = @{ message = $Message } | ConvertTo-Json
    try {
        $task = Invoke-RestMethod -Uri "$script:AgentBaseUrl/api/tasks" -Method Post -Body $body -ContentType "application/json"
    } catch {
        Write-Error "提交任务失败: $($_.Exception.Message)"
        return
    }

    Write-Host "✅ 任务已提交: $($task.taskId)" -ForegroundColor Green
    Write-Host "   消息: $Message"
    Write-Host "   状态: $($task.status)"

    if (-not $Wait) {
        return $task
    }

    # 等待完成
    return Watch-AgentTask -TaskId $task.taskId -Timeout $Timeout
}

function Watch-AgentTask {
    <#
    .SYNOPSIS
    监控任务执行状态
    .PARAMETER TaskId
    任务 ID
    .PARAMETER Timeout
    超时秒数 (默认 120)
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TaskId,
        [int]$Timeout = 120
    )

    $startTime = Get-Date
    $spinner = @('⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏')
    $i = 0

    while ($true) {
        $elapsed = ((Get-Date) - $startTime).TotalSeconds
        if ($elapsed -gt $Timeout) {
            Write-Host "`n⏰ 超时 ($Timeout 秒)" -ForegroundColor Yellow
            return $null
        }

        try {
            $task = Invoke-RestMethod -Uri "$script:AgentBaseUrl/api/tasks/$TaskId" -Method Get
        } catch {
            Write-Error "查询任务失败: $($_.Exception.Message)"
            return $null
        }

        $spin = $spinner[$i % $spinner.Length]
        $i++

        switch ($task.status) {
            "Pending" {
                Write-Host "`r$spin 等待中..." -NoNewline -ForegroundColor Yellow
            }
            "Running" {
                $toolInfo = ""
                if ($task.toolCalls -gt 0) {
                    $toolInfo = " | 工具调用: $($task.toolCalls)"
                }
                Write-Host "`r$spin 执行中... (轮次: $($task.iterations)$toolInfo)" -NoNewline -ForegroundColor Cyan
            }
            "Completed" {
                Write-Host "`r✅ 任务完成! (轮次: $($task.iterations), $($task.totalTokens) tokens)" -ForegroundColor Green
                Write-Host ""
                Write-Host $task.result -ForegroundColor White
                return $task
            }
            "Failed" {
                Write-Host "`r❌ 任务失败: $($task.error)" -ForegroundColor Red
                return $task
            }
            "Cancelled" {
                Write-Host "`r⚠️ 任务已取消" -ForegroundColor Yellow
                return $task
            }
        }

        Start-Sleep -Milliseconds 500
    }
}

function Get-AgentStatus {
    <#
    .SYNOPSIS
    查看 Agent 服务状态
    #>
    [CmdletBinding()]
    param()

    try {
        $health = Invoke-RestMethod -Uri "$script:AgentBaseUrl/health" -Method Get
        $tools = Invoke-RestMethod -Uri "$script:AgentBaseUrl/api/tools" -Method Get
        $tasks = Invoke-RestMethod -Uri "$script:AgentBaseUrl/api/tasks" -Method Get

        Write-Host "🤖 Ignorant Vega 状态" -ForegroundColor Cyan
        Write-Host "   服务: $($health.status)" -ForegroundColor Green
        Write-Host "   版本: $($health.version)"
        Write-Host "   工具: $($tools.Count) 个"
        Write-Host "   任务: $($tasks.Count) 个"
    } catch {
        Write-Error "无法连接 Agent 服务: $($_.Exception.Message)"
        Write-Host "   请确保服务已启动: dotnet run --project src/Agent.Host" -ForegroundColor Yellow
    }
}

function Get-AgentTools {
    <#
    .SYNOPSIS
    列出可用工具
    #>
    [CmdletBinding()]
    param()

    try {
        $tools = Invoke-RestMethod -Uri "$script:AgentBaseUrl/api/tools" -Method Get

        Write-Host "🔧 可用工具 ($($tools.Count) 个)" -ForegroundColor Cyan
        Write-Host ""

        $tools | ForEach-Object {
            $risk = switch ($_.riskLevel) {
                0 { "🟢" }
                1 { "🟡" }
                2 { "🟠" }
                3 { "🔴" }
            }
            Write-Host "  $risk $($_.name)" -ForegroundColor White
            Write-Host "     $($_.description)" -ForegroundColor Gray
        }
    } catch {
        Write-Error "获取工具列表失败: $($_.Exception.Message)"
    }
}

# 导出函数
Export-ModuleMember -Function Invoke-AgentTask, Watch-AgentTask, Get-AgentStatus, Get-AgentTools
