using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeepSeekWidget
{
    public sealed class WidgetWindow
    {
        const double BaseWidth = 318;
        const double BaseHeight = 235;

        readonly Config _config;
        readonly Window _window;
        readonly Grid _host;
        readonly Border _card;
        readonly List<FrameworkElement> _interactive = new List<FrameworkElement>();
        readonly TextBlock _txtPeriod = Txt(10, FontWeights.SemiBold);
        readonly Border _periodChip;
        readonly Border _chipLow;
        readonly TextBlock _txtBalance = Txt(32, FontWeights.SemiBold);
        readonly TextBlock _txtUsage = Txt(12, FontWeights.Normal);
        readonly TextBlock _txtUsageTokens = Txt(11, FontWeights.Normal);
        readonly TextBlock _txtUpdated = Txt(10, FontWeights.Normal);
        readonly StackPanel _spark = new StackPanel { Orientation = Orientation.Horizontal, Height = 28, VerticalAlignment = VerticalAlignment.Bottom };
        readonly StackPanel _trendPanel = new StackPanel { Visibility = Visibility.Collapsed };
        readonly Button _btnRefresh;
        readonly Button _btnLogin;
        readonly Button _btnRecharge;

        DispatcherTimer _clickTimer, _bottomTimer, _periodTimer, _moveTimer;
        IntPtr _hwnd;
        bool _positioned, _moveMode, _pinTop, _clickThrough, _dragging;
        Point _dragCursor;
        Point _dragWindow;
        UsageInfo _lastUsage;
        CacheHitRate _lastCacheRate;
        bool _usageSummaryVisible;

        public WidgetWindow(Config config)
        {
            _config = config;
            _window = new Window
            {
                Title = "DeepSeekWidget",
                Width = BaseWidth,
                Height = BaseHeight,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            _btnRecharge = CreateButton("充值", true, 30);
            _btnRecharge.Click += (s, e) => App.Instance.OpenRecharge();
            _btnRefresh = CreateButton("刷新", false, 26);
            _btnRefresh.Click += (s, e) => _ = App.Instance.RefreshNowAsync();
            _btnLogin = CreateButton("登录 DeepSeek 账号", false, 26);
            _btnLogin.HorizontalAlignment = HorizontalAlignment.Left;
            _btnLogin.Visibility = Visibility.Collapsed;
            _btnLogin.Click += (s, e) => App.Instance.OpenLogin();

            _periodChip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _txtPeriod
            };
            _chipLow = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(7, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "余额偏低", FontSize = 10, FontWeight = FontWeights.SemiBold }
            };

            _card = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(7),
                Child = BuildContent()
            };
            _host = new Grid
            {
                Width = BaseWidth,
                Height = BaseHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent
            };
            _host.Children.Add(_card);
            _window.Content = _host;
            _window.SourceInitialized += (s, e) => EnsureInitialized();
            _window.Closed += (s, e) => { StopTimers(); ThemeManager.Changed -= ApplyTheme; };
            ThemeManager.Changed += ApplyTheme;
            ApplyTheme();
        }

        static TextBlock Txt(double size, FontWeight weight) => new TextBlock { FontSize = size, FontWeight = weight };

        Grid BuildContent()
        {
            var grid = new Grid { Margin = new Thickness(14, 12, 13, 10) };
            for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = Txt(13, FontWeights.SemiBold);
            brand.Text = "DeepSeek 余额";
            brand.VerticalAlignment = VerticalAlignment.Center;
            var brandRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brandRow.Children.Add(brand);
            brandRow.Children.Add(_periodChip);
            Grid.SetRow(brandRow, 0);
            grid.Children.Add(brandRow);

            Grid.SetRow(_btnRecharge, 0); Grid.SetColumn(_btnRecharge, 1); grid.Children.Add(_btnRecharge);

            var balanceStack = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            balanceStack.Children.Add(new TextBlock { Text = "可用余额", FontSize = 11, Opacity = 0.62, FontWeight = FontWeights.SemiBold });
            var balanceRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, 0) };
            balanceRow.Children.Add(_txtBalance);
            balanceRow.Children.Add(_chipLow);
            balanceStack.Children.Add(balanceRow);
            Grid.SetRow(balanceStack, 1); grid.Children.Add(balanceStack);

            var usageStack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            usageStack.Children.Add(_txtUsage);
            usageStack.Children.Add(_txtUsageTokens);
            usageStack.Children.Add(_btnLogin);
            Grid.SetRow(usageStack, 2); grid.Children.Add(usageStack);

            _trendPanel.Children.Add(new TextBlock { Text = "近 7 日", FontSize = 10, Opacity = 0.48, HorizontalAlignment = HorizontalAlignment.Right });
            _trendPanel.Children.Add(_spark);
            _trendPanel.HorizontalAlignment = HorizontalAlignment.Right;
            _trendPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_trendPanel, 1); Grid.SetRowSpan(_trendPanel, 3); Grid.SetColumn(_trendPanel, 1); grid.Children.Add(_trendPanel);

            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtUpdated.VerticalAlignment = VerticalAlignment.Center;
            _txtUpdated.Opacity = 0.55;
            footer.Children.Add(_txtUpdated);
            Grid.SetColumn(_btnRefresh, 1); footer.Children.Add(_btnRefresh);
            Grid.SetRow(footer, 4); Grid.SetColumnSpan(footer, 2); grid.Children.Add(footer);

            _interactive.Add(_btnRecharge);
            _interactive.Add(_btnRefresh);
            _interactive.Add(_btnLogin);
            return grid;
        }

        static Button CreateButton(string text, bool accent, double height)
        {
            var button = new Button
            {
                Content = text,
                Height = height,
                MinWidth = 0,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            if (accent)
            {
                button.Background = Brush(15, 108, 189);
                button.Foreground = Brushes.White;
            }
            return button;
        }

        public void Show() => _window.Show();
        public void Close() { try { _window.Close(); } catch { } }

        void EnsureInitialized()
        {
            if (_positioned) return;
            _positioned = true;
            _hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            int style = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt32();
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(style | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
            PositionFromConfig();
            StartTimers();
            SetPinMode(_config.PinMode == "top");
        }

        void StartTimers()
        {
            _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _clickTimer.Tick += (s, e) => UpdateClickThrough();
            _clickTimer.Start();

            _periodTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _periodTimer.Tick += (s, e) => UpdatePeriod();
            _periodTimer.Start();
            UpdatePeriod();

            _bottomTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _bottomTimer.Tick += (s, e) => KeepBottom();
            _bottomTimer.Start();
        }

        void StopTimers()
        {
            _clickTimer?.Stop(); _periodTimer?.Stop(); _bottomTimer?.Stop(); _moveTimer?.Stop();
        }

        void KeepBottom()
        {
            if (_pinTop || _hwnd == IntPtr.Zero) return;
            Win32.SetWindowPos(_hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        public void SetOpacity(int percent) => _host.Opacity = Math.Max(0.2, Math.Min(1.0, percent / 100.0));

        public void SetScale(int percent)
        {
            double scale = Math.Max(0.6, Math.Min(2.5, percent / 100.0));
            _host.LayoutTransform = new ScaleTransform(scale, scale);
            _window.Width = BaseWidth * scale;
            _window.Height = BaseHeight * scale;
        }

        public void SetPinMode(bool top)
        {
            _pinTop = top;
            if (_hwnd != IntPtr.Zero)
                Win32.SetWindowPos(_hwnd, top ? Win32.HWND_TOPMOST : Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        public void ToggleMoveMode()
        {
            _moveMode = !_moveMode;
            _card.BorderBrush = _moveMode ? Brush(45, 127, 249) : (ThemeManager.IsLight ? Brush(42, 0, 0, 0) : Brush(58, 255, 255, 255));
            if (_moveMode)
            {
                _host.MouseLeftButtonDown += BeginDrag;
                if (_moveTimer == null)
                {
                    _moveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                    _moveTimer.Tick += (s, e) => ToggleMoveMode();
                }
                _moveTimer.Stop(); _moveTimer.Start();
            }
            else
            {
                _host.MouseLeftButtonDown -= BeginDrag;
                _moveTimer?.Stop();
                SavePosition();
            }
            UpdateClickThrough();
        }

        public void ResetPosition() { PositionFromConfig(true); SavePosition(); }

        void BeginDrag(object sender, MouseButtonEventArgs e)
        {
            if (!_moveMode) return;
            Win32.GetCursorPos(out var cursor);
            _dragCursor = new Point(cursor.X, cursor.Y);
            _dragWindow = new Point(_window.Left, _window.Top);
            _dragging = true;
            _host.CaptureMouse();
            _host.MouseMove += ContinueDrag;
            _host.MouseLeftButtonUp += EndDrag;
            e.Handled = true;
        }

        void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!_moveMode || !_dragging) return;
            Win32.GetCursorPos(out var cursor);
            double dpi = VisualTreeHelper.GetDpi(_host).DpiScaleX;
            _window.Left = _dragWindow.X + (cursor.X - _dragCursor.X) / dpi;
            _window.Top = _dragWindow.Y + (cursor.Y - _dragCursor.Y) / dpi;
        }

        void EndDrag(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _host.ReleaseMouseCapture();
            _host.MouseMove -= ContinueDrag;
            _host.MouseLeftButtonUp -= EndDrag;
            SavePosition();
        }

        void UpdateClickThrough()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool transparent = true;
            if (_moveMode) transparent = false;
            else if (Win32.GetCursorPos(out var cursor))
            {
                var local = new Win32.POINT { X = cursor.X, Y = cursor.Y };
                Win32.ScreenToClient(_hwnd, ref local);
                double dpi = VisualTreeHelper.GetDpi(_host).DpiScaleX;
                transparent = HitInteractive(new Point(local.X / dpi, local.Y / dpi)) == null;
            }
            if (transparent == _clickThrough) return;
            _clickThrough = transparent;
            int style = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt32();
            if (transparent) style |= Win32.WS_EX_TRANSPARENT; else style &= ~Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
        }

        FrameworkElement HitInteractive(Point point)
        {
            foreach (var element in _interactive)
            {
                if (element == null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                try
                {
                    var origin = element.TransformToAncestor(_host).Transform(new Point(0, 0));
                    if (new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)).Contains(point)) return element;
                }
                catch { }
            }
            return null;
        }

        void PositionFromConfig(bool reset = false)
        {
            if (!reset && _config.WindowX.HasValue && _config.WindowY.HasValue)
            {
                _window.Left = _config.WindowX.Value;
                _window.Top = _config.WindowY.Value;
                return;
            }
            var area = SystemParameters.WorkArea;
            _window.Left = area.Right - _window.Width - 24;
            _window.Top = area.Top + 24;
        }

        void SavePosition()
        {
            _config.WindowX = _window.Left;
            _config.WindowY = _window.Top;
            _config.Save();
        }

        public void ApplyTheme()
        {
            bool light = ThemeManager.IsLight;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradient.GradientStops.Add(new GradientStop(light ? Color.FromArgb(238, 255, 255, 255) : Color.FromArgb(220, 38, 44, 56), 0));
            gradient.GradientStops.Add(new GradientStop(light ? Color.FromArgb(232, 242, 242, 242) : Color.FromArgb(232, 21, 25, 33), 1));
            _card.Background = gradient;
            _card.BorderBrush = light ? Brush(42, 0, 0, 0) : Brush(58, 255, 255, 255);
            var main = light ? Brush(26, 27, 30) : Brush(245, 247, 250);
            var sub = light ? Brush(86, 91, 100) : Brush(165, 173, 188);
            _txtBalance.Foreground = main;
            _txtUsage.Foreground = main;
            _txtUsageTokens.Foreground = sub;
            _txtUpdated.Foreground = sub;
            _periodChip.Background = Brush(42, 46, 184, 105);
            _chipLow.Background = Brush(38, 255, 176, 32);
            if (_chipLow.Child is TextBlock low) low.Foreground = Brush(200, 105, 0);
            _btnRefresh.Background = light ? Brush(245, 245, 245) : Brush(52, 58, 70);
            _btnRefresh.Foreground = main;
            _btnLogin.Foreground = Brush(45, 127, 249);
        }

        public void SetCacheHitRate(CacheHitRate rate) { _lastCacheRate = rate; UpdateUsageSummaryText(); }

        public void UpdateData(BalanceInfo balance, UsageInfo usage, DateTime updatedAt, CacheHitRate cacheRate)
        {
            balance = balance ?? new BalanceInfo();
            bool hasKey = balance.HasKey;
            bool loggedIn = hasKey && balance.Error.Length == 0;
            bool low = loggedIn && balance.Total < _config.LowBalanceThreshold;
            _txtBalance.Text = loggedIn ? ((balance.Currency == "CNY" ? "¥ " : balance.Currency + " ") + balance.Total.ToString("0.00", CultureInfo.InvariantCulture)) : (hasKey ? "登录已过期" : "未登录");
            _chipLow.Visibility = low ? Visibility.Visible : Visibility.Collapsed;
            _txtBalance.Foreground = low ? Brush(255, 176, 32) : (ThemeManager.IsLight ? Brush(26, 27, 30) : Brush(245, 247, 250));

            bool needLogin = !loggedIn;
            _txtUsage.Visibility = needLogin ? Visibility.Collapsed : Visibility.Visible;
            _txtUsageTokens.Visibility = needLogin ? Visibility.Collapsed : Visibility.Visible;
            _btnLogin.Visibility = needLogin ? Visibility.Visible : Visibility.Collapsed;
            if (needLogin)
            {
                _btnLogin.Content = hasKey ? "登录已过期，点此重新登录" : "登录 DeepSeek 账号";
                _txtUsage.Text = ""; _txtUsageTokens.Text = "";
            }
            else if (usage == null || usage.ParseFailed || usage.Error.Length > 0)
            {
                _txtUsage.Text = usage != null && usage.Error.Length > 0 ? usage.Error : "用量暂不可用";
                _txtUsageTokens.Text = "";
            }
            else
            {
                _txtUsage.Text = "今日 " + FormatMoney("CNY", usage.CostToday);
                _txtUsageTokens.Text = FormatTokens(usage.TokensToday) + " · " + FormatCacheHitRate(cacheRate);
            }
            _lastUsage = usage; _lastCacheRate = cacheRate;
            _usageSummaryVisible = loggedIn && usage != null && !usage.ParseFailed && usage.Error.Length == 0;
            UpdateSparkline(usage);
            _txtUpdated.Text = "更新于 " + updatedAt.ToString("HH:mm");
        }

        void UpdateUsageSummaryText()
        {
            if (!_usageSummaryVisible || _lastUsage == null) return;
            _txtUsageTokens.Text = FormatTokens(_lastUsage.TokensToday) + " · " + FormatCacheHitRate(_lastCacheRate);
        }

        string FormatCacheHitRate(CacheHitRate rate)
        {
            int minutes = rate != null && rate.WindowMinutes > 0 ? rate.WindowMinutes : _config.CacheHitWindowMinutes;
            string prefix = minutes + "分";
            if (rate == null || !rate.HasValue) return prefix + "命中 --";
            if (!rate.HasActivity) return prefix + "无调用";
            return prefix + "命中 " + rate.Percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        void UpdateSparkline(UsageInfo usage)
        {
            List<decimal> values = Last7Days(usage);
            _spark.Children.Clear();
            bool show = values != null && values.Count > 0;
            _trendPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            decimal max = 0;
            foreach (decimal value in values) if (value > max) max = value;
            if (max <= 0) max = 1;
            for (int i = 0; i < values.Count; i++)
            {
                var bar = new Rectangle
                {
                    Width = 10,
                    Height = Math.Max(3, (double)(values[i] / max) * 26.0),
                    RadiusX = 3,
                    RadiusY = 3,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = i == values.Count - 1 ? Brush(70, 155, 255) : (ThemeManager.IsLight ? Brush(72, 0, 0, 0) : Brush(82, 255, 255, 255))
                };
                _spark.Children.Add(bar);
            }
        }

        static List<decimal> Last7Days(UsageInfo usage)
        {
            if (usage == null || usage.Daily.Count == 0) return null;
            DateTime today = DateTime.Now.Date;
            var sums = new decimal[7];
            foreach (DayStat day in usage.Daily)
            {
                int index = 6 - (today - day.Date.Date).Days;
                if (index >= 0 && index < 7) sums[index] += day.Cost;
            }
            return new List<decimal>(sums);
        }

        void UpdatePeriod()
        {
            PriceStatus status = PriceSchedule.GetStatus();
            bool peak = status.Period == PricePeriod.Peak;
            _txtPeriod.Text = PriceSchedule.PeriodName(status.Period);
            _txtPeriod.Foreground = peak ? Brush(255, 176, 32) : Brush(46, 184, 105);
            _periodChip.Background = peak ? Brush(42, 255, 176, 32) : Brush(42, 46, 184, 105);
            _periodChip.ToolTip = PriceSchedule.SwitchHint(status);
        }

        static string FormatMoney(string currency, decimal value) => (currency == "CNY" ? "¥" : currency + " ") + value.ToString("0.00", CultureInfo.InvariantCulture);
        static string FormatTokens(long value)
        {
            if (value >= 1000000000) return (value / 1000000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "B tokens";
            if (value >= 1000000) return (value / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M tokens";
            if (value >= 1000) return (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K tokens";
            return value.ToString(CultureInfo.InvariantCulture) + " tokens";
        }

        static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
        static SolidColorBrush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}
