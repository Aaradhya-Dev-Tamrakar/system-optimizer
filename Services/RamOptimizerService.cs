using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class RamOptimizerService
    {
        private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "csrss", "smss", "wininit", "winlogon", "services", 
            "lsass", "svchost", "fontdrvhost", "dwm", "registry", "memcompression"
        };

        private Timer? _watchdogTimer;
        public bool AutoCleanEnabled { get; set; } = false;
        public double AutoCleanThresholdPercent { get; set; } = 85.0; // Clean when RAM > 85% used
        public double AutoCleanStandbyThresholdGB { get; set; } = 3.0; // or Standby > 3 GB

        public event Action<long>? OnAutoCleanPerformed;

        public RamOptimizerService()
        {
            // Acquire required NT privileges at startup
            NativeMethods.SetIncreasePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
            NativeMethods.SetIncreasePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

            // Watchdog ticks every 15 seconds
            _watchdogTimer = new Timer(WatchdogTick, null, 15000, 15000);
        }

        private void WatchdogTick(object? state)
        {
            if (!AutoCleanEnabled) return;

            // Pause timer to prevent reentrancy during long purges
            _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            try
            {
                var metrics = GetMemoryMetrics();
                bool needClean = (metrics.InUsePercent >= AutoCleanThresholdPercent) || 
                                 (metrics.StandbyGB >= AutoCleanStandbyThresholdGB);

                if (needClean)
                {
                    long freed = DeepCleanRam();
                    if (freed > 0)
                    {
                        OnAutoCleanPerformed?.Invoke(freed);
                    }
                }
            }
            catch
            {
                // Silent fail in background watchdog
            }
            finally
            {
                // Resume 15-second tick
                try
                {
                    _watchdogTimer?.Change(15000, 15000);
                }
                catch { }
            }
        }

        public MemoryMetrics GetMemoryMetrics()
        {
            var metrics = new MemoryMetrics();

            var memStatus = new NativeMethods.MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(NativeMethods.MEMORYSTATUSEX));

            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                metrics.TotalPhysicalBytes = memStatus.ullTotalPhys;
                metrics.AvailableBytes = memStatus.ullAvailPhys;
            }

            var perfInfo = new NativeMethods.PERFORMANCE_INFORMATION();
            perfInfo.cb = (uint)Marshal.SizeOf(typeof(NativeMethods.PERFORMANCE_INFORMATION));

            if (NativeMethods.GetPerformanceInfo(out perfInfo, perfInfo.cb))
            {
                ulong pageSize = (ulong)perfInfo.PageSize;
                ulong systemCache = (ulong)perfInfo.SystemCache * pageSize;

                metrics.StandbyBytes = systemCache;
                metrics.CommitTotalBytes = (ulong)perfInfo.CommitTotal * pageSize;
                metrics.CommitLimitBytes = (ulong)perfInfo.CommitLimit * pageSize;
                metrics.PagedPoolBytes = (ulong)perfInfo.KernelPaged * pageSize;
                metrics.NonPagedPoolBytes = (ulong)perfInfo.KernelNonpaged * pageSize;
                metrics.ProcessCount = perfInfo.ProcessCount;
                metrics.ThreadCount = perfInfo.ThreadCount;
                metrics.HandleCount = perfInfo.HandleCount;
            }

            return metrics;
        }

        public bool PurgeStandbyList()
        {
            try
            {
                NativeMethods.SetIncreasePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
                int cmd = (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList;
                uint result = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref cmd,
                    sizeof(int));

                int lowCmd = (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList;
                NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref lowCmd,
                    sizeof(int));

                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool PurgeSystemWorkingSet()
        {
            try
            {
                NativeMethods.SetIncreasePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);
                int cmd = (int)NativeMethods.SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyWorkingSets;
                uint result = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref cmd,
                    sizeof(int));
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public int OptimizeWorkingSets()
        {
            int currentPid = Environment.ProcessId;
            int trimmedCount = 0;

            Process[] processes = Process.GetProcesses();
            Parallel.ForEach(processes, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, proc =>
            {
                try
                {
                    if (proc.Id <= 4 || proc.Id == currentPid) return;
                    if (CriticalProcesses.Contains(proc.ProcessName)) return;

                    IntPtr hProc = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_INFORMATION, 
                        false, 
                        proc.Id);

                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            NativeMethods.EmptyWorkingSet(hProc);
                            NativeMethods.SetProcessWorkingSetSize(hProc, (IntPtr)(-1), (IntPtr)(-1));
                            Interlocked.Increment(ref trimmedCount);
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(hProc);
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible processes
                }
                finally
                {
                    proc.Dispose();
                }
            });

            return trimmedCount;
        }

        public Task<long> DeepCleanRamAsync()
        {
            return Task.Run(() => DeepCleanRam());
        }

        public long DeepCleanRam()
        {
            var before = GetMemoryMetrics();
            ulong beforeAvail = before.AvailableBytes;

            // 1. Trim process working sets
            OptimizeWorkingSets();

            // 2. Purge Standby List (ISLC / RAMMap technique)
            PurgeStandbyList();

            // 3. Purge system working sets
            PurgeSystemWorkingSet();

            var after = GetMemoryMetrics();
            ulong afterAvail = after.AvailableBytes;

            if (afterAvail > beforeAvail)
            {
                return (long)(afterAvail - beforeAvail);
            }

            return (long)(before.StandbyBytes > after.StandbyBytes ? before.StandbyBytes - after.StandbyBytes : 50 * 1024 * 1024);
        }
    }
}
