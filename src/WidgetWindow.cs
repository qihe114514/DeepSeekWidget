using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace DeepSeekWidget {

    public sealed class WidgetWindow {
        const double BaseWidth = 318;
        const double BaseHeight = 235;

        readonly Config _config;
        readonly Window _window;
        readonly Grid _host;
        readonly Border _card;
        readonly List<FrameworkElement> _interactive = new List<FrameworkElement>();
        readonly TextBlock _txtPeriod = new TextBlock { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        readonly Border _periodChip;
        readonly Border _chipLow;
        readonly TextBlock _txtBalance = new TextBlock { FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        readonly TextBlock _txtUsage = new TextBlock { FontSize = 12 };
        readonly TextBlock _txtUsageTokens = new TextBlock { FontSize = 11 };
        readonly TextBlock _txtUpdated = new TextBlock { FontSize = 10 };
        readonly StackPanel _spark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Height = 28, VerticalAlignment = VerticalAlignment.Bottom };
        readonly StackPanel _trendPanel = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        readonly Button _btnRefresh;
        readonly Button _btnLogin;
        readonly Button _btnRecharge;

        DispatcherQueueTimer _clickTimer;
        DispatcherQueueTimer _bottomTimer;
        DispatcherQueueTimer _periodTimer;
        DispatcherQueueTimer _moveTimer;
        IntPtr _hwnd;
        Win32.WndProcDelegate _wndProc;
        IntPtr _originalWndProc;
        bool _positioned;
        bool _moveMode;
        bool _pinTop;
        bool _clickThrough;
        bool _dragging;
        PointInt32 _dragCursor;
        PointInt32 _dragWindow;
        UsageInfo _lastUsage;
        CacheHitRate _lastCacheRate;
        bool _usageSummaryVisible;

        public WidgetWindow(Config config) {
            _config = config;
            _window = new Window { Title = "DeepSeekWidget" };
            _window.AppWindow.Resize(new SizeInt32((int)BaseWidth, (int)BaseHeight));

            if (_window.AppWindow.Presenter is OverlappedPresenter presenter) {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
            try { _window.SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { }

            _btnRecharge = CreateButton("充值", "充值", true, 30);
            _btnRecharge.Click += (s, e) => App.Instance.OpenRecharge();
            _btnRefresh = CreateButton("刷新", "立即刷新", false, 26);
            _btnRefresh.Click += (s, e) => App.Instance.RefreshNow();
            _btnLogin = CreateButton("登录 DeepSeek 账号", "登录 DeepSeek 账号", false, 26);
            _btnLogin.HorizontalAlignment = HorizontalAlignment.Left;
            _btnLogin.Visibility = Visibility.Collapsed;
            _btnLogin.Click += (s, e) => App.Instance.OpenLogin();

            _periodChip = new Border {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _txtPeriod
            };
            _chipLow = new Border {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(7, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "余额偏低", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
            };

            var content = BuildContent();
            _card = new Border {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(7),
                Child = content
            };
            _host = new Grid { Width = BaseWidth, Height = BaseHeight, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _host.Children.Add(_card);
            _window.Content = _host;
            ThemeManager.Apply(_host);

            _window.Activated += (s, e) => EnsureInitialized();
            _window.Closed += (s, e) => {
                StopTimers();
                ThemeManager.Changed -= ApplyTheme;
                if (_hwnd != IntPtr.Zero && _originalWndProc != IntPtr.Zero) {
                    Win32.SetWindowLongPtr(_hwnd, Win32.GWLP_WNDPROC, _originalWndProc);
                }
            };
            ThemeManager.Changed += ApplyTheme;
            ApplyTheme();
        }

        Grid BuildContent() {
            var grid = new Grid { Padding = new Thickness(14, 12, 13, 10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new TextBlock {
                Text = "DeepSeek 余额",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var brandRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            brandRow.Children.Add(brand);
            brandRow.Children.Add(_periodChip);
            Grid.SetRow(brandRow, 0);
            grid.Children.Add(brandRow);

            Grid.SetRow(_btnRecharge, 0);
            Grid.SetColumn(_btnRecharge, 1);
            grid.Children.Add(_btnRecharge);

            var balanceStack = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var balanceLabel = new TextBlock { Text = "可用余额", FontSize = 11, Opacity = 0.62, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var balanceRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, 0) };
            balanceRow.Children.Add(_txtBalance);
            balanceRow.Children.Add(_chipLow);
            balanceStack.Children.Add(balanceLabel);
            balanceStack.Children.Add(balanceRow);
            Grid.SetRow(balanceStack, 1);
            grid.Children.Add(balanceStack);

            var usageGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            usageGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var usageStack = new StackPanel { Spacing = 3 };
            usageStack.Children.Add(_txtUsage);
            usageStack.Children.Add(_txtUsageTokens);
            usageStack.Children.Add(_btnLogin);
            usageGrid.Children.Add(usageStack);
            Grid.SetRow(usageGrid, 2);
            grid.Children.Add(usageGrid);

            var trendCaption = new TextBlock {
                Text = "近 7 日",
                FontSize = 10,
                Opacity = 0.48,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _trendPanel.Children.Add(trendCaption);
            _trendPanel.Children.Add(_spark);
            _trendPanel.HorizontalAlignment = HorizontalAlignment.Right;
            _trendPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_trendPanel, 1);
            Grid.SetRowSpan(_trendPanel, 3);
            Grid.SetColumn(_trendPanel, 1);
            grid.Children.Add(_trendPanel);

            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtUpdated.VerticalAlignment = VerticalAlignment.Center;
            _txtUpdated.Opacity = 0.55;
            footer.Children.Add(_txtUpdated);
            Grid.SetColumn(_btnRefresh, 1);
            footer.Children.Add(_btnRefresh);
            Grid.SetRow(footer, 4);
            Grid.SetColumnSpan(footer, 2);
            grid.Children.Add(footer);

            _interactive.Add(_btnRecharge);
            _interactive.Add(_btnRefresh);
            _interactive.Add(_btnLogin);
            return grid;
        }

        static Button CreateButton(string text, string automationName, bool accent, double height) {
            var button = new Button {
                Content = text,
                Height = height,
                MinWidth = 0,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Top
            };
            if (accent) {
                button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 108, 189));
                button.Foreground = new SolidColorBrush(Colors.White);
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
            ToolTipService.SetToolTip(button, automationName);
            return button;
        }

        public void Show() {
            _window.Activate();
            EnsureInitialized();
        }

        public void Close() {
            try { _window.Close(); } catch { }
        }

        void EnsureInitialized() {
            if (_positioned) return;
            _positioned = true;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            int style = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt32();
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE,
                new IntPtr(style | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
            _wndProc = WidgetWndProc;
            _originalWndProc = Win32.SetWindowLongPtr(_hwnd, Win32.GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProc));
            PositionFromConfig();
            StartTimers();
        }

        void StartTimers() {
            _clickTimer = _window.DispatcherQueue.CreateTimer();
            _clickTimer.Interval = TimeSpan.FromMilliseconds(150);
            _clickTimer.IsRepeating = true;
            _clickTimer.Tick += (s, e) => UpdateClickThrough();
            _clickTimer.Start();

            _periodTimer = _window.DispatcherQueue.CreateTimer();
            _periodTimer.Interval = TimeSpan.FromSeconds(10);
            _periodTimer.IsRepeating = true;
            _periodTimer.Tick += (s, e) => UpdatePeriod();
            _periodTimer.Start();
            UpdatePeriod();

            _bottomTimer = _window.DispatcherQueue.CreateTimer();
            _bottomTimer.Interval = TimeSpan.FromSeconds(2);
            _bottomTimer.IsRepeating = true;
            _bottomTimer.Tick += (s, e) => KeepBottom();
            _bottomTimer.Start();
        }

        void StopTimers() {
            try { if (_clickTimer != null) _clickTimer.Stop(); } catch { }
            try { if (_bottomTimer != null) _bottomTimer.Stop(); } catch { }
            try { if (_periodTimer != null) _periodTimer.Stop(); } catch { }
            try { if (_moveTimer != null) _moveTimer.Stop(); } catch { }
        }

        void KeepBottom() {
            if (_pinTop || _hwnd == IntPtr.Zero) return;
            Win32.SetWindowPos(_hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        public void SetOpacity(int percent) {
            _host.Opacity = Math.Max(0.2, Math.Min(1.0, percent / 100.0));
        }

        public void SetScale(int percent) {
            double scale = Math.Max(0.6, Math.Min(2.5, percent / 100.0));
            _host.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            _host.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
            uint dpi = _hwnd == IntPtr.Zero ? 96 : Win32.GetDpiForWindow(_hwnd);
            double multiplier = dpi / 96.0;
            _window.AppWindow.Resize(new SizeInt32(
                (int)Math.Ceiling(BaseWidth * scale * multiplier),
                (int)Math.Ceiling(BaseHeight * scale * multiplier)));
        }

        public void SetPinMode(bool top) {
            _pinTop = top;
            if (_hwnd != IntPtr.Zero) {
                Win32.SetWindowPos(_hwnd, top ? Win32.HWND_TOPMOST : Win32.HWND_BOTTOM,
                    0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }
        }

        public void ToggleMoveMode() {
            _moveMode = !_moveMode;
            _card.BorderBrush = new SolidColorBrush(_moveMode
                ? Windows.UI.Color.FromArgb(255, 45, 127, 249)
                : (ThemeManager.IsLight ? Windows.UI.Color.FromArgb(42, 0, 0, 0) : Windows.UI.Color.FromArgb(58, 255, 255, 255)));
            if (_moveMode) {
                _dragging = false;
                _host.PointerPressed += BeginDrag;
                _host.PointerMoved += ContinueDrag;
                _host.PointerReleased += EndDrag;
                if (_moveTimer == null) {
                    _moveTimer = _window.DispatcherQueue.CreateTimer();
                    _moveTimer.Interval = TimeSpan.FromSeconds(12);
                    _moveTimer.IsRepeating = false;
                    _moveTimer.Tick += (s, e) => ToggleMoveMode();
                }
                _moveTimer.Start();
            } else {
                _host.PointerPressed -= BeginDrag;
                _host.PointerMoved -= ContinueDrag;
                _host.PointerReleased -= EndDrag;
                if (_moveTimer != null) _moveTimer.Stop();
                SavePosition();
            }
            UpdateClickThrough();
        }

        public void ResetPosition() {
            PositionFromConfig(true);
            SavePosition();
        }

        void BeginDrag(object sender, PointerRoutedEventArgs e) {
            if (!_moveMode) return;
            Win32.POINT cursor;
            if (!Win32.GetCursorPos(out cursor)) return;
            _dragCursor = new PointInt32(cursor.X, cursor.Y);
            _dragWindow = _window.AppWindow.Position;
            _dragging = true;
            _host.CapturePointer(e.Pointer);
        }

        void ContinueDrag(object sender, PointerRoutedEventArgs e) {
            if (!_moveMode || !_dragging) return;
            Win32.POINT cursor;
            if (!Win32.GetCursorPos(out cursor)) return;
            _window.AppWindow.Move(new PointInt32(
                _dragWindow.X + cursor.X - _dragCursor.X,
                _dragWindow.Y + cursor.Y - _dragCursor.Y));
        }

        void EndDrag(object sender, PointerRoutedEventArgs e) {
            if (!_dragging) return;
            _dragging = false;
            _host.ReleasePointerCapture(e.Pointer);
            SavePosition();
        }

        void UpdateClickThrough() {
            if (_hwnd == IntPtr.Zero) return;
            bool transparent = true;
            if (_moveMode) {
                transparent = false;
            } else {
                Win32.POINT cursor;
                if (!Win32.GetCursorPos(out cursor)) return;
                var local = new Win32.POINT { X = cursor.X, Y = cursor.Y };
                Win32.ScreenToClient(_hwnd, ref local);
                var scale = _host.XamlRoot == null ? 1.0 : _host.XamlRoot.RasterizationScale;
                var point = new Windows.Foundation.Point(local.X / scale, local.Y / scale);
                transparent = HitInteractive(point) == null;
            }
            if (transparent == _clickThrough) return;
            _clickThrough = transparent;
            int style = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt32();
            if (transparent) style |= Win32.WS_EX_TRANSPARENT;
            else style &= ~Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
        }

        FrameworkElement HitInteractive(Windows.Foundation.Point point) {
            foreach (var element in _interactive) {
                if (element == null || element.Visibility != Visibility.Visible) continue;
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                try {
                    var transform = element.TransformToVisual(_host);
                    var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    var bounds = new Windows.Foundation.Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
                    if (bounds.Contains(point)) return element;
                } catch { }
            }
            return null;
        }

        IntPtr WidgetWndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam) {
            if (message == Win32.WM_WINDOWPOSCHANGING) {
                try {
                    var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                    if ((pos.flags & Win32.SWP_HIDEWINDOW) != 0) {
                        pos.flags &= ~Win32.SWP_HIDEWINDOW;
                        Marshal.StructureToPtr(pos, lParam, false);
                        return IntPtr.Zero;
                    }
                } catch { }
            }
            return CallWindowProc(_originalWndProc, hWnd, message, wParam, lParam);
        }

        void PositionFromConfig(bool reset = false) {
            uint dpi = _hwnd == IntPtr.Zero ? 96 : Win32.GetDpiForWindow(_hwnd);
            double scale = dpi / 96.0;
            int width = _window.AppWindow.Size.Width;
            int height = _window.AppWindow.Size.Height;
            Win32.POINT cursor;
            Win32.GetCursorPos(out cursor);
            var monitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFO)) };
            if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref info)) return;

            if (!reset && _config.WindowX.HasValue && _config.WindowY.HasValue) {
                _window.AppWindow.Move(new PointInt32(
                    (int)Math.Round(_config.WindowX.Value * scale),
                    (int)Math.Round(_config.WindowY.Value * scale)));
                return;
            }
            _window.AppWindow.Move(new PointInt32(
                info.rcWork.Right - width - 24,
                info.rcWork.Top + 24));
        }

        void SavePosition() {
            if (_hwnd == IntPtr.Zero) return;
            uint dpi = Win32.GetDpiForWindow(_hwnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var position = _window.AppWindow.Position;
            _config.WindowX = position.X / scale;
            _config.WindowY = position.Y / scale;
            _config.Save();
        }

        public void ApplyTheme() {
            bool light = ThemeManager.IsLight;
            var cardStart = light ? Windows.UI.Color.FromArgb(238, 255, 255, 255) : Windows.UI.Color.FromArgb(200, 40, 47, 60);
            var cardEnd = light ? Windows.UI.Color.FromArgb(232, 242, 242, 242) : Windows.UI.Color.FromArgb(222, 21, 25, 33);
            var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
            gradient.GradientStops.Add(new GradientStop { Offset = 0, Color = cardStart });
            gradient.GradientStops.Add(new GradientStop { Offset = 1, Color = cardEnd });
            _card.Background = gradient;
            _card.BorderBrush = new SolidColorBrush(light ? Windows.UI.Color.FromArgb(42, 0, 0, 0) : Windows.UI.Color.FromArgb(58, 255, 255, 255));

            var main = new SolidColorBrush(light ? Windows.UI.Color.FromArgb(255, 26, 27, 30) : Windows.UI.Color.FromArgb(255, 245, 247, 250));
            var sub = new SolidColorBrush(light ? Windows.UI.Color.FromArgb(255, 86, 91, 100) : Windows.UI.Color.FromArgb(255, 165, 173, 188));
            _txtBalance.Foreground = main;
            _txtUsage.Foreground = main;
            _txtUsageTokens.Foreground = sub;
            _txtUpdated.Foreground = sub;
            _txtPeriod.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 184, 105));
            _periodChip.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 46, 184, 105));
            _chipLow.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 255, 176, 32));
            if (_chipLow.Child is TextBlock lowText) lowText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 105, 0));
            _btnRefresh.Background = new SolidColorBrush(light ? Windows.UI.Color.FromArgb(255, 245, 245, 245) : Windows.UI.Color.FromArgb(255, 52, 58, 70));
            _btnRefresh.Foreground = main;
            _btnLogin.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 127, 249));
            ThemeManager.Apply(_host);
            ThemeManager.ApplyWindow(_window);
        }

        public void SetCacheHitRate(CacheHitRate rate) {
            _lastCacheRate = rate;
            UpdateUsageSummaryText();
        }

        public void UpdateData(BalanceInfo balance, UsageInfo usage, DateTime updatedAt, CacheHitRate cacheRate) {
            balance = balance ?? new BalanceInfo();
            bool hasKey = balance.HasKey;
            bool loggedIn = hasKey && balance.Error.Length == 0;
            bool low = loggedIn && balance.Total < _config.LowBalanceThreshold;

            if (loggedIn) _txtBalance.Text = (balance.Currency == "CNY" ? "¥ " : balance.Currency + " ") + balance.Total.ToString("0.00", CultureInfo.InvariantCulture);
            else if (!hasKey) _txtBalance.Text = "未登录";
            else _txtBalance.Text = "登录已过期";
            _chipLow.Visibility = low ? Visibility.Visible : Visibility.Collapsed;
            _txtBalance.Foreground = new SolidColorBrush(low ? Windows.UI.Color.FromArgb(255, 255, 176, 32)
                : (ThemeManager.IsLight ? Windows.UI.Color.FromArgb(255, 26, 27, 30) : Windows.UI.Color.FromArgb(255, 245, 247, 250)));

            bool needLogin = !loggedIn;
            _txtUsage.Visibility = needLogin ? Visibility.Collapsed : Visibility.Visible;
            _txtUsageTokens.Visibility = needLogin ? Visibility.Collapsed : Visibility.Visible;
            _btnLogin.Visibility = needLogin ? Visibility.Visible : Visibility.Collapsed;
            if (needLogin) {
                _btnLogin.Content = hasKey ? "登录已过期，点此重新登录" : "登录 DeepSeek 账号";
                _txtUsage.Text = "";
                _txtUsageTokens.Text = "";
            } else if (usage == null || usage.ParseFailed || usage.Error.Length > 0) {
                _txtUsage.Text = usage != null && usage.Error.Length > 0 ? usage.Error : "用量暂不可用";
                _txtUsageTokens.Text = "";
            } else {
                _txtUsage.Text = "今日 " + FormatMoney("CNY", usage.CostToday);
                _txtUsageTokens.Text = FormatTokens(usage.TokensToday) + " · " + FormatCacheHitRate(cacheRate);
            }

            _lastUsage = usage;
            _lastCacheRate = cacheRate;
            _usageSummaryVisible = loggedIn && usage != null && !usage.ParseFailed && usage.Error.Length == 0;
            UpdateSparkline(usage);
            _txtUpdated.Text = "更新于 " + updatedAt.ToString("HH:mm");
        }

        void UpdateUsageSummaryText() {
            if (!_usageSummaryVisible || _lastUsage == null) return;
            _txtUsageTokens.Text = FormatTokens(_lastUsage.TokensToday) + " · " + FormatCacheHitRate(_lastCacheRate);
        }

        string FormatCacheHitRate(CacheHitRate rate) {
            int minutes = rate != null && rate.WindowMinutes > 0 ? rate.WindowMinutes : _config.CacheHitWindowMinutes;
            string prefix = minutes + "分";
            if (rate == null || !rate.HasValue) return prefix + "命中 --";
            if (!rate.HasActivity) return prefix + "无调用";
            return prefix + "命中 " + rate.Percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        void UpdateSparkline(UsageInfo usage) {
            List<decimal> values = Last7Days(usage);
            _spark.Children.Clear();
            bool show = values != null && values.Count > 0;
            _trendPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            decimal max = 0;
            foreach (decimal value in values) if (value > max) max = value;
            if (max <= 0) max = 1;
            for (int i = 0; i < values.Count; i++) {
                double height = Math.Max(3, (double)(values[i] / max) * 26.0);
                var bar = new Rectangle {
                    Width = 10,
                    Height = height,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = new SolidColorBrush(i == values.Count - 1
                        ? Windows.UI.Color.FromArgb(255, 70, 155, 255)
                        : (ThemeManager.IsLight ? Windows.UI.Color.FromArgb(72, 0, 0, 0) : Windows.UI.Color.FromArgb(82, 255, 255, 255)))
                };
                _spark.Children.Add(bar);
            }
        }

        static List<decimal> Last7Days(UsageInfo usage) {
            if (usage == null || usage.Daily.Count == 0) return null;
            DateTime today = DateTime.Now.Date;
            var sums = new decimal[7];
            foreach (DayStat day in usage.Daily) {
                int index = 6 - (today - day.Date.Date).Days;
                if (index >= 0 && index < 7) sums[index] += day.Cost;
            }
            return new List<decimal>(sums);
        }

        void UpdatePeriod() {
            PriceStatus status = PriceSchedule.GetStatus();
            bool peak = status.Period == PricePeriod.Peak;
            _txtPeriod.Text = PriceSchedule.PeriodName(status.Period);
            _txtPeriod.Foreground = new SolidColorBrush(peak ? Windows.UI.Color.FromArgb(255, 255, 176, 32) : Windows.UI.Color.FromArgb(255, 46, 184, 105));
            _periodChip.Background = new SolidColorBrush(peak ? Windows.UI.Color.FromArgb(42, 255, 176, 32) : Windows.UI.Color.FromArgb(42, 46, 184, 105));
            ToolTipService.SetToolTip(_periodChip, PriceSchedule.SwitchHint(status));
        }

        static string FormatMoney(string currency, decimal value) {
            return (currency == "CNY" ? "¥" : currency + " ") + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        static string FormatTokens(long value) {
            if (value >= 1000000000) return (value / 1000000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "B tokens";
            if (value >= 1000000) return (value / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M tokens";
            if (value >= 1000) return (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K tokens";
            return value.ToString(CultureInfo.InvariantCulture) + " tokens";
        }

        [DllImport("user32.dll")]
        static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}





