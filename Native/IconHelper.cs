using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaOptimizer.Native
{
    public static class IconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Retrieves the small (16x16) icon for an executable or file path.
        /// Caches results to ensure zero UI lag.
        /// </summary>
        public static ImageSource? GetIcon(string? filePath, string fallbackName = "")
        {
            string key = !string.IsNullOrEmpty(filePath) ? filePath : fallbackName;
            if (string.IsNullOrWhiteSpace(key)) return null;

            return Cache.GetOrAdd(key, _ =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        return ExtractFromFile(filePath);
                    }
                }
                catch { }
                return null;
            });
        }

        private static ImageSource? ExtractFromFile(string filePath)
        {
            var shinfo = new SHFILEINFO();
            IntPtr ptr = SHGetFileInfo(
                filePath,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                SHGFI_ICON | SHGFI_SMALLICON);

            if (ptr != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze(); // Freezing allows cross-thread access in WPF
                    return bs;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
            return null;
        }
    }
}
