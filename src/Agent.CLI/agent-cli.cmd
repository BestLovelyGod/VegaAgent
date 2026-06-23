@echo off
REM ============================================================================
REM Ignorant Agent CLI 快捷启动脚本
REM ============================================================================

REM 加载模块
powershell -NoProfile -Command "Import-Module '%~dp0Agent.CLI.psd1'; Get-AgentStatus"
