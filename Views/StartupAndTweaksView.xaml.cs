using System;
using System.Windows;
using System.Windows.Controls;
using NovaOptimizer.Models;
using NovaOptimizer.Services;

namespace NovaOptimizer.Views
{
    public partial class StartupAndTweaksView : UserControl
    {
        private readonly StartupManagerService _startupManager;
        private readonly SystemTweakService _systemTweaks;

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
            IcTweaks.ItemsSource = _systemTweaks.GetTweaks();
            DgStartup.ItemsSource = _startupManager.GetStartupItems();
        }

        private void ChkApplyTweak_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is string tweakId)
            {
                bool isChecked = chk.IsChecked == true;
                if (isChecked)
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

        private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e)
        {
            DgStartup.ItemsSource = _startupManager.GetStartupItems();
            OnStatusNotification?.Invoke("Startup items list refreshed.");
        }

        private void BtnRemoveStartup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItem item)
            {
                var result = MessageBox.Show($"Remove '{item.Name}' from starting automatically with Windows?", "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _startupManager.RemoveStartupItem(item);
                    DgStartup.ItemsSource = _startupManager.GetStartupItems();
                    OnStatusNotification?.Invoke($"Removed {item.Name} from Windows startup.");
                }
            }
        }
    }
}
