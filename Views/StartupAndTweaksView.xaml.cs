using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinGovernor.Controls;
using WinGovernor.Models;
using WinGovernor.Services;

namespace WinGovernor.Views
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

            _startupManager.OnGovernorStartupChanged += (enabled) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _isSyncingNovaToggle = true;
                    ToggleGovernorStartup.IsChecked = enabled;
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
            ToggleGovernorStartup.IsChecked = _startupManager.IsGovernorStartupEnabled();
            _isSyncingNovaToggle = false;
        }

        private void TweakToggle_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            if (sender is ToggleSwitch ts && ts.Tag is string tweakId)
            {
                if (e.NewValue)
                {
                    bool success = _systemTweaks.ApplyTweak(tweakId);
                    if (success)
                    {
                        OnStatusNotification?.Invoke($"Applied performance tweak: {tweakId}");
                    }
                    else
                    {
                        OnStatusNotification?.Invoke($"⚠️ Administrator rights required to modify {tweakId}");
                        ts.IsChecked = false;
                    }
                }
                else
                {
                    bool success = _systemTweaks.RevertTweak(tweakId);
                    if (success)
                    {
                        OnStatusNotification?.Invoke($"Reverted tweak: {tweakId}");
                    }
                    else
                    {
                        OnStatusNotification?.Invoke($"⚠️ Administrator rights required to revert {tweakId}");
                        ts.IsChecked = true;
                    }
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

        private void ToggleGovernorStartup_CheckedChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
        {
            if (_isSyncingNovaToggle) return;
            if (_startupManager.IsGovernorStartupEnabled() != e.NewValue)
            {
                _startupManager.SetGovernorStartup(e.NewValue);
                OnStatusNotification?.Invoke(e.NewValue
                    ? "🚀 WinGovernor registered to start with Windows."
                    : "WinGovernor removed from Windows startup.");
            }
        }
    }
}
