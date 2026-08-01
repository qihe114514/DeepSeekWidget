using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekWidget {

    public class LoginWindow : Window {
        readonly WebView2 _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
        readonly TextBlock _txtStatus = new TextBlock();
        readonly Button _btnSave;
        DispatcherTimer _poll;
        string _token = "";
        string _lastKeys = "";

        // 扫描 localStorage / sessionStorage / cookie 里的登录 token（递归，兼容不同存储结构）
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

        public LoginWindow() {
            Title = "登录 DeepSeek 账号";
            Width = 780;
            Height = Math.Min(860, SystemParameters.PrimaryScreenHeight - 80);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            _btnSave = new Button {
                Content = "保存登录态",
                IsEnabled = false,
                Width = 110,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _btnSave.Click += async (s, e) => await SaveAsync();

            var btnCancel = new Button { Content = "取消", Width = 70, Height = 30 };
            btnCancel.Click += (s, e) => Close();

            var header = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
            header.Children.Add(new TextBlock {
                Text = "在下方窗口中登录 platform.deepseek.com（或确认已有登录状态）",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });
            _txtStatus.Text = "正在加载登录页…";
            _txtStatus.Foreground = Brushes.Gray;
            header.Children.Add(_txtStatus);

            var bottom = new StackPanel {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 8, 14, 12),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            bottom.Children.Add(_btnSave);
            bottom.Children.Add(btnCancel);

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(header);
            root.Children.Add(_web);
            Content = root;

            Loaded += async (s, e) => await InitAsync();
            Closed += (s, e) => {
                if (_poll != null) _poll.Stop();
            };
        }

        async System.Threading.Tasks.Task InitAsync() {
            try {
                var env = await WebView2Host.GetEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(env);
                _web.Source = new Uri("https://platform.deepseek.com/usage");
                _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _poll.Tick += PollTick;
                _poll.Start();
            } catch (Exception ex) {
                Log.Write("登录窗口初始化失败: " + ex);
                MessageBox.Show(this, "WebView2 初始化失败：" + ex.Message, "提示");
                try { Process.Start("https://platform.deepseek.com/usage"); } catch { }
                CloseSoon();
            }
        }

        void CloseSoon() {
            Dispatcher.BeginInvoke(new Action(Close));
        }

        void PollTick(object sender, EventArgs e) {
            if (_web.CoreWebView2 == null) return;
            try {
                var t = _web.CoreWebView2.ExecuteScriptAsync(ScanJs);
                t.ContinueWith(prev => {
                    Dispatcher.BeginInvoke(new Action(() => {
                        if (!prev.IsCompleted) return;
                        try {
                            var obj = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(prev.Result);
                            string best = null;
                            int bestScore = -1;
                            string keysText = "";
                            if (obj != null) {
                                var keys = obj.ContainsKey("keys") ? obj["keys"] as ArrayList : null;
                                if (keys != null) keysText = string.Join(",", keys.ToArray());
                                var arr = obj.ContainsKey("found") ? obj["found"] as ArrayList : null;
                                if (arr != null) {
                                    foreach (object o in arr) {
                                        var item = o as Dictionary<string, object>;
                                        if (item == null) continue;
                                        string k = Convert.ToString(item["key"]);
                                        string v = Convert.ToString(item["value"]);
                                        int sc = v.Length;
                                        if (k.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0) sc += 100000;
                                        if (k.IndexOf("user", StringComparison.OrdinalIgnoreCase) >= 0) sc += 50000;
                                        if (sc > bestScore) {
                                            bestScore = sc;
                                            best = v;
                                        }
                                    }
                                }
                            }
                            if (best != null) {
                                _token = best;
                                _btnSave.IsEnabled = true;
                                _txtStatus.Text = "已检测到登录状态，点击“保存登录态”完成。";
                                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x1d, 0x9a, 0x4a));
                            } else {
                                if (keysText != _lastKeys) {
                                    _lastKeys = keysText;
                                    Log.Write("登录页暂未检测到 token，storage keys: " + keysText);
                                }
                                _txtStatus.Text = "尚未检测到登录状态，请先登录（若已登录请稍候）。";
                                _txtStatus.Foreground = Brushes.Gray;
                            }
                        } catch (Exception ex) {
                            Log.Write("登录 token 解析异常: " + ex.Message);
                        }
                    }), DispatcherPriority.Background);
                });
            } catch (Exception ex) {
                Log.Write("ExecuteScriptAsync 异常: " + ex.Message);
            }
        }

        async System.Threading.Tasks.Task SaveAsync() {
            try {
                var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://platform.deepseek.com");
                var sb = new StringBuilder();
                foreach (var c in cookies) {
                    sb.Append(c.Name).Append('=').Append(c.Value).Append("; ");
                }
                var cfg = App.Instance.Config;
                cfg.PlatformTokenPlain = _token;
                cfg.CookieHeaderPlain = sb.ToString();
                cfg.Save();
                _txtStatus.Text = "已保存";
                App.Instance.RefreshNow();
                Close();
            } catch (Exception ex) {
                MessageBox.Show(this, "保存失败：" + ex.Message, "提示");
            }
        }
    }
}
