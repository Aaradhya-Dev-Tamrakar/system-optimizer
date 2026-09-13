# =====================================================================
# NovaOptimizer - Windows Services Optimization Script
# =====================================================================

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[!] Administrator privileges are required to configure Windows services." -ForegroundColor Red
    Write-Host "    Please right-click 'Run-Optimization.bat' and select 'Run as administrator'." -ForegroundColor Yellow
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Starting Windows Background Services Optimization      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. KEEP / ENSURE AUTOMATIC
$autoServices = @(
    "chromoting"   # Chrome Remote Desktop (User explicitly requested Auto)
)

# 2. DISABLE (Pure Telemetry & Analytics)
$disableServices = @(
    "IntelCollectorService",        # Intel Collector Telemetry
    "IntelTelemetryAgent",          # Intel Telemetry Agent
    "Killer Analytics Service"      # Killer Analytics Telemetry
)

# 3. SET TO MANUAL (Heavy Background Updaters & OEM Bloat)
$manualServices = @(
    "DSAService",                   # Intel Driver & Support Assistant (~111MB RAM)
    "DSAUpdateService",             # Intel DSA Updater
    "Killer Provider Data Helper Service",
    "ESRV_SVC_QUEENCREEK",          # Energy Server Service
    "USER_ESRV_SVC_QUEENCREEK",
    "AcerCCAgentSvis",              # Acer Care Center
    "AcerEZSvc",                    # Acer Experience Zone
    "PresentMonSharedService",      # PresentMon frame capture
    "edgeupdate"                    # Edge background updater
)

function Update-ServiceConfig {
    param (
        [string]$ServiceName,
        [string]$TargetStartupType,
        [bool]$StopIfRunning = $true
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        $svc = Get-Service -Name "$ServiceName*" -ErrorAction SilentlyContinue | Select-Object -First 1
    }

    if ($svc) {
        try {
            Set-Service -Name $svc.Name -StartupType $TargetStartupType -ErrorAction Stop
            Write-Host ("[OK] {0,-35} -> Startup set to {1}" -f $svc.Name, $TargetStartupType) -ForegroundColor Green

            if ($StopIfRunning -and $svc.Status -eq 'Running' -and $TargetStartupType -ne 'Automatic') {
                Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue
                Write-Host ("     Stopped active process for {0}" -f $svc.Name) -ForegroundColor Yellow
            }
        }
        catch {
            Write-Host ("[FAIL] Could not update {0}: {1}" -f $svc.Name, $_.Exception.Message) -ForegroundColor Red
        }
    } else {
        Write-Host ("[SKIP] Service '{0}' not found on this system." -f $ServiceName) -ForegroundColor DarkGray
    }
}

Write-Host "`n[1/3] Ensuring Keep-Alive Services (Automatic)..." -ForegroundColor White
foreach ($s in $autoServices) {
    Update-ServiceConfig -ServiceName $s -TargetStartupType "Automatic" -StopIfRunning $false
}

Write-Host "`n[2/3] Disabling Telemetry & Tracking Services..." -ForegroundColor White
foreach ($s in $disableServices) {
    Update-ServiceConfig -ServiceName $s -TargetStartupType "Disabled" -StopIfRunning $true
}

Write-Host "`n[3/3] Setting OEM Bloat & Updaters to Manual..." -ForegroundColor White
foreach ($s in $manualServices) {
    Update-ServiceConfig -ServiceName $s -TargetStartupType "Manual" -StopIfRunning $true
}

# Handle dynamic Google Updater services if present
Get-Service -Name "GoogleUpdater*" -ErrorAction SilentlyContinue | ForEach-Object {
    Update-ServiceConfig -ServiceName $_.Name -TargetStartupType "Manual" -StopIfRunning $true
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   Optimization Complete! Unwanted autostarts disabled.   " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "`nPress any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
