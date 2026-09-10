using System;
using System.Collections.Generic;
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
                }
            }
            catch { }
            return false;
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
