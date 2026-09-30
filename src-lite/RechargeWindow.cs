using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekWidget
{
    public sealed class RechargeWindow
    {
        readonly Window _window;
        readonly Grid _root;
        readonly WebView2 _web;
        readonly TextBlock _status;
        bool _initialized;

        public RechargeWindow()
        {
            _window = new Window
            {
                Title = "DeepSeek 充值",
                Width = 920,
                Height = Math.Min(700, SystemParameters.WorkArea.Height - 80),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brushes.White
            };
            _status = new TextBlock { Text = "正在打开充值页面…", Margin = new Thickness(18, 12, 18, 10), FontSize = 12 };
            _web = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_status, 0); Grid.SetRow(_web, 1);
            _root.Children.Add(_status); _root.Children.Add(_web);
            _window.Content = _root;
            _window.Loaded += async (s, e) => await InitAsync();
            _window.Closed += (s, e) => { ThemeManager.Changed -= OnThemeChanged; _ = App.Instance.RefreshNowAsync(); };
            ThemeManager.Changed += OnThemeChanged;
        }

        public void Show()
        {
            _window.Show();
            _window.Activate();
            ThemeManager.ApplyWindow(_window);
        }

        void OnThemeChanged()
        {
            try
            {
                if (_web.CoreWebView2 != null)
                {
                    _web.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.IsLight
                        ? CoreWebView2PreferredColorScheme.Light
                        : CoreWebView2PreferredColorScheme.Dark;
                }
            }
            catch { }
            ThemeManager.ApplyWindow(_window);
        }

        async Task InitAsync()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                var environment = await WebView2Host.GetEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(environment);
                OnThemeChanged();
                _web.CoreWebView2.NavigationCompleted += (s, e) => _status.Text = "";
                _web.Source = new Uri("https://platform.deepseek.com/top_up");
            }
            catch (Exception ex)
            {
                Log.Write("充值窗口初始化失败: " + ex);
                MessageBox.Show(_window, "无法打开充值页面：" + ex.Message, "DeepSeek 余额小组件", MessageBoxButton.OK, MessageBoxImage.Error);
                try { Process.Start(new ProcessStartInfo("https://platform.deepseek.com/top_up") { UseShellExecute = true }); } catch { }
                _window.Close();
            }
        }
    }
}
