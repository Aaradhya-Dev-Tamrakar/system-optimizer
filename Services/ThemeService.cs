using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinGovernor.Services
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    /// <summary>
    /// Runtime Light/Dark theme engine. Recolors the shared brushes declared in App.xaml in place,
    /// so every StaticResource/DynamicResource consumer updates without reloading views.
    /// Palette anchors: Light = bg #F0F0F0 / fg #101010 / accent #007ACC,
    /// Dark = bg #272822 / fg #F8F8F2 / accent #F92672.
    /// </summary>
    public static class ThemeService
    {
        private const string RegistryPath = @"Software\WinGovernor";
        private const string RegistryValue = "Theme";

        public static AppTheme Current { get; private set; } = AppTheme.Dark;
        public static event Action<AppTheme>? ThemeChanged;

        private static readonly Dictionary<string, string> Dark = new()
        {
            ["BgDark"] = "#272822",
            ["BgSidebar"] = "#1E1F1C",
            ["BgCard"] = "#1E1F1C",
            ["BgCardHover"] = "#32332C",
            ["BgCardSubtle"] = "#23241F",
            ["BgSurface"] = "#2F302A",
            ["BgCardActive"] = "#2E2429",

            ["BorderSubtle"] = "#34352E",
            ["BorderCard"] = "#3E3D32",
            ["BorderCardHover"] = "#55533F",
            ["BorderCardActive"] = "#75715E",

            ["AccentPrimary"] = "#F92672",
            ["AccentPrimaryHover"] = "#E01E65",
            ["AccentPrimaryDark"] = "#C41857",
            ["AccentCyan"] = "#66D9EF",
            ["AccentBlue"] = "#66D9EF",
            ["AccentIndigo"] = "#AE81FF",
            ["AccentPurple"] = "#AE81FF",
            ["AccentGreen"] = "#A6E22E",
            ["AccentYellow"] = "#E6DB74",
            ["AccentOrange"] = "#FD971F",
            ["AccentRed"] = "#FF5C5C",

            ["BadgeBgPrimary"] = "#3A1A28",
            ["BadgeBorderPrimary"] = "#6B2540",
            ["BadgeBgSuccess"] = "#24300F",
            ["BadgeBorderSuccess"] = "#43591A",
            ["BadgeBgWarning"] = "#3A2D12",
            ["BadgeBorderWarning"] = "#6B5320",
            ["BadgeBgDanger"] = "#3A1B1E",
            ["BadgeBorderDanger"] = "#6B2A30",
            ["BadgeBgInfo"] = "#2B2540",
            ["BadgeBorderInfo"] = "#4A3F70",
            ["BadgeBgDefault"] = "#2A2B25",
            ["BadgeBorderDefault"] = "#3E3D32",

            ["TextPrimary"] = "#F8F8F2",
            ["TextSecondary"] = "#A6A28C",
            ["TextMuted"] = "#8F8B77",
            ["TextDisabled"] = "#75715E",

            ["DangerHoverBg"] = "#4A2126",
            ["ScrollThumb"] = "#3E3D32",
            ["ScrollThumbHover"] = "#55533F"
        };

        private static readonly Dictionary<string, string> Light = new()
        {
            ["BgDark"] = "#F0F0F0",
            ["BgSidebar"] = "#E6E6E6",
            ["BgCard"] = "#FFFFFF",
            ["BgCardHover"] = "#E8E8E8",
            ["BgCardSubtle"] = "#F6F6F6",
            ["BgSurface"] = "#FAFAFA",
            ["BgCardActive"] = "#E6F2FA",

            ["BorderSubtle"] = "#E0E0E0",
            ["BorderCard"] = "#D4D4D4",
            ["BorderCardHover"] = "#BDBDBD",
            ["BorderCardActive"] = "#9E9E9E",

            ["AccentPrimary"] = "#007ACC",
            ["AccentPrimaryHover"] = "#0066AD",
            ["AccentPrimaryDark"] = "#005A9E",
            ["AccentCyan"] = "#0891B2",
            ["AccentBlue"] = "#2B7BD1",
            ["AccentIndigo"] = "#5C6BC0",
            ["AccentPurple"] = "#7E57C2",
            ["AccentGreen"] = "#16825D",
            ["AccentYellow"] = "#B8860B",
            ["AccentOrange"] = "#C77700",
            ["AccentRed"] = "#D32F2F",

            ["BadgeBgPrimary"] = "#DCEEFA",
            ["BadgeBorderPrimary"] = "#A8D3F0",
            ["BadgeBgSuccess"] = "#DDF3E9",
            ["BadgeBorderSuccess"] = "#A6D9BF",
            ["BadgeBgWarning"] = "#FBF0D6",
            ["BadgeBorderWarning"] = "#E8CF8F",
            ["BadgeBgDanger"] = "#FBE0E0",
            ["BadgeBorderDanger"] = "#EDA9A9",
            ["BadgeBgInfo"] = "#E6E3F7",
            ["BadgeBorderInfo"] = "#C2BBE8",
            ["BadgeBgDefault"] = "#EDEDED",
            ["BadgeBorderDefault"] = "#D4D4D4",

            ["TextPrimary"] = "#101010",
            ["TextSecondary"] = "#505050",
            ["TextMuted"] = "#707070",
            ["TextDisabled"] = "#A0A0A0",

            ["DangerHoverBg"] = "#F6CDCD",
            ["ScrollThumb"] = "#C8C8C8",
            ["ScrollThumbHover"] = "#A8A8A8"
        };

        /// <summary>Applies the saved theme, or follows the Windows app theme on first run.</summary>
        public static void Initialize()
        {
            AppTheme theme = LoadSaved() ?? DetectSystemTheme();
            Apply(theme, persist: false);
        }

        public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, persist: true);

        public static void Apply(AppTheme theme, bool persist)
        {
            var app = Application.Current;
            if (app == null) return;

            var palette = theme == AppTheme.Dark ? Dark : Light;
            foreach (var kv in palette)
            {
                var color = (Color)ColorConverter.ConvertFromString(kv.Value);
                if (app.Resources[kv.Key] is SolidColorBrush brush && !brush.IsFrozen)
                {
                    brush.Color = color;
                }
                else
                {
                    app.Resources[kv.Key] = new SolidColorBrush(color);
                }
            }

            Current = theme;
            if (persist) Save(theme);
            ThemeChanged?.Invoke(theme);
        }

        private static AppTheme DetectSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0 ? AppTheme.Dark : AppTheme.Light;
            }
            catch { }
            return AppTheme.Dark;
        }

        private static AppTheme? LoadSaved()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key?.GetValue(RegistryValue) is string s && Enum.TryParse<AppTheme>(s, out var t)) return t;
            }
            catch { }
            return null;
        }

        private static void Save(AppTheme theme)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(RegistryValue, theme.ToString());
            }
            catch { }
        }
    }
}
