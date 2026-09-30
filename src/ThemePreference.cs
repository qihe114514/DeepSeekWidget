using System;

namespace DeepSeekWidget {

    public enum ThemePreference {
        System,
        Light,
        Dark
    }

    public static class ThemePreferenceValues {

        public static ThemePreference Parse(string value) {
            if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)) {
                return ThemePreference.Light;
            }
            if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase)) {
                return ThemePreference.Dark;
            }
            return ThemePreference.System;
        }

        public static string ToConfig(ThemePreference value) {
            switch (value) {
                case ThemePreference.Light: return "light";
                case ThemePreference.Dark: return "dark";
                default: return "system";
            }
        }
    }
}
