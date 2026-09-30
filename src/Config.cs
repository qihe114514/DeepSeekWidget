using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekWidget {

    public class Config {
        public string PlatformToken = "";
        public string CookieHeader = "";
        public double? WindowX;
        public double? WindowY;
        public int RefreshSeconds = 120;
        public int CacheHitWindowMinutes = 5; // 近 5 / 10 分钟缓存命中率窗口
        public string PinMode = "bottom"; // "bottom" 置底（默认） / "top" 置顶
        public string ThemePreference = "system"; // system / light / dark
        public int Opacity = 100; // 组件整体不透明度（%），20–100
        public int Scale = 100;   // 组件整体缩放（%），60–250
        public decimal LowBalanceThreshold = 10m;
        public bool LowWarned = false;

        [JsonIgnore]
        public string PlatformTokenPlain {
            get { return Unprotect(PlatformToken); }
            set { PlatformToken = Protect(value); }
        }

        [JsonIgnore]
        public string CookieHeaderPlain {
            get { return Unprotect(CookieHeader); }
            set { CookieHeader = Protect(value); }
        }

        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeepSeekWidget.v1");

        // 与旧版 JavaScriptSerializer 保持同一份磁盘结构：公开字段参与序列化，明文访问器排除
        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
            IncludeFields = true,
            WriteIndented = false
        };

        static string Dir {
            get {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekWidget");
            }
        }

        public static string FilePath {
            get { return Path.Combine(Dir, "config.json"); }
        }

        static string Protect(string plain) {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        static string Unprotect(string data) {
            if (string.IsNullOrEmpty(data)) return "";
            try {
                byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            } catch {
                return "";
            }
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Dir);
                string json = JsonSerializer.Serialize(this, JsonOpts);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                // 原子替换，避免写入过程中断导致配置损坏
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            } catch {
                // 保存失败不阻断主流程
            }
        }

        public static Config Load() {
            var cfg = new Config();
            try {
                if (!File.Exists(FilePath)) return cfg;
                var map = Json.Parse(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (map == null) return cfg;
                // 逐字段容错读取：单个字段损坏不影响其余设置（尤其不能因此丢失登录态）
                cfg.PlatformToken = Str(map, "PlatformToken");
                cfg.CookieHeader = Str(map, "CookieHeader");
                cfg.WindowX = Dbl(map, "WindowX");
                cfg.WindowY = Dbl(map, "WindowY");
                int rs = Num(map, "RefreshSeconds", cfg.RefreshSeconds);
                if (rs >= 30) cfg.RefreshSeconds = rs;
                int cacheWindow = Num(map, "CacheHitWindowMinutes", cfg.CacheHitWindowMinutes);
                if (cacheWindow == 5 || cacheWindow == 10) cfg.CacheHitWindowMinutes = cacheWindow;
                string pm = Str(map, "PinMode");
                if (pm == "top" || pm == "bottom") cfg.PinMode = pm;
                cfg.ThemePreference = ThemePreferenceValues.ToConfig(
                    ThemePreferenceValues.Parse(Str(map, "ThemePreference")));
                int op = Num(map, "Opacity", cfg.Opacity);
                if (op >= 20 && op <= 100) cfg.Opacity = op;
                int sc = Num(map, "Scale", cfg.Scale);
                if (sc >= 60 && sc <= 250) cfg.Scale = sc;
                decimal lb = Num(map, "LowBalanceThreshold", cfg.LowBalanceThreshold);
                if (lb >= 0) cfg.LowBalanceThreshold = lb;
                cfg.LowWarned = Bool(map, "LowWarned");
            } catch {
                // 配置文件损坏时使用默认值
            }
            return cfg;
        }

        static object Get(Dictionary<string, object> map, string key) {
            object v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        static string Str(Dictionary<string, object> map, string key) {
            object v = Get(map, key);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        static double? Dbl(Dictionary<string, object> map, string key) {
            object v = Get(map, key);
            if (v == null) return null;
            try {
                double d;
                if (v is double) return (double)v;
                if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            } catch {
            }
            return null;
        }

        static int Num(Dictionary<string, object> map, string key, int fallback) {
            object v = Get(map, key);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        static decimal Num(Dictionary<string, object> map, string key, decimal fallback) {
            object v = Get(map, key);
            if (v == null) return fallback;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        static bool Bool(Dictionary<string, object> map, string key) {
            object v = Get(map, key);
            if (v is bool) return (bool)v;
            return v != null && string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture),
                "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}


