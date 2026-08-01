using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DeepSeekWidget {

    public class Config {
        public string ApiKey = "";
        public string PlatformToken = "";
        public string CookieHeader = "";
        public double? WindowX;
        public double? WindowY;
        public int RefreshSeconds = 120;
        public string PinMode = "bottom"; // "bottom" 置底（默认） / "top" 置顶

        [ScriptIgnore]
        public string ApiKeyPlain {
            get { return Unprotect(ApiKey); }
            set { ApiKey = Protect(value); }
        }

        [ScriptIgnore]
        public string PlatformTokenPlain {
            get { return Unprotect(PlatformToken); }
            set { PlatformToken = Protect(value); }
        }

        [ScriptIgnore]
        public string CookieHeaderPlain {
            get { return Unprotect(CookieHeader); }
            set { CookieHeader = Protect(value); }
        }

        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeepSeekWidget.v1");

        static string Dir {
            get {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekWidget");
            }
        }

        static string FilePath {
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
                string json = new JavaScriptSerializer().Serialize(this);
                File.WriteAllText(FilePath, json);
            } catch {
                // 保存失败不阻断主流程
            }
        }

        public static Config Load() {
            var cfg = new Config();
            try {
                if (File.Exists(FilePath)) {
                    var loaded = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath));
                    if (loaded != null) {
                        cfg.ApiKey = loaded.ApiKey;
                        cfg.PlatformToken = loaded.PlatformToken;
                        cfg.CookieHeader = loaded.CookieHeader;
                        cfg.WindowX = loaded.WindowX;
                        cfg.WindowY = loaded.WindowY;
                        if (loaded.RefreshSeconds >= 30) cfg.RefreshSeconds = loaded.RefreshSeconds;
                        if (loaded.PinMode == "top" || loaded.PinMode == "bottom") cfg.PinMode = loaded.PinMode;
                    }
                }
            } catch {
                // 配置文件损坏时使用默认值
            }
            return cfg;
        }
    }
}
