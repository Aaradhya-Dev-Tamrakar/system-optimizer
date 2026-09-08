using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class ProcessMonitorService
    {
        private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "csrss", "smss", "wininit", "winlogon", "services", 
            "lsass", "svchost", "fontdrvhost", "dwm", "registry", "memcompression"
        };

        private readonly Dictionary<int, (TimeSpan CpuTime, DateTime SnapshotTime)> _processHistory = new();
        private ulong _lastSystemIdle;
        private ulong _lastSystemKernel;
        private ulong _lastSystemUser;
        private DateTime _lastSystemSample = DateTime.UtcNow;

        public List<ProcessItem> GetProcesses()
        {
            var result = new List<ProcessItem>();

            // Sample overall system times
            NativeMethods.FILETIME idleTime, kernelTime, userTime;
            NativeMethods.GetSystemTimes(out idleTime, out kernelTime, out userTime);

            ulong currentIdle = idleTime.ToUlong();
            ulong currentKernel = kernelTime.ToUlong();
            ulong currentUser = userTime.ToUlong();
            DateTime now = DateTime.UtcNow;

            double totalSystemDelta = 0;
            if (_lastSystemSample != DateTime.MinValue)
            {
                ulong sysKernelDelta = currentKernel > _lastSystemKernel ? currentKernel - _lastSystemKernel : 0;
                ulong sysUserDelta = currentUser > _lastSystemUser ? currentUser - _lastSystemUser : 0;
                totalSystemDelta = (sysKernelDelta + sysUserDelta); // in 100ns units
            }

            _lastSystemIdle = currentIdle;
            _lastSystemKernel = currentKernel;
            _lastSystemUser = currentUser;
            _lastSystemSample = now;

            int processorCount = Environment.ProcessorCount;
            Process[] processes = Process.GetProcesses();

            var currentPids = new HashSet<int>();

            foreach (var p in processes)
            {
                try
                {
                    int pid = p.Id;
                    currentPids.Add(pid);

                    string name = p.ProcessName;
                    bool isCritical = pid <= 4 || CriticalProcesses.Contains(name);

                    double workingSetMB = 0;
                    double privateMB = 0;
                    try
                    {
                        workingSetMB = p.WorkingSet64 / (1024.0 * 1024.0);
                        privateMB = p.PrivateMemorySize64 / (1024.0 * 1024.0);
                    }
                    catch { }

                    double cpuPercent = 0.0;
                    try
                    {
                        TimeSpan currentTotalCpu = p.TotalProcessorTime;
                        if (_processHistory.TryGetValue(pid, out var prev))
                        {
                            double procDelta = (currentTotalCpu - prev.CpuTime).TotalMilliseconds * 10000.0; // to 100ns
                            if (totalSystemDelta > 0 && procDelta > 0)
                            {
                                cpuPercent = (procDelta / totalSystemDelta) * 100.0;
                                if (cpuPercent > 100.0) cpuPercent = 100.0;
                            }
                        }
                        _processHistory[pid] = (currentTotalCpu, now);
                    }
                    catch { }

                    string priority = "Normal";
                    try
                    {
                        priority = p.PriorityClass.ToString();
                    }
                    catch { }

                    string status = "Running";
                    try
                    {
                        if (!p.Responding) status = "Not Responding";
                    }
                    catch { }

                    string description = name;
                    string filePath = string.Empty;
                    try
                    {
                        if (!isCritical)
                        {
                            var module = p.MainModule;
                            if (module != null)
                            {
                                filePath = module.FileName ?? string.Empty;
                                description = module.FileVersionInfo.FileDescription ?? name;
                            }
                        }
                    }
                    catch { }

                    result.Add(new ProcessItem
                    {
                        Id = pid,
                        Name = name,
                        Description = string.IsNullOrWhiteSpace(description) ? name : description,
                        WorkingSetMB = workingSetMB,
                        PrivateMemoryMB = privateMB,
                        CpuPercent = Math.Round(cpuPercent, 1),
                        Priority = priority,
                        Status = status,
                        FilePath = filePath,
                        IsSystemCritical = isCritical
                    });
                }
                catch
                {
                    // Ignore transient process errors
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Cleanup dead processes from history
            var deadPids = _processHistory.Keys.Where(k => !currentPids.Contains(k)).ToList();
            foreach (var dead in deadPids)
            {
                _processHistory.Remove(dead);
            }

            return result;
        }

        public bool KillProcess(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool KillProcessTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {pid}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = priority;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TrimProcessRam(int pid)
        {
            try
            {
                IntPtr hProc = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_INFORMATION, 
                    false, 
                    pid);

                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.EmptyWorkingSet(hProc);
                        NativeMethods.SetProcessWorkingSetSize(hProc, (IntPtr)(-1), (IntPtr)(-1));
                        return true;
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProc);
                    }
                }
            }
            catch { }
            return false;
        }

        public void OpenProcessLocation(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                string path = p.MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
            }
            catch { }
        }
    }
}
