using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinGovernor.Services
{
    public sealed record PowerPlanInfo(Guid Id, string Name, bool IsActive)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Standalone Windows power plan control. Intentionally decoupled from Game/Work/Study boost
    /// profiles: boosts never touch the active scheme, and this service never touches services/RAM.
    /// </summary>
    public class PowerPlanService
    {
        private static readonly Regex PlanLine = new(
            @"GUID:\s*(?<guid>[0-9a-fA-F\-]{36})\s*\((?<name>[^)]*)\)(?<active>\s*\*)?",
            RegexOptions.Compiled);

        public List<PowerPlanInfo> GetPlans()
        {
            var plans = new List<PowerPlanInfo>();
            string output = RunPowerCfg("/list");
            foreach (Match m in PlanLine.Matches(output))
            {
                if (Guid.TryParse(m.Groups["guid"].Value, out var id))
                {
                    plans.Add(new PowerPlanInfo(id, m.Groups["name"].Value.Trim(), m.Groups["active"].Success));
                }
            }
            return plans;
        }

        public Guid? GetActivePlanId()
        {
            var m = PlanLine.Match(RunPowerCfg("/getactivescheme"));
            return m.Success && Guid.TryParse(m.Groups["guid"].Value, out var id) ? id : null;
        }

        public bool SetActivePlan(Guid id)
        {
            RunPowerCfg($"/setactive {id}");
            return GetActivePlanId() == id;
        }

        private static string RunPowerCfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return string.Empty;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
