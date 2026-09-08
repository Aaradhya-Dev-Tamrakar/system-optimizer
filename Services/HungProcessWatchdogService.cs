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

namespace NovaOptimizer.Services
{
    public class HungProcessWatchdogService : IDisposable
    {
        private static readonly string LogDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaOptimizer");
        private static readonly string LogFilePath =
            Path.Combine(LogDirectory, "hung_process_log.jsonl");

        // Tracks currently-hung PIDs → first detection time
        private readonly ConcurrentDictionary<int, HungProcessRecord> _activeHangs = new();
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
            // Run every 3 seconds
            _watchTimer = new Timer(ScanCallback, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
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
            Process[] processes;

            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }

            foreach (var p in processes)
            {
                try
                {
                    bool responding = true;
                    try
                    {
                        // .Responding only works for processes with a window/message loop
                        responding = p.Responding;
                    }
                    catch
                    {
                        continue; // Can't check — skip
                    }

                    if (!responding)
                    {
                        int pid = p.Id;
                        currentlyHungPids.Add(pid);

                        // Already tracking this hang episode?
                        if (_activeHangs.ContainsKey(pid))
                        {
                            // Update duration on the active record
                            if (_activeHangs.TryGetValue(pid, out var existing))
                            {
                                existing.HangDurationSeconds = (DateTime.Now - existing.Timestamp).TotalSeconds;
                            }
                            continue;
                        }

                        // --- New hang detected! Build the forensic record ---
                        var record = new HungProcessRecord
                        {
                            Timestamp = DateTime.Now,
                            ProcessName = p.ProcessName,
                            PID = pid,
                            Recovered = false
                        };

                        // Capture memory
                        try
                        {
                            record.WorkingSetMB = p.WorkingSet64 / (1024.0 * 1024.0);
                        }
                        catch { }

                        // Capture CPU (snapshot — won't be a delta, but gives a rough idea)
                        try
                        {
                            record.CpuPercent = Math.Round(p.TotalProcessorTime.TotalMilliseconds /
                                (Environment.ProcessorCount * (DateTime.Now - p.StartTime).TotalMilliseconds) * 100.0, 1);
                            if (double.IsNaN(record.CpuPercent) || double.IsInfinity(record.CpuPercent))
                                record.CpuPercent = 0;
                        }
                        catch { record.CpuPercent = 0; }

                        // Capture executable path & description
                        try
                        {
                            var module = p.MainModule;
                            if (module != null)
                            {
                                record.FilePath = module.FileName ?? string.Empty;
                                record.Description = module.FileVersionInfo.FileDescription ?? p.ProcessName;
                            }
                        }
                        catch
                        {
                            record.FilePath = string.Empty;
                            record.Description = p.ProcessName;
                        }

                        // Capture parent process via WMI
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(
                                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                int parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                                record.ParentPID = parentPid;
                                try
                                {
                                    using var parentProc = Process.GetProcessById(parentPid);
                                    record.ParentProcessName = parentProc.ProcessName;
                                }
                                catch
                                {
                                    record.ParentProcessName = "(exited)";
                                }
                                break;
                            }
                        }
                        catch
                        {
                            record.ParentProcessName = "(unknown)";
                        }

                        _activeHangs[pid] = record;
                        AppendRecord(record);
                        OnHungDetected?.Invoke(record);
                    }
                }
                catch
                {
                    // Ignore per-process errors
                }
                finally
                {
                    p.Dispose();
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
                    File.AppendAllText(LogFilePath, json + Environment.NewLine);
                }
            }
            catch
            {
                // Don't crash if we can't write
            }
        }

        /// <summary>
        /// Returns all records from the log file.
        /// </summary>
        public List<HungProcessRecord> GetAllRecords()
        {
            var records = new List<HungProcessRecord>();

            lock (_fileLock)
            {
                if (!File.Exists(LogFilePath)) return records;

                try
                {
                    foreach (var line in File.ReadAllLines(LogFilePath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var record = JsonSerializer.Deserialize<HungProcessRecord>(line);
                            if (record != null) records.Add(record);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return records;
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
