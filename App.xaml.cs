using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace NovaOptimizer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Debug.WriteLine($"[Unhandled UI Exception] {args.Exception}");
                args.Handled = true;
            };

            // Check if running directly from within a source repository tree
            // and if newer source code files exist compared to this executable
            CheckAndPerformSourceAutoUpdate(e.Args);

            base.OnStartup(e);
        }

        private static void CheckAndPerformSourceAutoUpdate(string[] args)
        {
            // Prevent recursive loop if this instance was just launched by an auto-rebuild
            if (args.Contains("--no-source-check")) return;

            try
            {
                string currentExePath = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath)) return;

                // Look upward for the project root (.csproj)
                string? searchDir = Path.GetDirectoryName(currentExePath);
                string? projectDir = null;

                for (int i = 0; i < 4 && searchDir != null; i++)
                {
                    if (File.Exists(Path.Combine(searchDir, "NovaOptimizer.csproj")))
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
                    string[] rootFiles = new[] { "App.xaml", "App.xaml.cs", "MainWindow.xaml", "MainWindow.xaml.cs", "NovaOptimizer.csproj" };
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
                    // Recompile the project seamlessly
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"build \"{Path.Combine(projectDir, "NovaOptimizer.csproj")}\" -c Release --nologo -v q",
                        WorkingDirectory = projectDir,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using var buildProc = Process.Start(psi);
                    if (buildProc != null)
                    {
                        buildProc.WaitForExit(15000);
                        if (buildProc.ExitCode == 0)
                        {
                            // Launch the newly recompiled binary
                            string newBinaryPath = Path.Combine(projectDir, "bin", "Release", "net10.0-windows", "NovaOptimizer.exe");
                            if (File.Exists(newBinaryPath))
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
            catch
            {
                // In case of permission or file lock issues, continue launching current instance
            }
        }
    }
}
