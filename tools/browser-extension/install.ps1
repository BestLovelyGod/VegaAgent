# ============================================================================
# Ignorant Vega Browser Bridge — 安装脚本
# 用法: .\install.ps1
# ============================================================================

$ErrorActionPreference = "Stop"
$ExtensionDir = Join-Path $PSScriptRoot "extension"
$HostDir = Join-Path $PSScriptRoot "native-host"
$HostName = "com.ignorantvega.browser"

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Ignorant Vega Browser Bridge — 安装             ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── 1. 生成扩展图标 ──────────────────────────────────────────────────────
Write-Host "▶ [1/4] 生成扩展图标..." -ForegroundColor Yellow

# 创建简单的 SVG 图标并转换为 PNG（使用 .NET 内置功能）
$iconSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
  <circle cx="64" cy="64" r="60" fill="#1a73e8"/>
  <text x="64" y="80" text-anchor="middle" fill="white" font-size="60" font-family="Segoe UI">V</text>
</svg>
"@

# 使用 PowerShell 创建简单的 PNG 图标
$iconSizes = @(16, 48, 128)
foreach ($size in $iconSizes) {
    $iconPath = Join-Path $ExtensionDir "icons\icon${size}.png"
    if (-not (Test-Path $iconPath)) {
        # 创建一个简单的蓝色方块作为占位图标
        Add-Type -AssemblyName System.Drawing
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(26, 115, 232))
        $font = New-Object System.Drawing.Font("Segoe UI", [math]::Max($size * 0.5, 6))
        $brush = [System.Drawing.Brushes]::White
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
        $g.DrawString("V", $font, $brush, $rect, $sf)
        $bmp.Save($iconPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose()
        $bmp.Dispose()
        Write-Host "  ✓ icon${size}.png" -ForegroundColor Green
    } else {
        Write-Host "  ✓ icon${size}.png (已存在)" -ForegroundColor Green
    }
}

# ── 2. 注册 Native Messaging Host ──────────────────────────────────────
Write-Host ""
Write-Host "▶ [2/4] 注册 Native Messaging Host..." -ForegroundColor Yellow

# 创建 manifest
$hostExe = Join-Path $HostDir "NativeHost.exe"
$manifest = @{
    name = $HostName
    description = "Ignorant Vega Browser Bridge"
    path = $hostExe
    type = "stdio"
    allowed_origins = @("extension://*/")
} | ConvertTo-Json -Depth 5

$manifestPath = Join-Path $HostDir "${HostName}-manifest.json"
$manifest | Out-File -FilePath $manifestPath -Encoding UTF8
Write-Host "  ✓ 清单文件: $manifestPath" -ForegroundColor Green

# 注册到 Chrome 注册表
$chromeKey = "HKCU:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $chromeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name "(Default)" -Value $manifestPath
Write-Host "  ✓ Chrome 注册表已配置" -ForegroundColor Green

# 注册到 Edge 注册表
$edgeKey = "HKCU:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\$HostName"
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $edgeKey -Name "(Default)" -Value $manifestPath
Write-Host "  ✓ Edge 注册表已配置" -ForegroundColor Green

# ── 3. 检查 Edge 扩展加载 ──────────────────────────────────────────────
Write-Host ""
Write-Host "▶ [3/4] 安装 Edge 扩展..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  请手动加载扩展:" -ForegroundColor White
Write-Host "  1. 打开 Edge，访问 edge://extensions/" -ForegroundColor White
Write-Host "  2. 开启「开发者模式」" -ForegroundColor White
Write-Host "  3. 点击「加载解压缩的扩展」" -ForegroundColor White
Write-Host "  4. 选择目录: $ExtensionDir" -ForegroundColor White
Write-Host ""

# ── 4. 验证安装 ──────────────────────────────────────────────────────
Write-Host "▶ [4/4] 验证安装..." -ForegroundColor Yellow

$checks = @(
    @{ Name = "扩展目录"; Path = $ExtensionDir; OK = (Test-Path $ExtensionDir) },
    @{ Name = "manifest.json"; Path = (Join-Path $ExtensionDir "manifest.json"); OK = (Test-Path (Join-Path $ExtensionDir "manifest.json")) },
    @{ Name = "background.js"; Path = (Join-Path $ExtensionDir "background.js"); OK = (Test-Path (Join-Path $ExtensionDir "background.js")) },
    @{ Name = "content.js"; Path = (Join-Path $ExtensionDir "content.js"); OK = (Test-Path (Join-Path $ExtensionDir "content.js")) },
    @{ Name = "Chrome 注册表"; Path = $chromeKey; OK = (Test-Path $chromeKey) },
    @{ Name = "Edge 注册表"; Path = $edgeKey; OK = (Test-Path $edgeKey) }
)

$allOk = $true
foreach ($check in $checks) {
    if ($check.OK) {
        Write-Host "  ✅ $($check.Name)" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $($check.Name)" -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "══════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  安装完成！请在 Edge 中加载扩展。              " -ForegroundColor Green
    Write-Host "══════════════════════════════════════════════════" -ForegroundColor Green
} else {
    Write-Host "══════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "  部分组件安装失败，请检查上方错误。            " -ForegroundColor Yellow
    Write-Host "══════════════════════════════════════════════════" -ForegroundColor Yellow
}
Write-Host ""
