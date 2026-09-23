using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace NovaOptimizer.Services
{
    public enum BoostProfile
    {
        None,
        GameMode,
        WorkMode,
        StudyMode
    }

    public class TurboBoostService
    {
        private readonly RamOptimizerService _ramOptimizer;

        // Background services to pause during Game Mode to stop stutter & micro-hiccups
        private static readonly string[] GameModeServices =
        [
            "DiagTrack",   // Connected User Experiences and Telemetry
            "SysMain",     // SuperFetch (causes disk thrashing during game asset loads)
            "WSearch",     // Windows Search Indexer
            "WerSvc",      // Windows Error Reporting Service
            "MapsBroker",  // Downloaded Maps Manager
            "PcaSvc",      // Program Compatibility Assistant Service
            "DPS"          // Diagnostic Policy Service
        ];

        // Services to suppress in Work / Productivity Mode
        private static readonly string[] WorkModeServices =
        [
            "DiagTrack",
            "WerSvc",
            "MapsBroker",
            "XblAuthManager",
            "XboxGipSvc",
            "XboxNetApiSvc"
        ];

        // Services to suppress in Study / Sustained Focus Mode
        private static readonly string[] StudyModeServices =
        [
            "DiagTrack",
            "WerSvc",
            "MapsBroker",
            "XblAuthManager",
            "XboxGipSvc",
            "XboxNetApiSvc",
            "SysMain"
        ];

        // Idle background updater workers to terminate
        private static readonly string[] BloatProcessNames =
        [
            "AdobeUpdateService", "GoogleUpdate", "MicrosoftEdgeUpdate",
            "OneDrive", "GameBarFTServer", "Cortana",
            "KillerTray", "KillerNetworkService", "esrv", "esrv_svc", "SurSvc"
        ];

        // Distractions to terminate during Study / Focus mode
        private static readonly string[] DistractionProcessNames =
        [
            "Discord", "Spotify", "Steam", "steamwebhelper",
            "EpicGamesLauncher", "GameBarFTServer", "Teams", "ms-teams"
        ];

        public BoostProfile ActiveProfile { get; private set; } = BoostProfile.None;
        private readonly List<string> _temporarilyStoppedServices = new();

        public event Action<BoostProfile>? OnProfileChanged;
        public event Action<string>? OnLogMessage;

        public TurboBoostService(RamOptimizerService ramOptimizer)
        {
            _ramOptimizer = ramOptimizer;
        }

        public async Task<bool> ActivateBoostAsync(BoostProfile profile)
        {
            if (profile == BoostProfile.None)
            {
                return await DeactivateBoostAsync();
            }

            ActiveProfile = profile;
            OnProfileChanged?.Invoke(profile);
            OnLogMessage?.Invoke($"Activating {profile}...");

            await Task.Run(() =>
            {
                // 1. Suspend non-critical background services
                string[] targetServices = profile switch
                {
                    BoostProfile.GameMode => GameModeServices,
                    BoostProfile.WorkMode => WorkModeServices,
                    BoostProfile.StudyMode => StudyModeServices,
                    _ => Array.Empty<string>()
                };

                _temporarilyStoppedServices.Clear();

                foreach (var svcName in targetServices)
                {
                    try
                    {
                        using var sc = new ServiceController(svcName);
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            _temporarilyStoppedServices.Add(svcName);
                            sc.Stop();
                            try
                            {
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(2));
                            }
                            catch (System.ServiceProcess.TimeoutException)
                            {
                                // Service stop requested; it will be restored upon Deactivate
                            }
                            OnLogMessage?.Invoke($"⏸️ Paused background service: {svcName}");
                        }
                    }
                    catch
                    {
                        // Service might not exist or need elevation
                    }
                }

                // 2. Terminate idle background updater bloatware and distractions
                int killedCount = 0;
                var targetsToKill = new List<string>(BloatProcessNames);
                if (profile == BoostProfile.StudyMode)
                {
                    targetsToKill.AddRange(DistractionProcessNames);
                }

                foreach (var procName in targetsToKill)
                {
                    try
                    {
                        var procs = Process.GetProcessesByName(procName);
                        foreach (var p in procs)
                        {
                            p.Kill();
                            p.Dispose();
                            killedCount++;
                        }
                    }
                    catch { }
                }
                if (killedCount > 0)
                {
                    string label = profile == BoostProfile.StudyMode 
                        ? "idle updaters & distraction processes" 
                        : "idle background updater workers";
                    OnLogMessage?.Invoke($"🧹 Terminated {killedCount} {label}");
                }

                // 3. Heavy RAM Deep Clean
                long freedBytes = _ramOptimizer.DeepCleanRam();
                double freedMB = (double)freedBytes / (1024 * 1024);
                OnLogMessage?.Invoke($"🚀 Deep RAM Purge complete: {freedMB:F1} MB recovered!");
            });

            return true;
        }

        public async Task<bool> DeactivateBoostAsync()
        {
            if (ActiveProfile == BoostProfile.None) return true;

            OnLogMessage?.Invoke("Restoring standard system configuration...");

            await Task.Run(() =>
            {
                // 1. Restore services
                foreach (var svcName in _temporarilyStoppedServices)
                {
                    try
                    {
                        using var sc = new ServiceController(svcName);
                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(2));
                            OnLogMessage?.Invoke($"▶️ Restored service: {svcName}");
                        }
                    }
                    catch { }
                }
                _temporarilyStoppedServices.Clear();
            });

            ActiveProfile = BoostProfile.None;
            OnProfileChanged?.Invoke(BoostProfile.None);
            OnLogMessage?.Invoke("Standard mode restored.");
            return true;
        }
    }
}
