using System;
using System.Text.Json.Serialization;

namespace NovaOptimizer.Models
{
    public class HungProcessRecord
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = string.Empty;

        [JsonPropertyName("pid")]
        public int PID { get; set; }

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("cpuPercent")]
        public double CpuPercent { get; set; }

        [JsonPropertyName("workingSetMB")]
        public double WorkingSetMB { get; set; }

        [JsonPropertyName("parentProcessName")]
        public string ParentProcessName { get; set; } = string.Empty;

        [JsonPropertyName("parentPID")]
        public int ParentPID { get; set; }

        [JsonPropertyName("hangDurationSeconds")]
        public double HangDurationSeconds { get; set; }

        [JsonPropertyName("recovered")]
        public bool Recovered { get; set; }

        [JsonPropertyName("recoveredAt")]
        public DateTime? RecoveredAt { get; set; }

        // Display helpers for the UI
        [JsonIgnore]
        public string DisplayTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        [JsonIgnore]
        public string DisplayDuration =>
            HangDurationSeconds < 60 ? $"{HangDurationSeconds:F0}s"
            : HangDurationSeconds < 3600 ? $"{HangDurationSeconds / 60.0:F1}m"
            : $"{HangDurationSeconds / 3600.0:F1}h";

        [JsonIgnore]
        public string DisplayRam => $"{WorkingSetMB:F1} MB";

        [JsonIgnore]
        public string DisplayCpu => $"{CpuPercent:F1}%";

        [JsonIgnore]
        public string DisplayStatus =>
            Recovered ? "✅ Recovered"
            : HangDurationSeconds > 0 ? "🔴 Hung"
            : "⚪ Logged";

        [JsonIgnore]
        public string DisplayParent =>
            string.IsNullOrEmpty(ParentProcessName) ? $"PID {ParentPID}" : $"{ParentProcessName} ({ParentPID})";
    }

    /// <summary>
    /// Aggregated view of a repeat-offender process for the summary table.
    /// </summary>
    public class HungProcessSummary
    {
        public string ProcessName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int HangCount { get; set; }
        public DateTime LastHung { get; set; }
        public double AvgDurationSeconds { get; set; }
        public double TotalRamWastedMB { get; set; }
        public int RecoveryCount { get; set; }

        // Display helpers
        public string DisplayLastHung => LastHung.ToString("yyyy-MM-dd HH:mm:ss");
        public string DisplayAvgDuration =>
            AvgDurationSeconds < 60 ? $"{AvgDurationSeconds:F0}s"
            : AvgDurationSeconds < 3600 ? $"{AvgDurationSeconds / 60.0:F1}m"
            : $"{AvgDurationSeconds / 3600.0:F1}h";
        public string DisplayTotalRam => $"{TotalRamWastedMB:F0} MB";
        public string DisplayRecoveryRate =>
            HangCount > 0 ? $"{(RecoveryCount * 100.0 / HangCount):F0}%" : "N/A";
    }
}
