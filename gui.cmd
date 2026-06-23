@echo off
chcp 65001 >nul
echo.
echo   无感 · 织女星 — 桌面控制台
echo.

REM 切换到脚本所在目录
cd /d "%~dp0"

REM ── 1. 检查内置 SDK ──
set DOTNET_CMD=Agent.Host\sdk\dotnet\dotnet.exe
if not exist "%DOTNET_CMD%" goto :no_sdk
goto :sdk_ok
:no_sdk
    echo [INFO] 首次运行，正在下载 .NET SDK...
    powershell -NoProfile -ExecutionPolicy Bypass -File bootstrap-sdk.ps1
    if not exist "%DOTNET_CMD%" (
        echo [WARN] SDK 引导脚本未成功，重试中...
        powershell -NoProfile -ExecutionPolicy Bypass -File bootstrap-sdk.ps1
    )
    if not exist "%DOTNET_CMD%" (
        echo [ERROR] SDK 下载失败，请检查网络连接
        pause
        exit /b 1
    )
    echo [INFO] SDK 下载完成
:sdk_ok

REM ── 2. 检查 WebView2 Runtime ──
powershell -NoProfile -ExecutionPolicy Bypass -File webview-bootstrap.ps1
if errorlevel 1 (
    echo [ERROR] WebView2 Runtime 安装失败
    pause
    exit /b 1
)

REM ── 3. 启动 GUI (管理员模式) ──
REM 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 goto :need_admin

REM 已是管理员，查找 GUI 可执行文件
set "GUI_DLL="
set "GUI_EXE="
if exist "Agent.GUI\Agent.GUI.dll" set "GUI_DLL=Agent.GUI\Agent.GUI.dll"
if exist "Agent.GUI\Agent.GUI.exe" set "GUI_EXE=Agent.GUI\Agent.GUI.exe"
if exist "publish\release\Agent.GUI\Agent.GUI.dll" set "GUI_DLL=publish\release\Agent.GUI\Agent.GUI.dll"
if exist "publish\release\Agent.GUI\Agent.GUI.exe" set "GUI_EXE=publish\release\Agent.GUI\Agent.GUI.exe"

if defined GUI_DLL (
    echo [START] 使用内置 SDK 启动 GUI...
    start "Vega GUI" /D "%~dp0" "%DOTNET_CMD%" "%GUI_DLL%"
    exit /b 0
)
if defined GUI_EXE (
    echo [START] 启动 GUI...
    start "Vega GUI" "%GUI_EXE%"
    exit /b 0
)
echo [ERROR] Agent.GUI 未找到，请先运行 publish.ps1
pause
exit /b 1

:need_admin
REM 非管理员，提权重启
echo [INFO] 请求管理员权限...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b 0
