using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaOptimizer.Services;
using NovaOptimizer.Views;

namespace NovaOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly RamOptimizerService _ramOptimizer;
        private readonly ProcessMonitorService _processMonitor;
        private readonly TurboBoostService _turboBoost;
        private readonly StartupManagerService _startupManager;
        private readonly SystemTweakService _systemTweaks;
        private readonly HungProcessWatchdogService _hungWatchdog;

        private readonly TurboBoostView _boostView;
        private readonly ProcessesView _processesView;
        private readonly PerformanceView _performanceView;
        private readonly StartupAndTweaksView _startupAndTweaksView;
        private readonly HungLogView _hungLogView;

        private readonly DispatcherTimer _headerTimer;
        private readonly DispatcherTimer _toastTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize Services
            _ramOptimizer = new RamOptimizerService();
            _processMonitor = new ProcessMonitorService();
            _turboBoost = new TurboBoostService(_ramOptimizer);
            _startupManager = new StartupManagerService();
            _systemTweaks = new SystemTweakService();
            _hungWatchdog = new HungProcessWatchdogService();

            // Initialize Views
            _boostView = new TurboBoostView(_ramOptimizer, _turboBoost);
            _processesView = new ProcessesView(_processMonitor);
            _performanceView = new PerformanceView(_ramOptimizer);
            _startupAndTweaksView = new StartupAndTweaksView(_startupManager, _systemTweaks);
            _hungLogView = new HungLogView(_hungWatchdog);

            // Connect notifications
            _boostView.OnStatusNotification += ShowNotification;
            _processesView.OnStatusNotification += ShowNotification;
            _startupAndTweaksView.OnStatusNotification += ShowNotification;
            _hungLogView.OnStatusNotification += ShowNotification;

            // Watchdog live notifications
            _hungWatchdog.OnHungDetected += (record) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification($"⚠️ Hung detected: {record.ProcessName} (PID {record.PID})");
                });
            };
            _hungWatchdog.OnHungRecovered += (record) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification($"✅ Recovered: {record.ProcessName} after {record.DisplayDuration}");
                });
            };

            _turboBoost.OnProfileChanged += (profile) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (profile == BoostProfile.GameMode)
                    {
                        TxtBadgeMode.Text = "🔥 GAME BOOST";
                        TxtBadgeMode.Foreground = System.Windows.Media.Brushes.OrangeRed;
                        BadgeMode.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3D161F")!;
                    }
                    else if (profile == BoostProfile.WorkMode)
                    {
                        TxtBadgeMode.Text = "💼 WORK BOOST";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentCyan");
                        BadgeMode.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#16283D")!;
                    }
                    else
                    {
                        TxtBadgeMode.Text = "🚀 Standard";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentPurple");
                        BadgeMode.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#241B33")!;
                    }
                });
            };

            // Set default view
            MainContent.Content = _boostView;

            // Timer for header RAM update
            _headerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _headerTimer.Tick += (s, e) =>
            {
                var metrics = _ramOptimizer.GetMemoryMetrics();
                TxtHeaderRam.Text = $"RAM: {metrics.InUsePercent:F0}% ({metrics.InUseGB:F1} / {metrics.TotalPhysicalGB:F1} GB)";
            };
            _headerTimer.Start();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastTimer.Tick += (s, e) =>
            {
                TxtNotification.Text = "";
                _toastTimer.Stop();
            };
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                switch (tag)
                {
                    case "Boost":
                        MainContent.Content = _boostView;
                        break;
                    case "Processes":
                        MainContent.Content = _processesView;
                        break;
                    case "Performance":
                        MainContent.Content = _performanceView;
                        break;
                    case "Tweaks":
                        MainContent.Content = _startupAndTweaksView;
                        break;
                    case "HungLog":
                        MainContent.Content = _hungLogView;
                        break;
                }
            }
        }

        private void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            long freed = _ramOptimizer.DeepCleanRam();
            double mb = freed / (1024.0 * 1024.0);
            ShowNotification($"⚡ Quick RAM Clean: {mb:F0} MB physical memory liberated!");
        }

        private void ShowNotification(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtNotification.Text = message;
                _toastTimer.Stop();
                _toastTimer.Start();
            });
        }
    }
}
