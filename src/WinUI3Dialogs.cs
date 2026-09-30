using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeepSeekWidget {

    internal static class WinUI3Dialogs {
        public const string AboutVersion = "v1.5.0";

        static Window _about;
        static Window _appearance;

        public static void ShowAbout() {
            if (_about != null) {
                _about.Activate();
                return;
            }
            _about = CreateDialog("关于 DeepSeek 小组件", 420, 390, BuildAbout());
            _about.Closed += (s, e) => _about = null;
            _about.Activate();
            ThemeManager.ApplyWindow(_about);
        }

        public static void ShowAppearance(int opacity, int scale,
            Action<int> onOpacity, Action<int> onScale) {
            if (_appearance != null) {
                _appearance.Activate();
                return;
            }
            _appearance = CreateDialog("外观设置", 450, 390,
                BuildAppearance(opacity, scale, onOpacity, onScale));
            _appearance.Closed += (s, e) => _appearance = null;
            _appearance.Activate();
            ThemeManager.ApplyWindow(_appearance);
        }

        public static async Task ShowMessageAsync(Window owner, string title, string message, string closeText = "确定") {
            if (owner == null || owner.Content == null || owner.Content.XamlRoot == null) {
                Log.Write("对话框无法显示: " + title + " " + message);
                return;
            }
            var dialog = new ContentDialog {
                XamlRoot = owner.Content.XamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close
            };
            ThemeManager.Apply(dialog);
            await dialog.ShowAsync();
        }

        static Window CreateDialog(string title, int width, int height, UIElement content) {
            var window = new Window { Title = title };
            window.AppWindow.Resize(new SizeInt32(width, height));
            var root = new Grid { Padding = new Thickness(24), Background = new SolidColorBrush(Colors.Transparent) };
            root.Children.Add(content);
            window.Content = root;
                        ThemeManager.Apply(root);
            Action themeChanged = () => {
                ThemeManager.Apply(root);
                ThemeManager.ApplyWindow(window);
            };
            ThemeManager.Changed += themeChanged;
            window.Closed += (s, e) => ThemeManager.Changed -= themeChanged;
            return window;
        }

        static StackPanel BuildAbout() {
            var root = new StackPanel { Spacing = 12 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            head.Children.Add(new Border {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 44, 60)),
                BorderBrush = AccentBrush(),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock {
                    Text = "¥",
                    FontSize = 26,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var title = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock {
                Text = "DeepSeek 余额小组件",
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            title.Children.Add(new TextBlock {
                Text = "版本 " + AboutVersion,
                FontSize = 12,
                Opacity = 0.62
            });
            head.Children.Add(title);
            root.Children.Add(head);

            root.Children.Add(new TextBlock { Text = "作者：@其核", FontSize = 13, Opacity = 0.68 });
            root.Children.Add(new Border { Height = 1, Opacity = 0.18, Background = new SolidColorBrush(Colors.Gray) });

            var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            links.Children.Add(LinkButton("GitHub", "https://github.com/qihe114514/DeepSeekWidget", "\uE71B"));
            links.Children.Add(LinkButton("B站", "https://space.bilibili.com/1049283248", "\uE774"));
            links.Children.Add(LinkButton("抖音", "https://www.douyin.com/user/MS4wLjABAAAAuUtKOArTFKTBm4C6o5MwDQuGMNZ9-0CWZfUay6U9wUI", "\uE8A5"));
            root.Children.Add(links);
            root.Children.Add(TextButton("打开数据目录（配置与日志）", OpenDataDir, "\uE8B7"));

            var close = new Button { Content = "关闭", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => _about.Close();
            root.Children.Add(close);
            return root;
        }

        static StackPanel BuildAppearance(int opacity, int scale, Action<int> onOpacity, Action<int> onScale) {
            var root = new StackPanel { Spacing = 12 };
            root.Children.Add(new TextBlock {
                Text = "调整透明度与缩放，实时生效并自动保存",
                FontSize = 12,
                Opacity = 0.66,
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(SliderRow("不透明度", opacity, 20, 100, onOpacity));
            root.Children.Add(SliderRow("缩放比例", scale, 60, 250, onScale));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var reset = new Button { Content = "重置 100%" };
            reset.Click += (s, e) => { onOpacity(100); onScale(100); };
            var close = new Button { Content = "关闭", MinWidth = 88 };
            close.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            close.Click += (s, e) => _appearance.Close();
            actions.Children.Add(reset);
            actions.Children.Add(close);
            root.Children.Add(actions);
            return root;
        }

        static StackPanel SliderRow(string label, int value, int min, int max, Action<int> onChange) {
            var panel = new StackPanel { Spacing = 6 };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var amount = new TextBlock { Text = value + "%", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(amount, 1);
            head.Children.Add(name);
            head.Children.Add(amount);
            panel.Children.Add(head);

            int last = value;
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, StepFrequency = 1 };
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

        static Button LinkButton(string text, string url, string glyph) {
            var button = new Button {
                Content = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = {
                        new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 },
                        new TextBlock { Text = text, FontSize = 12.5 }
                    }
                },
                Padding = new Thickness(12, 6, 12, 6)
            };
            button.Click += (s, e) => {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("打开链接失败: " + ex.Message); }
            };
            return button;
        }

        static Button TextButton(string text, Action action, string glyph) {
            var button = new Button {
                Content = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = {
                        new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 },
                        new TextBlock { Text = text, FontSize = 12.5 }
                    }
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 6, 12, 6)
            };
            button.Click += (s, e) => action();
            return button;
        }

        static void OpenDataDir() {
            try {
                string dir = Path.GetDirectoryName(Config.FilePath);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            } catch (Exception ex) {
                Log.Write("打开数据目录失败: " + ex.Message);
            }
        }

        static SolidColorBrush AccentBrush() {
            try {
                return (SolidColorBrush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            } catch {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 194, 255));
            }
        }
    }
}

