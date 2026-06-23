@echo off
chcp 65001 >nul
REM ============================================================================
REM  Ignorant Vega — 一键启动脚本
REM  双击即可启动 Agent 服务
REM  作者：Ignorant Star BeidouAgent
REM  支持两种模式:
REM    1. 发布模式: 直接运行 Agent.Host/Agent.Host.dll (推荐)
REM    2. 开发模式: dotnet run --project src/Agent.Host/Agent.Host.csproj
REM ============================================================================

title Ignorant Vega Agent

echo.
echo  ========================================
echo    Ignorant Vega (Agent)
echo    Windows PC Manager + AI Assistant
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

echo [START] Starting Agent service...
echo [URL] http://localhost:7300
echo [CONFIG] Edit data/config.json to set API Key
echo [INFO] Press Ctrl+C to stop
echo.

REM 发布模式: 直接运行 DLL
if exist "Agent.Host\Agent.Host.dll" (
    %DOTNET_CMD% Agent.Host\Agent.Host.dll
) else if exist "src\Agent.Host\Agent.Host.csproj" (
    REM 开发模式: dotnet run
    %DOTNET_CMD% run --project src\Agent.Host\Agent.Host.csproj
) else (
    echo [ERROR] Agent.Host.dll not found.
    pause
    exit /b 1
)

pause
