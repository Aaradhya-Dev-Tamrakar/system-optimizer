using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaOptimizer.Native;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class PerformanceView : UserControl
    {
        private readonly RamOptimizerService _ramOptimizer;
        private readonly DispatcherTimer _timer;

        private ulong _lastKernel;
        private ulong _lastUser;
        private ulong _lastIdle;

        public PerformanceView(RamOptimizerService ramOptimizer)
        {
            InitializeComponent();
            _ramOptimizer = ramOptimizer;

            TxtProcessorName.Text = $"{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "x64 Processor"}";
            TxtCores.Text = $"Logical Processors: {Environment.ProcessorCount}";

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => UpdateMetrics();
            _timer.Start();

            UpdateMetrics();
        }

        public void PauseMonitoring()
        {
            _timer.Stop();
        }

        public void ResumeMonitoring()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                UpdateMetrics();
            }
        }

        private void UpdateMetrics()
        {
            // Update CPU
            NativeMethods.FILETIME idleTime, kernelTime, userTime;
            NativeMethods.GetSystemTimes(out idleTime, out kernelTime, out userTime);

            ulong currentIdle = idleTime.ToUlong();
            ulong currentKernel = kernelTime.ToUlong();
            ulong currentUser = userTime.ToUlong();

            ulong kernelDelta = currentKernel > _lastKernel ? currentKernel - _lastKernel : 0;
            ulong userDelta = currentUser > _lastUser ? currentUser - _lastUser : 0;
            ulong idleDelta = currentIdle > _lastIdle ? currentIdle - _lastIdle : 0;

            ulong totalSys = kernelDelta + userDelta;
            if (totalSys > 0)
            {
                double cpuPercent = (double)(totalSys - idleDelta) / totalSys * 100.0;
                if (cpuPercent < 0) cpuPercent = 0;
                if (cpuPercent > 100) cpuPercent = 100;

                TxtCpuOverall.Text = $"{cpuPercent:F0}%";
                PbCpu.Value = cpuPercent;
                GraphCpu.AddValue(cpuPercent);
            }

            _lastKernel = currentKernel;
            _lastUser = currentUser;
            _lastIdle = currentIdle;

            // Update RAM
            var metrics = _ramOptimizer.GetMemoryMetrics();
            TxtRamOverall.Text = $"{metrics.InUsePercent:F0}%";
            PbRam.Value = metrics.InUsePercent;
            GraphRam.AddValue(metrics.InUsePercent);

            TxtRamTotals.Text = $"{metrics.InUseGB:F1} / {metrics.TotalPhysicalGB:F1} GB";
            TxtRamAvailable.Text = $"Available: {metrics.AvailableGB:F1} GB";
            TxtCommitted.Text = $"{metrics.CommitTotalGB:F1} / {metrics.CommitLimitGB:F1} GB";
            TxtCached.Text = $"{metrics.StandbyGB:F1} GB";
            TxtPagedPool.Text = $"{metrics.PagedPoolBytes / (1024.0 * 1024.0):F0} MB";
            TxtNonPagedPool.Text = $"{metrics.NonPagedPoolBytes / (1024.0 * 1024.0):F0} MB";

            // Zero-allocation instantaneous process count from kernel performance info
            TxtProcessCount.Text = $"{metrics.ProcessCount}";

            TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            TxtUptime.Text = $"{uptime.Days}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s";
        }
    }
}
