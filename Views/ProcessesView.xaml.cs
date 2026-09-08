using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class ProcessesView : UserControl
    {
        private readonly ProcessMonitorService _processMonitor;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly ObservableCollection<ProcessItem> _displayProcesses = new();
        private readonly Dictionary<int, ProcessItem> _processMap = new();
        private string _activeCategory = "All";
        private List<ProcessItem> _latestRawProcesses = new();
        private ProcessItem? _selectedItem;
        private bool _isRefreshing;
        private bool _isActive = true;

        public event Action<string>? OnStatusNotification;

        public ProcessesView(ProcessMonitorService processMonitor)
        {
            InitializeComponent();
            _processMonitor = processMonitor;

            DgProcesses.ItemsSource = _displayProcesses;

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ApplyFilter();
            };

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _refreshTimer.Tick += async (s, e) => await RefreshProcessesAsync();
            _refreshTimer.Start();

            _ = RefreshProcessesAsync();
        }

        public void PauseMonitoring()
        {
            _isActive = false;
            _refreshTimer.Stop();
            _searchDebounceTimer.Stop();
        }

        public void ResumeMonitoring()
        {
            if (!_isActive)
            {
                _isActive = true;
                _refreshTimer.Start();
                _ = RefreshProcessesAsync();
            }
        }

        private async Task RefreshProcessesAsync()
        {
            if (_isRefreshing || !_isActive) return;
            _isRefreshing = true;

            try
            {
                var rawList = await _processMonitor.GetProcessesAsync();
                if (!_isActive) return;

                _latestRawProcesses = rawList.OrderByDescending(p => p.WorkingSetMB).ToList();
                ApplyFilter();
            }
            catch
            {
                // Prevent unhandled errors from breaking timer loop
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void FilterChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _activeCategory = tag;
                ApplyFilter();
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            ApplyFilter();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshProcessesAsync();
            OnStatusNotification?.Invoke("Process list refreshed.");
        }

        private void ApplyFilter()
        {
            string filter = TxtSearch.Text.Trim();
            int selectedId = _selectedItem?.Id ?? -1;

            TxtPlaceholder.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearSearch.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Collapsed : Visibility.Visible;

            IEnumerable<ProcessItem> targetItems = _latestRawProcesses;

            // Apply category filter
            targetItems = _activeCategory switch
            {
                "Apps" => targetItems.Where(p => p.HasWindow),
                "Background" => targetItems.Where(p => !p.HasWindow && !p.IsSystemCritical),
                "HighRam" => targetItems.Where(p => p.IsHighRam),
                "HighCpu" => targetItems.Where(p => p.IsHighCpu),
                "Hung" => targetItems.Where(p => p.IsHung),
                _ => targetItems
            };

            // Apply text search
            if (!string.IsNullOrEmpty(filter))
            {
                targetItems = targetItems.Where(p =>
                    p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.ToString().Contains(filter) ||
                    p.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var targetList = targetItems.ToList();
            var targetPids = new HashSet<int>(targetList.Select(p => p.Id));

            // Remove processes no longer present
            for (int i = _displayProcesses.Count - 1; i >= 0; i--)
            {
                var item = _displayProcesses[i];
                if (!targetPids.Contains(item.Id))
                {
                    _processMap.Remove(item.Id);
                    _displayProcesses.RemoveAt(i);
                }
            }

            // In-place update existing items or insert new ones
            for (int i = 0; i < targetList.Count; i++)
            {
                var incoming = targetList[i];
                if (_processMap.TryGetValue(incoming.Id, out var existing))
                {
                    existing.UpdateFrom(incoming);
                }
                else
                {
                    _processMap[incoming.Id] = incoming;
                    if (i < _displayProcesses.Count)
                        _displayProcesses.Insert(i, incoming);
                    else
                        _displayProcesses.Add(incoming);
                }
            }

            // Restore selection without UI flicker
            if (selectedId != -1 && _processMap.TryGetValue(selectedId, out var reselect))
            {
                _selectedItem = reselect;
                DgProcesses.SelectedItem = reselect;
            }

            double totalRamGB = targetList.Sum(p => p.WorkingSetMB) / 1024.0;
            TxtStatus.Text = $"Showing {targetList.Count} of {_latestRawProcesses.Count} processes | RAM: {totalRamGB:F1} GB";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void DgProcesses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedItem = DgProcesses.SelectedItem as ProcessItem;
        }

        private void BtnEndTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                if (_selectedItem.IsSystemCritical)
                {
                    MessageBox.Show("Cannot terminate protected Windows system process.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_processMonitor.KillProcess(_selectedItem.Id))
                {
                    OnStatusNotification?.Invoke($"Terminated process {_selectedItem.Name} (PID {_selectedItem.Id})");
                    _ = RefreshProcessesAsync();
                }
            }
        }

        private void BtnTrimProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _processMonitor.TrimProcessRam(_selectedItem.Id);
                OnStatusNotification?.Invoke($"Trimmed Working Set RAM for {_selectedItem.Name}");
                _ = RefreshProcessesAsync();
            }
        }

        private void MenuEndTask_Click(object sender, RoutedEventArgs e) => BtnEndTask_Click(sender, e);

        private void MenuKillTree_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                if (_selectedItem.IsSystemCritical) return;
                _processMonitor.KillProcessTree(_selectedItem.Id);
                OnStatusNotification?.Invoke($"Terminated process tree for {_selectedItem.Name}");
                _ = RefreshProcessesAsync();
            }
        }

        private void MenuTrimRam_Click(object sender, RoutedEventArgs e) => BtnTrimProcess_Click(sender, e);

        private void MenuPriority_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null && sender is MenuItem mi && mi.Tag is string tag)
            {
                if (Enum.TryParse<ProcessPriorityClass>(tag, out var priority))
                {
                    _processMonitor.SetPriority(_selectedItem.Id, priority);
                    OnStatusNotification?.Invoke($"Set priority of {_selectedItem.Name} to {priority}");
                    _ = RefreshProcessesAsync();
                }
            }
        }

        private void MenuOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _processMonitor.OpenProcessLocation(_selectedItem.Id);
            }
        }
    }
}
