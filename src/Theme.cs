using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace DeepSeekWidget {

    internal static class ThemeManager {

        static UISettings _settings;
        static DispatcherQueue _dispatcherQueue;
        static bool _initialized;

        public static ThemePreference Preference { get; private set; } = ThemePreference.System;
        public static bool IsLight { get; private set; } = true;

        public static event Action Changed;

        public static ElementTheme ElementTheme {
            get { return IsLight ? ElementTheme.Light : ElementTheme.Dark; }
        }

        public static void Initialize(ThemePreference preference) {
            Preference = preference;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            IsLight = ResolveIsLight(preference);
            if (!_initialized) {
                _settings = new UISettings();
                _settings.ColorValuesChanged += OnColorValuesChanged;
                _initialized = true;
            }
            Log.Write("主题初始化: " + ThemePreferenceValues.ToConfig(preference)
                + "，当前=" + (IsLight ? "light" : "dark"));
            RaiseChanged();
        }

        public static void SetPreference(ThemePreference preference) {
            if (Preference == preference && _initialized) return;
            Preference = preference;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            IsLight = ResolveIsLight(preference);
            Log.Write("主题切换: " + ThemePreferenceValues.ToConfig(preference)
                + "，当前=" + (IsLight ? "light" : "dark"));
            RaiseChanged();
        }

        public static void Apply(FrameworkElement element) {
            if (element != null) element.RequestedTheme = ElementTheme;
        }

        public static void ApplyWindow(Window window) {
            if (window == null) return;
            Apply(window.Content as FrameworkElement);
            try {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                int dark = IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                int backdrop = IsLight ? 2 : 2;
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            } catch {
            }
            try {
                var bar = window.AppWindow.TitleBar;
                if (!bar.ExtendsContentIntoTitleBar) {
                    var foreground = IsLight ? Colors.Black : Colors.White;
                    bar.ButtonForegroundColor = foreground;
                    bar.ButtonHoverForegroundColor = foreground;
                    bar.ButtonBackgroundColor = Colors.Transparent;
                    bar.ButtonInactiveBackgroundColor = Colors.Transparent;
                }
            } catch {
            }
        }

        public static bool IsSystemLight() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value != 0;
                }
            } catch {
            }
            return true;
        }

        static bool ResolveIsLight(ThemePreference preference) {
            switch (preference) {
                case ThemePreference.Light: return true;
                case ThemePreference.Dark: return false;
                default: return IsSystemLight();
            }
        }

        static void OnColorValuesChanged(UISettings sender, object args) {
            if (Preference != ThemePreference.System) return;
            if (_dispatcherQueue == null) return;
            _dispatcherQueue.TryEnqueue(() => {
                bool light = IsSystemLight();
                if (light == IsLight) return;
                IsLight = light;
                Log.Write("跟随系统主题变化: " + (light ? "light" : "dark"));
                RaiseChanged();
            });
        }

        static void RaiseChanged() {
            var handler = Changed;
            if (handler != null) handler();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}



