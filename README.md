<p align="center">
  <img src="Assets/icon.png" width="128" height="128" alt="NovaOptimizer Logo" />
</p>

# 🌌 NovaOptimizer — High-Efficiency Windows System Optimizer

> Built with **C# / .NET 10** and **WPF** for Windows 10 & 11. Engineered for zero overhead, instant response, and maximum system optimization before gaming or intensive work.

---

## ⚡ What is NovaOptimizer?

NovaOptimizer is a native, ultra-lightweight Windows System Optimizer designed strictly to complement—not duplicate—default factory Windows and OEM tools. Rather than cloning built-in utilities like Windows Task Manager, Windows Clock Focus Sessions, or OEM thermal utilities (like Acer Sense), NovaOptimizer focuses exclusively on advanced NT kernel optimizations that default factory apps cannot perform.

NovaOptimizer runs with a micro-footprint (~25 MB RAM), launches in milliseconds, and directly manipulates native Windows NT kernel APIs to unlock maximum performance from your hardware.

---

## 🚀 Core Features (Retained & Optimized)

### 1. 🧹 Deep Physical RAM Purge (ISLC & Sysinternals RAMMap Level)
Standard tools only terminate processes. NovaOptimizer directly interfaces with the Windows NT memory manager:
- **Process Working Set Trimming**: Iterates active user and background processes (whitelisting protected system processes) and flushes dormant memory pages using `EmptyWorkingSet` and `SetProcessWorkingSetSize(-1, -1)`.
- **Kernel Standby List Purging**: Invokes undocumented kernel calls via `NtSetSystemInformation(SystemMemoryListInformation = 80)` with commands `MemoryPurgeStandbyList (4)` and `MemoryPurgeLowPriorityStandbyList (5)`. This instantly cleans gigabytes of cached standby RAM that cause micro-stutters and frame drops in modern games.
- **System Working Set Purge**: Flushes system-level cache using `MemoryEmptyWorkingSets (2)`.
- **Intelligent Auto-RAM Watchdog**: Runs silently in the background every 15 seconds. If In-Use RAM exceeds 85% or Standby cache exceeds 3.0 GB, it triggers a silent purge without interrupting your workflow.

### 2. 🎮 Turbo Boost Engine (Game, Work & Study Profiles)
- **🎮 Game Mode**:
  - **Background Service Suppressor**: Temporarily pauses services known to cause micro-stuttering, disk thrashing, or background telemetry:
    - `DiagTrack` (Connected User Experiences and Telemetry)
    - `SysMain` (SuperFetch / disk churn during level loading)
    - `WSearch` (Windows Search Indexer)
    - `WerSvc` (Windows Error Reporting)
    - `MapsBroker` (Downloaded Maps Manager)
    - `PcaSvc` (Program Compatibility Assistant)
    - `DPS` (Diagnostic Policy Service)
  - **Bloat Worker Killer**: Automatically terminates idle background updater agents (`AdobeUpdateService`, `GoogleUpdate`, `MicrosoftEdgeUpdate`, `OneDrive` background sync, `Cortana`, `GameBarFTServer`).
  - **Windows Power Plan Unconstraining**: Engages the High Performance power scheme to prevent aggressive CPU core parking and downclocking.
  - **One-Click Restoration**: Restores all original services and previous power scheme upon deactivating boost.
- **💼 Work / Dev Mode**:
  - Trims memory on idle apps and dev tools, devotes maximum physical RAM to IDEs (Visual Studio, VS Code, JetBrains, Docker) and creative viewport suites.
  - Pauses unnecessary background telemetry and Xbox subsystems.
- **📚 Study Mode**:
  - **Distraction Killer**: Terminates intrusive entertainment, streaming, and chatting apps (`Discord`, `Spotify`, `Steam`, `EpicGamesLauncher`, `Teams`).
  - **Cool & Quiet Operation**: Switches Windows power scheme to **Balanced**, keeping CPU temperatures low and reducing fan noise during long library and desk study sessions.
  - **Telemetry & SuperFetch Suppressed**: Disables background indexing and reporting churn.

### 3. ⚡ CPU Cooldown & Background Hog Tamer
- Real-time CPU load meter with adaptive threshold status.
- **Intelligent Hog Detection**: Continuously monitors top background CPU consumers (excluding foreground applications and protected OS processes).
- **EcoQoS Efficiency Mode**: Automatically or manually throttles rogue background hogs using Windows EcoQoS (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`) and Idle priority.

### 4. ⚙️ System Latency & Debloat Registry Tweaks
- **Nova Auto-Start**: Launches NovaOptimizer minimized to the system notification tray at logon with highest privileges to maintain standby memory cleaning silently.
- **System Performance Tweaks**:
  - **Disable Game DVR / Background Captures**: Stops Windows from background-recording 3D viewports, fixing input latency.
  - **Disable Network Throttling Index**: Sets `NetworkThrottlingIndex = 0xFFFFFFFF` to eliminate network latency packet caps.
  - **Foreground Task Responsiveness**: Allocates 100% processing priority to foreground tasks instead of Windows reserving 20% for background apps.
  - **Disable Telemetry**: Disables Microsoft diagnostic data uploads.

### 5. 🔍 Hung Process Watchdog & Event Log
- Background monitor tracking non-responsive (frozen / "Not Responding") desktop applications using Win32 `IsHungAppWindow`.
- Logs timestamped hung events, recovery durations, and crash/force-kill actions.
- Real-time toast notifications alerting users when processes freeze or recover.

---

## 🛠️ How to Run

### Option 1: Run Pre-Built Executable (Recommended)
The ready-to-run optimized executable is located in:
```powershell
publish\NovaOptimizer.exe
```
> **Note**: Right-click `NovaOptimizer.exe` and select **Run as administrator** (or accept the UAC prompt) to enable full NT kernel memory purging and Windows service suppression.

### Option 2: Run via .NET CLI
```powershell
dotnet run -c Release
```

### Option 3: Build & Publish
```powershell
dotnet publish -c Release -r win-x64 --no-self-contained -o ./publish
```

---

## 📁 Project Structure

```
system-optimizer/
├── App.xaml                   # Global WPF resource dictionary, fluent dark theme
├── App.xaml.cs                # Application entry point
├── app.manifest               # Administrator elevation & PerMonitorV2 DPI manifest
├── MainWindow.xaml            # Main shell with sidebar navigation & top status bar
├── MainWindow.xaml.cs         # Navigation controller & notification engine
├── NovaOptimizer.csproj       # Project configuration (.NET 10 WPF Windows)
├── Native/
│   ├── IconHelper.cs          # Icon extractor for application executables
│   ├── NativeMethods.cs       # P/Invoke kernel32, ntdll, psapi, advapi32, user32, token privileges
│   └── TrayIconManager.cs     # Notification system tray icon and background manager
├── Models/
│   ├── HungProcessRecord.cs   # Data model for frozen application events & durations
│   ├── MemoryMetrics.cs       # Physical RAM, Standby, Free, Commit metrics
│   └── TweakItem.cs           # System debloat & performance tweak model
├── Services/
│   ├── CpuOptimizerService.cs # Background CPU hog detector & EcoQoS tamer
│   ├── HungProcessWatchdogService.cs # IsHungAppWindow polling watchdog & event dispatch
│   ├── RamOptimizerService.cs # Working Set trimmer, NT Standby cleaner, Watchdog
│   ├── StartupManagerService.cs # NovaOptimizer logon autostart manager
│   ├── SystemTweakService.cs  # Windows Registry latency & debloat tweaks
│   └── TurboBoostService.cs   # Game/Work/Study boost manager, service suspension, power plan
└── Views/
    ├── HungLogView.xaml       # Real-time event log for hung/frozen process incidents
    ├── StartupAndTweaksView.xaml # Safe registry performance tweaks & auto-start toggle
    └── TurboBoostView.xaml    # Boost profiles, RAM breakdown bar, CPU Cooldown & Hog Tamer
```

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
