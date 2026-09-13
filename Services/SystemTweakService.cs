using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class SystemTweakService
    {
        public List<TweakItem> GetTweaks()
        {
            return new List<TweakItem>
            {
                new TweakItem
                {
                    Id = "GameDVR",
                    Title = "Disable Windows Game DVR / Background Captures",
                    Description = "Stops Windows from constantly recording game screens in the background. Eliminates micro-stutters and improves input response time.",
                    Category = "Gaming Performance",
                    IsApplied = CheckGameDVRDisabled()
                },
                new TweakItem
                {
                    Id = "NetworkThrottling",
                    Title = "Disable Network Throttling for Low Latency",
                    Description = "Windows limits non-multimedia network packets to 10/ms. Disabling throttling eliminates ping spikes in games and live applications.",
                    Category = "Network & Latency",
                    IsApplied = CheckNetworkThrottlingDisabled()
                },
                new TweakItem
                {
                    Id = "SystemResponsiveness",
                    Title = "Prioritize Foreground Applications (100% CPU to Tasks)",
                    Description = "By default Windows reserves 20% CPU for background services. Setting responsiveness to 0 allocates 100% processing priority to foreground apps.",
                    Category = "CPU & Responsiveness",
                    IsApplied = CheckSystemResponsivenessApplied()
                },
                new TweakItem
                {
                    Id = "Telemetry",
                    Title = "Disable Diagnostic Data & Background Telemetry",
                    Description = "Stops Microsoft background telemetry logging and diagnostics uploading, reducing CPU wakeups and disk usage.",
                    Category = "Privacy & Background Bloat",
                    IsApplied = CheckTelemetryDisabled()
                },
                new TweakItem
                {
                    Id = "KillerSuite",
                    Title = "Disable Killer Performance Suite & Network Telemetry",
                    Description = "Disables Killer Network Service, Analytics, and Data Helper. Kills KillerTray.exe and stops the tray icon and background polling permanently.",
                    Category = "Background Services & Bloat",
                    IsApplied = CheckServicesDisabled("Killer Network Service", "Killer Analytics Service", "Killer Provider Data Helper Service")
                },
                new TweakItem
                {
                    Id = "IntelSUR",
                    Title = "Disable Intel System Usage Report & Energy Server (esrv.exe)",
                    Description = "Disables ESRV_SVC_QUEENCREEK, USER_ESRV, and Intel SUR services. Terminates esrv.exe and background telemetry.",
                    Category = "Background Services & Bloat",
                    IsApplied = CheckServicesDisabled("ESRV_SVC_QUEENCREEK", "USER_ESRV_SVC_QUEENCREEK", "SystemUsageReportSvc_QUEENCREEK")
                },
                new TweakItem
                {
                    Id = "OEMUpdaters",
                    Title = "Set OEM Bloat & Heavy Updaters to Manual (DSA & Acer)",
                    Description = "Sets Acer Care Center, Acer Experience Zone, DSAService, and browser updaters to on-demand Manual startup, saving RAM.",
                    Category = "Background Services & Bloat",
                    IsApplied = CheckServicesManualOrDisabled("DSAService", "DSAUpdateService", "AcerEZSvc")
                }
            };
        }

        public bool ApplyTweak(string tweakId)
        {
            if (!NativeMethods.IsAdministrator() && tweakId != "GameDVR")
            {
                return false;
            }

            try
            {
                switch (tweakId)
                {
                    case "GameDVR":
                        return SetGameDVR(false);
                    case "NetworkThrottling":
                        return SetNetworkThrottling(false);
                    case "SystemResponsiveness":
                        return SetSystemResponsiveness(0);
                    case "Telemetry":
                        return SetTelemetry(false);
                    case "KillerSuite":
                        return SetServicesStartupType(4, true, new[] { "KillerTray", "KillerNetworkService", "IntelNetworkHelperService" },
                            "Killer Network Service", "Killer Analytics Service", "Killer Provider Data Helper Service", "Intel Network Helper Service");
                    case "IntelSUR":
                        return SetServicesStartupType(4, true, new[] { "esrv", "esrv_svc", "SurSvc", "IntelSoftwareAssetManagerService" },
                            "ESRV_SVC_QUEENCREEK", "USER_ESRV_SVC_QUEENCREEK", "SystemUsageReportSvc_QUEENCREEK", "Intel(R) SUR QC SAM", "IntelCollectorService", "IntelTelemetryAgent");
                    case "OEMUpdaters":
                        return SetServicesStartupType(3, true, new[] { "DSAService", "DSAUpdateService", "PresentMonService" },
                            "DSAService", "DSAUpdateService", "AcerCCAgentSvis", "AcerEZSvc", "PresentMonSharedService", "edgeupdate");
                }
            }
            catch { }
            return false;
        }

        public bool RevertTweak(string tweakId)
        {
            if (!NativeMethods.IsAdministrator() && tweakId != "GameDVR")
            {
                return false;
            }

            try
            {
                switch (tweakId)
                {
                    case "GameDVR":
                        return SetGameDVR(true);
                    case "NetworkThrottling":
                        return SetNetworkThrottling(true);
                    case "SystemResponsiveness":
                        return SetSystemResponsiveness(20);
                    case "Telemetry":
                        return SetTelemetry(true);
                    case "KillerSuite":
                        return SetServicesStartupType(2, false, null, "Killer Network Service") &&
                               SetServicesStartupType(3, false, null, "Killer Analytics Service", "Killer Provider Data Helper Service");
                    case "IntelSUR":
                        return SetServicesStartupType(3, false, null, "ESRV_SVC_QUEENCREEK", "USER_ESRV_SVC_QUEENCREEK", "SystemUsageReportSvc_QUEENCREEK", "Intel(R) SUR QC SAM");
                    case "OEMUpdaters":
                        return SetServicesStartupType(2, false, null, "DSAService", "DSAUpdateService", "AcerCCAgentSvis", "AcerEZSvc", "PresentMonSharedService", "edgeupdate");
                }
            }
            catch { }
            return false;
        }

        private bool CheckServicesDisabled(params string[] serviceNames)
        {
            foreach (var name in serviceNames)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                    if (key != null)
                    {
                        var val = key.GetValue("Start");
                        if (val is int i && i != 4) return false;
                    }
                }
                catch { }
            }
            return true;
        }

        private bool CheckServicesManualOrDisabled(params string[] serviceNames)
        {
            foreach (var name in serviceNames)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                    if (key != null)
                    {
                        var val = key.GetValue("Start");
                        if (val is int i && i == 2) return false;
                    }
                }
                catch { }
            }
            return true;
        }

        private bool SetServicesStartupType(int startType, bool stopService, string[]? killProcessNames, params string[] serviceNames)
        {
            bool anyConfigured = false;
            foreach (var name in serviceNames)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", true);
                    if (key != null)
                    {
                        key.SetValue("Start", startType, RegistryValueKind.DWord);
                        anyConfigured = true;
                    }

                    if (stopService)
                    {
                        try
                        {
                            using var sc = new ServiceController(name);
                            if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                            {
                                sc.Stop();
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (killProcessNames != null)
            {
                foreach (var proc in killProcessNames)
                {
                    try
                    {
                        foreach (var p in Process.GetProcessesByName(proc))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }

            return anyConfigured;
        }

        private bool CheckGameDVRDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
                if (key != null)
                {
                    var val = key.GetValue("GameDVR_Enabled");
                    if (val is int i && i == 0) return true;
                }
            }
            catch { }
            return false;
        }

        private bool SetGameDVR(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                key?.SetValue("GameDVR_Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);

                using var policyKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
                policyKey?.SetValue("AllowGameDVR", enabled ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }

        private bool CheckNetworkThrottlingDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                if (key != null)
                {
                    var val = key.GetValue("NetworkThrottlingIndex");
                    if (val is int i && (uint)i == 0xFFFFFFFF) return true;
                }
            }
            catch { }
            return false;
        }

        private bool SetNetworkThrottling(bool enabled)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                key?.SetValue("NetworkThrottlingIndex", enabled ? 10 : unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }

        private bool CheckSystemResponsivenessApplied()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                if (key != null)
                {
                    var val = key.GetValue("SystemResponsiveness");
                    if (val is int i && i == 0) return true;
                }
            }
            catch { }
            return false;
        }

        private bool SetSystemResponsiveness(int value)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                key?.SetValue("SystemResponsiveness", value, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }

        private bool CheckTelemetryDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                if (key != null)
                {
                    var val = key.GetValue("AllowTelemetry");
                    if (val is int i && i == 0) return true;
                }
            }
            catch { }
            return false;
        }

        private bool SetTelemetry(bool enabled)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                key?.SetValue("AllowTelemetry", enabled ? 3 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }
    }
}
