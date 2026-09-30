using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeepSeekWidget
{
    internal static class Dialogs
    {
        public const string AboutVersion = "v1.6.0";
        static Window _about;
        static Window _appearance;

        public static void ShowAbout()
        {
            if (_about != null) { _about.Activate(); return; }
            _about = new Window
            {
                Title = "关于 DeepSeek 小组件",
                Width = 420,
                Height = 390,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = BuildAbout()
            };
            _about.Closed += (s, e) => _about = null;
            ThemeManager.ApplyWindow(_about);
            _about.Show();
        }

        public static void ShowAppearance(int opacity, int scale, Action<int> onOpacity, Action<int> onScale)
        {
            if (_appearance != null) { _appearance.Activate(); return; }
            _appearance = new Window
            {
                Title = "外观设置",
                Width = 450,
                Height = 390,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = BuildAppearance(opacity, scale, onOpacity, onScale)
            };
            _appearance.Closed += (s, e) => _appearance = null;
            ThemeManager.ApplyWindow(_appearance);
            _appearance.Show();
        }

        static FrameworkElement BuildAbout()
        {
            var root = new StackPanel { Margin = new Thickness(24), Background = Brushes.Transparent };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(38, 44, 60)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 155, 255)),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock { Text = "¥", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var title = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "DeepSeek 余额小组件", FontSize = 17, FontWeight = FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = "版本 " + AboutVersion, FontSize = 12, Opacity = 0.62 });
            head.Children.Add(title);
            root.Children.Add(head);

            root.Children.Add(new TextBlock { Text = "作者：@其核", FontSize = 13, Opacity = 0.68, Margin = new Thickness(0, 16, 0, 0) });
            root.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12), Opacity = 0.35 });
            var links = new StackPanel { Orientation = Orientation.Horizontal };
            links.Children.Add(LinkButton("GitHub", "https://github.com/qihe114514/DeepSeekWidget"));
            links.Children.Add(LinkButton("B站", "https://space.bilibili.com/1049283248"));
            links.Children.Add(LinkButton("抖音", "https://www.douyin.com/user/MS4wLjABAAAAXfVk4uXTOWwZfZq2eGkQwVQ7HwX5h1W9O0G6Q0i0n0M"));
            root.Children.Add(links);
            root.Children.Add(TextButton("打开数据目录", OpenDataDir));
            root.Children.Add(TextButton("访问 DeepSeek 平台", () => OpenUrl("https://platform.deepseek.com/usage")));
            return root;
        }

        static FrameworkElement BuildAppearance(int opacity, int scale, Action<int> onOpacity, Action<int> onScale)
        {
            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(new TextBlock { Text = "调整透明度与缩放，实时生效并自动保存", FontSize = 12, Opacity = 0.66, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(SliderRow("不透明度", opacity, 20, 100, onOpacity));
            root.Children.Add(SliderRow("缩放比例", scale, 60, 250, onScale));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var reset = new Button { Content = "重置 100%", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += (s, e) => { onOpacity(100); onScale(100); };
            var close = new Button { Content = "关闭", MinWidth = 88 };
            close.Click += (s, e) => _appearance?.Close();
            actions.Children.Add(reset);
            actions.Children.Add(close);
            root.Children.Add(actions);
            return root;
        }

        static FrameworkElement SliderRow(string label, int value, int min, int max, Action<int> onChange)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold };
            var amount = new TextBlock { Text = value + "%", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(amount, 1);
            head.Children.Add(name);
            head.Children.Add(amount);
            panel.Children.Add(head);
            int last = value;
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
            slider.ValueChanged += (s, e) => {
                int current = (int)Math.Round(e.NewValue);
                amount.Text = current + "%";
                if (current == last) return;
                last = current;
                onChange(current);
            };
            panel.Children.Add(slider);
            return panel;
        }

        static Button LinkButton(string text, string url)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (s, e) => OpenUrl(url);
            return button;
        }

        static Button TextButton(string text, Action action)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 10, 0, 0) };
            button.Click += (s, e) => action();
            return button;
        }

        static void OpenDataDir()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Config.FilePath);
                Directory.CreateDirectory(dir);
                OpenUrl(dir);
            }
            catch (Exception ex) { Log.Write("打开数据目录失败: " + ex.Message); }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("打开链接失败: " + ex.Message); }
        }
    }
}
