# System Diagnostics & Optimization Log

**Date:** September 12, 2026  
**Target Machine:** Acer Swift SFG16-72 (Intel Core Ultra 7 155H, 16 Cores / 22 Threads, 16 GB LPDDR5x)  
**Display:** 16-inch 3.2K OLED 120Hz (16:10)  
**GPU:** Intel(R) Arc(TM) Graphics (Xe-LPG, Driver 32.0.101.8991)

---

## 1. Process & High-CPU Troubleshooting (`dllhost.exe`)
* **Incident:** `dllhost.exe` (PID 4972, COM Surrogate) was consuming >300 seconds of CPU time, causing continuous high fan noise.
* **Resolution:** Terminated via Winaero Tweaker context menu shortcut `Kill not responding tasks` (`taskkill /f /fi "status eq not responding"`).
* **Root Cause Analysis:** A hung/deadlocked media thumbnail decoder or shell extension inside COM Surrogate. Explained mechanics of thumbnail generation, cache corruption, and isolation from `explorer.exe`.

---

## 2. Storage & File System Audit
* **Storage Sense:** Audited current configuration. Confirmed `Automatic User content cleanup` is disabled to prevent cloud-dehydration of local OneDrive desktop files and unintended `Downloads` directory pruning.
* **SSD Optimization (TRIM):** Verified all partitions (C:, D:, E:, F: Dev Drive) are detected as Solid State Drives with native TRIM enabled (`DisableDeleteNotify = 0` on both NTFS and ReFS). Weekly scheduled re-trim is active.

---

## 3. OLED & Display Optimization
* **Desktop Profile:** Pure black `#000000` wallpaper + auto-hiding taskbar. 
  * Pixels physically power off (0 Watts).
  * Eliminates OLED burn-in risk from static taskbar elements.
  * Near-zero Desktop Window Manager (`dwm.exe`) rendering overhead.
* **Gaming Resolution:** Configured to `1680 x 1050 @ 120Hz`:
  * Matches native 16:10 panel aspect ratio perfectly (no stretching or letterboxing).
  * ~73% reduction in pixel rendering load compared to native 3.2K (6.4M -> 1.76M pixels), allowing high frame rates on Intel Arc integrated graphics with sub-millisecond OLED motion clarity.
* **Intel Graphics Software Tuning:**
  * `Frame Synchronization`: Smart VSync enabled.
  * `Low Latency Mode`: On + Boost.
  * `Color Depth`: 10 bits per color (1.07B colors, RGB Full Range).
  * `Panel Self Refresh`: On.
  * `Display Power Saving Technology (DPST)`: Calibrated to prevent contrast/gamma shifts on OLED.

---

## 4. Background Services & Startup Pruning
* **Startup Apps:** Disabled 19 resource-heavy background agents (Docker Desktop, Ollama, Steam, Discord, Opera GX Assistant, Canva Agent, Lively Wallpaper, etc.).
* **Boot Performance:** Achieved a clean **4.8s Last BIOS Time**.
* **AI Tooling & MCP Server Audit:** Verified background `node.exe` and `python.exe` processes belong to idle Antigravity/MCP plugins (`lm-studio`, `sequential-thinking`, `drawio`, `shadcn`, `filesystem`, `super-nlm`, `notebooklm`), all operating silently at 0.0% CPU.

---

## 5. Developer Environment Caches Relocation (Dev Drive `F:`)
* **npm Global Cache:** Relocated to ReFS Dev Drive:
  ```powershell
  npm config set cache F:\.npm-cache --global
  ```
* **Python (`uv` & `pip`) Caches:** Permanently configured in Windows User environment variables:
  ```powershell
  [System.Environment]::SetEnvironmentVariable('UV_CACHE_DIR', 'F:\.uv-cache', 'User')
  [System.Environment]::SetEnvironmentVariable('PIP_CACHE_DIR', 'F:\.pip-cache', 'User')
  ```
  Verified entries in `HKCU:\Environment`.

---

## 6. Battery Longevity
* Enabled Acer Care Center / Acer Sense **80% Battery Charge Limit** (Optimized Battery Protection) to prevent battery swelling and cell degradation while running connected to AC power.

---

## 7. Native Tooling De-Duplication & Redundancy Audit
* **De-Duplication Strategy:** Audited NovaOptimizer codebase against Windows 11 default factory applications and Acer OEM software (`Acer Sense`) to eliminate redundant feature overlap and maintain a zero-overhead footprint.
* **Pruned Redundancies:**
  * **Acer Hardware Fan Control:** Removed internal WMI ACPI fan profile hooks and UI controls. Hardware thermal curves, cooling mode transitions (Quiet, Balanced, Performance), and keyboard shortcuts are delegated exclusively to factory **Acer Sense**.
  * **Pomodoro Focus Timer:** Removed embedded countdown strip and timer loops from Turbo Boost, delegating focus sessions to the factory **Windows Clock ("Focus Sessions")** application (native Spotify integration, break alerts, and Do Not Disturb linking).
  * **Startup Apps Auditor:** Removed third-party startup program registry enumeration and deletion grid. Startup program impact and toggling remain handled by the default factory **Windows Task Manager ("Startup apps" tab)** and **Windows Settings**.
  * **Generic Task Manager & Performance Charts:** Removed standard process list and CPU/RAM history graphs, leaving general task monitoring to native **Windows Task Manager (`taskmgr.exe`)**.
* **Retained Core Differentiators:**
  * **NT Kernel Standby List & Working Set Purge** (`NtSetSystemInformation` + Auto-RAM Watchdog).
  * **Turbo Boost Service Suspension** (temporary pausing of `DiagTrack`, `SysMain`, `WSearch`, etc., with one-click restore).
  * **CPU Cooldown & Hog Tamer** (Windows EcoQoS / Efficiency Mode auto-tamer).
  * **Safe Windows Latency Tweaks** (Network Throttling Index, Game DVR, Foreground CPU Priority).
  * **Hung Process Watchdog & Event Log** (`IsHungAppWindow` detection and recovery logging).

