using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaOptimizer.Models
{
    public class ProcessItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double WorkingSetMB { get; set; }
        public double PrivateMemoryMB { get; set; }
        public double CpuPercent { get; set; }
        public string Priority { get; set; } = "Normal";
        public string Status { get; set; } = "Running";
        public string FilePath { get; set; } = string.Empty;
        public bool IsSystemCritical { get; set; }

        public string DisplayRam => $"{WorkingSetMB:F1} MB";
        public string DisplayCpu => $"{CpuPercent:F1}%";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
