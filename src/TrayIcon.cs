using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace DeepSeekWidget {

    public class TrayIcon : IDisposable {
        const uint NIM_ADD = 0;
        const uint NIM_MODIFY = 1;
        const uint NIM_DELETE = 2;
        const uint NIF_MESSAGE = 1;
        const uint NIF_ICON = 2;
        const uint NIF_TIP = 4;
        const uint NIF_INFO = 0x10;
        const uint NIIF_INFO = 1;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONUP = 0x0205;
        const uint TrayMessage = Win32.WM_APP + 101;
        const uint IconId = 1;

        readonly NativeWindowHost _window;
        readonly WinUI3Menu _menu;
        readonly IntPtr _hIcon;
        readonly bool _ownsIcon;

        public event Action MoveRequested;
        public event Action ResetPositionRequested;
        public event Action LoginRequested;
        public event Action ExitRequested;
        public event Action<bool> AutoStartChanged;
        public event Action<int> RefreshIntervalChanged;
        public event Action<int> CacheHitWindowChanged;
        public event Action<bool> PinModeChanged;
        public event Action<ThemePreference> ThemeChanged;
        public event Action AboutRequested;
        public event Action AppearanceRequested;
        public event Action RefreshRequested;

        readonly Dictionary<int, MenuFlyoutItem> _refreshItems = new Dictionary<int, MenuFlyoutItem>();
        readonly Dictionary<int, MenuFlyoutItem> _cacheWindowItems = new Dictionary<int, MenuFlyoutItem>();
        readonly Dictionary<ThemePreference, MenuFlyoutItem> _themeItems = new Dictionary<ThemePreference, MenuFlyoutItem>();
        MenuFlyoutItem _miPinTop;
        MenuFlyoutItem _miPinBottom;
        MenuFlyoutItem _miAuto;

        public TrayIcon(int refreshSeconds, bool pinTop, int cacheWindowMinutes, ThemePreference theme) {
            IntPtr large;
            Win32.ExtractIconEx(Environment.ProcessPath, 0, out large, out _hIcon, 1);
            _ownsIcon = _hIcon != IntPtr.Zero;
            if (_hIcon == IntPtr.Zero) _hIcon = large;
            if (large != IntPtr.Zero && large != _hIcon) Win32.DestroyIcon(large);

            _window = new NativeWindowHost("DeepSeekWidgetTray", unchecked((uint)Win32.WS_OVERLAPPED), 0,
                0, 0, OnNativeMessage);
            var data = CreateIconData("DeepSeek 余额小组件");
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = TrayMessage;
            if (!Win32.Shell_NotifyIcon(NIM_ADD, ref data)) {
                Log.Write("Shell_NotifyIcon 添加失败: " + Marshal.GetLastWin32Error());
            }

            _menu = new WinUI3Menu();
            _menu.Command += OnCommand;
            _menu.Add("refresh", "立即刷新", "\uE72C");

            var position = _menu.AddSub("位置", "\uE707");
            _menu.AddTo(position, "move", "移动位置", "\uE7C2");
            _menu.AddTo(position, "reset", "重置位置", "\uE7A7");

            var layer = _menu.AddSub("窗口层级", "\uE81E");
            _miPinTop = _menu.AddTo(layer, "pin-top", "窗口置顶", "\uE81E");
            _miPinBottom = _menu.AddTo(layer, "pin-bottom", "窗口置底", "\uE81E");

            var refresh = _menu.AddSub("刷新频率", "\uE916");
            var opts = new[] {
                new { Sec = 30, Label = "30 秒" },
                new { Sec = 60, Label = "1 分钟" },
                new { Sec = 120, Label = "2 分钟" },
                new { Sec = 300, Label = "5 分钟" },
                new { Sec = 600, Label = "10 分钟" }
            };
            foreach (var option in opts) {
                _refreshItems[option.Sec] = _menu.AddTo(refresh, "refresh:" + option.Sec, option.Label, "\uE916");
            }

            var cacheWindow = _menu.AddSub("缓存命中率窗口", "\uE916");
            _cacheWindowItems[5] = _menu.AddTo(cacheWindow, "cache-window:5", "近 5 分钟", "\uE916");
            _cacheWindowItems[10] = _menu.AddTo(cacheWindow, "cache-window:10", "近 10 分钟", "\uE916");

            var themes = _menu.AddSub("主题", "\uE790");
            _themeItems[ThemePreference.System] = _menu.AddTo(themes, "theme:system", "跟随系统", "\uE770");
            _themeItems[ThemePreference.Light] = _menu.AddTo(themes, "theme:light", "浅色", "\uE706");
            _themeItems[ThemePreference.Dark] = _menu.AddTo(themes, "theme:dark", "深色", "\uE708");

            _menu.Add("appearance", "外观设置…", "\uE790");
            _miAuto = _menu.Add("autostart", "开机自启动", "\uE945");
            _menu.Add("login", "登录 DeepSeek 账号…", "\uE77B");
            _menu.Add("about", "关于", "\uE946");
            _menu.AddSeparator();
            _menu.Add("exit", "退出", "\uE8BB");

            SelectPin(pinTop);
            SelectRefresh(refreshSeconds);
            SelectCacheWindow(cacheWindowMinutes);
            SelectTheme(theme, false);
            UpdateAutoIcon(AutoStart.IsEnabled());
        }

        void OnNativeMessage(uint message, IntPtr wParam, IntPtr lParam) {
            if (message != TrayMessage) return;
            int mouseMessage = unchecked((int)(lParam.ToInt64() & 0xFFFF));
            if (mouseMessage == WM_RBUTTONUP) ShowMenu();
            else if (mouseMessage == WM_LBUTTONUP && RefreshRequested != null) RefreshRequested();
        }

        void ShowMenu() {
            Win32.POINT point;
            if (!Win32.GetCursorPos(out point)) return;
            _menu.Show(new Windows.Graphics.PointInt32(point.X, point.Y));
        }

        public void ShowMenuAt(int x, int y) {
            _menu.Show(new Windows.Graphics.PointInt32(x, y));
        }

        void OnCommand(string id) {
            switch (id) {
                case "refresh":
                    if (RefreshRequested != null) RefreshRequested();
                    break;
                case "move":
                    if (MoveRequested != null) MoveRequested();
                    break;
                case "reset":
                    if (ResetPositionRequested != null) ResetPositionRequested();
                    break;
                case "pin-top":
                    SelectPin(true);
                    break;
                case "pin-bottom":
                    SelectPin(false);
                    break;
                case "appearance":
                    if (AppearanceRequested != null) AppearanceRequested();
                    break;
                case "autostart": {
                    bool enabled = !AutoStart.IsEnabled();
                    UpdateAutoIcon(enabled);
                    if (AutoStartChanged != null) AutoStartChanged(enabled);
                    break;
                }
                case "login":
                    if (LoginRequested != null) LoginRequested();
                    break;
                case "about":
                    if (AboutRequested != null) AboutRequested();
                    break;
                case "exit":
                    if (ExitRequested != null) ExitRequested();
                    break;
                default:
                    if (id.StartsWith("refresh:", StringComparison.Ordinal)) {
                        int seconds;
                        if (int.TryParse(id.Substring("refresh:".Length), out seconds)) SelectRefresh(seconds);
                    } else if (id.StartsWith("cache-window:", StringComparison.Ordinal)) {
                        int minutes;
                        if (int.TryParse(id.Substring("cache-window:".Length), out minutes)) SelectCacheWindow(minutes);
                    } else if (id.StartsWith("theme:", StringComparison.Ordinal)) {
                        SelectTheme(ThemePreferenceValues.Parse(id.Substring("theme:".Length)), true);
                    }
                    break;
            }
        }

        public void ShowBalloon(string title, string text) {
            try {
                var data = CreateIconData("DeepSeek 余额小组件");
                data.uFlags = NIF_INFO;
                data.szInfoTitle = title ?? "";
                data.szInfo = text ?? "";
                data.dwInfoFlags = NIIF_INFO;
                data.uTimeoutOrVersion = 5000;
                Win32.Shell_NotifyIcon(NIM_MODIFY, ref data);
            } catch { }
        }

        void SelectPin(bool top) {
            WinUI3Menu.SetGlyph(_miPinTop, top ? "\uE73E" : "\uE81E");
            WinUI3Menu.SetGlyph(_miPinBottom, top ? "\uE81E" : "\uE73E");
            if (PinModeChanged != null) PinModeChanged(top);
        }

        void SelectRefresh(int seconds) {
            foreach (var item in _refreshItems) {
                WinUI3Menu.SetGlyph(item.Value, item.Key == seconds ? "\uE73E" : "\uE916");
            }
            if (RefreshIntervalChanged != null) RefreshIntervalChanged(seconds);
        }

        void SelectCacheWindow(int minutes) {
            if (minutes != 5 && minutes != 10) minutes = 5;
            foreach (var item in _cacheWindowItems) {
                WinUI3Menu.SetGlyph(item.Value, item.Key == minutes ? "\uE73E" : "\uE916");
            }
            if (CacheHitWindowChanged != null) CacheHitWindowChanged(minutes);
        }

        void SelectTheme(ThemePreference preference, bool raiseEvent) {
            foreach (var item in _themeItems) {
                WinUI3Menu.SetGlyph(item.Value, item.Key == preference ? "\uE73E" : "\uE770");
            }
            if (raiseEvent && ThemeChanged != null) ThemeChanged(preference);
        }

        public void SetTheme(ThemePreference preference) {
            SelectTheme(preference, false);
        }

        void UpdateAutoIcon(bool enabled) {
            WinUI3Menu.SetGlyph(_miAuto, enabled ? "\uE73E" : "\uE945");
        }

        Win32.NOTIFYICONDATA CreateIconData(string tip) {
            return new Win32.NOTIFYICONDATA {
                cbSize = Marshal.SizeOf(typeof(Win32.NOTIFYICONDATA)),
                hWnd = _window.Handle,
                uID = IconId,
                hIcon = _hIcon,
                szTip = tip ?? "",
                szInfo = "",
                szInfoTitle = ""
            };
        }

        public void Dispose() {
            try {
                var data = CreateIconData("DeepSeek 余额小组件");
                Win32.Shell_NotifyIcon(NIM_DELETE, ref data);
            } catch { }
            if (_menu != null) _menu.Dispose();
            if (_window != null) _window.Dispose();
            if (_ownsIcon && _hIcon != IntPtr.Zero) Win32.DestroyIcon(_hIcon);
        }
    }
}
