@echo off
REM Cross-platform setup entry point for Windows (batch file wrapper)
REM This file detects Windows and runs the PowerShell setup script
REM Usage: setup.bat or double-click from File Explorer

setlocal enabledelayedexpansion

echo [setup] Windows setup starting...

REM Check if PowerShell is available
where powershell >nul 2>&1
if errorlevel 1 (
    echo [error] PowerShell not found. Please install PowerShell 5.0+ or use WSL.
    exit /b 1
)

REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0
REM Remove trailing backslash
set SCRIPT_DIR=%SCRIPT_DIR:~0,-1%

REM Run the PowerShell setup script
echo [setup] Running PowerShell setup script...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\setup-env.ps1"

if errorlevel 1 (
    echo [error] Setup script failed with exit code %errorlevel%
    pause
    exit /b 1
) else (
    echo [setup] Setup completed successfully!
    pause
    exit /b 0
)
