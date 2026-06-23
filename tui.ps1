# ============================================================================
#  Ignorant Vega TUI — 终端界面启动脚本
#  用法: .\tui.ps1 [-HostUrl <url>]
#  作者：Ignorant Star BeidouAgent
# ============================================================================

[CmdletBinding()]
param(
    [string]$HostUrl = "http://localhost:7300"
)

# UTF-8 编码设置 (防止中文乱码)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────
# 1. Banner
# ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "  ║   Ignorant Vega TUI (织女星终端界面)         ║" -ForegroundColor Magenta
Write-Host "  ╚══════════════════════════════════════════════╝" -ForegroundColor Magenta
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
# 3. 检查 Agent Host 连接
# ─────────────────────────────────────────────────────────
Write-Host "[连接] Agent Host: $HostUrl" -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest -Uri "$HostUrl/health" -TimeoutSec 3 -ErrorAction Stop
    Write-Host "  ✓ Agent Host 在线" -ForegroundColor Green
} catch {
    Write-Host "  ⚠ Agent Host 未响应 (将进入离线模式)" -ForegroundColor Yellow
    Write-Host "    请先启动 Agent Host: .\start.ps1" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────
# 4. 启动 TUI
# ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[启动] 正在启动 TUI..." -ForegroundColor Green
Write-Host ""

# 切换到项目根目录
Set-Location $PSScriptRoot

# 启动 Agent.TUI
try {
    $tuiDll = Join-Path $PSScriptRoot "Agent.TUI\Agent.TUI.dll"
    $tuiProject = Join-Path $PSScriptRoot "src\Agent.TUI\Agent.TUI.csproj"
    if (Test-Path $tuiDll) {
        # 发布模式
        Write-Host "  [模式] 发布模式" -ForegroundColor DarkGray
        & $dotnetCmd $tuiDll $HostUrl
    } elseif (Test-Path $tuiProject) {
        # 开发模式
        Write-Host "  [模式] 开发模式" -ForegroundColor DarkGray
        & $dotnetCmd run --project $tuiProject -- $HostUrl
    } else {
        Write-Host "[错误] Agent.TUI.dll 或项目文件未找到" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "[错误] TUI 启动失败: $_" -ForegroundColor Red
    exit 1
}
