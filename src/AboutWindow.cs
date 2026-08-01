using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekWidget {

    public class AboutWindow : Window {
        public const string Version = "v1.2.0";
        const string Author = "@其核";

        const string UrlGithub = "https://github.com/qihe114514/DeepSeekWidget";
        const string UrlBili = "https://space.bilibili.com/1049283248";
        const string UrlDouyin = "https://www.douyin.com/user/MS4wLjABAAAAuUtKOArTFKTBm4C6o5MwDQuGMNZ9-0CWZfUay6U9wUI";

        const string IconUrlBili = "https://i0.hdslb.com/bfs/static/jinkela/long/images/favicon.ico";
        const string IconUrlDouyin = "https://lf-douyin-pc-web.douyinstatic.com/obj/douyin-pc-web/2025_0313_logo.png";

        // GitHub octocat（pinned-octocat svg，viewBox 36x36）path 数据
        const string OctoPath =
            "M18,1.4C9,1.4,1.7,8.7,1.7,17.7c0,7.2,4.7,13.3,11.1,15.5" +
            "c0.8,0.1,1.1-0.4,1.1-0.8c0-0.4,0-1.4,0-2.8c-4.5,1-5.5-2.2-5.5-2.2c-0.7-1.9-1.8-2.4-1.8-2.4c-1.5-1,0.1-1,0.1-1" +
            "c1.6,0.1,2.5,1.7,2.5,1.7c1.5,2.5,3.8,1.8,4.7,1.4c0.1-1.1,0.6-1.8,1-2.2c-3.6-0.4-7.4-1.8-7.4-8.1c0-1.8,0.6-3.2,1.7-4.4" +
            "c-0.2-0.4-0.7-2.1,0.2-4.3c0,0,1.4-0.4,4.5,1.7c1.3-0.4,2.7-0.5,4.1-0.5c1.4,0,2.8,0.2,4.1,0.5c3.1-2.1,4.5-1.7,4.5-1.7" +
            "c0.9,2.2,0.3,3.9,0.2,4.3c1,1.1,1.7,2.6,1.7,4.4c0,6.3-3.8,7.6-7.4,8c0.6,0.5,1.1,1.5,1.1,3c0,2.2,0,3.9,0,4.5" +
            "c0,0.4,0.3,0.9,1.1,0.8c6.5-2.2,11.1-8.3,11.1-15.5C34.3,8.7,27,1.4,18,1.4z";

        public AboutWindow() {
            Title = "关于";
            Width = 340;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2A));
            Foreground = new SolidColorBrush(Color.FromRgb(245, 247, 250));

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            // 顶部：图标 + 名称/版本
            var logo = new Border {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(38, 44, 60)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 127, 249)),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock {
                    Text = "¥",
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var titleBox = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titleBox.Children.Add(new TextBlock {
                Text = "DeepSeek 余额小组件",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 247, 250))
            });
            titleBox.Children.Add(new TextBlock {
                Text = "版本 " + Version,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(143, 152, 168))
            });
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(logo);
            head.Children.Add(titleBox);
            root.Children.Add(head);

            // 作者
            root.Children.Add(new TextBlock {
                Text = "作者：" + Author,
                FontSize = 13,
                Margin = new Thickness(0, 18, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 208, 220))
            });

            // 分隔线
            root.Children.Add(new Border {
                Height = 1,
                Margin = new Thickness(0, 16, 0, 14),
                Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
            });

            // 三个圆形按钮
            var buttons = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttons.Children.Add(MakeRoundButton("GitHub", UrlGithub, GithubIcon()));
            buttons.Children.Add(MakeRoundButton("B站", UrlBili, LoadIcon("bili.ico", IconUrlBili, "B")));
            buttons.Children.Add(MakeRoundButton("抖音", UrlDouyin, LoadIcon("douyin.png", IconUrlDouyin, "D")));
            root.Children.Add(buttons);

            root.Children.Add(new TextBlock {
                Text = "点击图标跳转到我的主页",
                FontSize = 11,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 143, 152, 168))
            });

            Content = root;
        }

        static Geometry GithubGeometry() {
            // SVG 紧凑语法（如 1.1-0.4）转 WPF 可解析语法
            string d = Regex.Replace(OctoPath, @"(?<=[0-9.])-(?=[0-9])", " -");
            return Geometry.Parse(d);
        }

        static UIElement GithubIcon() {
            try {
                return new System.Windows.Shapes.Path {
                    Data = GithubGeometry(),
                    Fill = new SolidColorBrush(Color.FromRgb(25, 23, 23)),
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform
                };
            } catch {
                return FallbackText("G");
            }
        }

        // 从 exe 目录 icons/ 读取图标；缺失则尝试下载；失败用首字母占位
        static UIElement LoadIcon(string fileName, string url, string fallback) {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            string file = Path.Combine(dir, fileName);
            try {
                if (!File.Exists(file)) {
                    Directory.CreateDirectory(dir);
                    using (var wc = new WebClient()) {
                        wc.Headers.Add("User-Agent", "DeepSeekWidget/" + Version);
                        wc.DownloadFile(url, file);
                    }
                }
                BitmapSource bmp;
                if (fileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) {
                    var dec = new IconBitmapDecoder(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    bmp = dec.Frames[dec.Frames.Count - 1]; // 取最大尺寸帧
                } else {
                    bmp = new BitmapImage(new Uri(file));
                }
                var img = new Image {
                    Source = bmp,
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform
                };
                // 圆角遮罩：方形图标（如抖音/B站 logo）在圆形按钮里更协调
                img.Clip = new RectangleGeometry(new Rect(0, 0, 26, 26), 7, 7);
                return img;
            } catch {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                return FallbackText(fallback);
            }
        }

        static UIElement FallbackText(string letter) {
            return new TextBlock {
                Text = letter,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(25, 23, 23)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static Button MakeRoundButton(string tooltip, string url, UIElement content) {
            var btn = new Button {
                Width = 48,
                Height = 48,
                Margin = new Thickness(10, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)),
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Content = content
            };
            var border = new Border {
                CornerRadius = new CornerRadius(24),
                Background = new SolidColorBrush(Color.FromRgb(244, 246, 248))
            };
            var template = new ControlTemplate(typeof(Button));
            var f = new FrameworkElementFactory(typeof(Border));
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(24));
            f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var p = new FrameworkElementFactory(typeof(ContentPresenter));
            p.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            p.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.AppendChild(p);
            template.VisualTree = f;
            btn.Template = template;
            btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromRgb(226, 231, 238));
            btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(Color.FromRgb(244, 246, 248));
            btn.Click += (s, e) => {
                try {
                    Process.Start(url);
                } catch (Exception ex) {
                    Log.Write("打开链接失败 " + url + ": " + ex.Message);
                }
            };
            return btn;
        }
    }
}
