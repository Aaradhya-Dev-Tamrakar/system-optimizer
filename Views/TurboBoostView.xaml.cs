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
        private readonly CpuOptimizerService _cpuOptimizer;
        private readonly DispatcherTimer _timer;
        private int _uiTickCount = 0;

        public event Action<string>? OnStatusNotification;

        public TurboBoostView(RamOptimizerService ramOptimizer, TurboBoostService turboBoost, CpuOptimizerService cpuOptimizer)
        {
            InitializeComponent();
            _ramOptimizer = ramOptimizer;
            _turboBoost = turboBoost;
            _cpuOptimizer = cpuOptimizer;

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
            _cpuOptimizer.OnCpuAutoTamed += (res) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LogMessage($"❄️ [Auto-Watchdog] {res.Summary}");
                    OnStatusNotification?.Invoke($"❄️ CPU Auto-Tamed: {res.TamedCount} background hogs calmed!");
                });
            };
            _cpuOptimizer.OnCpuCleanPerformed += (res) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LogMessage($"⚡ [CPU Clean] {res.Summary}");
                });
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                UpdateMemoryUi();
                UpdateCpuUi();
            };
            _timer.Start();

            ToggleAutoClean.IsChecked = _ramOptimizer.AutoCleanEnabled;
            ToggleAutoTameCpu.IsChecked = _cpuOptimizer.AutoTameEnabled;
            UpdateMemoryUi();
            UpdateCpuUi();

            Loaded += (s, e) => RootScrollViewer.ScrollToTop();
        }

        private void ScrollViewer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        public void PauseMonitoring()
        {
            _timer.Stop();
        }

        public void ResumeMonitoring()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                UpdateMemoryUi();
            }
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

        private System.Windows.Media.Brush GetBrush(string key, string fallbackHex = "#3B82F6")
        {
            try
            {
                if (TryFindResource(key) is System.Windows.Media.Brush b) return b;
            }
            catch { }
            return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(fallbackHex)!;
        }

        private Style? GetStyle(string key)
        {
            try
            {
                return TryFindResource(key) as Style;
            }
            catch { }
            return null;
        }

        private void UpdateCpuUi()
        {
            double cpu = _cpuOptimizer.GetSystemCpuUsage();
            PbCpuUsage.Value = cpu;

            if (cpu < 40.0)
            {
                TxtCpuLoadStatus.Text = $"Optimal ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = GetBrush("AccentGreen", "#10B981");
                BadgeCpuLoad.Background = GetBrush("BadgeBgSuccess", "#0B271E");
                BadgeCpuLoad.BorderBrush = GetBrush("BadgeBorderSuccess", "#144A37");
            }
            else if (cpu < 70.0)
            {
                TxtCpuLoadStatus.Text = $"Moderate ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = GetBrush("AccentYellow", "#EAB308");
                BadgeCpuLoad.Background = GetBrush("BadgeBgWarning", "#291E0E");
                BadgeCpuLoad.BorderBrush = GetBrush("BadgeBorderWarning", "#4D3819");
            }
            else
            {
                TxtCpuLoadStatus.Text = $"High Load ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = GetBrush("AccentRed", "#F43F5E");
                BadgeCpuLoad.Background = GetBrush("BadgeBgDanger", "#2A1319");
                BadgeCpuLoad.BorderBrush = GetBrush("BadgeBorderDanger", "#501E29");
            }

            _uiTickCount++;
            if (_uiTickCount % 2 == 0) // refresh hog list every 2 seconds
            {
                var hogs = _cpuOptimizer.GetTopCpuHogs(4);
                IcTopCpuHogs.ItemsSource = hogs;
            }
        }

        private async void BtnGameBoost_Click(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                OnStatusNotification?.Invoke($"⚠️ Could not activate Game Boost: {ex.Message}");
            }
        }

        private async void BtnWorkBoost_Click(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                OnStatusNotification?.Invoke($"⚠️ Could not activate Work Boost: {ex.Message}");
            }
        }

        private async void BtnStudyMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_turboBoost.ActiveProfile == BoostProfile.StudyMode)
                {
                    await _turboBoost.DeactivateBoostAsync();
                }
                else
                {
                    await _turboBoost.ActivateBoostAsync(BoostProfile.StudyMode);
                    OnStatusNotification?.Invoke("📚 Study Mode Activated! Distractions blocked & silent sustained profile enabled.");
                }
            }
            catch (Exception ex)
            {
                OnStatusNotification?.Invoke($"⚠️ Could not activate Study Mode: {ex.Message}");
            }
        }

        private async void BtnDeepClean_Click(object sender, RoutedEventArgs e)
        {
            BtnDeepClean.IsEnabled = false;
            string origContent = BtnDeepClean.Content?.ToString() ?? "🧹 Clean RAM Now";
            BtnDeepClean.Content = "⚡ Purging RAM...";

            try
            {
                long freed = await _ramOptimizer.DeepCleanRamAsync();
                double mb = freed / (1024.0 * 1024.0);
                LogMessage($"🧹 Manual Deep Purge: Liberated {mb:F0} MB of physical memory.");
                OnStatusNotification?.Invoke($"✨ Memory Optimized! {mb:F0} MB physical RAM liberated.");
                UpdateMemoryUi();
            }
            finally
            {
                BtnDeepClean.Content = origContent;
                BtnDeepClean.IsEnabled = true;
            }
        }

        private void ToggleAutoClean_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            _ramOptimizer.AutoCleanEnabled = e.NewValue;
            LogMessage(_ramOptimizer.AutoCleanEnabled 
                ? "Intelligent Auto-RAM Watchdog enabled (silent background cleaning)." 
                : "Intelligent Auto-RAM Watchdog disabled.");
        }

        private async void BtnTameCpuHogs_Click(object sender, RoutedEventArgs e)
        {
            BtnTameCpuHogs.IsEnabled = false;
            string orig = BtnTameCpuHogs.Content?.ToString() ?? "⚡ Tame CPU Hogs Now";
            BtnTameCpuHogs.Content = "⚡ Taming Hogs...";

            try
            {
                var result = await _cpuOptimizer.CleanCpuBusyProcessesAsync(minCpuThreshold: 4.0);
                if (result.TamedCount > 0)
                {
                    OnStatusNotification?.Invoke($"❄️ CPU Cooled: {result.TamedCount} background hogs tamed to Eco Mode!");
                    LogMessage($"⚡ Tamed {result.TamedCount} background CPU hogs: {string.Join(", ", result.TamedProcesses)}");
                }
                else
                {
                    OnStatusNotification?.Invoke("⚡ CPU is running cool! No rogue hogs found.");
                    LogMessage("⚡ CPU Tamer scan: All active background processes are operating within normal limits.");
                }
                UpdateCpuUi();
            }
            catch (Exception ex)
            {
                OnStatusNotification?.Invoke($"⚠️ CPU Tamer notice: {ex.Message}");
            }
            finally
            {
                BtnTameCpuHogs.Content = orig;
                BtnTameCpuHogs.IsEnabled = true;
            }
        }

        private void ToggleAutoTameCpu_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            _cpuOptimizer.AutoTameEnabled = e.NewValue;
            LogMessage(_cpuOptimizer.AutoTameEnabled 
                ? "⚡ Intelligent Auto-CPU Watchdog enabled (auto-throttles background hogs >75% CPU)." 
                : "Intelligent Auto-CPU Watchdog disabled.");
        }

        private void BtnTameSingleHog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int pid)
            {
                if (_cpuOptimizer.TameProcess(pid))
                {
                    OnStatusNotification?.Invoke($"❄️ Process PID {pid} throttled to Windows EcoQoS (Idle Priority).");
                    LogMessage($"❄️ Process PID {pid} throttled to Eco Mode (Efficiency cores & Idle priority).");
                    UpdateCpuUi();
                }
            }
        }

        private void ProfileChanged(BoostProfile profile)
        {
            Dispatcher.Invoke(() =>
            {
                var primaryBrush = GetBrush("AccentPrimary", "#3B82F6");
                var borderCard = GetBrush("BorderCard", "#1E293B");
                var bgCard = GetBrush("BgCard", "#131926");
                var dangerBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#E11D48")!;

                // Reset badges and borders
                BadgeGameActive.Visibility = Visibility.Collapsed;
                BadgeWorkActive.Visibility = Visibility.Collapsed;
                BadgeStudyActive.Visibility = Visibility.Collapsed;
                CardGame.BorderBrush = borderCard;
                CardWork.BorderBrush = borderCard;
                CardStudy.BorderBrush = borderCard;
                CardGame.Background = bgCard;
                CardWork.Background = bgCard;
                CardStudy.Background = bgCard;

                if (profile == BoostProfile.GameMode)
                {
                    BtnGameBoost.Content = "Deactivate Game Boost";
                    BtnGameBoost.Background = dangerBrush;
                    BadgeGameActive.Visibility = Visibility.Visible;
                    CardGame.BorderBrush = dangerBrush;
                    CardGame.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1C1522")!;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = primaryBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = primaryBrush;
                }
                else if (profile == BoostProfile.WorkMode)
                {
                    BtnWorkBoost.Content = "Deactivate Work Boost";
                    BtnWorkBoost.Background = dangerBrush;
                    BadgeWorkActive.Visibility = Visibility.Visible;
                    CardWork.BorderBrush = primaryBrush;
                    CardWork.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#101726")!;

                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = primaryBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = primaryBrush;
                }
                else if (profile == BoostProfile.StudyMode)
                {
                    BtnStudyMode.Content = "Deactivate Study Mode";
                    BtnStudyMode.Background = dangerBrush;
                    BadgeStudyActive.Visibility = Visibility.Visible;
                    CardStudy.BorderBrush = primaryBrush;
                    CardStudy.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#10162B")!;

                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = primaryBrush;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = primaryBrush;
                }
                else
                {
                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = primaryBrush;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = primaryBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = primaryBrush;
                }
            });
        }

        private readonly System.Collections.Generic.List<string> _logEntries = new(100);

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string newEntry = $"[{timestamp}] {message}";

                if (_logEntries.Count >= 80)
                {
                    _logEntries.RemoveAt(_logEntries.Count - 1);
                }
                _logEntries.Insert(0, newEntry);

                TxtActivityLog.Text = string.Join("\n", _logEntries);
            });
        }
    }
}
