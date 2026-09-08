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

        public event Action<string>? OnStatusNotification;

        public StartupAndTweaksView(StartupManagerService startupManager, SystemTweakService systemTweaks)
        {
            InitializeComponent();
            _startupManager = startupManager;
            _systemTweaks = systemTweaks;

            LoadData();
        }

        private void LoadData()
        {
            _tweaks = _systemTweaks.GetTweaks();
            IcTweaks.ItemsSource = _tweaks;

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
