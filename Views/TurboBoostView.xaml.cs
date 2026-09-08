using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class TurboBoostView : UserControl
    {
        private readonly RamOptimizerService _ramOptimizer;
        private readonly TurboBoostService _turboBoost;
        private readonly DispatcherTimer _timer;

        public event Action<string>? OnStatusNotification;

        public TurboBoostView(RamOptimizerService ramOptimizer, TurboBoostService turboBoost)
        {
            InitializeComponent();
            _ramOptimizer = ramOptimizer;
            _turboBoost = turboBoost;

            _turboBoost.OnLogMessage += LogMessage;
            _turboBoost.OnProfileChanged += ProfileChanged;
            _ramOptimizer.OnAutoCleanPerformed += (freed) =>
            {
                Dispatcher.Invoke(() =>
                {
                    double mb = freed / (1024.0 * 1024.0);
                    LogMessage($"⚡ [Auto-Watchdog] Purged {mb:F0} MB of standby memory silently.");
                });
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => UpdateMemoryUi();
            _timer.Start();

            UpdateMemoryUi();
        }

        private void UpdateMemoryUi()
        {
            var metrics = _ramOptimizer.GetMemoryMetrics();

            TxtInUse.Text = $"{metrics.InUseGB:F1} GB ({metrics.InUsePercent:F0}%)";
            TxtStandby.Text = $"{metrics.StandbyGB:F1} GB ({metrics.StandbyPercent:F0}%)";
            TxtFree.Text = $"{metrics.FreeGB:F1} GB ({metrics.FreePercent:F0}%)";

            double inUse = Math.Max(1, metrics.InUsePercent);
            double standby = Math.Max(1, metrics.StandbyPercent);
            double free = Math.Max(1, metrics.FreePercent);

            ColInUse.Width = new GridLength(inUse, GridUnitType.Star);
            ColStandby.Width = new GridLength(standby, GridUnitType.Star);
            ColFree.Width = new GridLength(free, GridUnitType.Star);
        }

        private async void BtnGameBoost_Click(object sender, RoutedEventArgs e)
        {
            if (_turboBoost.ActiveProfile == BoostProfile.GameMode)
            {
                await _turboBoost.DeactivateBoostAsync();
            }
            else
            {
                await _turboBoost.ActivateBoostAsync(BoostProfile.GameMode);
                OnStatusNotification?.Invoke("🔥 Game Boost Activated! System is fully optimized.");
            }
        }

        private async void BtnWorkBoost_Click(object sender, RoutedEventArgs e)
        {
            if (_turboBoost.ActiveProfile == BoostProfile.WorkMode)
            {
                await _turboBoost.DeactivateBoostAsync();
            }
            else
            {
                await _turboBoost.ActivateBoostAsync(BoostProfile.WorkMode);
                OnStatusNotification?.Invoke("💼 Work Boost Activated! Memory primed for heavy tasks.");
            }
        }

        private void BtnDeepClean_Click(object sender, RoutedEventArgs e)
        {
            long freed = _ramOptimizer.DeepCleanRam();
            double mb = freed / (1024.0 * 1024.0);
            LogMessage($"🧹 Manual Deep Purge: Liberated {mb:F0} MB of physical memory.");
            OnStatusNotification?.Invoke($"✨ Memory Optimized! {mb:F0} MB physical RAM liberated.");
            UpdateMemoryUi();
        }

        private void ChkAutoClean_Changed(object sender, RoutedEventArgs e)
        {
            _ramOptimizer.AutoCleanEnabled = ChkAutoClean.IsChecked == true;
            LogMessage(_ramOptimizer.AutoCleanEnabled 
                ? "Intelligent Auto-RAM Watchdog enabled (silent background cleaning)." 
                : "Intelligent Auto-RAM Watchdog disabled.");
        }

        private void ProfileChanged(BoostProfile profile)
        {
            Dispatcher.Invoke(() =>
            {
                if (profile == BoostProfile.GameMode)
                {
                    BtnGameBoost.Content = "Deactivate Game Boost";
                    BtnGameBoost.Background = System.Windows.Media.Brushes.DarkRed;
                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = (System.Windows.Media.Brush)FindResource("AccentCyan");
                }
                else if (profile == BoostProfile.WorkMode)
                {
                    BtnWorkBoost.Content = "Deactivate Work Boost";
                    BtnWorkBoost.Background = System.Windows.Media.Brushes.DarkRed;
                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = (System.Windows.Media.Brush)FindResource("AccentPurple");
                }
                else
                {
                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = (System.Windows.Media.Brush)FindResource("AccentPurple");
                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = (System.Windows.Media.Brush)FindResource("AccentCyan");
                }
            });
        }

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                TxtActivityLog.Text = $"[{timestamp}] {message}\n" + TxtActivityLog.Text;
            });
        }
    }
}
