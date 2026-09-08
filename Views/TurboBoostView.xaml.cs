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

            ToggleAutoClean.IsChecked = _ramOptimizer.AutoCleanEnabled;
            UpdateMemoryUi();
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

        private async void BtnStudyMode_Click(object sender, RoutedEventArgs e)
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
                BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1E3A5F")!;
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
                BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#4A2810")!;
            }
        }

        private void BtnPomodoroReset_Click(object sender, RoutedEventArgs e)
        {
            _pomodoroTimer?.Stop();
            _isTimerRunning = false;
            _isBreakPhase = false;
            _remainingSeconds = GetFocusMinutes() * 60;
            BtnPomodoroToggle.Content = "▶ Start Timer";
            BtnPomodoroToggle.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1E3A5F")!;
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

        private void ProfileChanged(BoostProfile profile)
        {
            Dispatcher.Invoke(() =>
            {
                var purpleBrush = (System.Windows.Media.Brush)FindResource("AccentPurple");
                var cyanBrush = (System.Windows.Media.Brush)FindResource("AccentCyan");
                var blueBrush = (System.Windows.Media.Brush)FindResource("AccentBlue");
                var redBrush = (System.Windows.Media.Brush)FindResource("AccentRed");
                var borderCard = (System.Windows.Media.Brush)FindResource("BorderCard");
                var bgCard = (System.Windows.Media.Brush)FindResource("BgCard");
                var darkRed = System.Windows.Media.Brushes.DarkRed;

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
                    BtnGameBoost.Background = darkRed;
                    BadgeGameActive.Visibility = Visibility.Visible;
                    CardGame.BorderBrush = redBrush;
                    CardGame.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#221520")!;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = cyanBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = blueBrush;
                }
                else if (profile == BoostProfile.WorkMode)
                {
                    BtnWorkBoost.Content = "Deactivate Work Boost";
                    BtnWorkBoost.Background = darkRed;
                    BadgeWorkActive.Visibility = Visibility.Visible;
                    CardWork.BorderBrush = cyanBrush;
                    CardWork.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#141D2B")!;

                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = purpleBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = blueBrush;
                }
                else if (profile == BoostProfile.StudyMode)
                {
                    BtnStudyMode.Content = "Deactivate Study Mode";
                    BtnStudyMode.Background = darkRed;
                    BadgeStudyActive.Visibility = Visibility.Visible;
                    CardStudy.BorderBrush = blueBrush;
                    CardStudy.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#131C30")!;

                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = purpleBrush;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = cyanBrush;
                }
                else
                {
                    BtnGameBoost.Content = "Activate Game Boost";
                    BtnGameBoost.Background = purpleBrush;

                    BtnWorkBoost.Content = "Activate Work Boost";
                    BtnWorkBoost.Background = cyanBrush;

                    BtnStudyMode.Content = "Activate Study Mode";
                    BtnStudyMode.Background = blueBrush;
                }
            });
        }

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string newEntry = $"[{timestamp}] {message}\n";
                string current = TxtActivityLog.Text;

                // Keep log size bounded to avoid UI render degradation and excessive memory allocations
                if (current.Length > 8000)
                {
                    int newlineIdx = current.IndexOf('\n', 4000);
                    if (newlineIdx > 0)
                    {
                        current = current.Substring(0, newlineIdx + 1);
                    }
                }

                TxtActivityLog.Text = newEntry + current;
            });
        }
    }
}
