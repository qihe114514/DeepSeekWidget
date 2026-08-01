using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DeepSeekWidget {

    public class WidgetWindow : Window {
        readonly Config _config;
        readonly Button _btnRecharge;
        readonly TextBlock _txtBalance;
        readonly TextBlock _txtBalanceDetail;
        readonly TextBlock _txtUsage;
        readonly TextBlock _txtUpdated;
        readonly Border _card;
        DispatcherTimer _bottomTimer;
        HwndSource _source;
        bool _moveMode;

        static readonly Brush BorderNormal = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        static readonly Brush BorderMove = new SolidColorBrush(Color.FromArgb(255, 45, 127, 249));
        static readonly Brush TextMain = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        static readonly Brush TextSub = new SolidColorBrush(Color.FromRgb(143, 152, 168));
        static readonly Brush TextDim = new SolidColorBrush(Color.FromRgb(106, 115, 131));
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(45, 127, 249));

        public WidgetWindow(Config config) {
            _config = config;
            Width = 312;
            Height = 192;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            Title = "DeepSeekWidget";
            FontFamily = new FontFamily("Microsoft YaHei UI");

            _card = new Border {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x1E, 0x22, 0x2A)),
                BorderBrush = BorderNormal,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(21)
            };
            _card.Effect = new DropShadowEffect {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 2,
                Opacity = 0.45
            };

            var grid = new Grid { Margin = new Thickness(14, 10, 12, 8) };
            for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var title = new TextBlock {
                Text = "DeepSeek API",
                Foreground = TextMain,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(title, 0);
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);

            _btnRecharge = new Button {
                Content = "充值",
                Width = 52,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Accent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            _btnRecharge.Template = RoundButtonTemplate();
            _btnRecharge.MouseEnter += (s, e) => _btnRecharge.Background = new SolidColorBrush(Color.FromRgb(70, 145, 255));
            _btnRecharge.MouseLeave += (s, e) => _btnRecharge.Background = Accent;
            _btnRecharge.Click += (s, e) => App.Instance.OpenRecharge();
            Grid.SetRow(_btnRecharge, 0);
            Grid.SetColumn(_btnRecharge, 1);
            grid.Children.Add(_btnRecharge);

            _txtBalance = new TextBlock {
                Text = "加载中…",
                Foreground = TextMain,
                FontSize = 23,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(_txtBalance, 1);
            Grid.SetColumnSpan(_txtBalance, 2);
            grid.Children.Add(_txtBalance);

            _txtBalanceDetail = new TextBlock {
                Foreground = TextSub,
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(_txtBalanceDetail, 2);
            Grid.SetColumnSpan(_txtBalanceDetail, 2);
            grid.Children.Add(_txtBalanceDetail);

            _txtUsage = new TextBlock {
                Foreground = TextSub,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(_txtUsage, 3);
            Grid.SetColumnSpan(_txtUsage, 2);
            grid.Children.Add(_txtUsage);

            _txtUpdated = new TextBlock {
                Foreground = TextDim,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetRow(_txtUpdated, 4);
            Grid.SetColumnSpan(_txtUpdated, 2);
            grid.Children.Add(_txtUpdated);

            _card.Child = grid;
            Content = _card;

            SourceInitialized += (s, e) => {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
                _source = HwndSource.FromHwnd(hwnd);
                if (_source != null) _source.AddHook(WndProc);
            };

            Loaded += (s, e) => {
                PositionFromConfig();
                _bottomTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _bottomTimer.Tick += (t1, t2) => KeepBottomTick();
                _bottomTimer.Start();
            };

            Closed += (s, e) => {
                if (_bottomTimer != null) _bottomTimer.Stop();
                if (_source != null) _source.RemoveHook(WndProc);
            };
        }

        static ControlTemplate RoundButtonTemplate() {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        Rect ButtonRect {
            get {
                return _btnRecharge.TransformToAncestor(this).TransformBounds(
                    new Rect(0, 0, _btnRecharge.ActualWidth, _btnRecharge.ActualHeight));
            }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            if (msg == Win32.WM_NCHITTEST) {
                int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var pt = PointFromScreen(new Point(x, y));
                if (ButtonRect.Contains(pt)) {
                    handled = true;
                    return new IntPtr(Win32.HTCLIENT);
                }
                if (_moveMode) {
                    handled = true;
                    return new IntPtr(Win32.HTCAPTION);
                }
                handled = true;
                return new IntPtr(Win32.HTTRANSPARENT);
            }
            if (msg == Win32.WM_MOUSEACTIVATE) {
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);
            }
            if (msg == Win32.WM_EXITSIZEMOVE) {
                if (_moveMode) EndMoveMode(true);
            }
            return IntPtr.Zero;
        }

        void KeepBottomTick() {
            if (_moveMode || _source == null) return;
            ClampIntoView();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetWindowPos(hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        void PositionFromConfig() {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            double x = _config.WindowX.HasValue ? _config.WindowX.Value : (vl + vw - Width - 20);
            double y = _config.WindowY.HasValue ? _config.WindowY.Value : (vt + 24);
            x = Math.Max(vl, Math.Min(x, vl + vw - Width));
            y = Math.Max(vt, Math.Min(y, vt + vh - Height));
            Left = x;
            Top = y;
        }

        public void ResetPosition() {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            Left = vl + vw - Width - 20;
            Top = vt + 24;
            _config.WindowX = Left;
            _config.WindowY = Top;
            _config.Save();
            Log.Write("重置位置到 " + Left + "," + Top);
        }

        void ClampIntoView() {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;
            double x = Left;
            double y = Top;
            if (x < vl) x = vl;
            if (x + Width > vl + vw) x = Math.Max(vl, vl + vw - Width);
            if (y < vt) y = vt;
            if (y + Height > vt + vh) y = Math.Max(vt, vt + vh - Height);
            if (Math.Abs(x - Left) > 0.5 || Math.Abs(y - Top) > 0.5) {
                Left = x;
                Top = y;
                _config.WindowX = x;
                _config.WindowY = y;
                _config.Save();
            }
        }

        public void ToggleMoveMode() {
            SetMoveMode(!_moveMode);
        }

        void SetMoveMode(bool on) {
            if (_moveMode == on) return;
            _moveMode = on;
            if (on) {
                Cursor = Cursors.SizeAll;
                _card.BorderBrush = BorderMove;
                _card.BorderThickness = new Thickness(1.5);
            } else {
                Cursor = Cursors.Arrow;
                _card.BorderBrush = BorderNormal;
                _card.BorderThickness = new Thickness(1);
            }
        }

        void EndMoveMode(bool save) {
            SetMoveMode(false);
            if (save) {
                _config.WindowX = Left;
                _config.WindowY = Top;
                _config.Save();
            }
        }

        public void ShowLoading() {
            _txtBalance.Text = "加载中…";
            _txtUsage.Text = "";
            _txtUpdated.Text = "";
        }

        public void UpdateData(BalanceInfo bal, UsageInfo usage, DateTime updated) {
            if (bal == null) bal = new BalanceInfo();
            if (bal.HasKey && string.IsNullOrEmpty(bal.Error)) {
                _txtBalance.Text = Money(bal.Currency, bal.Total);
                string detail = "充值 " + Money(bal.Currency, bal.ToppedUp) + " · 赠送 " + Money(bal.Currency, bal.Granted);
                if (!bal.IsAvailable) detail += "（余额不可用）";
                _txtBalanceDetail.Text = detail;
            } else if (!bal.HasKey) {
                _txtBalance.Text = "未配置 API Key";
                _txtBalanceDetail.Text = "托盘右键 → 登录 DeepSeek 账号";
            } else {
                _txtBalance.Text = "余额获取失败";
                _txtBalanceDetail.Text = bal.Error;
            }

            if (usage == null) {
                _txtUsage.Text = "";
            } else if (!usage.HasSession) {
                _txtUsage.Text = "今日用量：登录后显示";
            } else if (usage.SessionExpired) {
                _txtUsage.Text = "今日用量：登录已过期，请重新登录（托盘 → 登录 DeepSeek 账号）";
            } else if (usage.ParseFailed || usage.Error.Length > 0) {
                // 展示具体失败原因（如超时/Key 无效/频率限制），不再笼统显示"获取失败"
                _txtUsage.Text = string.IsNullOrEmpty(usage.Error)
                    ? "今日用量：获取失败（详见日志）"
                    : "今日用量：" + usage.Error;
            } else {
                _txtUsage.Text = "今日用量  " + Money("CNY", usage.CostToday) + "  ·  " + FormatTokens(usage.TokensToday);
            }
            _txtUpdated.Text = "更新于 " + updated.ToString("HH:mm");
        }

        static string Money(string currency, decimal v) {
            return (currency == "CNY" ? "¥" : currency + " ") + v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        static string FormatTokens(long n) {
            if (n >= 10000) return (n / 10000.0).ToString("0.0") + " 万 tokens";
            return n.ToString("N0", CultureInfo.InvariantCulture) + " tokens";
        }
    }
}
