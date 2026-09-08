using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using NovaOptimizer.Models;
using NovaOptimizer.Native;

namespace NovaOptimizer.Services
{
    public class HungProcessWatchdogService : IDisposable
    {
        private static readonly string LogDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaOptimizer");
        private static readonly string LogFilePath =
            Path.Combine(LogDirectory, "hung_process_log.jsonl");

        private readonly ConcurrentDictionary<int, HungProcessRecord> _activeHangs = new();
        private readonly List<HungProcessRecord> _cachedRecords = new();
        private readonly Timer _watchTimer;
        private readonly object _fileLock = new();
        private bool _disposed;

        /// <summary>
        /// Fired when a new hung process is first detected (not on every tick).
        /// </summary>
        public event Action<HungProcessRecord>? OnHungDetected;

        /// <summary>
        /// Fired when a previously-hung process recovers on its own.
        /// </summary>
        public event Action<HungProcessRecord>? OnHungRecovered;

        public HungProcessWatchdogService()
        {
            Directory.CreateDirectory(LogDirectory);
            LoadRecordsInitial();
            // Run every 3 seconds
            _watchTimer = new Timer(ScanCallback, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        }

        private void LoadRecordsInitial()
        {
            lock (_fileLock)
            {
                if (!File.Exists(LogFilePath)) return;
                try
                {
                    foreach (var line in File.ReadAllLines(LogFilePath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var record = JsonSerializer.Deserialize<HungProcessRecord>(line);
                            if (record != null) _cachedRecords.Add(record);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void ScanCallback(object? state)
        {
            try
            {
                Scan();
            }
            catch
            {
                // Never let the watchdog crash the app
            }
        }

        private void Scan()
        {
            var currentlyHungPids = new HashSet<int>();

            // Ultra-fast top-level window check via EnumWindows & IsHungAppWindow (< 1ms, non-blocking)
            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (NativeMethods.IsHungAppWindow(hWnd))
                    {
                        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                        if (pid > 4)
                        {
                            currentlyHungPids.Add((int)pid);
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            foreach (int pid in currentlyHungPids)
            {
                try
                {
                    // Already tracking this hang episode?
                    if (_activeHangs.TryGetValue(pid, out var existing))
                    {
                        existing.HangDurationSeconds = (DateTime.Now - existing.Timestamp).TotalSeconds;
                        continue;
                    }

                    // --- New hang detected! Build the forensic record ---
                    string procName = $"PID {pid}";
                    string filePath = string.Empty;
                    string description = procName;
                    double workingSetMB = 0;
                    double cpuPercent = 0;

                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        procName = p.ProcessName;
                        description = procName;

                        try { workingSetMB = p.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                        try
                        {
                            cpuPercent = Math.Round(p.TotalProcessorTime.TotalMilliseconds /
                                (Environment.ProcessorCount * (DateTime.Now - p.StartTime).TotalMilliseconds) * 100.0, 1);
                            if (double.IsNaN(cpuPercent) || double.IsInfinity(cpuPercent)) cpuPercent = 0;
                        }
                        catch { }
                    }
                    catch { }

                    try
                    {
                        string? path = NativeMethods.GetProcessFilePath(pid);
                        if (!string.IsNullOrEmpty(path))
                        {
                            filePath = path;
                            if (File.Exists(path))
                            {
                                var vi = FileVersionInfo.GetVersionInfo(path);
                                description = vi.FileDescription ?? procName;
                            }
                        }
                    }
                    catch { }

                    int parentPid = NativeMethods.GetParentProcessId(pid);
                    string parentProcessName = "(unknown)";
                    if (parentPid > 0)
                    {
                        try
                        {
                            using var parentProc = Process.GetProcessById(parentPid);
                            parentProcessName = parentProc.ProcessName;
                        }
                        catch
                        {
                            parentProcessName = "(exited)";
                        }
                    }

                    var record = new HungProcessRecord
                    {
                        Timestamp = DateTime.Now,
                        ProcessName = procName,
                        PID = pid,
                        FilePath = filePath,
                        Description = string.IsNullOrWhiteSpace(description) ? procName : description,
                        WorkingSetMB = workingSetMB,
                        CpuPercent = cpuPercent,
                        ParentPID = parentPid,
                        ParentProcessName = parentProcessName,
                        Recovered = false
                    };

                    _activeHangs[pid] = record;
                    AppendRecord(record);
                    OnHungDetected?.Invoke(record);
                }
                catch
                {
                    // Ignore per-process errors
                }
            }

            // --- Check for recoveries: previously hung PIDs that are now responding ---
            foreach (var kvp in _activeHangs.ToArray())
            {
                int pid = kvp.Key;
                if (currentlyHungPids.Contains(pid))
                    continue; // Still hung

                // Process is either responding again or has exited
                if (_activeHangs.TryRemove(pid, out var recoveredRecord))
                {
                    recoveredRecord.Recovered = true;
                    recoveredRecord.RecoveredAt = DateTime.Now;
                    recoveredRecord.HangDurationSeconds =
                        (recoveredRecord.RecoveredAt.Value - recoveredRecord.Timestamp).TotalSeconds;

                    AppendRecord(recoveredRecord);
                    OnHungRecovered?.Invoke(recoveredRecord);
                }
            }
        }

        private void AppendRecord(HungProcessRecord record)
        {
            try
            {
                string json = JsonSerializer.Serialize(record,
                    new JsonSerializerOptions { WriteIndented = false });

                lock (_fileLock)
                {
                    _cachedRecords.Add(record);
                    File.AppendAllText(LogFilePath, json + Environment.NewLine);
                }
            }
            catch
            {
                // Don't crash if we can't write
            }
        }

        /// <summary>
        /// Returns all records from the log file (cached in memory for high speed).
        /// </summary>
        public List<HungProcessRecord> GetAllRecords()
        {
            lock (_fileLock)
            {
                return new List<HungProcessRecord>(_cachedRecords);
            }
        }

        /// <summary>
        /// Returns repeat offenders grouped by process name + path, sorted by hang count descending.
        /// </summary>
        public List<HungProcessSummary> GetRepeatOffenders()
        {
            var records = GetAllRecords();

            return records
                .GroupBy(r => new { r.ProcessName, r.FilePath })
                .Select(g => new HungProcessSummary
                {
                    ProcessName = g.Key.ProcessName,
                    FilePath = g.Key.FilePath,
                    HangCount = g.Count(),
                    LastHung = g.Max(r => r.Timestamp),
                    AvgDurationSeconds = g.Average(r => r.HangDurationSeconds),
                    TotalRamWastedMB = g.Sum(r => r.WorkingSetMB),
                    RecoveryCount = g.Count(r => r.Recovered)
                })
                .OrderByDescending(s => s.HangCount)
                .ToList();
        }

        /// <summary>
        /// Gets records from the last N hours.
        /// </summary>
        public List<HungProcessRecord> GetRecentRecords(int hours = 24)
        {
            var cutoff = DateTime.Now.AddHours(-hours);
            return GetAllRecords().Where(r => r.Timestamp >= cutoff).ToList();
        }

        /// <summary>
        /// Returns the number of currently-tracked active hangs.
        /// </summary>
        public int GetActiveHangCount() => _activeHangs.Count;

        /// <summary>
        /// Gets the currently active hang records (live).
        /// </summary>
        public List<HungProcessRecord> GetActiveHangs() => _activeHangs.Values.ToList();

        /// <summary>
        /// Clears the persistent log file.
        /// </summary>
        public void ClearLog()
        {
            lock (_fileLock)
            {
                try
                {
                    _cachedRecords.Clear();
                    File.WriteAllText(LogFilePath, string.Empty);
                }
                catch { }
            }
        }

        /// <summary>
        /// Exports log records to a CSV file.
        /// </summary>
        public void ExportCsv(string outputPath)
        {
            var records = GetAllRecords();
            var lines = new List<string>
            {
                "Timestamp,Process Name,PID,File Path,Description,CPU%,RAM (MB),Parent Process,Parent PID,Hang Duration (s),Recovered,Recovered At"
            };

            foreach (var r in records)
            {
                lines.Add(string.Join(",",
                    Escape(r.DisplayTimestamp),
                    Escape(r.ProcessName),
                    r.PID,
                    Escape(r.FilePath),
                    Escape(r.Description),
                    r.CpuPercent,
                    $"{r.WorkingSetMB:F1}",
                    Escape(r.ParentProcessName),
                    r.ParentPID,
                    $"{r.HangDurationSeconds:F1}",
                    r.Recovered,
                    r.RecoveredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
                ));
            }

            File.WriteAllLines(outputPath, lines);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _watchTimer.Dispose();
            }
        }
    }
}
