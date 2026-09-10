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

        public bool AddStartupItem(string name, string executablePath, string arguments = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(executablePath))
                    return false;

                string cleanPath = executablePath.Trim('\"');
                string command = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{cleanPath}\""
                    : $"\"{cleanPath}\" {arguments.Trim()}";

                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    key.SetValue(name, command);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public List<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();

            // 1. Current User Run
            ReadRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "User Registry", items);

            // 2. Local Machine Run
            ReadRegistryRun(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "System Registry", items);

            // 3. Startup Folders
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ReadFolder(userStartup, "User Startup Folder", items);

            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ReadFolder(commonStartup, "Common Startup Folder", items);

            return items;
        }

        private void ReadRegistryRun(RegistryKey root, string keyPath, string location, List<StartupItem> items)
        {
            try
            {
                using var key = root.OpenSubKey(keyPath, false);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var val = key.GetValue(name)?.ToString() ?? string.Empty;
                        string cleanPath = ExtractFilePath(val);
                        var icon = IconHelper.GetIcon(cleanPath, name);
                        string impact = CalculateImpact(name, val);

                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = val,
                            Location = location,
                            RegistryPath = keyPath,
                            IsEnabled = true,
                            Icon = icon,
                            Impact = impact
                        });
                    }
                }
            }
            catch { }
        }

        private void ReadFolder(string folderPath, string location, List<StartupItem> items)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    foreach (var file in Directory.GetFiles(folderPath))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        var icon = IconHelper.GetIcon(file, name);
                        string impact = CalculateImpact(name, file);

                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = file,
                            Location = location,
                            RegistryPath = string.Empty,
                            IsEnabled = true,
                            Icon = icon,
                            Impact = impact
                        });
                    }
                }
            }
            catch { }
        }

        private static string ExtractFilePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string trimmed = command.Trim();
            if (trimmed.StartsWith("\""))
            {
                int endQuote = trimmed.IndexOf('\"', 1);
                if (endQuote > 1) return trimmed.Substring(1, endQuote - 1);
            }
            int firstSpace = trimmed.IndexOf(' ');
            return firstSpace > 0 ? trimmed.Substring(0, firstSpace) : trimmed;
        }

        private static string CalculateImpact(string name, string command)
        {
            string lower = (name + " " + command).ToLowerInvariant();
            if (lower.Contains("discord") || lower.Contains("spotify") || lower.Contains("steam") || 
                lower.Contains("onedrive") || lower.Contains("teams") || lower.Contains("epicgames") ||
                lower.Contains("creative cloud") || lower.Contains("chrome") || lower.Contains("edge"))
            {
                return "High Impact";
            }
            if (lower.Contains("update") || lower.Contains("helper") || lower.Contains("agent") || 
                lower.Contains("daemon") || lower.Contains("tray") || lower.Contains("notif"))
            {
                return "Low Impact";
            }
            return "Medium Impact";
        }

        public bool RemoveStartupItem(StartupItem item)
        {
            try
            {
                if (item.Name.Equals("NovaOptimizer", StringComparison.OrdinalIgnoreCase))
                {
                    return SetNovaStartup(false);
                }

                if (!string.IsNullOrEmpty(item.RegistryPath))
                {
                    RegistryKey root = item.Location.Contains("User") ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = root.OpenSubKey(item.RegistryPath, true);
                    key?.DeleteValue(item.Name, false);
                    return true;
                }
                else if (File.Exists(item.Command))
                {
                    File.Delete(item.Command);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
