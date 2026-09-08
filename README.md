<p align="center">
  <img src="Assets/icon.png" width="128" height="128" alt="NovaOptimizer Logo" />
</p>

# 🌌 NovaOptimizer — High-Efficiency Windows Task Manager & System Optimizer

> Built with **C# / .NET 10** and **WPF** for Windows 10 & 11. Engineered for zero overhead, instant response, and maximum system optimization before gaming or intensive work.

---

## ⚡ What is NovaOptimizer?

NovaOptimizer is a native, ultra-lightweight Windows Task Manager and System Optimizer. Rather than being a heavy electron or browser-based utility that consumes hundreds of megabytes of your memory, NovaOptimizer runs on a micro-footprint (~25 MB RAM), launches in milliseconds, and directly manipulates native Windows NT kernel APIs to unlock maximum performance from your hardware.

---

## 🚀 Key Features

### 1. 🧹 Deep Physical RAM Purge (ISLC & Sysinternals RAMMap Level)
Standard task managers can only kill processes. NovaOptimizer interfaces with the Windows NT memory manager:
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
  - **Windows Power Plan Unconstraining**: Engages the High Performance or Ultimate Performance power scheme to prevent aggressive CPU core parking and downclocking.
  - **One-Click Restoration**: Restores all original services and previous power scheme upon deactivating boost.
- **💼 Work / Dev Mode**:
  - Trims memory on idle apps and dev tools, devotes maximum physical RAM to IDEs (Visual Studio, VS Code, JetBrains, Docker) and viewport suites (Blender, Premiere, Unreal).
  - Pauses unnecessary background telemetry and Xbox subsystems.
- **📚 Study Mode (Sustained Focus & Battery Quiet Profile)**:
  - **Distraction Killer**: Terminating intrusive entertainment, streaming, and chatting apps (`Discord`, `Spotify`, `Steam`, `EpicGamesLauncher`, `Teams`).
  - **Cool & Quiet Operation**: Switches Windows power scheme to **Balanced**, keeping CPU temperatures low and reducing fan noise during long library and desk study sessions.
  - **Telemetry & SuperFetch Suppressed**: Disables background indexing and reporting churn.
  - **Built-in Pomodoro Focus Timer**: Integrated directly into the card with configurable Focus and Break lengths (e.g. 25m/5m), audio cue notifications, session iteration tracking, and one-click play/pause/reset.

### 3. 📋 Real-Time Task Manager
- Live process table with PID, Process Name, Working Set (RAM in MB), CPU %, Status, Priority Class, and Description.
- Search and filter by Process Name, PID, or Description.
- Context Menu Controls:
  - **End Task** (graceful kill)
  - **Kill Process Tree** (forceful `/F /T` termination of parent and all children)
  - **Trim Working Set** (reclaim RAM from an individual process on demand)
  - **Set Priority Class** (Realtime, High, Above Normal, Normal, Below Normal, Idle)
  - **Open File Location** (reveals process binary in Windows File Explorer)

### 4. 📊 Performance & Architecture Dashboard
- Real-time CPU % and RAM % utilization gauges and progress meters.
- Physical Memory breakdown: In-Use, Standby Cache, and Free Unallocated RAM.
- Windows Memory Architecture details: Committed / Pagefile limit, Paged Pool, Non-Paged Pool (Hardware drivers), Total Processes, System Uptime, and Processor Specs.

### 5. 🚀 Startup Apps & Windows Debloat Tweaks
- **Startup Apps Auditor**: Inspects Registry (`HKCU` & `HKLM` `Software\Microsoft\Windows\CurrentVersion\Run`) and Windows Startup folders. Allows removing autorun bloat to accelerate Windows boot times.
- **System Performance Tweaks**:
  - **Disable Game DVR / Background Captures**: Stops Windows from background-recording 3D viewports, fixing input latency.
  - **Disable Network Throttling Index**: Sets `NetworkThrottlingIndex = 0xFFFFFFFF` to eliminate network latency packet caps.
  - **Foreground Task Responsiveness**: Allocates 100% processing priority to foreground tasks instead of Windows reserving 20% for background apps.
  - **Disable Telemetry**: Disables Microsoft diagnostic data uploads.

### 6. 🔍 Hung Process Watchdog & Event Log
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
│   └── NativeMethods.cs       # P/Invoke kernel32, ntdll, psapi, advapi32, user32, token privileges
├── Models/
│   ├── HungProcessRecord.cs   # Data model for frozen application events & durations
│   ├── MemoryMetrics.cs       # Physical RAM, Standby, Free, Commit metrics
│   ├── ProcessItem.cs         # Data model for Task Manager process table
│   ├── StartupItem.cs         # Startup app registry & folder model
│   └── TweakItem.cs           # System debloat & performance tweak model
├── Services/
│   ├── HungProcessWatchdogService.cs # IsHungAppWindow polling watchdog & event dispatch
│   ├── RamOptimizerService.cs # Working Set trimmer, NT Standby cleaner, Watchdog
│   ├── ProcessMonitorService.cs # Process enumerator, CPU% calculator, tree killer
│   ├── TurboBoostService.cs   # Game/Work/Study boost manager, service suspension, power plan
│   ├── StartupManagerService.cs # Startup items inspector & remover
│   └── SystemTweakService.cs  # Windows Registry latency & debloat tweaks
└── Views/
    ├── HungLogView.xaml       # Real-time event log for hung/frozen process incidents
    ├── TurboBoostView.xaml    # Boost profiles (Game, Work, Study + Pomodoro), RAM breakdown bar
    ├── ProcessesView.xaml     # Task Manager process grid with search & context menu
    ├── PerformanceView.xaml   # Real-time resource meters & kernel pool specs
    └── StartupAndTweaksView.xaml # Startup manager & performance tweak switches
```

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
