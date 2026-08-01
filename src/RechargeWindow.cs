using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekWidget {

    public class RechargeWindow : Window {
        readonly WebView2 _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };

        public RechargeWindow() {
            Title = "DeepSeek 充值";
            Width = 920;
            Height = Math.Min(700, SystemParameters.PrimaryScreenHeight - 80);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _web;
            Loaded += async (s, e) => await InitAsync();
            Closed += (s, e) => {
                var app = App.Instance;
                if (app != null) app.RefreshNow();
            };
        }

        async System.Threading.Tasks.Task InitAsync() {
            try {
                var env = await WebView2Host.GetEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(env);
                _web.Source = new Uri("https://platform.deepseek.com/top_up");
            } catch (Exception ex) {
                Log.Write("充值窗口初始化失败: " + ex);
                MessageBox.Show(this, "WebView2 初始化失败：" + ex.Message, "提示");
                try { Process.Start("https://platform.deepseek.com/top_up"); } catch { }
                CloseSoon();
            }
        }

        void CloseSoon() {
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
