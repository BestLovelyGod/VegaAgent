# ============================================================================
#  Ignorant Vega — 启动脚本 (PowerShell)
#  用法: .\start.ps1 [-ApiKey <key>] [-Port <port>] [-OpenBrowser]
#  作者：Ignorant Star BeidouAgent
# ============================================================================

[CmdletBinding()]
param(
    [string]$ApiKey,
    [int]$Port = 7300,
    [switch]$OpenBrowser,
    [switch]$Dev
)

# UTF-8 编码设置 (防止中文乱码)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null
    [switch]$Dev
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────
# 1. Banner
# ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║   Ignorant Vega (织女星)                     ║" -ForegroundColor Cyan
Write-Host "  ║   Windows 个人电脑管家 + 编程助手             ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────────────────
# 2. 环境检查
# ─────────────────────────────────────────────────────────
Write-Host "[检查] 环境依赖..." -ForegroundColor Yellow

# 使用内置 SDK (首次运行自动下载)
$bundledSdk = Join-Path $PSScriptRoot "Agent.Host\sdk\dotnet\dotnet.exe"
if (-not (Test-Path $bundledSdk)) {
    & (Join-Path $PSScriptRoot "bootstrap-sdk.ps1") -SdkDir (Join-Path $PSScriptRoot "Agent.Host\sdk\dotnet")
    if (-not (Test-Path $bundledSdk)) {
        exit 1
    }
}
$dotnetCmd = $bundledSdk

# ─────────────────────────────────────────────────────────
# 3. 配置
# ─────────────────────────────────────────────────────────
# API Key (环境变量优先，配置文件其次)
if ($ApiKey) {
    $env:AGENT_API_KEY = $ApiKey
    Write-Host "  ✓ API Key: 已通过参数设置" -ForegroundColor Green
} else {
    Write-Host "  ℹ API Key: 从 data/config.json 读取" -ForegroundColor DarkGray
}

# 端口
$env:ASPNETCORE_URLS = "http://localhost:$Port"

# 开发模式
if ($Dev) {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Write-Host "  ✓ 模式: Development" -ForegroundColor Green
} else {
    Write-Host "  ✓ 模式: Production" -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────
# 4. 启动服务
# ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  地址:   http://localhost:$Port" -ForegroundColor Cyan
Write-Host "  Swagger: http://localhost:$Port/swagger" -ForegroundColor Cyan
Write-Host "  健康检查: http://localhost:$Port/health" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
Write-Host "[启动] 正在启动 Agent 服务... (按 Ctrl+C 停止)" -ForegroundColor Green
Write-Host ""

# 可选: 自动打开浏览器
if ($OpenBrowser) {
    Start-Sleep -Seconds 3
    Start-Process "http://localhost:$Port/swagger"
}

# 切换到项目根目录
Set-Location $PSScriptRoot

# 启动 Agent.Host
try {
    $hostDll = Join-Path $PSScriptRoot "Agent.Host\Agent.Host.dll"
    $hostProject = Join-Path $PSScriptRoot "src\Agent.Host\Agent.Host.csproj"
    if (Test-Path $hostDll) {
        # 发布模式: 直接运行 DLL
        Write-Host "  [模式] 发布模式" -ForegroundColor DarkGray
        & $dotnetCmd $hostDll
    } elseif (Test-Path $hostProject) {
        # 开发模式: dotnet run
        Write-Host "  [模式] 开发模式" -ForegroundColor DarkGray
        & $dotnetCmd run --project $hostProject --no-launch-profile
    } else {
        Write-Host "[错误] Agent.Host.dll 或项目文件未找到" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "[错误] 服务启动失败: $_" -ForegroundColor Red
    exit 1
}
