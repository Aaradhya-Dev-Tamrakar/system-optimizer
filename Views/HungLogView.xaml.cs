using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    /// <summary>
    /// MultiValueConverter: shows End Task button only when NOT recovered AND NOT already killed.
    /// </summary>
    public class HungEndTaskVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is bool recovered && values[1] is bool killed)
            {
                return (!recovered && !killed) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class HungLogView : UserControl
    {
        private readonly HungProcessWatchdogService _watchdog;
        private readonly DispatcherTimer _refreshTimer;
        private string _activeTab = "Offenders";
        private bool _isInitializing = true;

        public event Action<string>? OnStatusNotification;

        /// <summary>
        /// Static converter instance referenced from XAML via {x:Static local:HungLogView.HungVisibilityConverter}
        /// </summary>
        public static readonly HungEndTaskVisibilityConverter HungVisibilityConverter = new();

        public HungLogView(HungProcessWatchdogService watchdog)
        {
            InitializeComponent();
            _watchdog = watchdog;

            // Show the log file path in the footer
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaOptimizer", "hung_process_log.jsonl");
            TxtLogPath.Text = $"Log: {logPath}";

            // Initialize auto-kill UI from persisted settings
            ChkAutoKill.IsChecked = _watchdog.AutoKillEnabled;
            SetComboBoxToTimeout(_watchdog.AutoKillTimeoutSeconds);
            _isInitializing = false;

            // Refresh the UI every 5 seconds when active
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += (s, e) => RefreshData();
            _refreshTimer.Start();

            // Reactive update on detection, recovery, or kill
            _watchdog.OnHungDetected += (r) => Dispatcher.Invoke(() => { if (_refreshTimer.IsEnabled) RefreshData(); });
            _watchdog.OnHungRecovered += (r) => Dispatcher.Invoke(() => { if (_refreshTimer.IsEnabled) RefreshData(); });
            _watchdog.OnHungKilled += (r) => Dispatcher.Invoke(() =>
            {
                if (_refreshTimer.IsEnabled) RefreshData();
                OnStatusNotification?.Invoke($"💀 Terminated hung process: {r.ProcessName} (PID {r.PID}) — {r.WorkingSetMB:F0} MB freed");
            });

            RefreshData();
        }

        private void SetComboBoxToTimeout(int seconds)
        {
            foreach (ComboBoxItem item in CmbAutoKillTimeout.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out int tagVal) && tagVal == seconds)
                {
                    CmbAutoKillTimeout.SelectedItem = item;
                    return;
                }
            }
            // Default to 30s if not found
            CmbAutoKillTimeout.SelectedIndex = 1;
        }

        public void PauseMonitoring()
        {
            _refreshTimer.Stop();
        }

        public void ResumeMonitoring()
        {
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
                RefreshData();
            }
        }

        private async void RefreshData()
        {
            try
            {
                string filter = TxtSearchLog.Text.Trim();
                string activeTab = _activeTab;

                var data = await Task.Run(() =>
                {
                    var allRecords = _watchdog.GetAllRecords();
                    var recentRecords = allRecords.Where(r => r.Timestamp >= DateTime.Now.AddHours(-24)).ToList();
                    var offenders = _watchdog.GetRepeatOffenders();
                    int activeHangs = _watchdog.GetActiveHangCount();

                    if (!string.IsNullOrEmpty(filter))
                    {
                        offenders = offenders.Where(o => 
                            o.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) || 
                            o.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                        allRecords = allRecords.Where(r => 
                            r.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) || 
                            r.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    var sortedFullLog = allRecords.OrderByDescending(r => r.Timestamp).ToList();

                    return (allRecords, recentRecords, offenders, activeHangs, sortedFullLog);
                });

                if (!_refreshTimer.IsEnabled) return;

                // Update summary cards
                TxtHangs24h.Text = data.recentRecords.Count.ToString();
                TxtTotalEvents.Text = data.allRecords.Count.ToString();
                TxtActiveHangs.Text = data.activeHangs.ToString();

                // Color the active hangs count and show/hide End All button
                TxtActiveHangs.Foreground = data.activeHangs > 0
                    ? (System.Windows.Media.Brush)FindResource("AccentRed")
                    : (System.Windows.Media.Brush)FindResource("AccentGreen");

                BtnEndAllHangs.Visibility = data.activeHangs > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (data.offenders.Count > 0)
                {
                    var top = data.offenders[0];
                    TxtTopOffender.Text = top.ProcessName;
                    TxtTopOffenderCount.Text = $"{top.HangCount} hangs — {top.DisplayAvgDuration} avg";
                }
                else
                {
                    TxtTopOffender.Text = "—";
                    TxtTopOffenderCount.Text = "No hangs detected yet";
                }

                // Update the active table
                if (activeTab == "Offenders")
                {
                    DgOffenders.ItemsSource = data.offenders;
                }
                else
                {
                    DgFullLog.ItemsSource = data.sortedFullLog;
                }

                string autoKillStatus = _watchdog.AutoKillEnabled 
                    ? $" | Auto-end: {_watchdog.AutoKillTimeoutSeconds}s" 
                    : "";
                TxtFooter.Text = $"Watchdog active — monitoring every 3s{autoKillStatus} | Last refresh: {DateTime.Now:HH:mm:ss}";
            }
            catch
            {
                // Silently handle refresh errors
            }
        }

        private void TxtSearchLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshData();
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _activeTab = tag;

                if (tag == "Offenders")
                {
                    PanelOffenders.Visibility = Visibility.Visible;
                    PanelFullLog.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PanelOffenders.Visibility = Visibility.Collapsed;
                    PanelFullLog.Visibility = Visibility.Visible;
                }

                RefreshData();
            }
        }

        private void BtnEndAllHangs_Click(object sender, RoutedEventArgs e)
        {
            int activeCount = _watchdog.GetActiveHangCount();
            if (activeCount == 0) return;

            var result = MessageBox.Show(
                $"Terminate {activeCount} frozen process{(activeCount > 1 ? "es" : "")}?\n\nProtected system processes (dwm, csrss, explorer, etc.) will be skipped.",
                "End All Hung Processes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                int killed = _watchdog.KillAllActiveHangs();
                RefreshData();
                if (killed > 0)
                {
                    OnStatusNotification?.Invoke($"💀 Terminated {killed} frozen process{(killed > 1 ? "es" : "")}");
                }
                else
                {
                    OnStatusNotification?.Invoke("⚠️ No processes could be terminated (protected or already exited)");
                }
            }
        }

        private void BtnEndTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int pid)
            {
                bool killed = _watchdog.KillHungProcess(pid);
                RefreshData();
                if (!killed)
                {
                    OnStatusNotification?.Invoke($"⚠️ Could not terminate PID {pid} (protected or already exited)");
                }
            }
        }

        private void ChkAutoKill_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _watchdog.AutoKillEnabled = ChkAutoKill.IsChecked == true;
            RefreshData();
        }

        private void CmbAutoKillTimeout_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (CmbAutoKillTimeout.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
            {
                _watchdog.AutoKillTimeoutSeconds = seconds;
                RefreshData();
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"hung_processes_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Hung Process Log"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_watchdog.ExportCsv(dialog.FileName))
                {
                    OnStatusNotification?.Invoke($"📥 Exported hung process log to {Path.GetFileName(dialog.FileName)}");
                }
                else
                {
                    MessageBox.Show("Failed to export CSV file. The file may be currently open in another application.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all hung process log history? This cannot be undone.",
                "Clear Log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _watchdog.ClearLog();
                RefreshData();
                OnStatusNotification?.Invoke("🗑️ Hung process log cleared");
            }
        }

        private void TxtLogPath_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaOptimizer", "hung_process_log.jsonl");

            if (File.Exists(logPath))
            {
                Process.Start("explorer.exe", $"/select,\"{logPath}\"");
            }
            else
            {
                string dir = Path.GetDirectoryName(logPath) ?? string.Empty;
                if (Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            }
        }
    }
}
