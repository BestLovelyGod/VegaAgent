# ============================================================================
#  Ignorant Vega — WebView2 Runtime 引导脚本
#  首次运行时自动下载安装 Microsoft Edge WebView2 Runtime
#  GUI 模式必需依赖
#  作者：Ignorant Star BeidouAgent
# ============================================================================

# UTF-8 编置 (防止中文乱码)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = "Stop"

$webView2InstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

# ── 多方式检查是否已安装 ──
# 1. 注册表 (HKLM 64 位原生路径)
$regHKLM64 = "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}"
# 2. 注册表 (HKLM 32 位兼容路径)
$regHKLM = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}"
# 3. 注册表 (HKCU 当前用户安装)
$regHKCU = "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEB-235B8DB51B8F}"
# 4. 可执行文件路径
$webView2Exe = Join-Path $env:SystemRoot "System32\Microsoft-Edge-WebView\msedgewebview2.exe"
# 5. EdgeWebView 安装目录
$webView2Dir = Join-Path ${env:ProgramFiles(x86)} "Microsoft\EdgeWebView"

function Test-WebView2Installed {
    if (Test-Path $script:regHKLM64) { return $true }
    if (Test-Path $script:regHKLM) { return $true }
    if (Test-Path $script:regHKCU) { return $true }
    if (Test-Path $script:webView2Exe) { return $true }
    if ((Test-Path $script:webView2Dir) -and (Get-ChildItem $script:webView2Dir -ErrorAction SilentlyContinue).Count -gt 0) { return $true }
    return $false
}

if (Test-WebView2Installed) {
    Write-Host "  [OK] WebView2 Runtime ready" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "  [WebView2] Runtime not found" -ForegroundColor Yellow
Write-Host "  [WebView2] Downloading installer..." -ForegroundColor Cyan
Write-Host ""

# ── 下载安装器 ──
$installerPath = Join-Path $env:TEMP "MicrosoftEdgeWebview2Setup.exe"
try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $webView2InstallerUrl -OutFile $installerPath -UseBasicParsing -TimeoutSec 120
    if (-not (Test-Path $installerPath)) {
        Write-Host "  [WebView2] Download failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  [WebView2] Download complete" -ForegroundColor Green
} catch {
    Write-Host "  [WebView2] Download failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# ── 静默安装 ──
Write-Host "  [WebView2] Installing (silent)..." -ForegroundColor Cyan
try {
    $process = Start-Process -FilePath $installerPath -ArgumentList "/silent", "/install" -Wait -PassThru
    if ($process.ExitCode -eq 0) {
        Write-Host "  [WebView2] Installation complete" -ForegroundColor Green
    } else {
        Write-Host "  [WebView2] Installation finished with exit code: $($process.ExitCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  [WebView2] Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    # 清理安装器
    if (Test-Path $installerPath) {
        Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
    }
}

# ── 验证安装 (引导安装器异步执行，需要重试等待) ──
$maxRetries = 6
for ($i = 1; $i -le $maxRetries; $i++) {
    if (Test-WebView2Installed) {
        Write-Host "  [WebView2] Ready" -ForegroundColor Green
        exit 0
    }
    if ($i -lt $maxRetries) {
        Write-Host "  [WebView2] Waiting for installation to complete... ($i/$maxRetries)" -ForegroundColor Cyan
        Start-Sleep -Seconds 5
    }
}
Write-Host "  [WebView2] Installation may have failed. Please install manually." -ForegroundColor Yellow
Write-Host "  [WebView2] Download: https://developer.microsoft.com/en-us/microsoft-edge/webview2/" -ForegroundColor Cyan
exit 1
