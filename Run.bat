@echo off
title WinGovernor Launcher
cd /d "%~dp0"

echo [WinGovernor] Checking for project updates and building...
del /f /q "%~dp0bin\Release\net8.0-windows\*.bak" >nul 2>nul
if exist "%~dp0bin\Release\net8.0-windows\WinGovernor.exe" ren "%~dp0bin\Release\net8.0-windows\WinGovernor.exe" WinGovernor.exe.bak >nul 2>nul
if exist "%~dp0bin\Release\net8.0-windows\WinGovernor.dll" ren "%~dp0bin\Release\net8.0-windows\WinGovernor.dll" WinGovernor.dll.bak >nul 2>nul
dotnet build -c Release --nologo -v q
if %errorlevel% neq 0 (
    echo [ERROR] Build failed. Press any key to exit.
    pause
    exit /b %errorlevel%
)

if exist "%~dp0bin\Release\net8.0-windows\WinGovernor.exe" (
    start "" "%~dp0bin\Release\net8.0-windows\WinGovernor.exe" %*
) else if exist "%~dp0bin\Publish\win-x64\WinGovernor.exe" (
    start "" "%~dp0bin\Publish\win-x64\WinGovernor.exe" %*
) else (
    start "" "%~dp0bin\Release\net10.0-windows\WinGovernor.exe" %*
)
exit /b 0
