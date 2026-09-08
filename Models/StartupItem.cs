using System.Windows.Media;

namespace NovaOptimizer.Models
{
    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public ImageSource? Icon { get; set; }
        public string Impact { get; set; } = "Medium";
    }
}
