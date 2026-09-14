@echo off
setlocal
title ReignServer
if not exist "%~dp0Start-ReignServer.ps1" (
    echo ReignServer setup is required. Run the supplied Reign setup package.
    pause
    exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-ReignServer.ps1"
if errorlevel 1 (
    echo ReignServer could not start. See the message above or run setup to repair it.
    pause
    exit /b 1
)
exit /b 0
