using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaOptimizer.Models
{
    public class ProcessItem : INotifyPropertyChanged
    {
        private double _workingSetMB;
        private double _privateMemoryMB;
        private double _cpuPercent;
        private string _priority = "Normal";
        private string _status = "Running";
        private string _description = string.Empty;
        private string _filePath = string.Empty;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

        public double WorkingSetMB
        {
            get => _workingSetMB;
            set
            {
                if (Math.Abs(_workingSetMB - value) > 0.05)
                {
                    _workingSetMB = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayRam));
                }
            }
        }

        public double PrivateMemoryMB
        {
            get => _privateMemoryMB;
            set
            {
                if (Math.Abs(_privateMemoryMB - value) > 0.05)
                {
                    _privateMemoryMB = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CpuPercent
        {
            get => _cpuPercent;
            set
            {
                if (Math.Abs(_cpuPercent - value) > 0.05)
                {
                    _cpuPercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayCpu));
                }
            }
        }

        public string Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSystemCritical { get; set; }
        public bool HasWindow { get; set; }
        public System.Windows.Media.ImageSource? Icon { get; set; }

        public bool IsHighRam => WorkingSetMB >= 500;
        public bool IsHighCpu => CpuPercent >= 5.0;
        public bool IsHung => Status == "Not Responding";

        public string DisplayRam => $"{WorkingSetMB:F1} MB";
        public string DisplayCpu => $"{CpuPercent:F1}%";

        public void UpdateFrom(ProcessItem other)
        {
            WorkingSetMB = other.WorkingSetMB;
            PrivateMemoryMB = other.PrivateMemoryMB;
            CpuPercent = other.CpuPercent;
            Priority = other.Priority;
            Status = other.Status;
            HasWindow = other.HasWindow;
            if (Icon == null && other.Icon != null)
                Icon = other.Icon;
            if (string.IsNullOrEmpty(Description) && !string.IsNullOrEmpty(other.Description))
                Description = other.Description;
            if (string.IsNullOrEmpty(FilePath) && !string.IsNullOrEmpty(other.FilePath))
                FilePath = other.FilePath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
