using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace WinGovernor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Always prefer the newest build of this app over a stale binary (e.g. an old autostart target).
            if (TryRelaunchNewestBuild(e.Args))
            {
                Environment.Exit(0);
                return;
            }

            StopStaleInstancesAndCleanBak();
            MigrateLegacyData();
            Services.ThemeService.Initialize();

            DispatcherUnhandledException += (s, args) =>
            {
                Debug.WriteLine($"[Unhandled UI Exception] {args.Exception}");
                args.Handled = true;
            };

            // Auto-rebuild is only enabled if explicitly requested via --enable-source-rebuild or in DEBUG builds
            // This prevents arbitrary code execution vulnerabilities when running elevated in production.
            bool allowSourceRebuild = false;
#if DEBUG
            allowSourceRebuild = true;
#endif
            if (e.Args.Contains("--enable-source-rebuild", StringComparer.OrdinalIgnoreCase))
            {
                allowSourceRebuild = true;
            }

            if (allowSourceRebuild)
            {
                CheckAndPerformSourceAutoUpdate(e.Args);
            }

            base.OnStartup(e);
        }

        private static string? FindProjectDir(string exePath)
        {
            string? dir = Path.GetDirectoryName(exePath);
            for (int i = 0; i < 7 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "WinGovernor.csproj"))) return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        /// <summary>
        /// If a newer WinGovernor.exe exists in this project's bin/publish outputs, launch it and tell the caller to exit.
        /// Prevents boot/autostart from running a stale build.
        /// </summary>
        private static bool TryRelaunchNewestBuild(string[] args)
        {
            try
            {
                if (args.Contains("--no-newer-check", StringComparer.OrdinalIgnoreCase)) return false;

                string? current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current) || !File.Exists(current)) return false;

                string? projectDir = FindProjectDir(current);
                if (projectDir == null) return false;

                var roots = new[] { Path.Combine(projectDir, "bin"), Path.Combine(projectDir, "publish") };
                var enumOpts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

                string? newest = roots
                    .Where(Directory.Exists)
                    .SelectMany(r => Directory.EnumerateFiles(r, "WinGovernor.exe", enumOpts))
                    .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest == null || string.Equals(newest, current, StringComparison.OrdinalIgnoreCase)) return false;
                if (File.GetLastWriteTimeUtc(newest) <= File.GetLastWriteTimeUtc(current).AddSeconds(2)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = newest,
                    Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append("--no-newer-check")),
                    WorkingDirectory = Path.GetDirectoryName(newest)!,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Newest-build check error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Terminates any older running instances of WinGovernor or legacy NovaOptimizer and removes .bak build artifacts.
        /// </summary>
        private static void StopStaleInstancesAndCleanBak()
        {
            int selfPid = Environment.ProcessId;
            foreach (var name in new[] { "NovaOptimizer", "WinGovernor" })
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        using (proc)
                        {
                            if (proc.Id == selfPid) continue;
                            try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                        }
                    }
                }
                catch { }
            }

            try
            {
                string? dir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var bak in Directory.GetFiles(dir, "*.bak"))
                    {
                        try { File.Delete(bak); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>One-time carry-over of preferences and logs saved under the previous NovaOptimizer name.</summary>
        private static void MigrateLegacyData()
        {
            try
            {
                using var legacy = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\NovaOptimizer");
                if (legacy != null && Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\WinGovernor") == null)
                {
                    using var target = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\WinGovernor");
                    foreach (var name in legacy.GetValueNames())
                    {
                        target.SetValue(name, legacy.GetValue(name)!, legacy.GetValueKind(name));
                    }
                }
            }
            catch { }

            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string oldDir = Path.Combine(local, "NovaOptimizer");
                string newDir = Path.Combine(local, "WinGovernor");
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);
            }
            catch { }
        }

        private static void CheckAndPerformSourceAutoUpdate(string[] args)
        {
            // Prevent recursive loop if this instance was just launched by an auto-rebuild
            if (args.Contains("--no-source-check", StringComparer.OrdinalIgnoreCase)) return;

            try
            {
                string currentExePath = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath)) return;

                // Look upward for the project root (.csproj)
                string? searchDir = Path.GetDirectoryName(currentExePath);
                string? projectDir = null;

                for (int i = 0; i < 4 && searchDir != null; i++)
                {
                    if (File.Exists(Path.Combine(searchDir, "WinGovernor.csproj")))
                    {
                        projectDir = searchDir;
                        break;
                    }
                    searchDir = Directory.GetParent(searchDir)?.FullName;
                }

                if (projectDir == null) return;

                DateTime exeWriteTime = File.GetLastWriteTimeUtc(currentExePath);

                // Check source code directories for any file modified after this exe was built
                string[] sourceFolders = new[] { "Services", "Views", "Models", "Native" };
                bool hasNewerSource = false;

                foreach (var folder in sourceFolders)
                {
                    string dirPath = Path.Combine(projectDir, folder);
                    if (!Directory.Exists(dirPath)) continue;

                    var enumOpts = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true
                    };

                    var newerFile = Directory.EnumerateFiles(dirPath, "*.*", enumOpts)
                        .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                        .Any(f => File.GetLastWriteTimeUtc(f) > exeWriteTime);

                    if (newerFile)
                    {
                        hasNewerSource = true;
                        break;
                    }
                }

                // Check top-level root files (App.xaml, MainWindow.xaml, .csproj)
                if (!hasNewerSource)
                {
                    string[] rootFiles = new[] { "App.xaml", "App.xaml.cs", "MainWindow.xaml", "MainWindow.xaml.cs", "WinGovernor.csproj" };
                    foreach (var rf in rootFiles)
                    {
                        string p = Path.Combine(projectDir, rf);
                        if (File.Exists(p) && File.GetLastWriteTimeUtc(p) > exeWriteTime)
                        {
                            hasNewerSource = true;
                            break;
                        }
                    }
                }

                if (hasNewerSource)
                {
                    // Recompile the project
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"build \"{Path.Combine(projectDir, "WinGovernor.csproj")}\" -c Release --nologo -v q",
                        WorkingDirectory = projectDir,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using var buildProc = Process.Start(psi);
                    if (buildProc != null)
                    {
                        buildProc.WaitForExit(20000);
                        if (buildProc.ExitCode == 0)
                        {
                            // Dynamically locate the newly compiled binary under bin/Release
                            string releaseBinDir = Path.Combine(projectDir, "bin", "Release");
                            string? newBinaryPath = null;

                            if (Directory.Exists(releaseBinDir))
                            {
                                var candidates = Directory.GetFiles(releaseBinDir, "WinGovernor.exe", SearchOption.AllDirectories);
                                newBinaryPath = candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                            }

                            if (!string.IsNullOrEmpty(newBinaryPath) && File.Exists(newBinaryPath))
                            {
                                var runPsi = new ProcessStartInfo
                                {
                                    FileName = newBinaryPath,
                                    Arguments = "--no-source-check",
                                    UseShellExecute = true
                                };
                                Process.Start(runPsi);
                                Environment.Exit(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Source auto-update check error: {ex.Message}");
            }
        }
    }
}
