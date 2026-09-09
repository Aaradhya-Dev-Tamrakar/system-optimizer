using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using NovaOptimizer.Controls;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class StartupAndTweaksView : UserControl
    {
        private readonly StartupManagerService _startupManager;
        private readonly SystemTweakService _systemTweaks;
        private List<TweakItem> _tweaks = new();
        private bool _isSyncingNovaToggle = false;

        public event Action<string>? OnStatusNotification;

        public StartupAndTweaksView(StartupManagerService startupManager, SystemTweakService systemTweaks)
        {
            InitializeComponent();
            _startupManager = startupManager;
            _systemTweaks = systemTweaks;

            _startupManager.OnNovaStartupChanged += (enabled) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _isSyncingNovaToggle = true;
                    ToggleNovaStartup.IsChecked = enabled;
                    _isSyncingNovaToggle = false;
                });
            };

            LoadData();
        }

        private void LoadData()
        {
            _tweaks = _systemTweaks.GetTweaks();
            IcTweaks.ItemsSource = _tweaks;

            _isSyncingNovaToggle = true;
            ToggleNovaStartup.IsChecked = _startupManager.IsNovaStartupEnabled();
            _isSyncingNovaToggle = false;

            var startupItems = _startupManager.GetStartupItems();
            DgStartup.ItemsSource = startupItems;
            TxtStartupCount.Text = $"{startupItems.Count} Items Found";
        }

        private void TweakToggle_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            if (sender is ToggleSwitch ts && ts.Tag is string tweakId)
            {
                if (e.NewValue)
                {
                    _systemTweaks.ApplyTweak(tweakId);
                    OnStatusNotification?.Invoke($"Applied performance tweak: {tweakId}");
                }
                else
                {
                    _systemTweaks.RevertTweak(tweakId);
                    OnStatusNotification?.Invoke($"Reverted tweak: {tweakId}");
                }
            }
        }

        private void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            int applied = 0;
            foreach (var tweak in _tweaks)
            {
                if (!tweak.IsApplied)
                {
                    _systemTweaks.ApplyTweak(tweak.Id);
                    tweak.IsApplied = true;
                    applied++;
                }
            }
            IcTweaks.ItemsSource = null;
            IcTweaks.ItemsSource = _tweaks;
            OnStatusNotification?.Invoke($"✨ Applied all {applied} recommended performance tweaks!");
        }

        private void BtnRevertAll_Click(object sender, RoutedEventArgs e)
        {
            int reverted = 0;
            foreach (var tweak in _tweaks)
            {
                if (tweak.IsApplied)
                {
                    _systemTweaks.RevertTweak(tweak.Id);
                    tweak.IsApplied = false;
                    reverted++;
                }
            }
            IcTweaks.ItemsSource = null;
            IcTweaks.ItemsSource = _tweaks;
            OnStatusNotification?.Invoke($"↺ Reverted {reverted} tweaks back to Windows defaults.");
        }

        private void ToggleNovaStartup_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            if (_isSyncingNovaToggle) return;
            if (_startupManager.IsNovaStartupEnabled() != e.NewValue)
            {
                _startupManager.SetNovaStartup(e.NewValue);
                OnStatusNotification?.Invoke(e.NewValue
                    ? "🚀 NovaOptimizer registered to start with Windows."
                    : "NovaOptimizer removed from Windows startup.");

                // Refresh list so NovaOptimizer appears/disappears in the auditor list
                var items = _startupManager.GetStartupItems();
                DgStartup.ItemsSource = items;
                TxtStartupCount.Text = $"{items.Count} Items Found";
            }
        }

        private void BtnAddStartup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Application to Add to Startup",
                Filter = "Executables & Shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                string selectedFile = dlg.FileName;
                string appName = System.IO.Path.GetFileNameWithoutExtension(selectedFile);

                bool success = _startupManager.AddStartupItem(appName, selectedFile);
                if (success)
                {
                    var items = _startupManager.GetStartupItems();
                    DgStartup.ItemsSource = items;
                    TxtStartupCount.Text = $"{items.Count} Items Found";
                    OnStatusNotification?.Invoke($"✨ Added '{appName}' to Windows startup apps!");
                }
                else
                {
                    OnStatusNotification?.Invoke($"Failed to add '{appName}' to startup.");
                }
            }
        }

        private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e)
        {
            var items = _startupManager.GetStartupItems();
            DgStartup.ItemsSource = items;
            TxtStartupCount.Text = $"{items.Count} Items Found";
            OnStatusNotification?.Invoke("Startup items list refreshed.");
        }

        private void BtnRemoveStartup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItem item)
            {
                var result = MessageBox.Show(
                    $"Remove '{item.Name}' from starting automatically with Windows?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _startupManager.RemoveStartupItem(item);
                    var items = _startupManager.GetStartupItems();
                    DgStartup.ItemsSource = items;
                    TxtStartupCount.Text = $"{items.Count} Items Found";
                    OnStatusNotification?.Invoke($"Removed {item.Name} from Windows startup.");
                }
            }
        }
    }
}
