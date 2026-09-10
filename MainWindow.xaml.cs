using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaOptimizer.Native;
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
        private TrayIconManager? _trayManager;
        private bool _isSyncingStartupCheck;

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
            _boostView.OnStatusNotification += (msg) => ShowNotification(msg, "⚡");
            _processesView.OnStatusNotification += (msg) => ShowNotification(msg, "📋");
            _startupAndTweaksView.OnStatusNotification += (msg) => ShowNotification(msg, "⚙️");
            _hungLogView.OnStatusNotification += (msg) => ShowNotification(msg, "🔍");

            // Watchdog live notifications & badge
            _hungWatchdog.OnHungDetected += (record) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification($"Hung detected: {record.ProcessName} (PID {record.PID})", "⚠️");
                    UpdateHungBadge();
                });
            };
            _hungWatchdog.OnHungRecovered += (record) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification($"Recovered: {record.ProcessName} after {record.DisplayDuration}", "✅");
                    UpdateHungBadge();
                });
            };

            _turboBoost.OnProfileChanged += (profile) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (profile == BoostProfile.GameMode)
                    {
                        TxtBadgeMode.Text = "🔥 GAME BOOST";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentRed");
                        BadgeMode.Background = (System.Windows.Media.Brush)FindResource("BadgeBgDanger");
                        BadgeMode.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderDanger");
                    }
                    else if (profile == BoostProfile.WorkMode)
                    {
                        TxtBadgeMode.Text = "💼 WORK BOOST";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentPrimary");
                        BadgeMode.Background = (System.Windows.Media.Brush)FindResource("BadgeBgPrimary");
                        BadgeMode.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderPrimary");
                    }
                    else if (profile == BoostProfile.StudyMode)
                    {
                        TxtBadgeMode.Text = "📚 STUDY MODE";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlue");
                        BadgeMode.Background = (System.Windows.Media.Brush)FindResource("BadgeBgInfo");
                        BadgeMode.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderInfo");
                    }
                    else
                    {
                        TxtBadgeMode.Text = "🚀 Standard";
                        TxtBadgeMode.Foreground = (System.Windows.Media.Brush)FindResource("AccentPurple");
                        BadgeMode.Background = (System.Windows.Media.Brush)FindResource("BadgeBgDefault");
                        BadgeMode.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderDefault");
                    }
                });
            };

            // Set default view and pause inactive views
            MainContent.Content = _boostView;
            _boostView.ResumeMonitoring();
            _processesView.PauseMonitoring();
            _performanceView.PauseMonitoring();
            _hungLogView.PauseMonitoring();

            // Timer for header RAM update
            _headerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _headerTimer.Tick += (s, e) =>
            {
                var metrics = _ramOptimizer.GetMemoryMetrics();
                string ramStr = $"RAM: {metrics.InUsePercent:F0}% ({metrics.InUseGB:F1} / {metrics.TotalPhysicalGB:F1} GB)";
                TxtHeaderRam.Text = ramStr;
                _trayManager?.UpdateTooltip($"NovaOptimizer — {ramStr}");
            };
            _headerTimer.Start();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _toastTimer.Tick += (s, e) =>
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                ToastBanner.BeginAnimation(OpacityProperty, fadeOut);
                _toastTimer.Stop();
            };

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _trayManager = new TrayIconManager(this);
                _trayManager.Initialize();
                _trayManager.OnQuickCleanRequested += () => BtnQuickClean_Click(this, new RoutedEventArgs());
                _trayManager.OnToggleBoostRequested += () =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        if (_turboBoost.ActiveProfile == BoostProfile.None)
                            await _turboBoost.ActivateBoostAsync(BoostProfile.GameMode);
                        else
                            await _turboBoost.DeactivateBoostAsync();
                    });
                };
            }
            catch { }
            UpdateHungBadge();

            // Sync Start with Windows checkbox
            _startupManager.OnNovaStartupChanged += (enabled) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _isSyncingStartupCheck = true;
                    ChkStartWithWindows.IsChecked = enabled;
                    _isSyncingStartupCheck = false;
                });
            };

            _isSyncingStartupCheck = true;
            ChkStartWithWindows.IsChecked = _startupManager.IsNovaStartupEnabled();
            _isSyncingStartupCheck = false;

            // If launched at boot with --autostart, hide window into tray
            if (System.Linq.Enumerable.Any(Environment.GetCommandLineArgs(), a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)))
            {
                Hide();
                _trayManager?.UpdateTooltip("NovaOptimizer — Running in background");
            }
        }

        private void ChkStartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncingStartupCheck) return;
            bool enable = ChkStartWithWindows.IsChecked == true;
            _startupManager.SetNovaStartup(enable);
            ShowNotification(enable 
                ? "NovaOptimizer registered to start with Windows." 
                : "NovaOptimizer removed from Windows startup.", "🚀");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ChkMinimizeToTray.IsChecked == true)
            {
                e.Cancel = true;
                Hide();
                ShowNotification("NovaOptimizer is still running in background.", "ℹ️");
            }
            else
            {
                _trayManager?.Dispose();
            }
        }

        private void UpdateHungBadge()
        {
            int count = _hungWatchdog.GetActiveHangCount();
            if (count > 0)
            {
                TxtHungBadge.Text = count.ToString();
                BadgeHungCount.Visibility = Visibility.Visible;
            }
            else
            {
                BadgeHungCount.Visibility = Visibility.Collapsed;
            }
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                // Pause background monitoring on inactive views
                _boostView.PauseMonitoring();
                _processesView.PauseMonitoring();
                _performanceView.PauseMonitoring();
                _hungLogView.PauseMonitoring();

                switch (tag)
                {
                    case "Boost":
                        MainContent.Content = _boostView;
                        _boostView.ResumeMonitoring();
                        break;
                    case "Processes":
                        MainContent.Content = _processesView;
                        _processesView.ResumeMonitoring();
                        break;
                    case "Performance":
                        MainContent.Content = _performanceView;
                        _performanceView.ResumeMonitoring();
                        break;
                    case "Tweaks":
                        MainContent.Content = _startupAndTweaksView;
                        break;
                    case "HungLog":
                        MainContent.Content = _hungLogView;
                        _hungLogView.ResumeMonitoring();
                        break;
                }
            }
        }

        private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            BtnQuickClean.IsEnabled = false;
            string originalContent = BtnQuickClean.Content?.ToString() ?? "⚡ Quick Clean";
            BtnQuickClean.Content = "⚡ Cleaning...";

            try
            {
                long freed = await _ramOptimizer.DeepCleanRamAsync();
                double mb = freed / (1024.0 * 1024.0);
                ShowNotification($"Quick Clean: {mb:F0} MB physical RAM liberated!", "✨");
            }
            catch { }
            finally
            {
                BtnQuickClean.Content = originalContent;
                BtnQuickClean.IsEnabled = true;
            }
        }

        public void ShowNotification(string message, string icon = "⚡")
        {
            Dispatcher.Invoke(() =>
            {
                TxtToastIcon.Text = icon;
                TxtNotification.Text = message;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                ToastBanner.BeginAnimation(OpacityProperty, fadeIn);

                _toastTimer.Stop();
                _toastTimer.Start();
            });
        }

        #region Window Controls
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (ChkMinimizeToTray.IsChecked == true)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                BtnMaximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion
    }
}
