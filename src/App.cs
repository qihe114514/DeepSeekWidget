using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace DeepSeekWidget {

    public static class Log {
        static readonly object Sync = new object();
        static string Path {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekWidget", "app.log"); }
        }

        public static void Write(string message) {
            try {
                lock (Sync) {
                    string dir = System.IO.Path.GetDirectoryName(Path);
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    try {
                        var info = new System.IO.FileInfo(Path);
                        if (info.Exists && info.Length > 512 * 1024) {
                            string old = Path + ".1";
                            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                            System.IO.File.Move(Path, old);
                        }
                    } catch { }
                    System.IO.File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + "\r\n");
                }
            } catch { }
        }
    }

    public static class Program {
        [STAThread]
        public static void Main(string[] args) {
            Log.Write("Main 启动");
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DeepSeekWidget_9f3a1c2b", out createdNew)) {
                if (!createdNew) {
                    MessageBox(IntPtr.Zero, "DeepSeek 桌面小组件已在运行。", "DeepSeek 小组件", 0x40);
                    return;
                }
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("UnhandledException: " + e.ExceptionObject);
                var app = new App(args ?? new string[0]);
                app.Run();
            }
            Log.Write("Main 结束");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }

    public class App : System.Windows.Application {
        public static App Instance;
        public Config Config;
        readonly string[] _args;
        WidgetWindow _widget;
        TrayIcon _tray;
        DispatcherQueueTimer _refreshTimer;
        bool _refreshing;
        readonly CacheHitTracker _cacheHitTracker = new CacheHitTracker();

        public App(string[] args) {
            _args = args ?? new string[0];
        }

        protected override void OnStartup(System.Windows.StartupEventArgs e) {
            base.OnStartup(e);
            Instance = this;
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) => Log.Write("DispatcherUnhandledException: " + ex.Exception);
            Log.Write("OnStartup 开始");

            WinUI3.Initialize();
            Config = Config.Load();
            ThemeManager.Initialize(ThemePreferenceValues.Parse(Config.ThemePreference));
            ThemeManager.Changed += OnThemeChanged;

            _widget = new WidgetWindow(Config);
            _tray = new TrayIcon(Config.RefreshSeconds, Config.PinMode == "top", Config.CacheHitWindowMinutes, ThemeManager.Preference);
            _tray.MoveRequested += () => _widget.ToggleMoveMode();
            _tray.ResetPositionRequested += () => _widget.ResetPosition();
            _tray.LoginRequested += OpenLogin;
            _tray.AutoStartChanged += value => AutoStart.SetEnabled(value);
            _tray.RefreshIntervalChanged += seconds => {
                Config.RefreshSeconds = seconds;
                Config.Save();
                SetRefreshInterval(seconds);
            };
            _tray.CacheHitWindowChanged += minutes => {
                Config.CacheHitWindowMinutes = minutes;
                Config.Save();
                _widget.SetCacheHitRate(_cacheHitTracker.GetRate(minutes, DateTime.Now));
            };
            _tray.PinModeChanged += top => {
                Config.PinMode = top ? "top" : "bottom";
                Config.Save();
                _widget.SetPinMode(top);
            };
            _tray.ThemeChanged += preference => {
                Config.ThemePreference = ThemePreferenceValues.ToConfig(preference);
                Config.Save();
                ThemeManager.SetPreference(preference);
            };
            _tray.AppearanceRequested += OpenAppearance;
            _tray.AboutRequested += OpenAbout;
            _tray.ExitRequested += Shutdown;
            _tray.RefreshRequested += RefreshNow;

            _widget.SetPinMode(Config.PinMode == "top");
            _widget.Show();
            _widget.ApplyTheme();
            SetRefreshInterval(Math.Max(30, Config.RefreshSeconds));
            RefreshNow();
            HandleArguments();
            Log.Write("OnStartup 完成");
        }

        void OnThemeChanged() {
            try {
                _tray.SetTheme(ThemeManager.Preference);
                _widget.ApplyTheme();
            } catch (Exception ex) {
                Log.Write("主题刷新失败: " + ex.Message);
            }
        }

        void SetRefreshInterval(int seconds) {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue == null) return;
            if (_refreshTimer == null) {
                _refreshTimer = queue.CreateTimer();
                _refreshTimer.Tick += (s, e) => RefreshNow();
            }
            _refreshTimer.Stop();
            _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(30, seconds));
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Start();
        }

        public async void RefreshNow() {
            if (_refreshing || Config == null || _widget == null) return;
            _refreshing = true;
            DateTime updatedAt = DateTime.Now;
            try {
                string token = Config.PlatformTokenPlain;
                string cookie = Config.CookieHeaderPlain;
                BalanceInfo balance = await ApiClient.FetchPlatformBalanceAsync(token, cookie);
                UsageInfo usage = await ApiClient.FetchUsageAsync(token, cookie);
                if (usage != null && usage.HasTokenBreakdown) {
                    _cacheHitTracker.Record(updatedAt, usage.TokensToday, usage.CacheHitTokensToday);
                }
                CacheHitRate cacheRate = _cacheHitTracker.GetRate(Config.CacheHitWindowMinutes, updatedAt);
                Log.Write("刷新完成: 余额=" + (balance != null && balance.HasKey
                    ? (balance.Error.Length > 0 ? balance.Error : "¥" + balance.Total.ToString("0.00"))
                    : "未登录"));
                NotifyLowBalance(balance);
                _widget.UpdateData(balance, usage, updatedAt, cacheRate);
            } catch (Exception ex) {
                Log.Write("刷新异常: " + ex);
                _widget.UpdateData(new BalanceInfo {
                    HasKey = !string.IsNullOrEmpty(Config.PlatformTokenPlain),
                    Error = ex.Message
                }, null, updatedAt, null);
            } finally {
                _refreshing = false;
            }
        }

        void NotifyLowBalance(BalanceInfo balance) {
            if (balance == null || !balance.HasKey || balance.Error.Length > 0) return;
            if (balance.Total < Config.LowBalanceThreshold) {
                if (!Config.LowWarned) {
                    Config.LowWarned = true;
                    Config.Save();
                    _tray.ShowBalloon("DeepSeek 余额偏低", "当前余额 ¥" + balance.Total.ToString("0.00") + "，低于阈值 ¥" + Config.LowBalanceThreshold.ToString("0.##") + "。");
                }
            } else if (Config.LowWarned) {
                Config.LowWarned = false;
                Config.Save();
            }
        }

        public void OpenLogin() { new LoginWindow().Show(); }
        public void OpenRecharge() { new RechargeWindow().Show(); }
        public void OpenAbout() { WinUI3Dialogs.ShowAbout(); }
        public void OpenAppearance() {
            WinUI3Dialogs.ShowAppearance(Config.Opacity, Config.Scale,
                value => { Config.Opacity = value; Config.Save(); _widget.SetOpacity(value); },
                value => { Config.Scale = value; Config.Save(); _widget.SetScale(value); });
        }

        public new void Shutdown() {
            try { if (_refreshTimer != null) _refreshTimer.Stop(); } catch { }
            try { if (_tray != null) _tray.Dispose(); } catch { }
            try { if (_widget != null) _widget.Close(); } catch { }
            Shutdown();
        }

        void HandleArguments() {
            foreach (string arg in _args) {
                if (string.Equals(arg, "--login", StringComparison.OrdinalIgnoreCase)) OpenLogin();
                else if (string.Equals(arg, "--recharge", StringComparison.OrdinalIgnoreCase)) OpenRecharge();
                else if (string.Equals(arg, "--about", StringComparison.OrdinalIgnoreCase)) OpenAbout();
                else if (string.Equals(arg, "--appearance", StringComparison.OrdinalIgnoreCase)) OpenAppearance();
                else if (string.Equals(arg, "--menu", StringComparison.OrdinalIgnoreCase)) {
                    Win32.POINT point;
                    if (Win32.GetCursorPos(out point)) _tray.ShowMenuAt(point.X, point.Y);
                }
            }
        }
    }

    static class AutoStart {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "DeepSeekWidget";
        public static bool IsEnabled() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue(Name) != null;
            } catch { return false; }
        }
        public static void SetEnabled(bool enabled) {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey(Key)) {
                    if (key == null) return;
                    if (enabled) key.SetValue(Name, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    else key.DeleteValue(Name, false);
                }
            } catch { }
        }
    }
}

