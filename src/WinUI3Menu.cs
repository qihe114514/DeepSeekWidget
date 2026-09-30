using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace DeepSeekWidget {

    internal sealed class WinUI3Menu : IDisposable {
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        readonly NativeWindowHost _host;
        readonly DesktopWindowXamlSource _island;
        readonly Grid _root;
        readonly MenuFlyout _flyout;
        bool _opening;
        PointInt32 _pendingPoint;
        double _scale = 1.0;
        int _workLeft;
        int _workTop;

        public event Action<string> Command;

        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Thickness ItemPadding = new Thickness(12, 0, 12, 0);
        const double ItemMinHeight = 32;
        const double IconSize = 16;

        static void ApplyMetrics(Control item) {
            item.Padding = ItemPadding;
            item.MinHeight = ItemMinHeight;
        }

        public WinUI3Menu() {
            _host = new NativeWindowHost("DeepSeekWidgetWinUI3Menu",
                unchecked((uint)Win32.WS_POPUP),
                (uint)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST),
                1, 1, OnNativeMessage);
            _island = new DesktopWindowXamlSource();
            _island.Initialize(new WindowId((ulong)(long)_host.Handle));
            _root = new Grid {
                Width = 1,
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            };
            _island.Content = _root;
            _flyout = new MenuFlyout();
            _flyout.Closed += (s, e) => RestoreHost();
            try { _island.SiteBridge.MoveAndResize(new RectInt32(0, 0, 1, 1)); } catch { }
            Win32.SetWindowPos(_host.Handle, Win32.HWND_TOPMOST, 0, 0, 1, 1,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _host.ShowNoActivate();
        }

        void OnNativeMessage(uint message, IntPtr wParam, IntPtr lParam) { }

        static FontIcon Icon(string glyph) {
            return new FontIcon { Glyph = glyph, FontFamily = IconFont, FontSize = IconSize };
        }

        public MenuFlyoutItem Add(string id, string text, string glyph) {
            var item = new MenuFlyoutItem { Text = text };
            ApplyMetrics(item);
            if (!string.IsNullOrEmpty(glyph)) item.Icon = Icon(glyph);
            item.Click += (s, e) => Invoke(id);
            _flyout.Items.Add(item);
            return item;
        }

        public static void SetGlyph(MenuFlyoutItem item, string glyph) {
            if (item == null) return;
            var icon = item.Icon as FontIcon;
            if (icon == null) item.Icon = Icon(glyph);
            else icon.Glyph = glyph;
        }

        public MenuFlyoutSubItem AddSub(string text, string glyph) {
            var sub = new MenuFlyoutSubItem { Text = text };
            ApplyMetrics(sub);
            if (!string.IsNullOrEmpty(glyph)) sub.Icon = Icon(glyph);
            _flyout.Items.Add(sub);
            return sub;
        }

        public MenuFlyoutItem AddTo(MenuFlyoutSubItem parent, string id, string text, string glyph) {
            var item = new MenuFlyoutItem { Text = text };
            ApplyMetrics(item);
            if (!string.IsNullOrEmpty(glyph)) item.Icon = Icon(glyph);
            item.Click += (s, e) => Invoke(id);
            parent.Items.Add(item);
            return item;
        }

        public void AddSeparator() {
            _flyout.Items.Add(new MenuFlyoutSeparator());
        }

        void Invoke(string id) {
            try { _flyout.Hide(); } catch { }
            var handler = Command;
            if (handler != null) handler(id);
        }

        public void Show(PointInt32 screenPoint) {
            try {
                if (_opening) return;
                _opening = true;
                _pendingPoint = screenPoint;
                Win32.RECT work;
                if (!TryGetWorkArea(screenPoint, out work)) {
                    work = new Win32.RECT { Left = 0, Top = 0, Right = screenPoint.X + 1, Bottom = screenPoint.Y + 1 };
                }
                _scale = DpiScale();
                _workLeft = work.Left;
                _workTop = work.Top;
                ThemeManager.Apply(_root);
                _root.Width = Math.Max(1, work.Right - work.Left) / _scale;
                _root.Height = Math.Max(1, work.Bottom - work.Top) / _scale;
                ForceForeground(_host.Handle);
                if (_root.IsLoaded) OpenFlyout();
                else _root.Loaded += OnRootLoaded;
            } catch (Exception ex) {
                _opening = false;
                Log.Write("显示 WinUI 3 菜单失败: " + ex);
            }
        }

        void OnRootLoaded(object sender, RoutedEventArgs e) {
            _root.Loaded -= OnRootLoaded;
            OpenFlyout();
        }

        void OpenFlyout() {
            try {
                var options = new FlyoutShowOptions {
                    Position = new Point((_pendingPoint.X - _workLeft) / _scale,
                                         (_pendingPoint.Y - _workTop) / _scale),
                    ShowMode = FlyoutShowMode.Transient,
                    Placement = FlyoutPlacementMode.Auto
                };
                _flyout.ShowAt(_root, options);
                if (_flyout.IsOpen) {
                    _opening = false;
                    return;
                }
                var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (queue != null) {
                    queue.TryEnqueue(() => {
                        try { if (!_flyout.IsOpen) _flyout.ShowAt(_root, options); }
                        finally { _opening = false; }
                    });
                } else {
                    _opening = false;
                }
            } catch (Exception ex) {
                _opening = false;
                Log.Write("打开 WinUI 3 菜单失败: " + ex);
            }
        }

        public void Hide() {
            try { _flyout.Hide(); } catch { }
        }

        void RestoreHost() {
            _root.Width = 1;
            _root.Height = 1;
        }

        static void ForceForeground(IntPtr hwnd) {
            try {
                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
                uint currentThread = GetCurrentThreadId();
                bool attached = false;
                if (foregroundThread != 0 && foregroundThread != currentThread) {
                    attached = AttachThreadInput(currentThread, foregroundThread, true);
                }
                try {
                    BringWindowToTop(hwnd);
                    SetActiveWindow(hwnd);
                    SetForegroundWindow(hwnd);
                } finally {
                    if (attached) AttachThreadInput(currentThread, foregroundThread, false);
                }
            } catch { }
        }

        double DpiScale() {
            uint dpi = Win32.GetDpiForWindow(_host.Handle);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }

        static bool TryGetWorkArea(PointInt32 point, out Win32.RECT work) {
            work = new Win32.RECT();
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFO)) };
            IntPtr monitor = Win32.MonitorFromPoint(new Win32.POINT { X = point.X, Y = point.Y },
                Win32.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info)) {
                work = info.rcWork;
                return true;
            }
            return false;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern IntPtr SetActiveWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public void Dispose() {
            try { _island.Dispose(); } catch { }
            try { _host.Dispose(); } catch { }
        }
    }
}
