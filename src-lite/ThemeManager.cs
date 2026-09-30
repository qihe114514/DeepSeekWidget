using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace DeepSeekWidget
{
    internal static class ThemeManager
    {
        public static ThemePreference Preference { get; private set; } = ThemePreference.System;
        public static bool IsLight { get; private set; } = true;
        public static event Action Changed;
        static bool _subscribed;

        public static void Initialize(ThemePreference preference)
        {
            Preference = preference;
            IsLight = ResolveIsLight(preference);
            if (!_subscribed)
            {
                SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
                _subscribed = true;
            }
            RaiseChanged();
        }

        public static void SetPreference(ThemePreference preference)
        {
            Preference = preference;
            IsLight = ResolveIsLight(preference);
            RaiseChanged();
        }

        public static bool IsSystemLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value != 0;
                }
            }
            catch { }
            return true;
        }

        public static void ApplyWindow(Window window)
        {
            if (window == null) return;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                int dark = IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        static bool ResolveIsLight(ThemePreference preference)
        {
            if (preference == ThemePreference.Light) return true;
            if (preference == ThemePreference.Dark) return false;
            return IsSystemLight();
        }

        static void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (Preference != ThemePreference.System) return;
            bool light = IsSystemLight();
            if (light == IsLight) return;
            IsLight = light;
            RaiseChanged();
        }

        static void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
