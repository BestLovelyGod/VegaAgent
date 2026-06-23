@echo off
chcp 65001 >nul
REM ============================================================================
REM  Vega 启动器 — 自动请求管理员权限运行
REM  双击即可，会自动弹出 UAC 确认框
REM  作者：Ignorant Star BeidouAgent
REM ============================================================================

REM ── 检查是否已是管理员 ──
net session >nul 2>&1
if %errorLevel% equ 0 goto :run

REM ── 非管理员，自动提权重启 ──
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList 'elevated'"
exit /b

:run
title Vega 启动器
cd /d "%~dp0"

REM ── 优先运行发布版 (自包含 EXE，无需 SDK) ──
set "LAUNCHER_EXE=Agent.Launcher\Agent.Launcher.exe"
if exist "%LAUNCHER_EXE%" (
    start "" "%LAUNCHER_EXE%"
    exit /b 0
)

REM ── 发布版不存在时，尝试开发模式 (需要 SDK) ──
set "DOTNET_CMD=Agent.Host\sdk\dotnet\dotnet.exe"
if not exist "%DOTNET_CMD%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File bootstrap-sdk.ps1
    if not exist "%DOTNET_CMD%" (
        echo [ERROR] 未找到启动器且 SDK 下载失败
        echo [INFO]  请先运行 publish.ps1 生成发布包
        pause
        exit /b 1
    )
)

if not exist "src\Agent.Launcher\Agent.Launcher.csproj" (
    echo [ERROR] Agent.Launcher 项目文件不存在
    pause
    exit /b 1
)

start "Vega 启动器" %DOTNET_CMD% run --project src\Agent.Launcher\Agent.Launcher.csproj
