@echo off
title NovaOptimizer - Universal Release Publisher
cd /d "%~dp0"

echo ======================================================================
echo    NovaOptimizer — Universal Standalone Release Publisher
echo    Target: Windows 10 ^& Windows 11 (64-bit, Zero-Dependency)
echo ======================================================================
echo.
echo [*] Publishing self-contained single-file binary for win-x64...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%~dp0bin\Publish\win-x64"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Publish failed! Please ensure .NET SDK is installed.
    pause
    exit /b %errorlevel%
)

echo.
echo ======================================================================
echo  [SUCCESS] Universal Windows 10 / 11 executable ready:
echo  "%~dp0bin\Publish\win-x64\NovaOptimizer.exe"
echo ======================================================================
echo.
echo This standalone binary can be copied to ANY Windows 10 or 11 PC and run
echo without installing any .NET runtimes or extra dependencies.
echo.
pause
exit /b 0
