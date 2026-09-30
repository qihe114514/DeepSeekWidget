using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeepSeekWidget
{
    public static class Log
    {
        static readonly object Sync = new object();
        static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekWidget", "app.log");

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    string dir = System.IO.Path.GetDirectoryName(Path);
                    Directory.CreateDirectory(dir);
                    var info = new FileInfo(Path);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        string old = Path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Path, old);
                    }
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + "\r\n");
                }
            }
            catch { }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DeepSeekWidget_Lite_6c2b8f4e", out createdNew))
            {
                if (!createdNew) return;
                var app = new App(args ?? new string[0]);
                app.Run();
            }
        }
    }

    public class App : System.Windows.Application
    {
        public static App Instance { get; private set; }
        public Config Config { get; private set; }

        readonly string[] _args;
        WidgetWindow _widget;
        TrayIcon _tray;
        DispatcherTimer _refreshTimer;
        bool _refreshing;
        readonly CacheHitTracker _cacheHitTracker = new CacheHitTracker();

        public App(string[] args)
        {
            _args = args;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Instance = this;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) => Log.Write("DispatcherUnhandledException: " + ex.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("UnhandledException: " + ex.ExceptionObject);

            Config = Config.Load();
            ThemeManager.Initialize(ThemePreferenceValues.Parse(Config.ThemePreference));
            ThemeManager.Changed += OnThemeChanged;

            _widget = new WidgetWindow(Config);
            _tray = new TrayIcon(Config.RefreshSeconds, Config.PinMode == "top", Config.CacheHitWindowMinutes, ThemeManager.Preference);

            _tray.MoveRequested += () => _widget.ToggleMoveMode();
            _tray.ResetPositionRequested += () => _widget.ResetPosition();
            _tray.LoginRequested += OpenLogin;
            _tray.AutoStartChanged += AutoStart.SetEnabled;
            _tray.RefreshIntervalChanged += seconds => { Config.RefreshSeconds = seconds; Config.Save(); SetRefreshInterval(seconds); };
            _tray.CacheHitWindowChanged += minutes => { Config.CacheHitWindowMinutes = minutes; Config.Save(); _widget.SetCacheHitRate(_cacheHitTracker.GetRate(minutes, DateTime.Now)); };
            _tray.PinModeChanged += top => { Config.PinMode = top ? "top" : "bottom"; Config.Save(); _widget.SetPinMode(top); };
            _tray.ThemeChanged += preference => { Config.ThemePreference = ThemePreferenceValues.ToConfig(preference); Config.Save(); ThemeManager.SetPreference(preference); };
            _tray.AppearanceRequested += OpenAppearance;
            _tray.AboutRequested += OpenAbout;
            _tray.ExitRequested += ShutdownApp;
            _tray.RefreshRequested += () => _ = RefreshNowAsync();

            _widget.Show();
            _widget.ApplyTheme();
            SetRefreshInterval(Math.Max(30, Config.RefreshSeconds));
            HandleArguments();
            _ = RefreshNowAsync();
        }

        void OnThemeChanged()
        {
            _widget?.ApplyTheme();
            _tray?.SetTheme(ThemeManager.Preference);
        }

        void SetRefreshInterval(int seconds)
        {
            seconds = Math.Max(30, seconds);
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                _refreshTimer.Tick += (s, e) => _ = RefreshNowAsync();
            }
            _refreshTimer.Stop();
            _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
            _refreshTimer.Start();
        }

        public async Task RefreshNowAsync()
        {
            if (_refreshing || Config == null || _widget == null) return;
            _refreshing = true;
            DateTime updatedAt = DateTime.Now;
            try
            {
                string token = Config.PlatformTokenPlain;
                string cookie = Config.CookieHeaderPlain;
                BalanceInfo balance = await ApiClient.FetchPlatformBalanceAsync(token, cookie);
                UsageInfo usage = await ApiClient.FetchUsageAsync(token, cookie);
                if (usage != null && usage.HasTokenBreakdown) _cacheHitTracker.Record(updatedAt, usage.TokensToday, usage.CacheHitTokensToday);
                CacheHitRate rate = _cacheHitTracker.GetRate(Config.CacheHitWindowMinutes, updatedAt);
                NotifyLowBalance(balance);
                _widget.UpdateData(balance, usage, updatedAt, rate);
            }
            catch (Exception ex)
            {
                Log.Write("刷新异常: " + ex);
                _widget.UpdateData(new BalanceInfo { HasKey = !string.IsNullOrEmpty(Config.PlatformTokenPlain), Error = ex.Message }, null, updatedAt, null);
            }
            finally { _refreshing = false; }
        }

        void NotifyLowBalance(BalanceInfo balance)
        {
            if (balance == null || !balance.HasKey || balance.Error.Length > 0) return;
            if (balance.Total < Config.LowBalanceThreshold)
            {
                if (!Config.LowWarned)
                {
                    Config.LowWarned = true;
                    Config.Save();
                    _tray.ShowBalloon("DeepSeek 余额偏低", "当前余额 ¥" + balance.Total.ToString("0.00") + "，低于阈值 ¥" + Config.LowBalanceThreshold.ToString("0.##") + "。");
                }
            }
            else if (Config.LowWarned)
            {
                Config.LowWarned = false;
                Config.Save();
            }
        }

        public void OpenLogin() => new LoginWindow().Show();
        public void OpenRecharge() => new RechargeWindow().Show();
        public void OpenAbout() => Dialogs.ShowAbout();
        public void OpenAppearance() => Dialogs.ShowAppearance(Config.Opacity, Config.Scale,
            value => { Config.Opacity = value; Config.Save(); _widget.SetOpacity(value); },
            value => { Config.Scale = value; Config.Save(); _widget.SetScale(value); });

        void HandleArguments()
        {
            foreach (string arg in _args)
            {
                if (string.Equals(arg, "--login", StringComparison.OrdinalIgnoreCase)) OpenLogin();
                else if (string.Equals(arg, "--recharge", StringComparison.OrdinalIgnoreCase)) OpenRecharge();
                else if (string.Equals(arg, "--about", StringComparison.OrdinalIgnoreCase)) OpenAbout();
                else if (string.Equals(arg, "--appearance", StringComparison.OrdinalIgnoreCase)) OpenAppearance();
            }
        }

        void ShutdownApp()
        {
            try { _refreshTimer?.Stop(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _widget?.Close(); } catch { }
            Shutdown();
        }
    }

    internal static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "DeepSeekWidget";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(Name, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                    else key.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
