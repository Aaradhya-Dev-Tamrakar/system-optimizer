using System;
using System.Diagnostics;
using System.Management;

namespace NovaOptimizer.Services
{
    public enum AcerThermalProfile
    {
        Quiet = 0x00,
        Balanced = 0x01,
        Performance = 0x04,
        Turbo = 0x05
    }

    /// <summary>
    /// Communicates directly with Acer ACPI / WMI firmware (root\wmi:AcerGamingFunction)
    /// to adjust cooling fan profiles (Quiet, Balanced, Performance, Turbo) and monitor fan telemetry.
    /// Hardware thermal watchdogs remain active at all times in firmware.
    /// </summary>
    public class AcerHardwareCoolingService
    {
        private const string WmiNamespace = @"\\.\root\wmi";
        private const string WmiClassName = "AcerGamingFunction";
        private const ulong MiscPlatformProfileKey = 0x0B;

        public bool IsSupported { get; private set; }
        public string LaptopModel { get; private set; } = "Unknown";
        public AcerThermalProfile CurrentProfile { get; private set; } = AcerThermalProfile.Balanced;
        public int CpuFanRpm { get; private set; }
        public int GpuFanRpm { get; private set; }

        public event Action<AcerThermalProfile>? OnProfileChanged;
        public event Action<string>? OnLogMessage;

        public AcerHardwareCoolingService()
        {
            DetectHardwareSupport();
        }

        private void DetectHardwareSupport()
        {
            try
            {
                // Check manufacturer
                using var csSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject cs in csSearcher.Get())
                {
                    string mfg = cs["Manufacturer"]?.ToString() ?? "";
                    LaptopModel = cs["Model"]?.ToString() ?? "Acer Device";

                    if (!mfg.Contains("Acer", StringComparison.OrdinalIgnoreCase))
                    {
                        IsSupported = false;
                        return;
                    }
                }

                // Verify AcerGamingFunction presence in root\wmi
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var wmiSearcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {WmiClassName}"));
                using var results = wmiSearcher.Get();

                foreach (ManagementObject _ in results)
                {
                    IsSupported = true;
                    break;
                }

                if (IsSupported)
                {
                    QueryCurrentProfile();
                    UpdateFanSpeeds();
                }
            }
            catch (Exception ex)
            {
                IsSupported = false;
                Debug.WriteLine($"[AcerCooling] WMI probe not supported or non-elevated: {ex.Message}");
            }
        }

        public bool SetProfile(AcerThermalProfile profile)
        {
            if (!IsSupported) return false;

            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {WmiClassName}"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    // 1. Invoke SetGamingMiscSetting with packed profile key (profile << 8) | 0x0B
                    ulong payload = ((ulong)(byte)profile << 8) | MiscPlatformProfileKey;
                    using var inParams = obj.GetMethodParameters("SetGamingMiscSetting");
                    inParams["gmInput"] = payload;
                    using var outParams = obj.InvokeMethod("SetGamingMiscSetting", inParams, null);

                    // 2. Set fan behavior mode to align with profile (0 = Auto/Balanced, 1 = Max/Turbo, etc.)
                    try
                    {
                        ulong fanBehavior = profile switch
                        {
                            AcerThermalProfile.Turbo => 1UL,       // Max Cooling
                            AcerThermalProfile.Performance => 0UL, // Dynamic Aggressive
                            AcerThermalProfile.Quiet => 0UL,       // Dynamic Whisper
                            _ => 0UL                               // Auto Factory
                        };

                        using var fanParams = obj.GetMethodParameters("SetGamingFanBehavior");
                        fanParams["gmInput"] = fanBehavior;
                        obj.InvokeMethod("SetGamingFanBehavior", fanParams, null);
                    }
                    catch
                    {
                        // Some Acer models only accept MiscSetting; non-fatal
                    }

                    CurrentProfile = profile;
                    OnProfileChanged?.Invoke(profile);

                    string profileName = profile switch
                    {
                        AcerThermalProfile.Quiet => "Quiet (Silent Acoustic Curve)",
                        AcerThermalProfile.Balanced => "Balanced (Standard Curve)",
                        AcerThermalProfile.Performance => "Performance (High Fan Profile)",
                        AcerThermalProfile.Turbo => "Turbo (Max Cooling Fan Profile)",
                        _ => profile.ToString()
                    };

                    OnLogMessage?.Invoke($"❄️ [Firmware Cooling] Profile set to {profileName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"⚠️ [Firmware Cooling] Failed to set profile: {ex.Message}");
            }

            return false;
        }

        public AcerThermalProfile QueryCurrentProfile()
        {
            if (!IsSupported) return CurrentProfile;

            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {WmiClassName}"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    using var inParams = obj.GetMethodParameters("GetGamingMiscSetting");
                    inParams["gmInput"] = MiscPlatformProfileKey;
                    using var outParams = obj.InvokeMethod("GetGamingMiscSetting", inParams, null);

                    if (outParams?["gmOutput"] is uint rawOutput)
                    {
                        byte profileByte = (byte)((rawOutput >> 8) & 0xFF);
                        if (Enum.IsDefined(typeof(AcerThermalProfile), (AcerThermalProfile)profileByte))
                        {
                            CurrentProfile = (AcerThermalProfile)profileByte;
                        }
                    }
                    break;
                }
            }
            catch { }

            return CurrentProfile;
        }

        public (int cpuRpm, int gpuRpm) UpdateFanSpeeds()
        {
            if (!IsSupported) return (0, 0);

            try
            {
                var scope = new ManagementScope(WmiNamespace);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {WmiClassName}"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    // Query Fan 0 (CPU Fan)
                    try
                    {
                        using var inParams0 = obj.GetMethodParameters("GetGamingFanSpeed");
                        inParams0["gmInput"] = 0UL;
                        using var out0 = obj.InvokeMethod("GetGamingFanSpeed", inParams0, null);
                        if (out0?["gmOutput"] is uint rpm0 && rpm0 > 0 && rpm0 < 15000)
                        {
                            CpuFanRpm = (int)rpm0;
                        }
                    }
                    catch { }

                    // Query Fan 1 (System / GPU Fan)
                    try
                    {
                        using var inParams1 = obj.GetMethodParameters("GetGamingFanSpeed");
                        inParams1["gmInput"] = 1UL;
                        using var out1 = obj.InvokeMethod("GetGamingFanSpeed", inParams1, null);
                        if (out1?["gmOutput"] is uint rpm1 && rpm1 > 0 && rpm1 < 15000)
                        {
                            GpuFanRpm = (int)rpm1;
                        }
                    }
                    catch { }

                    break;
                }
            }
            catch { }

            return (CpuFanRpm, GpuFanRpm);
        }
    }
}
