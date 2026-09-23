using System;
using System.Collections.Generic;

namespace NovaOptimizer.Native
{
    public static class SystemProcessAllowlist
    {
        private static readonly HashSet<string> CoreCriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system",
            "idle",
            "csrss",
            "smss",
            "wininit",
            "winlogon",
            "services",
            "lsass",
            "svchost",
            "fontdrvhost",
            "dwm",
            "registry",
            "memcompression"
        };

        public static bool IsProtected(string? processName, bool includeExplorer = false)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;

            if (CoreCriticalProcesses.Contains(processName))
                return true;

            if (includeExplorer && string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static IReadOnlySet<string> GetCoreProcesses() => CoreCriticalProcesses;
    }
}
