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
        private readonly AcerHardwareCoolingService _acerCooling;
        private readonly DispatcherTimer _timer;
        private int _uiTickCount = 0;

        public event Action<string>? OnStatusNotification;

        public TurboBoostView(RamOptimizerService ramOptimizer, TurboBoostService turboBoost, CpuOptimizerService cpuOptimizer, AcerHardwareCoolingService? acerCooling = null)
        {
            InitializeComponent();
            _ramOptimizer = ramOptimizer;
            _turboBoost = turboBoost;
            _cpuOptimizer = cpuOptimizer;
            _acerCooling = acerCooling ?? new AcerHardwareCoolingService();

            _turboBoost.OnLogMessage += LogMessage;
            _turboBoost.OnProfileChanged += ProfileChanged;
            _acerCooling.OnProfileChanged += (p) => Dispatcher.Invoke(UpdateCoolingUi);
            _acerCooling.OnLogMessage += LogMessage;
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
            UpdateCoolingUi();

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

        private void UpdateCpuUi()
        {
            double cpu = _cpuOptimizer.GetSystemCpuUsage();
            PbCpuUsage.Value = cpu;

            if (cpu < 40.0)
            {
                TxtCpuLoadStatus.Text = $"Optimal ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreen");
                BadgeCpuLoad.Background = (System.Windows.Media.Brush)FindResource("BadgeBgSuccess");
                BadgeCpuLoad.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderSuccess");
            }
            else if (cpu < 70.0)
            {
                TxtCpuLoadStatus.Text = $"Moderate ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentYellow");
                BadgeCpuLoad.Background = (System.Windows.Media.Brush)FindResource("BadgeBgWarning");
                BadgeCpuLoad.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderWarning");
            }
            else
            {
                TxtCpuLoadStatus.Text = $"High Load ({cpu:F0}%)";
                TxtCpuLoadStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentRed");
                BadgeCpuLoad.Background = (System.Windows.Media.Brush)FindResource("BadgeBgDanger");
                BadgeCpuLoad.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderDanger");
            }

            _uiTickCount++;
            if (_uiTickCount % 2 == 0) // refresh hog list and cooling telemetry every 2 seconds
            {
                var hogs = _cpuOptimizer.GetTopCpuHogs(4);
                IcTopCpuHogs.ItemsSource = hogs;

                if (_acerCooling.IsSupported)
                {
                    _acerCooling.UpdateFanSpeeds();
                    UpdateCoolingUi();
                }
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

        #region Pomodoro Focus Timer
        private DispatcherTimer? _pomodoroTimer;
        private int _remainingSeconds = 25 * 60;
        private bool _isBreakPhase = false;
        private bool _isTimerRunning = false;
        private int _sessionCount = 1;

        private void InitPomodoroTimer()
        {
            _pomodoroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pomodoroTimer.Tick += PomodoroTimer_Tick;
            UpdatePomodoroDisplay();
        }

        private void PomodoroTimer_Tick(object? sender, EventArgs e)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                UpdatePomodoroDisplay();
            }
            else
            {
                // Switch phase
                _isBreakPhase = !_isBreakPhase;
                if (!_isBreakPhase)
                {
                    _sessionCount++;
                }

                int nextMins = _isBreakPhase ? GetBreakMinutes() : GetFocusMinutes();
                _remainingSeconds = nextMins * 60;
                UpdatePomodoroDisplay();

                string alertMsg = _isBreakPhase 
                    ? "☕ Focus session complete! Time for a short break." 
                    : $"📖 Break over! Starting Focus Session #{_sessionCount}.";

                LogMessage($"[Pomodoro] {alertMsg}");
                OnStatusNotification?.Invoke(alertMsg);

                try
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }
                catch { }
            }
        }

        private void BtnPomodoroToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_pomodoroTimer == null)
            {
                InitPomodoroTimer();
            }

            if (_isTimerRunning)
            {
                _pomodoroTimer?.Stop();
                _isTimerRunning = false;
                BtnPomodoroToggle.Content = "▶ Resume Timer";
                BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0F2238")!;
                BtnPomodoroToggle.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3B82F6")!;
            }
            else
            {
                // Check if remaining seconds is 0 or needs reset
                if (_remainingSeconds <= 0)
                {
                    int mins = _isBreakPhase ? GetBreakMinutes() : GetFocusMinutes();
                    _remainingSeconds = mins * 60;
                }

                _pomodoroTimer?.Start();
                _isTimerRunning = true;
                BtnPomodoroToggle.Content = "⏸ Pause Timer";
                BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#291E0E")!;
                BtnPomodoroToggle.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F59E0B")!;
            }
        }

        private void BtnPomodoroReset_Click(object sender, RoutedEventArgs e)
        {
            _pomodoroTimer?.Stop();
            _isTimerRunning = false;
            _isBreakPhase = false;
            _remainingSeconds = GetFocusMinutes() * 60;
            BtnPomodoroToggle.Content = "▶ Start Timer";
            BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0F2238")!;
            BtnPomodoroToggle.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3B82F6")!;
            UpdatePomodoroDisplay();
            LogMessage("[Pomodoro] Timer reset to Focus Session.");
        }

        private int GetFocusMinutes()
        {
            return int.TryParse(TxtFocusMins.Text.Trim(), out int val) && val > 0 ? val : 25;
        }

        private int GetBreakMinutes()
        {
            return int.TryParse(TxtBreakMins.Text.Trim(), out int val) && val > 0 ? val : 5;
        }

        private void UpdatePomodoroDisplay()
        {
            int mins = _remainingSeconds / 60;
            int secs = _remainingSeconds % 60;
            TxtPomodoroCountdown.Text = $"{mins:D2}:{secs:D2}";

            if (_isBreakPhase)
            {
                TxtPomodoroPhase.Text = "☕ Break Time";
                TxtPomodoroPhase.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreen");
            }
            else
            {
                TxtPomodoroPhase.Text = "📖 Focus Session";
                TxtPomodoroPhase.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlue");
            }

            TxtPomodoroCounter.Text = $"Session #{_sessionCount}";
        }
        #endregion

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

        private void Preset25_Click(object sender, RoutedEventArgs e)
        {
            TxtFocusMins.Text = "25";
            TxtBreakMins.Text = "5";
            BtnPomodoroReset_Click(sender, e);
        }

        private void Preset50_Click(object sender, RoutedEventArgs e)
        {
            TxtFocusMins.Text = "50";
            TxtBreakMins.Text = "10";
            BtnPomodoroReset_Click(sender, e);
        }

        private void Preset90_Click(object sender, RoutedEventArgs e)
        {
            TxtFocusMins.Text = "90";
            TxtBreakMins.Text = "15";
            BtnPomodoroReset_Click(sender, e);
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
                var primaryBrush = (System.Windows.Media.Brush)FindResource("AccentPrimary");
                var borderCard = (System.Windows.Media.Brush)FindResource("BorderCard");
                var bgCard = (System.Windows.Media.Brush)FindResource("BgCard");
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

        private void BtnCoolingQuiet_Click(object sender, RoutedEventArgs e)
        {
            if (_acerCooling.SetProfile(AcerThermalProfile.Quiet))
            {
                OnStatusNotification?.Invoke("🌙 Acer Cooling: Quiet Mode engaged (whisper fans).");
                UpdateCoolingUi();
            }
        }

        private void BtnCoolingBalanced_Click(object sender, RoutedEventArgs e)
        {
            if (_acerCooling.SetProfile(AcerThermalProfile.Balanced))
            {
                OnStatusNotification?.Invoke("⚖️ Acer Cooling: Balanced Mode engaged (factory curve).");
                UpdateCoolingUi();
            }
        }

        private void BtnCoolingPerformance_Click(object sender, RoutedEventArgs e)
        {
            if (_acerCooling.SetProfile(AcerThermalProfile.Performance))
            {
                OnStatusNotification?.Invoke("⚡ Acer Cooling: Performance Mode engaged (high fan speed).");
                UpdateCoolingUi();
            }
        }

        private void BtnCoolingTurbo_Click(object sender, RoutedEventArgs e)
        {
            if (_acerCooling.SetProfile(AcerThermalProfile.Turbo))
            {
                OnStatusNotification?.Invoke("🔥 Acer Cooling: Turbo Mode engaged (maximum dissipation).");
                UpdateCoolingUi();
            }
        }

        private void UpdateCoolingUi()
        {
            if (!_acerCooling.IsSupported)
            {
                TxtAcerActiveProfile.Text = "Hardware WMI Inactive";
                TxtAcerActiveProfile.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
                TxtHardwareLinkNote.Text = "Acer ACPI Firmware WMI link requires Administrator privileges or supported Acer model.";
                BtnCoolingQuiet.IsEnabled = false;
                BtnCoolingBalanced.IsEnabled = false;
                BtnCoolingPerformance.IsEnabled = false;
                BtnCoolingTurbo.IsEnabled = false;
                return;
            }

            TxtHardwareLinkNote.Text = $"Firmware Link: {_acerCooling.LaptopModel} • Hardware Thermal Watchdog Active";

            if (_acerCooling.CpuFanRpm > 0)
            {
                TxtCpuFanRpm.Text = $"{_acerCooling.CpuFanRpm} RPM";
            }
            if (_acerCooling.GpuFanRpm > 0)
            {
                TxtGpuFanRpm.Text = $"{_acerCooling.GpuFanRpm} RPM";
            }

            var profile = _acerCooling.CurrentProfile;

            var secondaryStyle = (Style)FindResource("SecondaryButton");
            var primaryStyle = (Style)FindResource("PrimaryButton");

            BtnCoolingQuiet.Style = profile == AcerThermalProfile.Quiet ? primaryStyle : secondaryStyle;
            BtnCoolingBalanced.Style = profile == AcerThermalProfile.Balanced ? primaryStyle : secondaryStyle;
            BtnCoolingPerformance.Style = profile == AcerThermalProfile.Performance ? primaryStyle : secondaryStyle;
            BtnCoolingTurbo.Style = profile == AcerThermalProfile.Turbo ? primaryStyle : secondaryStyle;

            switch (profile)
            {
                case AcerThermalProfile.Quiet:
                    TxtAcerActiveProfile.Text = "● Quiet (Whisper Fans)";
                    TxtAcerActiveProfile.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlue");
                    BadgeAcerProfile.Background = (System.Windows.Media.Brush)FindResource("BadgeBgPrimary");
                    BadgeAcerProfile.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderPrimary");
                    break;
                case AcerThermalProfile.Balanced:
                    TxtAcerActiveProfile.Text = "● Balanced (Factory Standard)";
                    TxtAcerActiveProfile.Foreground = (System.Windows.Media.Brush)FindResource("AccentPrimary");
                    BadgeAcerProfile.Background = (System.Windows.Media.Brush)FindResource("BadgeBgPrimary");
                    BadgeAcerProfile.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderPrimary");
                    break;
                case AcerThermalProfile.Performance:
                    TxtAcerActiveProfile.Text = "● Performance (Aggressive)";
                    TxtAcerActiveProfile.Foreground = (System.Windows.Media.Brush)FindResource("AccentYellow");
                    BadgeAcerProfile.Background = (System.Windows.Media.Brush)FindResource("BadgeBgWarning");
                    BadgeAcerProfile.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderWarning");
                    break;
                case AcerThermalProfile.Turbo:
                    TxtAcerActiveProfile.Text = "● Turbo (Max Dissipation)";
                    TxtAcerActiveProfile.Foreground = (System.Windows.Media.Brush)FindResource("AccentRed");
                    BadgeAcerProfile.Background = (System.Windows.Media.Brush)FindResource("BadgeBgDanger");
                    BadgeAcerProfile.BorderBrush = (System.Windows.Media.Brush)FindResource("BadgeBorderDanger");
                    break;
            }
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
