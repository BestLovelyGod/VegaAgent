# ============================================================================
#  Ignorant Vega — 内置 .NET SDK 引导脚本
#  首次运行时自动下载 .NET SDK 到 Agent.Host/sdk/dotnet/
#  使用 aria2c 多线程下载 + 7z 快速解压
#  作者：Ignorant Star BeidouAgent
# ============================================================================

param(
    [string]$SdkDir = "Agent.Host\sdk\dotnet"
)

# UTF-8 编码设置 (防止中文乱码)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

$ErrorActionPreference = "Stop"

$sdkVersion = "10.0.204"
$dotnetExe = Join-Path $SdkDir "dotnet.exe"
$toolsDir = "Agent.Host\sdk\tools"
$aria2c = Join-Path $toolsDir "aria2c.exe"
$szExe = Join-Path $toolsDir "7z.exe"

# 已安装则跳过
if (Test-Path $dotnetExe) {
    Write-Host "  [OK] Bundled .NET SDK ready" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "  [SDK] Bundled .NET SDK not found" -ForegroundColor Yellow
Write-Host "  [SDK] Downloading .NET SDK v$sdkVersion..." -ForegroundColor Cyan
Write-Host ""

# ── 1. 引导下载工具 (aria2c + 7z) ─────────────────────────────────────────
function Ensure-Tool {
    param([string]$ToolPath, [string]$Url, [string]$Name)
    if (Test-Path $ToolPath) { return $true }
    Write-Host "  [TOOL] Downloading $Name..." -ForegroundColor Gray
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $Url -OutFile $ToolPath -UseBasicParsing -TimeoutSec 60
        if (Test-Path $ToolPath) {
            Write-Host "  [TOOL] $Name ready" -ForegroundColor Green
            return $true
        }
    } catch {
        Write-Host "  [TOOL] $Name download failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    return $false
}

New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null

$hasAria2c = Ensure-Tool -ToolPath $aria2c -Url "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip" -Name "aria2c"
$has7z = (Ensure-Tool -ToolPath $szExe -Url "https://www.7-zip.org/a/7zr.exe" -Name "7z.exe") -and
         (Ensure-Tool -ToolPath (Join-Path $toolsDir "7z.dll") -Url "https://www.7-zip.org/a/7z2409-x64.exe" -Name "7z.dll")

# ── 2. 下载 SDK ────────────────────────────────────────────────────────────
$mirrors = @(
    "https://dotnetcli.azureedge.net/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip",
    "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
)

$tempZip = Join-Path $env:TEMP "dotnet-sdk-$sdkVersion.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue }

$downloaded = $false

if ($hasAria2c) {
    # aria2c 多线程下载 (16 连接, 自动重试)
    Write-Host "  [SDK] Using aria2c (16 connections)..." -ForegroundColor Gray
    $urlFile = Join-Path $env:TEMP "dotnet-sdk-urls.txt"
    ($mirrors -join "`n") | Out-File -FilePath $urlFile -Encoding ascii -Force

    & $aria2c `
        --input-file=$urlFile `
        --dir=$env:TEMP `
        --out="dotnet-sdk-$sdkVersion.zip" `
        --max-connection-per-server=16 `
        --split=16 `
        --min-split-size=1M `
        --continue=true `
        --auto-file-renaming=false `
        --allow-overwrite=true `
        --max-tries=3 `
        --retry-wait=3 `
        --timeout=60 `
        --connect-timeout=15 `
        --console-log-level=notice `
        --summary-interval=0

    Remove-Item $urlFile -Force -ErrorAction SilentlyContinue

    if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 10MB) {
        $downloaded = $true
        Write-Host "  [SDK] Download complete: $([math]::Round((Get-Item $tempZip).Length / 1MB, 1)) MB" -ForegroundColor Green
    }
}

if (-not $downloaded) {
    # 回退: Invoke-WebRequest 逐源尝试
    Write-Host "  [SDK] Falling back to Invoke-WebRequest..." -ForegroundColor Gray
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    foreach ($url in $mirrors) {
        $hostName = ([Uri]$url).Host
        Write-Host "  [SDK] Trying $hostName..." -ForegroundColor Gray
        try {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $url -OutFile $tempZip -UseBasicParsing -TimeoutSec 300
            if ((Test-Path $tempZip) -and (Get-Item $tempZip).Length -gt 10MB) {
                $downloaded = $true
                Write-Host "  [SDK] Download complete: $([math]::Round((Get-Item $tempZip).Length / 1MB, 1)) MB" -ForegroundColor Green
                break
            }
        } catch {
            Write-Host "  [SDK] Failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

if (-not $downloaded) {
    Write-Host ""
    Write-Host "  [ERROR] All download sources failed." -ForegroundColor Red
    Write-Host "  [INFO]  Manual download: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Yellow
    exit 1
}

# ── 3. 解压 SDK ────────────────────────────────────────────────────────────
Write-Host "  [SDK] Extracting..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $SdkDir -Force | Out-Null

if ($has7z) {
    # 7z 快速解压
    & $szExe x $tempZip -o"$SdkDir" -y | Out-Null
} else {
    # 回退: Expand-Archive
    Expand-Archive -Path $tempZip -DestinationPath $SdkDir -Force
}

Remove-Item $tempZip -Force -ErrorAction SilentlyContinue

# 验证
if (Test-Path $dotnetExe) {
    Write-Host "  [OK] Bundled .NET SDK v$sdkVersion ready" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] SDK extraction failed" -ForegroundColor Red
    exit 1
}
