using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class StartupManagerService
    {
        public event Action<bool>? OnNovaStartupChanged;

        public bool IsNovaStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key?.GetValue("NovaOptimizer") != null) return true;

                // Also verify Task Scheduler
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /tn \"NovaOptimizer_Autostart\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(2000);
                    return proc.ExitCode == 0;
                }
            }
            catch { }
            return false;
        }

        public bool SetNovaStartup(bool enable)
        {
            const string taskName = "NovaOptimizer_Autostart";
            try
            {
                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        string[] args = Environment.GetCommandLineArgs();
                        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                        {
                            exePath = args[0];
                        }
                    }

                    if (!string.IsNullOrEmpty(exePath) && exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        string exeCandidate = Path.ChangeExtension(exePath, ".exe");
                        if (File.Exists(exeCandidate)) exePath = exeCandidate;
                    }

                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        return false;
                    }

                    // On Windows 11 with administrator privileges:
                    // Use Task Scheduler with highest privileges to run seamlessly at logon without UAC prompts.
                    // Clean up any old registry run key to prevent dual-launch.
                    bool isAdmin = NativeMethods.IsAdministrator();
                    bool taskCreated = false;

                    if (isAdmin)
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "schtasks.exe",
                                Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" --autostart\" /sc onlogon /rl highest /f",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            using var proc = System.Diagnostics.Process.Start(psi);
                            if (proc != null)
                            {
                                proc.WaitForExit(4000);
                                taskCreated = proc.ExitCode == 0;
                            }
                        }
                        catch { }
                    }

                    // If task scheduler succeeded, remove registry entry so it doesn't double-start
                    if (taskCreated)
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                            key?.DeleteValue("NovaOptimizer", false);
                        }
                        catch { }
                    }
                    else
                    {
                        // Fallback: Registry Run key for standard startup
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.SetValue("NovaOptimizer", $"\"{exePath}\" --autostart");
                    }
                }
                else
                {
                    // 1. Remove HKCU Run Key
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.DeleteValue("NovaOptimizer", false);
                    }
                    catch { }

                    // 2. Remove Task Scheduler
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/delete /tn \"{taskName}\" /f",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit(3000);
                    }
                    catch { }
                }

                OnNovaStartupChanged?.Invoke(enable);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
