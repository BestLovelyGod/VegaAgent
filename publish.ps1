# ============================================================================
#  Ignorant Vega — 发布打包脚本
#  用法: .\publish.ps1 [-Configuration Release] [-SelfContained] [-IncludeSdk] [-Version "1.0.0"]
#  作者：Ignorant Star BeidouAgent
# ============================================================================

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$SelfContained,
    
    [switch]$IncludeSdk,
    
    [string]$Version = "1.2.1",
    
    [string]$OutputDir = "publish"
)

# UTF-8 编码设置 (防止中文乱码)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 >$null

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot
$PublishRoot = Join-Path $SolutionDir $OutputDir

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║       Ignorant Vega — 发布打包工具                       ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  配置:      $Configuration"
Write-Host "  版本:      $Version"
Write-Host "  自包含:    $($SelfContained.IsPresent)"
Write-Host "  内置SDK:   $($IncludeSdk.IsPresent)"
Write-Host "  输出目录:  $PublishRoot"
Write-Host ""

# ── 0. 检查内置 SDK ──────────────────────────────────────────────────────
Write-Host "▶ [0/5] 检查 .NET SDK..." -ForegroundColor Yellow
$bundledSdk = Join-Path $SolutionDir "Agent.Host\sdk\dotnet\dotnet.exe"
if (-not (Test-Path $bundledSdk)) {
    Write-Host "  ⚠ 内置 SDK 未找到，尝试引导下载..." -ForegroundColor Yellow
    & (Join-Path $SolutionDir "bootstrap-sdk.ps1") -SdkDir (Join-Path $SolutionDir "Agent.Host\sdk\dotnet")
    if (-not (Test-Path $bundledSdk)) {
        throw "无法找到或下载 .NET SDK"
    }
}
$dotnetCmd = $bundledSdk
Write-Host "  ✓ .NET SDK: $dotnetCmd" -ForegroundColor Green
Write-Host ""

# ── 1. 清理旧构建产物 ──────────────────────────────────────────────────────
Write-Host "▶ [1/5] 清理旧构建产物..." -ForegroundColor Yellow
$cleanOk = $true
if (Test-Path $PublishRoot) {
    # 重试清理 (防止文件锁定导致残留)
    for ($retry = 1; $retry -le 3; $retry++) {
        try {
            Remove-Item -Recurse -Force $PublishRoot -ErrorAction Stop
            break
        } catch {
            if ($retry -lt 3) {
                Write-Host "  ⚠ 清理失败，${retry}s 后重试..." -ForegroundColor Yellow
                Start-Sleep -Seconds $retry
            } else {
                Write-Host "  ⚠ 无法完全清理 $PublishRoot (部分文件被锁定)，将覆盖更新" -ForegroundColor Yellow
                $cleanOk = $false
            }
        }
    }
    if ($cleanOk) { Write-Host "  ✓ 已清理 $PublishRoot" -ForegroundColor Green }
}

# 清理各项目的 bin/obj
Get-ChildItem -Path $SolutionDir -Filter "bin" -Directory -Recurse | ForEach-Object {
    Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue
}
Get-ChildItem -Path $SolutionDir -Filter "obj" -Directory -Recurse | ForEach-Object {
    Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue
}
Write-Host "  ✓ 已清理所有 bin/obj 目录" -ForegroundColor Green
Write-Host ""

# ── 2. 还原 NuGet 包 ─────────────────────────────────────────────────────
Write-Host "▶ [2/5] 还原 NuGet 包..." -ForegroundColor Yellow
& $dotnetCmd restore $SolutionDir/Agent.slnx --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "NuGet 还原失败" }
Write-Host "  ✓ NuGet 包还原完成" -ForegroundColor Green
Write-Host ""

# ── 3. 构建解决方案 ───────────────────────────────────────────────────────
Write-Host "▶ [3/5] 构建解决方案 ($Configuration)..." -ForegroundColor Yellow
& $dotnetCmd build $SolutionDir/Agent.slnx --configuration $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败" }
Write-Host "  ✓ 构建成功" -ForegroundColor Green
Write-Host ""

# ── 4. 发布各项目 ───────────────────────────────────────────────────────
Write-Host "▶ [4/5] 发布项目..." -ForegroundColor Yellow

$publishArgs = @("--configuration", $Configuration, "--verbosity", "minimal")
if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "-r"
    $publishArgs += "win-x64"
}

# 直接发布到 release/ 子目录 (避免中间产物导致目录嵌套)
$releaseDir = Join-Path $PublishRoot "release"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# 发布 Agent.Host (Web API)
$hostOut = Join-Path $releaseDir "Agent.Host"
Write-Host "  发布 Agent.Host..."
& $dotnetCmd publish $SolutionDir/src/Agent.Host/Agent.Host.csproj -o $hostOut @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Agent.Host 发布失败" }
Write-Host "  ✓ Agent.Host → $hostOut" -ForegroundColor Green

# 发布 Agent.TUI (终端界面)
$tuiOut = Join-Path $releaseDir "Agent.TUI"
Write-Host "  发布 Agent.TUI..."
& $dotnetCmd publish $SolutionDir/src/Agent.TUI/Agent.TUI.csproj -o $tuiOut @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Agent.TUI 发布失败" }
Write-Host "  ✓ Agent.TUI → $tuiOut" -ForegroundColor Green

# 发布 Agent.GUI (桌面界面)
$guiOut = Join-Path $releaseDir "Agent.GUI"
Write-Host "  发布 Agent.GUI..."
$guiArgs = @("--configuration", $Configuration, "--verbosity", "minimal")
if ($SelfContained) {
    $guiArgs += "--self-contained"
    $guiArgs += "-r"
    $guiArgs += "win-x64"
}
& $dotnetCmd publish $SolutionDir/src/Agent.GUI/Agent.GUI.csproj -o $guiOut @guiArgs
if ($LASTEXITCODE -ne 0) { throw "Agent.GUI 发布失败" }
Write-Host "  ✓ Agent.GUI → $guiOut" -ForegroundColor Green

# 发布 Agent.Launcher (启动器 — 始终自包含，独立运行无需 SDK)
$launcherOut = Join-Path $releaseDir "Agent.Launcher"
Write-Host "  发布 Agent.Launcher (自包含)..."
$launcherArgs = @("--configuration", $Configuration, "--verbosity", "minimal", "--self-contained", "-r", "win-x64")
& $dotnetCmd publish $SolutionDir/src/Agent.Launcher/Agent.Launcher.csproj -o $launcherOut @launcherArgs
if ($LASTEXITCODE -ne 0) { throw "Agent.Launcher 发布失败" }
Write-Host "  ✓ Agent.Launcher → $launcherOut (自包含)" -ForegroundColor Green

Write-Host ""

# ── 5. 打包发布产物 ─────────────────────────────────────────────────────
Write-Host "▶ [5/5] 打包发布产物..." -ForegroundColor Yellow

# (项目已直接发布到 release/ 子目录，无需复制)

# 复制配置目录 (使用 \* 复制内容而非目录本身，避免嵌套)
$releaseData = Join-Path $releaseDir "data"
New-Item -ItemType Directory -Path $releaseData -Force | Out-Null
Copy-Item -Path "$(Join-Path $SolutionDir 'data')\*" -Destination $releaseData -Recurse -Force

# 清除敏感信息 (API Key 等)
$configFile = Join-Path $releaseData "config.json"
if (Test-Path $configFile) {
    $config = Get-Content $configFile -Raw | ConvertFrom-Json
    $cleaned = $false
    if ($config.Agent.Llm.ApiKey) {
        $config.Agent.Llm.ApiKey = ""
        $cleaned = $true
    }
    if ($config.Agent.Llm.Endpoint) {
        $config.Agent.Llm.Endpoint = ""
        $cleaned = $true
    }
    if ($cleaned) {
        $config | ConvertTo-Json -Depth 10 | Out-File $configFile -Encoding UTF8
        Write-Host "  ✓ 已清除 config.json 中的 ApiKey 和 Endpoint" -ForegroundColor Green
    }
}

$llmConfigFile = Join-Path $releaseData "llm-config.json"
if (Test-Path $llmConfigFile) {
    $llmConfig = Get-Content $llmConfigFile -Raw | ConvertFrom-Json
    $cleaned = $false
    foreach ($provider in $llmConfig.providers.PSObject.Properties) {
        if ($provider.Value.apiKey) {
            $provider.Value.apiKey = ""
            $cleaned = $true
        }
    }
    if ($cleaned) {
        $llmConfig | ConvertTo-Json -Depth 10 | Out-File $llmConfigFile -Encoding UTF8
        Write-Host "  ✓ 已清除 llm-config.json 中的 ApiKey" -ForegroundColor Green
    }
}

# 清除会话数据 (防止测试数据泄漏到发布包)
$sessionsJson = Join-Path $releaseData "sessions.json"
if (Test-Path $sessionsJson) {
    Remove-Item $sessionsJson -Force
    Write-Host "  ✓ 已清除 sessions.json" -ForegroundColor Green
}
$sessionsDir = Join-Path $releaseData "sessions"
if (Test-Path $sessionsDir) {
    Remove-Item $sessionsDir -Recurse -Force
    Write-Host "  ✓ 已清除 sessions/ 目录" -ForegroundColor Green
}
# 清除日志 (旧日志不应打包)
$logsDir = Join-Path $releaseData "logs"
if (Test-Path $logsDir) {
    Remove-Item "$logsDir\*" -Force -ErrorAction SilentlyContinue
    Write-Host "  ✓ 已清除旧日志" -ForegroundColor Green
}

# 复制 tools 目录 (使用 \* 复制内容，避免嵌套)
$releaseTools = Join-Path $releaseDir "tools"
New-Item -ItemType Directory -Path $releaseTools -Force | Out-Null
Copy-Item -Path "$(Join-Path $SolutionDir 'tools')\*" -Destination $releaseTools -Recurse -Force
Write-Host "  ✓ 已复制 tools/" -ForegroundColor Green

# 复制 plugins 目录 (使用 \* 复制内容，避免嵌套)
$releasePlugins = Join-Path $releaseDir "plugins"
New-Item -ItemType Directory -Path $releasePlugins -Force | Out-Null
Copy-Item -Path "$(Join-Path $SolutionDir 'plugins')\*" -Destination $releasePlugins -Recurse -Force
Write-Host "  ✓ 已复制 plugins/" -ForegroundColor Green

# 复制 nssm.exe (服务管理工具)
$nssmSrc = Join-Path $SolutionDir "nssm.exe"
if (Test-Path $nssmSrc) {
    Copy-Item -Path $nssmSrc -Destination $releaseDir -Force
    Write-Host "  ✓ 已复制 nssm.exe" -ForegroundColor Green
}

# 复制内置工具 (aria2c + 7z，供 bootstrap 和 Agent 工具使用)
$releaseSdkTools = Join-Path $releaseDir "Agent.Host\sdk\tools"
$bundledTools = Join-Path $SolutionDir "Agent.Host\sdk\tools"
if (Test-Path $bundledTools) {
    New-Item -ItemType Directory -Path $releaseSdkTools -Force | Out-Null
    Copy-Item -Path "$bundledTools\*" -Destination $releaseSdkTools -Recurse -Force
    Write-Host "  ✓ 已复制 sdk/tools/ (aria2c + 7z)" -ForegroundColor Green
}

# 复制完整 SDK (企业离线部署包)
if ($IncludeSdk) {
    $sdkSource = Join-Path $SolutionDir "Agent.Host\sdk\dotnet"
    $sdkTarget = Join-Path $releaseDir "Agent.Host\sdk\dotnet"
    if (Test-Path $sdkSource) {
        New-Item -ItemType Directory -Path $sdkTarget -Force | Out-Null
        Copy-Item -Path "$sdkSource\*" -Destination $sdkTarget -Recurse -Force
        $sdkSize = [math]::Round((Get-ChildItem $sdkTarget -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 0)
        Write-Host "  ✓ 已复制完整 SDK ($sdkSize MB)" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ SDK 目录不存在: $sdkSource" -ForegroundColor Yellow
    }
}

# 复制启动脚本
Copy-Item -Path (Join-Path $SolutionDir "start.cmd") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "start.ps1") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "tui.cmd") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "tui.ps1") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "gui.cmd") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "gui.ps1") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "launcher.cmd") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "bootstrap-sdk.ps1") -Destination $releaseDir -Force
Copy-Item -Path (Join-Path $SolutionDir "webview-bootstrap.ps1") -Destination $releaseDir -Force

# 复制 README
Copy-Item -Path (Join-Path $SolutionDir "README.md") -Destination $releaseDir -Force

# 创建版本文件
@"
Ignorant Vega v$Version
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration
Self-Contained: $($SelfContained.IsPresent)
BundledSdk: $($IncludeSdk.IsPresent)
"@ | Out-File -FilePath (Join-Path $releaseDir "VERSION.txt") -Encoding UTF8

# 创建 ZIP 包 (使用 7z 高压缩率)
$zipPath = Join-Path $PublishRoot "IgnorantVega-v$Version-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
# 解除 Web 标记 (防止 Windows Defender / SmartScreen 拦截)
Write-Host "  解除 Web 标记..." -ForegroundColor Yellow
Get-ChildItem -Path $releaseDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
Write-Host "  ✓ 已解除所有文件的 Web 标记" -ForegroundColor Green
# 临时移除日志文件再打包
Get-ChildItem -Path $releaseDir -Filter "*.log" -File | Remove-Item -Force -ErrorAction SilentlyContinue
# 使用 7z 压缩 (比 Compress-Archive 快且压缩率更高)
$sevenZip = Join-Path $SolutionDir "Agent.Host\sdk\tools\7z.exe"
if (Test-Path $sevenZip) {
    & $sevenZip a -tzip -mx=9 $zipPath "$releaseDir\*" | Out-Null
    Write-Host "  ✓ 已使用 7z 压缩 (高压缩率)" -ForegroundColor Green
} else {
    Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "  ✓ 已使用 PowerShell 压缩 (7z 不可用)" -ForegroundColor Yellow
}

Write-Host "  ✓ 发布包已创建: $zipPath" -ForegroundColor Green
Write-Host ""

# ── 完成 ───────────────────────────────────────────────────────────────
$hostSize = [math]::Round((Get-ChildItem $hostOut -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$tuiSize = [math]::Round((Get-ChildItem $tuiOut -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$launcherSize = [math]::Round((Get-ChildItem $launcherOut -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "                    发布完成！                           " -ForegroundColor Green
Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "  发布产物:" -ForegroundColor White
Write-Host ("    Agent.Host:    {0}  ({1} MB)" -f $hostOut, $hostSize) -ForegroundColor White
Write-Host ("    Agent.TUI:     {0}  ({1} MB)" -f $tuiOut, $tuiSize) -ForegroundColor White
Write-Host ("    Agent.Launcher: {0}  ({1} MB)" -f $launcherOut, $launcherSize) -ForegroundColor White
if ($IncludeSdk) {
    Write-Host ("    Bundled SDK:   已内置 (离线部署)") -ForegroundColor Cyan
}
Write-Host ""
Write-Host "  压缩包:" -ForegroundColor White
Write-Host ("    {0}  ({1} MB)" -f $zipPath, $zipSize) -ForegroundColor Cyan
Write-Host ""
