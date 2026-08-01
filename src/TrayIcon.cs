using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepSeekWidget {

    public class TrayIcon : IDisposable {
        readonly NotifyIcon _notify;
        readonly ContextMenuStrip _menu;
        readonly Icon _icon;
        readonly IntPtr _hIcon;

        public event Action MoveRequested;
        public event Action ResetPositionRequested;
        public event Action LoginRequested;
        public event Action ExitRequested;
        public event Action<bool> AutoStartChanged;
        public event Action<int> RefreshIntervalChanged;

        readonly List<Tuple<ToolStripMenuItem, int>> _refreshItems = new List<Tuple<ToolStripMenuItem, int>>();

        public TrayIcon(int refreshSeconds) {
            using (var bmp = new Bitmap(32, 32)) {
                _hIcon = DrawIcon(bmp);
            }
            _icon = Icon.FromHandle(_hIcon);
            _notify = new NotifyIcon {
                Icon = _icon,
                Text = "DeepSeek 余额小组件",
                Visible = true
            };

            _menu = new ContextMenuStrip();
            var miMove = new ToolStripMenuItem("移动位置");
            miMove.Click += (s, e) => { if (MoveRequested != null) MoveRequested(); };

            var miReset = new ToolStripMenuItem("重置位置");
            miReset.Click += (s, e) => { if (ResetPositionRequested != null) ResetPositionRequested(); };

            var miAuto = new ToolStripMenuItem("开机自启动") { Checked = AutoStart.IsEnabled() };
            miAuto.Click += (s, e) => {
                bool v = !miAuto.Checked;
                miAuto.Checked = v;
                if (AutoStartChanged != null) AutoStartChanged(v);
            };

            var miLogin = new ToolStripMenuItem("登录 DeepSeek 账号…");
            miLogin.Click += (s, e) => { if (LoginRequested != null) LoginRequested(); };

            // 刷新频率子菜单：30秒 / 1分钟 / 5分钟 / 10分钟
            var miRefresh = new ToolStripMenuItem("刷新频率");
            var opts = new[] {
                new { Sec = 30, Label = "30 秒" },
                new { Sec = 60, Label = "1 分钟" },
                new { Sec = 300, Label = "5 分钟" },
                new { Sec = 600, Label = "10 分钟" }
            };
            foreach (var o in opts) {
                var mi = new ToolStripMenuItem(o.Label) { Checked = (refreshSeconds == o.Sec) };
                int sec = o.Sec;
                mi.Click += (s, e) => SelectRefresh(sec);
                _refreshItems.Add(Tuple.Create(mi, sec));
                miRefresh.DropDownItems.Add(mi);
            }

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += (s, e) => { if (ExitRequested != null) ExitRequested(); };

            _menu.Items.AddRange(new ToolStripItem[] { miMove, miReset, miAuto, miRefresh, miLogin, miExit });
            _notify.ContextMenuStrip = _menu;
        }

        void SelectRefresh(int sec) {
            foreach (var t in _refreshItems) t.Item1.Checked = (t.Item2 == sec);
            if (RefreshIntervalChanged != null) RefreshIntervalChanged(sec);
        }

        static IntPtr DrawIcon(Bitmap bmp) {
            using (var g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8)) {
                    using (var fill = new SolidBrush(Color.FromArgb(255, 38, 44, 60))) {
                        g.FillPath(fill, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(255, 45, 127, 249), 2f)) {
                        g.DrawPath(pen, path);
                    }
                }
                using (var font = new Font("Microsoft YaHei", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                using (var brush = new SolidBrush(Color.White)) {
                    g.DrawString("¥", font, brush, new RectangleF(0, 0, 32, 32), sf);
                }
            }
            return bmp.GetHicon();
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius) {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose() {
            if (_notify != null) {
                _notify.Visible = false;
                _notify.Dispose();
            }
            if (_menu != null) _menu.Dispose();
            if (_icon != null) _icon.Dispose();
            if (_hIcon != IntPtr.Zero) Win32.DestroyIcon(_hIcon);
        }
    }
}
