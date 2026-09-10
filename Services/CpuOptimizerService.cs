using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class CpuCleanResult
    {
        public int TamedCount { get; set; }
        public double EstimatedCpuFreed { get; set; }
        public List<string> TamedProcesses { get; set; } = new();

        public string Summary => TamedCount > 0
            ? $"{TamedCount} background {(TamedCount == 1 ? "hog" : "hogs")} tamed to Eco Mode ({string.Join(", ", TamedProcesses.Take(3))}{(TamedProcesses.Count > 3 ? "..." : "")})"
            : "No busy background CPU hogs detected.";
    }

    public class CpuHogItem
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuPercent { get; set; }
        public bool IsForeground { get; set; }
        public string Priority { get; set; } = "Normal";
    }

    public class CpuOptimizerService : IDisposable
    {
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "csrss", "smss", "wininit", "winlogon", "services",
            "lsass", "svchost", "fontdrvhost", "dwm", "registry", "memcompression",
            "explorer"
        };

        private readonly Timer? _watchdogTimer;
        private readonly Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)> _procHistory = new();
        private ulong _lastSysIdle;
        private ulong _lastSysKernel;
        private ulong _lastSysUser;
        private DateTime _lastSysSample = DateTime.MinValue;

        public bool AutoTameEnabled { get; set; } = false;
        public double AutoTameCpuThreshold { get; set; } = 75.0; // Trigger when overall CPU > 75%
        public double SingleProcessHogThreshold { get; set; } = 12.0; // Or single background process > 12%

        public event Action<CpuCleanResult>? OnCpuAutoTamed;
        public event Action<CpuCleanResult>? OnCpuCleanPerformed;

        public CpuOptimizerService()
        {
            // Acquire required NT privileges for quota & process information
            NativeMethods.SetIncreasePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
            NativeMethods.SetIncreasePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

            // Watchdog tick runs every 5 seconds
            _watchdogTimer = new Timer(WatchdogTick, null, 5000, 5000);
        }

        private void WatchdogTick(object? state)
        {
            if (!AutoTameEnabled) return;

            // Pause timer to prevent reentrancy during scan
            _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            try
            {
                double overallCpu = GetSystemCpuUsage();
                var hogs = SampleProcessesCpu(150);

                int fgPid = NativeMethods.GetForegroundProcessId();
                int currentPid = Environment.ProcessId;

                // Identify background hogs exceeding thresholds
                var actionableHogs = hogs.Where(h =>
                    h.Pid > 4 &&
                    h.Pid != currentPid &&
                    h.Pid != fgPid &&
                    !ProtectedProcesses.Contains(h.Name) &&
                    !string.Equals(h.Priority, "Idle", StringComparison.OrdinalIgnoreCase) &&
                    (overallCpu >= AutoTameCpuThreshold || h.CpuPercent >= SingleProcessHogThreshold)
                ).ToList();

                if (actionableHogs.Count > 0)
                {
                    var result = CleanSpecificHogs(actionableHogs);
                    if (result.TamedCount > 0)
                    {
                        OnCpuAutoTamed?.Invoke(result);
                    }
                }
            }
            catch
            {
                // Silent fail in background watchdog
            }
            finally
            {
                try
                {
                    _watchdogTimer?.Change(5000, 5000);
                }
                catch { }
            }
        }

        public double GetSystemCpuUsage()
        {
            NativeMethods.FILETIME idleTime, kernelTime, userTime;
            if (!NativeMethods.GetSystemTimes(out idleTime, out kernelTime, out userTime))
                return 0.0;

            ulong currentIdle = idleTime.ToUlong();
            ulong currentKernel = kernelTime.ToUlong();
            ulong currentUser = userTime.ToUlong();

            if (_lastSysSample == DateTime.MinValue)
            {
                _lastSysIdle = currentIdle;
                _lastSysKernel = currentKernel;
                _lastSysUser = currentUser;
                _lastSysSample = DateTime.UtcNow;
                return 0.0;
            }

            ulong kernelDelta = currentKernel > _lastSysKernel ? currentKernel - _lastSysKernel : 0;
            ulong userDelta = currentUser > _lastSysUser ? currentUser - _lastSysUser : 0;
            ulong idleDelta = currentIdle > _lastSysIdle ? currentIdle - _lastSysIdle : 0;

            _lastSysIdle = currentIdle;
            _lastSysKernel = currentKernel;
            _lastSysUser = currentUser;
            _lastSysSample = DateTime.UtcNow;

            ulong totalSys = kernelDelta + userDelta;
            if (totalSys == 0) return 0.0;

            double cpuPercent = (double)(totalSys - idleDelta) / totalSys * 100.0;
            return Math.Clamp(cpuPercent, 0.0, 100.0);
        }

        public List<CpuHogItem> GetTopCpuHogs(int count = 5)
        {
            var hogs = SampleProcessesCpu(150);
            int fgPid = NativeMethods.GetForegroundProcessId();
            int currentPid = Environment.ProcessId;

            return hogs
                .Where(h => h.Pid > 4 && h.Pid != currentPid && !ProtectedProcesses.Contains(h.Name))
                .Select(h => { h.IsForeground = (h.Pid == fgPid); return h; })
                .OrderByDescending(h => h.CpuPercent)
                .Take(count)
                .ToList();
        }

        public Task<CpuCleanResult> CleanCpuBusyProcessesAsync(double minCpuThreshold = 4.0)
        {
            return Task.Run(() => CleanCpuBusyProcesses(minCpuThreshold));
        }

        public CpuCleanResult CleanCpuBusyProcesses(double minCpuThreshold = 4.0)
        {
            // Sample over a fast 250ms window to accurately catch current CPU spikes
            var hogs = SampleProcessesCpu(250);

            int fgPid = NativeMethods.GetForegroundProcessId();
            int currentPid = Environment.ProcessId;

            var targets = hogs.Where(h =>
                h.Pid > 4 &&
                h.Pid != currentPid &&
                h.Pid != fgPid &&
                !ProtectedProcesses.Contains(h.Name) &&
                !string.Equals(h.Priority, "Idle", StringComparison.OrdinalIgnoreCase) &&
                h.CpuPercent >= minCpuThreshold
            ).ToList();

            var result = CleanSpecificHogs(targets);
            OnCpuCleanPerformed?.Invoke(result);
            return result;
        }

        private CpuCleanResult CleanSpecificHogs(List<CpuHogItem> hogs)
        {
            var result = new CpuCleanResult();
            double freedCpu = 0.0;

            foreach (var hog in hogs)
            {
                if (NativeMethods.TameProcess(hog.Pid))
                {
                    result.TamedCount++;
                    result.TamedProcesses.Add($"{hog.Name} ({hog.CpuPercent:F0}%)");
                    freedCpu += hog.CpuPercent;
                }
            }

            result.EstimatedCpuFreed = Math.Round(freedCpu, 1);
            return result;
        }

        public bool TameProcess(int pid)
        {
            int currentPid = Environment.ProcessId;
            if (pid <= 4 || pid == currentPid) return false;
            return NativeMethods.TameProcess(pid);
        }

        private List<CpuHogItem> SampleProcessesCpu(int sampleDelayMs)
        {
            NativeMethods.FILETIME idle1, kernel1, user1;
            NativeMethods.GetSystemTimes(out idle1, out kernel1, out user1);
            ulong sys1 = kernel1.ToUlong() + user1.ToUlong();

            var procs1 = Process.GetProcesses();
            var snap1 = new Dictionary<int, (string Name, TimeSpan Cpu, string Priority)>(procs1.Length);

            foreach (var p in procs1)
            {
                try
                {
                    snap1[p.Id] = (p.ProcessName, p.TotalProcessorTime, p.PriorityClass.ToString());
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            Thread.Sleep(sampleDelayMs);

            NativeMethods.FILETIME idle2, kernel2, user2;
            NativeMethods.GetSystemTimes(out idle2, out kernel2, out user2);
            ulong sys2 = kernel2.ToUlong() + user2.ToUlong();
            ulong sysDelta = sys2 > sys1 ? sys2 - sys1 : 0;

            var items = new List<CpuHogItem>();
            if (sysDelta == 0) return items;

            var procs2 = Process.GetProcesses();
            foreach (var p in procs2)
            {
                try
                {
                    int pid = p.Id;
                    if (snap1.TryGetValue(pid, out var firstSnap))
                    {
                        TimeSpan cpu2 = p.TotalProcessorTime;
                        double procDelta = (cpu2 - firstSnap.Cpu).TotalMilliseconds * 10000.0; // 100ns units
                        if (procDelta > 0)
                        {
                            double percent = (procDelta / sysDelta) * 100.0;
                            items.Add(new CpuHogItem
                            {
                                Pid = pid,
                                Name = firstSnap.Name,
                                CpuPercent = Math.Round(Math.Clamp(percent, 0.0, 100.0), 1),
                                Priority = firstSnap.Priority
                            });
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            return items;
        }

        public void Dispose()
        {
            _watchdogTimer?.Dispose();
        }
    }
}
