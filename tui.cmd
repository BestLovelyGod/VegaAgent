@echo off
chcp 65001 >nul
REM ============================================================================
REM  Ignorant Vega TUI — 终端界面启动脚本
REM  双击即可启动 TUI (需先启动 Agent Host)
REM  作者：Ignorant Star BeidouAgent
REM ============================================================================

title Ignorant Vega TUI

echo.
echo  ========================================
echo    Ignorant Vega TUI
echo    Terminal Interface
echo  ========================================
echo.

REM 切换到脚本所在目录
cd /d "%~dp0"

REM 使用内置 SDK (首次运行自动下载)
set DOTNET_CMD=Agent.Host\sdk\dotnet\dotnet.exe
if not exist "%DOTNET_CMD%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File bootstrap-sdk.ps1
    if not exist "%DOTNET_CMD%" (
        echo [ERROR] SDK download failed.
        pause
        exit /b 1
    )
)

REM 默认连接地址
if "%AGENT_URL%"=="" set AGENT_URL=http://localhost:7300

echo [CONNECT] Agent Host: %AGENT_URL%
echo [INFO] Make sure Agent Host is running (start.cmd)
echo.

REM 发布模式: 直接运行 DLL
if exist "Agent.TUI\Agent.TUI.dll" (
    %DOTNET_CMD% Agent.TUI\Agent.TUI.dll %AGENT_URL%
) else if exist "src\Agent.TUI\Agent.TUI.csproj" (
    REM 开发模式: dotnet run
    %DOTNET_CMD% run --project src\Agent.TUI\Agent.TUI.csproj -- %AGENT_URL%
) else (
    echo [ERROR] Agent.TUI.dll not found.
    pause
    exit /b 1
)

pause
