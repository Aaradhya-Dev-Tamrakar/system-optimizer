using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace NovaOptimizer.Native
{
    public class TrayIconManager : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 102;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, int nIcons);

        [DllImport("user32.dll", EntryPoint = "LoadIconW")]
        private static extern IntPtr Win32LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;

        private readonly Window _window;
        private IntPtr _hWnd;
        private IntPtr _hIcon;
        private bool _isAdded;
        private readonly ContextMenu _contextMenu;
        private HwndSource? _hwndSource;

        public event Action? OnQuickCleanRequested;
        public event Action? OnToggleBoostRequested;

        public TrayIconManager(Window window)
        {
            _window = window;
            _contextMenu = CreateContextMenu();
        }

        public void Initialize()
        {
            _hWnd = new WindowInteropHelper(_window).EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(WndProc);

            LoadIcon();
            AddTrayIcon();
        }

        private void LoadIcon()
        {
            try
            {
                int cx = GetSystemMetrics(SM_CXSMICON);
                int cy = GetSystemMetrics(SM_CYSMICON);
                if (cx <= 0) cx = 16;
                if (cy <= 0) cy = 16;

                // 1. Primary: Assets folder in BaseDirectory
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(iconPath))
                {
                    _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, cx, cy, 0x00000010 /*LR_LOADFROMFILE*/);
                    if (_hIcon != IntPtr.Zero) return;
                }

                // 2. Fallback: Search parent directories (useful during dev / test runs)
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 4 && dir != null; i++)
                {
                    string candidate = Path.Combine(dir, "Assets", "icon.ico");
                    if (File.Exists(candidate))
                    {
                        _hIcon = LoadImage(IntPtr.Zero, candidate, 1, cx, cy, 0x00000010);
                        if (_hIcon != IntPtr.Zero) return;
                    }
                    dir = Directory.GetParent(dir)?.FullName;
                }

                // 3. Fallback: Extract from embedded WPF resource safely
                try
                {
                    var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
                    if (sri != null)
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), $"NovaTray_{Guid.NewGuid():N}.ico");
                        try
                        {
                            using (var src = sri.Stream)
                            using (var dst = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                src.CopyTo(dst);
                            }

                            _hIcon = LoadImage(IntPtr.Zero, tempFile, 1, cx, cy, 0x00000010 /*LR_LOADFROMFILE*/);
                        }
                        finally
                        {
                            try
                            {
                                if (File.Exists(tempFile)) File.Delete(tempFile);
                            }
                            catch { }
                        }

                        if (_hIcon != IntPtr.Zero) return;
                    }
                }
                catch { }

                // 4. Fallback: Extract embedded icon from executable PE resource
                string? procPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                {
                    ExtractIconEx(procPath, 0, out IntPtr hLarge, out IntPtr hSmall, 1);
                    _hIcon = hSmall != IntPtr.Zero ? hSmall : hLarge;
                    if (hLarge != IntPtr.Zero && hLarge != _hIcon)
                    {
                        DestroyIcon(hLarge);
                    }
                    if (_hIcon != IntPtr.Zero) return;
                }

                // 5. Ultimate fallback: System application icon
                if (_hIcon == IntPtr.Zero)
                {
                    _hIcon = Win32LoadIcon(IntPtr.Zero, (IntPtr)32512 /* IDI_APPLICATION */);
                }
            }
            catch { }
        }

        private void AddTrayIcon()
        {
            uint flags = NIF_MESSAGE | NIF_TIP;
            if (_hIcon != IntPtr.Zero)
            {
                flags |= NIF_ICON;
            }

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = flags,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "NovaOptimizer — High-Perf System Optimizer"
            };

            _isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        }

        public void UpdateTooltip(string tooltip)
        {
            if (!_isAdded) return;
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_TIP,
                szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
            };
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#161926")!,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#282F48")!
            };

            var itemOpen = new MenuItem { Header = "🌌 Open NovaOptimizer", FontWeight = FontWeights.Bold };
            itemOpen.Click += (s, e) => ShowWindow();

            var itemClean = new MenuItem { Header = "⚡ Quick RAM Clean" };
            itemClean.Click += (s, e) => OnQuickCleanRequested?.Invoke();

            var itemBoost = new MenuItem { Header = "🎮 Toggle Game Boost" };
            itemBoost.Click += (s, e) => OnToggleBoostRequested?.Invoke();

            var itemExit = new MenuItem { Header = "❌ Exit" };
            itemExit.Click += (s, e) =>
            {
                Dispose();
                Application.Current.Shutdown();
            };

            menu.Items.Add(itemOpen);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemClean);
            menu.Items.Add(itemBoost);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemExit);

            return menu;
        }

        private void ShowWindow()
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONUP)
                {
                    ShowWindow();
                    handled = true;
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    SetForegroundWindow(_hWnd);
                    _contextMenu.IsOpen = true;
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hwndSource != null)
                {
                    try
                    {
                        _hwndSource.RemoveHook(WndProc);
                    }
                    catch { }
                    _hwndSource = null;
                }
            }

            if (_isAdded)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                    hWnd = _hWnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _isAdded = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }

        ~TrayIconManager()
        {
            Dispose(false);
        }
    }
}
