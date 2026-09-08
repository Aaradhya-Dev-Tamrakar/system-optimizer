using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using NovaOptimizer.Models;

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
                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = val,
                            Location = location,
                            RegistryPath = keyPath,
                            IsEnabled = true
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
                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = file,
                            Location = location,
                            RegistryPath = string.Empty,
                            IsEnabled = true
                        });
                    }
                }
            }
            catch { }
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
