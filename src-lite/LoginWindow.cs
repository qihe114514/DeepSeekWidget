using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekWidget
{
    public sealed class LoginWindow
    {
        readonly Window _window;
        readonly Grid _root;
        readonly TextBlock _status;
        readonly WebView2 _web;
        readonly System.Windows.Threading.DispatcherTimer _poll;

        bool _initialized;
        bool _polling;
        bool _saving;
        string _lastAttemptToken = "";
        string _lastAttemptCookies = "";
        DateTime _nextAttemptAt = DateTime.MinValue;
        int _failureCount;

        const string ScanJs = @"(function(){
  var found=[];
  var seenKeys={};
  function looks(s){
    return typeof s==='string' && s.length>=16 && s.length<=4096 && !/\s/.test(s) && /^[A-Za-z0-9._~+\/=-]+$/.test(s);
  }
  function walk(v, path, depth){
    if(depth>6 || v==null) return;
    if(typeof v==='string'){ if(looks(v)) found.push({key:path, value:v}); return; }
    if(Array.isArray(v)){ for(var i=0;i<v.length;i++) walk(v[i], path+'['+i+']', depth+1); return; }
    if(typeof v==='object'){ var ks=Object.keys(v); for(var j=0;j<ks.length;j++) walk(v[ks[j]], path+'.'+ks[j], depth+1); }
  }
  function scan(st, name){
    for(var i=0;i<st.length;i++){
      var k=st.key(i); if(!k) continue;
      var raw='';
      try{ raw=st.getItem(k)||''; }catch(e){ continue; }
      seenKeys[name+'.'+k]='';
      try{ walk(JSON.parse(raw), k, 0); }catch(e){ if(looks(raw)) found.push({key:k, value:raw}); }
    }
  }
  try{ scan(localStorage,'localStorage'); }catch(e){}
  try{ scan(sessionStorage,'sessionStorage'); }catch(e){}
  try{
    var parts=document.cookie.split(';');
    for(var i=0;i<parts.length;i++){
      var eq=parts[i].indexOf('=');
      if(eq<0) continue;
      var ck=parts[i].substring(0,eq).trim(), cv=parts[i].substring(eq+1).trim();
      if(looks(cv)) found.push({key:'cookie.'+ck, value:cv});
    }
  }catch(e){}
  return {found:found, keys:Object.keys(seenKeys)};
})();";

        public LoginWindow()
        {
            _window = new Window
            {
                Title = "登录 DeepSeek 账号",
                Width = 780,
                Height = Math.Min(860, SystemParameters.WorkArea.Height - 80),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brushes.White
            };

            _status = new TextBlock
            {
                Text = "请登录 DeepSeek 账号",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var header = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            header.Children.Add(_status);

            _web = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            var cancel = new Button { Content = "取消", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 10, 18, 14) };
            cancel.Click += (s, e) => _window.Close();

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(header, 0); Grid.SetRow(_web, 1); Grid.SetRow(cancel, 2);
            _root.Children.Add(header); _root.Children.Add(_web); _root.Children.Add(cancel);
            _window.Content = _root;
            _window.Loaded += async (s, e) => await InitAsync();
            _window.Closed += (s, e) => { _poll?.Stop(); ThemeManager.Changed -= OnThemeChanged; };
            ThemeManager.Changed += OnThemeChanged;

            _poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _poll.Tick += (s, e) => PollTick();
        }

        public void Show()
        {
            _window.Show();
            _window.Activate();
            ThemeManager.ApplyWindow(_window);
        }

        void OnThemeChanged()
        {
            UpdateWebTheme();
            ThemeManager.ApplyWindow(_window);
        }

        void UpdateWebTheme()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;
                _web.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.IsLight
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
            }
            catch { }
        }

        async Task InitAsync()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _status.Text = "正在打开登录页面…";
                var environment = await WebView2Host.GetEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(environment);
                UpdateWebTheme();
                _web.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _lastAttemptToken = "";
                    _nextAttemptAt = DateTime.MinValue;
                    _status.Text = "请登录 DeepSeek 账号";
                    _poll.Start();
                    PollTick();
                };
                _web.Source = new Uri("https://platform.deepseek.com/usage");
            }
            catch (Exception ex)
            {
                Log.Write("登录窗口初始化失败: " + ex);
                MessageBox.Show(_window, "无法打开登录页面：" + ex.Message, "DeepSeek 余额小组件", MessageBoxButton.OK, MessageBoxImage.Error);
                _window.Close();
            }
        }

        async void PollTick()
        {
            if (!_initialized || _polling || _saving || _web.CoreWebView2 == null) return;
            if (DateTime.Now < _nextAttemptAt) return;
            _polling = true;
            try
            {
                string json = await _web.CoreWebView2.ExecuteScriptAsync(ScanJs);
                LoginCandidate candidate = LoginCredentialDetector.SelectBest(json);
                if (candidate == null)
                {
                    _status.Text = "请登录 DeepSeek 账号";
                    _lastAttemptToken = "";
                    return;
                }

                string cookies = await ReadCookiesAsync();
                string token = candidate.Token ?? "";
                if (string.IsNullOrEmpty(token)) return;
                if (token == _lastAttemptToken && cookies == _lastAttemptCookies)
                {
                    _failureCount++;
                    if (_failureCount > 3)
                    {
                        _nextAttemptAt = DateTime.Now.AddSeconds(10);
                        _failureCount = 0;
                    }
                    return;
                }

                _lastAttemptToken = token;
                _lastAttemptCookies = cookies;
                _failureCount = 0;
                await SaveAsync(token, cookies);
            }
            catch (Exception ex)
            {
                _failureCount++;
                Log.Write("读取登录凭据失败: " + ex.Message);
                _status.Text = LooksLikeNetworkError(ex.Message) ? "网络不稳定，正在重试…" : "请在页面中完成登录";
                _nextAttemptAt = DateTime.Now.AddSeconds(Math.Min(10, _failureCount + 1));
            }
            finally { _polling = false; }
        }

        async Task<string> ReadCookiesAsync()
        {
            var builder = new StringBuilder();
            var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://platform.deepseek.com");
            foreach (var cookie in cookies) builder.Append(cookie.Name).Append('=').Append(cookie.Value).Append("; ");
            return builder.ToString();
        }

        async Task SaveAsync(string token, string cookies)
        {
            _saving = true;
            _poll.Stop();
            _status.Text = "登录成功，正在保存";
            try
            {
                var config = App.Instance.Config;
                config.PlatformTokenPlain = token;
                config.CookieHeaderPlain = cookies;
                config.Save();
                _ = App.Instance.RefreshNowAsync();
                await Task.Delay(650);
                _window.Close();
            }
            catch (Exception ex)
            {
                _saving = false;
                _poll.Start();
                _status.Text = "保存失败，请重试";
                Log.Write("保存登录状态失败: " + ex.Message);
            }
        }

        static bool LooksLikeNetworkError(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            return error.Contains("网络") || error.Contains("超时") || error.Contains("HTTP 5")
                || error.Contains("SSL") || error.Contains("连接");
        }
    }
}
