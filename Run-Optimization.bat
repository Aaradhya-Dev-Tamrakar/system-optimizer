@echo off
title NovaOptimizer - Service Optimization
cd /d "%~dp0"

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Administrator rights required.
    echo Right-click this file and select "Run as administrator".
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0optimize-services.ps1"

