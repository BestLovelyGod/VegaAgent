# ============================================================================
#  无感 · 织女星 — 桌面控制台启动脚本
#  作者：Ignorant Star BeidouAgent
# ============================================================================

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ── 1. 检查内置 SDK ──
$bundledSdk = Join-Path $ScriptDir "Agent.Host\sdk\dotnet\dotnet.exe"
if (-not (Test-Path $bundledSdk)) {
    Write-Host "📦 首次运行，正在下载 .NET SDK..." -ForegroundColor Yellow
    $bootstrapScript = Join-Path $ScriptDir "bootstrap-sdk.ps1"
    if (Test-Path $bootstrapScript) {
        & $bootstrapScript
        if (-not (Test-Path $bundledSdk)) {
            Write-Host "❌ SDK 下载失败，请检查网络连接" -ForegroundColor Red
            exit 1
        }
        Write-Host "✅ SDK 下载完成" -ForegroundColor Green
    } else {
        Write-Host "⚠️ 未找到 bootstrap-sdk.ps1，跳过 SDK 检查" -ForegroundColor Yellow
    }
}

# ── 2. 检查 WebView2 Runtime ──
$webviewBootstrap = Join-Path $ScriptDir "webview-bootstrap.ps1"
if (Test-Path $webviewBootstrap) {
    & $webviewBootstrap
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ WebView2 Runtime 安装失败" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "⚠️ 未找到 webview-bootstrap.ps1，跳过 WebView2 检查" -ForegroundColor Yellow
}

# ── 3. 启动 GUI (提权运行) ──
$GuiDll = Join-Path $ScriptDir "Agent.GUI\Agent.GUI.dll"
$GuiExe = Join-Path $ScriptDir "Agent.GUI\Agent.GUI.exe"

# 如果根目录没有，尝试 publish/release 目录
if (-not (Test-Path $GuiDll) -and -not (Test-Path $GuiExe)) {
    $releaseGuiDll = Join-Path $ScriptDir "publish\release\Agent.GUI\Agent.GUI.dll"
    $releaseGuiExe = Join-Path $ScriptDir "publish\release\Agent.GUI\Agent.GUI.exe"
    if (Test-Path $releaseGuiDll) {
        $GuiDll = $releaseGuiDll
        $bundledSdk = Join-Path $ScriptDir "Agent.Host\sdk\dotnet\dotnet.exe"
    } elseif (Test-Path $releaseGuiExe) {
        $GuiExe = $releaseGuiExe
    }
}

# 检查当前是否已是管理员
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (Test-Path $GuiDll) {
    Write-Host "🚀 使用内置 SDK 启动 GUI (管理员模式)..." -ForegroundColor Cyan
    if ($isAdmin) {
        Start-Process -FilePath $bundledSdk -ArgumentList "`"$GuiDll`"" -WorkingDirectory $ScriptDir
    } else {
        # 非管理员，提权启动
        Start-Process -FilePath "powershell" -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    }
} elseif (Test-Path $GuiExe) {
    Write-Host "🚀 启动 GUI (管理员模式)..." -ForegroundColor Cyan
    if ($isAdmin) {
        Start-Process -FilePath $GuiExe
    } else {
        Start-Process -FilePath $GuiExe -Verb RunAs
    }
} else {
    Write-Host "❌ Agent.GUI 未找到，请先运行 publish.ps1" -ForegroundColor Red
    exit 1
}
