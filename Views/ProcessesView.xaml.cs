using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private List<ProcessItem> _allProcesses = new();
        private ProcessItem? _selectedItem;

        public event Action<string>? OnStatusNotification;

        public ProcessesView(ProcessMonitorService processMonitor)
        {
            InitializeComponent();
            _processMonitor = processMonitor;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _refreshTimer.Tick += (s, e) => RefreshProcesses();
            _refreshTimer.Start();

            RefreshProcesses();
        }

        private void RefreshProcesses()
        {
            int selectedId = _selectedItem?.Id ?? -1;

            _allProcesses = _processMonitor.GetProcesses()
                .OrderByDescending(p => p.WorkingSetMB)
                .ToList();

            ApplyFilter();

            if (selectedId != -1)
            {
                _selectedItem = _allProcesses.FirstOrDefault(p => p.Id == selectedId);
                DgProcesses.SelectedItem = _selectedItem;
            }
        }

        private void ApplyFilter()
        {
            string filter = TxtSearch.Text.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                DgProcesses.ItemsSource = _allProcesses;
                TxtStatus.Text = $"Processes: {_allProcesses.Count}";
            }
            else
            {
                var filtered = _allProcesses.Where(p => 
                    p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.ToString().Contains(filter) ||
                    p.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                DgProcesses.ItemsSource = filtered;
                TxtStatus.Text = $"Showing {filtered.Count} of {_allProcesses.Count} processes";
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

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
                    RefreshProcesses();
                }
            }
        }

        private void BtnTrimProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _processMonitor.TrimProcessRam(_selectedItem.Id);
                OnStatusNotification?.Invoke($"Trimmed Working Set RAM for {_selectedItem.Name}");
                RefreshProcesses();
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
                RefreshProcesses();
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
                    RefreshProcesses();
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
