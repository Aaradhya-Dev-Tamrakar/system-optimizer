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
