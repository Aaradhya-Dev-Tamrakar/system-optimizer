using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class HungLogView : UserControl
    {
        private readonly HungProcessWatchdogService _watchdog;
        private readonly DispatcherTimer _refreshTimer;
        private string _activeTab = "Offenders";

        public event Action<string>? OnStatusNotification;

        public HungLogView(HungProcessWatchdogService watchdog)
        {
            InitializeComponent();
            _watchdog = watchdog;

            // Show the log file path in the footer
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaOptimizer", "hung_process_log.jsonl");
            TxtLogPath.Text = $"Log: {logPath}";

            // Refresh the UI every 5 seconds when active
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += (s, e) => RefreshData();
            _refreshTimer.Start();

            // Reactive update on detection or recovery
            _watchdog.OnHungDetected += (r) => Dispatcher.Invoke(() => { if (_refreshTimer.IsEnabled) RefreshData(); });
            _watchdog.OnHungRecovered += (r) => Dispatcher.Invoke(() => { if (_refreshTimer.IsEnabled) RefreshData(); });

            RefreshData();
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

                // Color the active hangs count
                TxtActiveHangs.Foreground = data.activeHangs > 0
                    ? (System.Windows.Media.Brush)FindResource("AccentRed")
                    : (System.Windows.Media.Brush)FindResource("AccentGreen");

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

                TxtFooter.Text = $"Watchdog active — monitoring every 3s | Last refresh: {DateTime.Now:HH:mm:ss}";
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
