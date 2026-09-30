using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeepSeekWidget
{
    public sealed class TrayIcon : IDisposable
    {
        readonly NotifyIcon _icon;
        readonly ContextMenuStrip _menu;
        readonly Dictionary<int, ToolStripMenuItem> _refreshItems = new Dictionary<int, ToolStripMenuItem>();
        readonly Dictionary<int, ToolStripMenuItem> _cacheItems = new Dictionary<int, ToolStripMenuItem>();
        readonly Dictionary<ThemePreference, ToolStripMenuItem> _themeItems = new Dictionary<ThemePreference, ToolStripMenuItem>();
        readonly ToolStripMenuItem _pinTop;
        readonly ToolStripMenuItem _pinBottom;
        readonly ToolStripMenuItem _autoStart;

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

        public TrayIcon(int refreshSeconds, bool pinTop, int cacheWindowMinutes, ThemePreference theme)
        {
            _menu = new ContextMenuStrip { ShowImageMargin = false };
            _menu.Items.Add(Item("立即刷新", () => RefreshRequested?.Invoke()));

            var position = new ToolStripMenuItem("位置");
            position.DropDownItems.Add(Item("移动位置", () => MoveRequested?.Invoke()));
            position.DropDownItems.Add(Item("重置位置", () => ResetPositionRequested?.Invoke()));
            _menu.Items.Add(position);

            var layer = new ToolStripMenuItem("窗口层级");
            _pinTop = Item("窗口置顶", () => SelectPin(true));
            _pinBottom = Item("窗口置底", () => SelectPin(false));
            layer.DropDownItems.Add(_pinTop);
            layer.DropDownItems.Add(_pinBottom);
            _menu.Items.Add(layer);

            var refresh = new ToolStripMenuItem("刷新频率");
            foreach (var entry in new[] { new Tuple<int,string>(30,"30 秒"), new Tuple<int,string>(60,"1 分钟"), new Tuple<int,string>(120,"2 分钟"), new Tuple<int,string>(300,"5 分钟"), new Tuple<int,string>(600,"10 分钟") })
            {
                var item = Item(entry.Item2, null);
                item.Tag = entry.Item1;
                item.Click += (s, e) => SelectRefresh((int)((ToolStripMenuItem)s).Tag);
                _refreshItems[entry.Item1] = item;
                refresh.DropDownItems.Add(item);
            }
            _menu.Items.Add(refresh);

            var cache = new ToolStripMenuItem("缓存命中率窗口");
            foreach (var minutes in new[] { 5, 10 })
            {
                var item = Item("近 " + minutes + " 分钟", null);
                item.Tag = minutes;
                item.Click += (s, e) => SelectCacheWindow((int)((ToolStripMenuItem)s).Tag);
                _cacheItems[minutes] = item;
                cache.DropDownItems.Add(item);
            }
            _menu.Items.Add(cache);

            var themes = new ToolStripMenuItem("主题");
            foreach (ThemePreference preference in new[] { ThemePreference.System, ThemePreference.Light, ThemePreference.Dark })
            {
                var item = Item(preference == ThemePreference.System ? "跟随系统" : (preference == ThemePreference.Light ? "浅色" : "深色"), null);
                item.Tag = preference;
                item.Click += (s, e) => SelectTheme((ThemePreference)((ToolStripMenuItem)s).Tag, true);
                _themeItems[preference] = item;
                themes.DropDownItems.Add(item);
            }
            _menu.Items.Add(themes);

            _menu.Items.Add(Item("外观设置…", () => AppearanceRequested?.Invoke()));
            _autoStart = Item("开机自启动", () =>
            {
                bool enabled = !AutoStart.IsEnabled();
                UpdateAutoIcon(enabled);
                AutoStartChanged?.Invoke(enabled);
            });
            _menu.Items.Add(_autoStart);
            _menu.Items.Add(Item("登录 DeepSeek 账号…", () => LoginRequested?.Invoke()));
            _menu.Items.Add(Item("关于", () => AboutRequested?.Invoke()));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Item("退出", () => ExitRequested?.Invoke()));

            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
            _icon = new NotifyIcon
            {
                Icon = icon ?? SystemIcons.Application,
                Text = "DeepSeek 余额小组件",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => RefreshRequested?.Invoke();
            _menu.Opening += (s, e) => UpdateAutoIcon(AutoStart.IsEnabled());

            SelectPin(pinTop);
            SelectRefresh(refreshSeconds);
            SelectCacheWindow(cacheWindowMinutes);
            SelectTheme(theme, false);
        }

        static ToolStripMenuItem Item(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            if (action != null) item.Click += (s, e) => action();
            return item;
        }

        public void ShowBalloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void ShowMenuAt(int x, int y)
        {
            try { _menu.Show(new Point(x, y)); } catch { }
        }

        void SelectPin(bool top)
        {
            _pinTop.Checked = top;
            _pinBottom.Checked = !top;
            PinModeChanged?.Invoke(top);
        }

        void SelectRefresh(int seconds)
        {
            foreach (var entry in _refreshItems) entry.Value.Checked = entry.Key == seconds;
            RefreshIntervalChanged?.Invoke(seconds);
        }

        void SelectCacheWindow(int minutes)
        {
            if (minutes != 5 && minutes != 10) minutes = 5;
            foreach (var entry in _cacheItems) entry.Value.Checked = entry.Key == minutes;
            CacheHitWindowChanged?.Invoke(minutes);
        }

        void SelectTheme(ThemePreference preference, bool raiseEvent)
        {
            foreach (var entry in _themeItems) entry.Value.Checked = entry.Key == preference;
            if (raiseEvent) ThemeChanged?.Invoke(preference);
        }

        public void SetTheme(ThemePreference preference) => SelectTheme(preference, false);

        void UpdateAutoIcon(bool enabled) => _autoStart.Checked = enabled;

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
