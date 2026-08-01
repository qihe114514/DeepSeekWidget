using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeepSeekWidget {

    public static class Log {
        static readonly object Sync = new object();

        static string Path {
            get {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "app.log");
            }
        }

        public static void Write(string msg) {
            try {
                lock (Sync) {
                    string dir = System.IO.Path.GetDirectoryName(Path);
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + "\r\n");
                }
            } catch {
            }
        }
    }

    public static class Program {
        [STAThread]
        public static void Main(string[] args) {
            Log.Write("Main 启动");
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DeepSeekWidget_9f3a1c2b", out createdNew)) {
                if (!createdNew) {
                    Log.Write("已有实例，退出");
                    MessageBox.Show("DeepSeek 桌面小组件已在运行。", "DeepSeek 小组件",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                    Log.Write("UnhandledException: " + ex.ExceptionObject);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
                Log.Write("创建 App");
                var app = new App(args);
                app.Run();
            }
            Log.Write("Main 结束");
        }

        static Assembly ResolveEmbedded(object sender, ResolveEventArgs args) {
            string name = new AssemblyName(args.Name).Name;
            string res = null;
            if (name.Equals("Microsoft.Web.WebView2.Core", StringComparison.OrdinalIgnoreCase)) {
                res = "DeepSeekWidget.Bin.WebView2.Core.dll";
            } else if (name.Equals("Microsoft.Web.WebView2.Wpf", StringComparison.OrdinalIgnoreCase)) {
                res = "DeepSeekWidget.Bin.WebView2.Wpf.dll";
            }
            if (res == null) return null;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(res)) {
                if (st == null) return null;
                byte[] buf = new byte[st.Length];
                int off = 0;
                while (off < buf.Length) {
                    int n = st.Read(buf, off, buf.Length - off);
                    if (n <= 0) break;
                    off += n;
                }
                return Assembly.Load(buf);
            }
        }
    }

    public class App : Application {
        public static App Instance;
        public Config Config;
        readonly string[] _args;
        WidgetWindow _widget;
        TrayIcon _tray;
        DispatcherTimer _refreshTimer;
        bool _refreshing;

        public App(string[] args) {
            _args = args ?? new string[0];
        }

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            Log.Write("OnStartup 开始");
            Instance = this;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) => {
                Log.Write("DispatcherUnhandledException: " + ex.Exception);
            };

            try {
                WebView2Host.CleanupBin();
            } catch {
            }

            Config = Config.Load();
            Log.Write("配置已加载");
            _widget = new WidgetWindow(Config);
            Log.Write("组件窗口已创建");
            _tray = new TrayIcon(Config.RefreshSeconds);
            Log.Write("托盘已创建");
            _tray.MoveRequested += () => _widget.ToggleMoveMode();
            _tray.ResetPositionRequested += () => _widget.ResetPosition();
            _tray.LoginRequested += OpenLogin;
            _tray.AutoStartChanged += v => AutoStart.SetEnabled(v);
            _tray.RefreshIntervalChanged += seconds => {
                Config.RefreshSeconds = seconds;
                Config.Save();
                _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
                _refreshTimer.Stop();
                _refreshTimer.Start();
                Log.Write("刷新频率改为 " + seconds + " 秒");
            };
            _tray.ExitRequested += Shutdown;

            int sec = Math.Max(30, Config.RefreshSeconds);
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
            _refreshTimer.Tick += (s, ev) => {
                var _ = RefreshAsync();
            };
            _refreshTimer.Start();

            _widget.Show();
            Log.Write("组件已 Show");

            foreach (string a in _args) {
                if (a == "--login") Dispatcher.BeginInvoke(new Action(OpenLogin));
                if (a == "--recharge") Dispatcher.BeginInvoke(new Action(OpenRecharge));
            }

            RefreshNow();
            Log.Write("OnStartup 结束");
        }

        public void RefreshNow() {
            _refreshTimer.Stop();
            _refreshTimer.Start();
            var _ = RefreshAsync();
        }

        public async System.Threading.Tasks.Task RefreshAsync() {
            if (_refreshing) return;
            _refreshing = true;
            try {
                _widget.ShowLoading();
                string token = Config.PlatformTokenPlain;
                string cookie = Config.CookieHeaderPlain;
                var bal = await ApiClient.FetchPlatformBalanceAsync(token, cookie);
                var usage = await ApiClient.FetchUsageAsync(token, cookie);
                string balState = bal == null ? "null"
                    : (bal.Error.Length > 0 ? "失败(" + bal.Error + ")"
                       : "成功 ¥" + bal.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                string usageState = usage == null ? "null"
                    : (usage.SessionExpired ? "登录过期"
                       : ((usage.Error.Length > 0 || usage.ParseFailed) ? "失败(" + usage.Error + ")"
                          : "成功"));
                Log.Write("刷新完成: 余额=" + balState
                    + " 用量=" + usageState
                    + " 用量解析失败=" + (usage == null ? "null" : usage.ParseFailed.ToString())
                    + " 登录过期=" + (usage == null ? "null" : usage.SessionExpired.ToString())
                    + " tokens=" + (usage == null ? "null" : usage.TokensToday.ToString()));
                _widget.UpdateData(bal, usage, DateTime.Now);
            } catch (Exception ex) {
                Log.Write("刷新异常: " + ex);
                var bal = new BalanceInfo {
                    HasKey = !string.IsNullOrEmpty(Config.PlatformTokenPlain),
                    Error = ex.Message
                };
                _widget.UpdateData(bal, null, DateTime.Now);
            } finally {
                _refreshing = false;
            }
        }

        public void OpenLogin() {
            new LoginWindow().Show();
        }

        public void OpenRecharge() {
            new RechargeWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e) {
            if (_tray != null) _tray.Dispose();
            base.OnExit(e);
        }
    }

    static class AutoStart {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "DeepSeekWidget";

        public static bool IsEnabled() {
            try {
                using (var k = Registry.CurrentUser.OpenSubKey(Key)) {
                    return k != null && k.GetValue(Name) != null;
                }
            } catch {
                return false;
            }
        }

        public static void SetEnabled(bool on) {
            try {
                using (var k = Registry.CurrentUser.CreateSubKey(Key)) {
                    if (k == null) return;
                    if (on) {
                        k.SetValue(Name, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    } else {
                        k.DeleteValue(Name, false);
                    }
                }
            } catch {
            }
        }
    }
}
