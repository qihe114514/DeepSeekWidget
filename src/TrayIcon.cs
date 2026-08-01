using System;
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

        public TrayIcon() {
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

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += (s, e) => { if (ExitRequested != null) ExitRequested(); };

            _menu.Items.AddRange(new ToolStripItem[] { miMove, miReset, miAuto, miLogin, miExit });
            _notify.ContextMenuStrip = _menu;
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
