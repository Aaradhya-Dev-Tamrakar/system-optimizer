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
        WorkMode
    }

    public class TurboBoostService
    {
        private readonly RamOptimizerService _ramOptimizer;

        // Background services to pause during Game Mode to stop stutter & micro-hiccups
        private static readonly string[] GameModeServices = new[]
        {
            "DiagTrack",   // Connected User Experiences and Telemetry
            "SysMain",     // SuperFetch (causes disk thrashing during game asset loads)
            "WSearch",     // Windows Search Indexer
            "WerSvc",      // Windows Error Reporting Service
            "MapsBroker",  // Downloaded Maps Manager
            "PcaSvc",      // Program Compatibility Assistant Service
            "DPS"          // Diagnostic Policy Service
        };

        // Services to suppress in Work / Productivity Mode
        private static readonly string[] WorkModeServices = new[]
        {
            "DiagTrack",
            "WerSvc",
            "MapsBroker",
            "XblAuthManager",
            "XboxGipSvc",
            "XboxNetApiSvc"
        };

        // Idle background updater workers to terminate
        private static readonly string[] BloatProcessNames = new[]
        {
            "AdobeUpdateService", "GoogleUpdate", "MicrosoftEdgeUpdate",
            "OneDrive", "GameBarFTServer", "Cortana"
        };

        public BoostProfile ActiveProfile { get; private set; } = BoostProfile.None;
        private readonly List<string> _temporarilyStoppedServices = new();
        private string? _previousPowerPlanGuid;

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
                // 1. Record and switch power scheme to High Performance
                try
                {
                    _previousPowerPlanGuid = GetActivePowerPlan();
                    // 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c is High Performance
                    SetPowerPlan("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                    OnLogMessage?.Invoke("⚡ Windows Power Plan set to High Performance");
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke($"Power plan notice: {ex.Message}");
                }

                // 2. Suspend non-critical background services
                string[] targetServices = profile == BoostProfile.GameMode ? GameModeServices : WorkModeServices;
                _temporarilyStoppedServices.Clear();

                foreach (var svcName in targetServices)
                {
                    try
                    {
                        using var sc = new ServiceController(svcName);
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(2));
                            _temporarilyStoppedServices.Add(svcName);
                            OnLogMessage?.Invoke($"⏸️ Paused background service: {svcName}");
                        }
                    }
                    catch
                    {
                        // Service might not exist or need elevation
                    }
                }

                // 3. Terminate idle background updater bloatware
                int killedCount = 0;
                foreach (var procName in BloatProcessNames)
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
                    OnLogMessage?.Invoke($"🧹 Terminated {killedCount} idle background updater workers");
                }

                // 4. Heavy RAM Deep Clean
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

                // 2. Restore power plan
                if (!string.IsNullOrEmpty(_previousPowerPlanGuid))
                {
                    try
                    {
                        SetPowerPlan(_previousPowerPlanGuid);
                        OnLogMessage?.Invoke("⚡ Restored original Windows Power Scheme");
                    }
                    catch { }
                }
            });

            ActiveProfile = BoostProfile.None;
            OnProfileChanged?.Invoke(BoostProfile.None);
            OnLogMessage?.Invoke("Standard mode restored.");
            return true;
        }

        private string? GetActivePowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                    // Output format: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                    int guidIndex = output.IndexOf("GUID: ", StringComparison.OrdinalIgnoreCase);
                    if (guidIndex != -1)
                    {
                        string sub = output.Substring(guidIndex + 6).Trim();
                        string guid = sub.Split(' ')[0];
                        return guid;
                    }
                }
            }
            catch { }
            return null;
        }

        private void SetPowerPlan(string guid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = $"/setactive {guid}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { }
        }
    }
}
