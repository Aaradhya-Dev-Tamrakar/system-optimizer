@echo off
title NovaOptimizer Launcher
cd /d "%~dp0"

echo [NovaOptimizer] Checking for project updates and building...
dotnet build -c Release --nologo -v q
if %errorlevel% neq 0 (
    echo [ERROR] Build failed. Press any key to exit.
    pause
    exit /b %errorlevel%
)

if exist "%~dp0bin\Release\net8.0-windows\NovaOptimizer.exe" (
    start "" "%~dp0bin\Release\net8.0-windows\NovaOptimizer.exe" %*
) else if exist "%~dp0bin\Publish\win-x64\NovaOptimizer.exe" (
    start "" "%~dp0bin\Publish\win-x64\NovaOptimizer.exe" %*
) else (
    start "" "%~dp0bin\Release\net10.0-windows\NovaOptimizer.exe" %*
)
exit /b 0
