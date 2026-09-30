using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace DeepSeekWidget {

    public sealed class RechargeWindow {
        readonly Window _window;
        readonly Grid _root;
        readonly Microsoft.UI.Xaml.Controls.WebView2 _web;
        readonly TextBlock _status;

        bool _initialized;

        public RechargeWindow() {
            _window = new Window { Title = "DeepSeek 充值" };
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(920,
                Math.Min(700, DisplayArea.Primary.WorkArea.Height - 80)));

            _status = new TextBlock {
                Text = "正在打开充值页面…",
                Margin = new Thickness(18, 12, 18, 10),
                FontSize = 12
            };
            _web = new Microsoft.UI.Xaml.Controls.WebView2 {
                DefaultBackgroundColor = Colors.White
            };

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_status, 0);
            Grid.SetRow(_web, 1);
            _root.Children.Add(_status);
            _root.Children.Add(_web);
            _window.Content = _root;
            ThemeManager.Apply(_root);

            _window.Activated += async (s, e) => await InitAsync();
            _window.Closed += (s, e) => {
                ThemeManager.Changed -= OnThemeChanged;
                if (App.Instance != null) App.Instance.RefreshNow();
            };
            ThemeManager.Changed += OnThemeChanged;
        }

        public void Show() {
            _window.Activate();
            ThemeManager.ApplyWindow(_window);
        }

        void OnThemeChanged() {
            ThemeManager.Apply(_root);
            ThemeManager.ApplyWindow(_window);
            try {
                if (_web.CoreWebView2 != null) {
                    _web.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.IsLight
                        ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light
                        : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
                }
            } catch { }
        }

        async Task InitAsync() {
            if (_initialized) return;
            _initialized = true;
            try {
                var environment = await WebView2Host.GetEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(environment);
                _web.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.IsLight
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
                _web.CoreWebView2.NavigationCompleted += (s, e) => _status.Text = "";
                _web.Source = new Uri("https://platform.deepseek.com/top_up");
            } catch (Exception ex) {
                Log.Write("充值窗口初始化失败: " + ex);
                await WinUI3Dialogs.ShowMessageAsync(_window, "无法打开充值页面", ex.Message);
                try { Process.Start(new ProcessStartInfo("https://platform.deepseek.com/top_up") { UseShellExecute = true }); } catch { }
                _window.Close();
            }
        }
    }
}

