namespace NovaOptimizer.Models
{
    public class MemoryMetrics
    {
        public ulong TotalPhysicalBytes { get; set; }
        public ulong AvailableBytes { get; set; }
        public ulong InUseBytes => TotalPhysicalBytes > AvailableBytes ? TotalPhysicalBytes - AvailableBytes : 0;
        public ulong StandbyBytes { get; set; }
        public ulong ModifiedBytes { get; set; }
        public ulong FreeBytes => AvailableBytes > StandbyBytes ? AvailableBytes - StandbyBytes : 0;
        public ulong CommitTotalBytes { get; set; }
        public ulong CommitLimitBytes { get; set; }
        public ulong PagedPoolBytes { get; set; }
        public ulong NonPagedPoolBytes { get; set; }
        public uint ProcessCount { get; set; }
        public uint ThreadCount { get; set; }
        public uint HandleCount { get; set; }

        public double TotalPhysicalGB => (double)TotalPhysicalBytes / (1024.0 * 1024 * 1024);
        public double AvailableGB => (double)AvailableBytes / (1024.0 * 1024 * 1024);
        public double InUseGB => (double)InUseBytes / (1024.0 * 1024 * 1024);
        public double StandbyGB => (double)StandbyBytes / (1024.0 * 1024 * 1024);
        public double FreeGB => (double)FreeBytes / (1024.0 * 1024 * 1024);
        public double CommitTotalGB => (double)CommitTotalBytes / (1024.0 * 1024 * 1024);
        public double CommitLimitGB => (double)CommitLimitBytes / (1024.0 * 1024 * 1024);

        public double InUsePercent => TotalPhysicalBytes > 0 ? ((double)InUseBytes / TotalPhysicalBytes) * 100.0 : 0;
        public double StandbyPercent => TotalPhysicalBytes > 0 ? ((double)StandbyBytes / TotalPhysicalBytes) * 100.0 : 0;
        public double FreePercent => TotalPhysicalBytes > 0 ? ((double)FreeBytes / TotalPhysicalBytes) * 100.0 : 0;
    }
}
