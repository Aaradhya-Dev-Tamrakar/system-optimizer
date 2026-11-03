using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using WinGovernor.Models;
using WinGovernor.Native;

namespace WinGovernor.Services
{
    public class StartupManagerService
    {
        public event Action<bool>? OnGovernorStartupChanged;

        public bool IsGovernorStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key?.GetValue("WinGovernor") != null) return true;

                // Also verify Task Scheduler
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /tn \"WinGovernor_Autostart\"",
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

        private static string RunSchtasks(string args, int timeoutMs = 3000)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return string.Empty;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(timeoutMs);
                return proc.ExitCode == 0 ? output : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? GetRegisteredTaskExe(string taskName)
        {
            string xml = RunSchtasks($"/query /tn \"{taskName}\" /xml");
            var m = System.Text.RegularExpressions.Regex.Match(xml, @"<Command>(?<cmd>.*?)</Command>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups["cmd"].Value).Trim().Trim('"') : null;
        }

        /// <summary>
        /// Self-heal on launch: migrates the legacy NovaOptimizer autostart entries and re-points the
        /// autostart task / Run key at the currently running executable so boot never launches a stale build.
        /// </summary>
        public void MigrateAndHealAutostart()
        {
            try
            {
                const string legacyTask = "NovaOptimizer_Autostart";
                bool legacyEnabled = false;

                if (!string.IsNullOrEmpty(RunSchtasks($"/query /tn \"{legacyTask}\"")))
                {
                    legacyEnabled = true;
                    RunSchtasks($"/delete /tn \"{legacyTask}\" /f");
                }

                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey?.GetValue("NovaOptimizer") != null)
                    {
                        legacyEnabled = true;
                        runKey.DeleteValue("NovaOptimizer", false);
                    }
                }

                string? current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current) || !File.Exists(current)) return;
                if (current.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return;

                bool enabled = legacyEnabled || IsGovernorStartupEnabled();
                if (!enabled) return;

                string? taskExe = GetRegisteredTaskExe("WinGovernor_Autostart");
                string? runValue = null;
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    runValue = runKey?.GetValue("WinGovernor") as string;
                }

                bool taskStale = taskExe != null && !string.Equals(taskExe, current, StringComparison.OrdinalIgnoreCase);
                bool runStale = runValue != null && !runValue.Contains(current, StringComparison.OrdinalIgnoreCase);
                bool missing = taskExe == null && runValue == null;

                if (legacyEnabled || taskStale || runStale || missing)
                {
                    SetGovernorStartup(true);
                }
            }
            catch { }
        }

        public bool SetGovernorStartup(bool enable)
        {
            const string taskName = "WinGovernor_Autostart";
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
                            key?.DeleteValue("WinGovernor", false);
                        }
                        catch { }
                    }
                    else
                    {
                        // Fallback: Registry Run key for standard startup
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.SetValue("WinGovernor", $"\"{exePath}\" --autostart");
                    }
                }
                else
                {
                    // 1. Remove HKCU Run Key
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.DeleteValue("WinGovernor", false);
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

                OnGovernorStartupChanged?.Invoke(enable);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
