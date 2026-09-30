using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekWidget {

    // WebView2 环境：SDK 随应用一起发布（Microsoft.Web.WebView2 包自带 WebView2Loader.dll），
    // 这里只负责创建环境，并在默认发现失败时按已安装的运行时目录兜底。
    public static class WebView2Host {
        static Task<CoreWebView2Environment> _envTask;
        static readonly object _envLock = new object();

        public static string UserDataFolder {
            get {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "WebView2");
            }
        }

        // 清理旧版内嵌释放出来的临时 DLL 目录（保留兼容，非必需）
        public static void CleanupBin() {
            try {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "bin");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            } catch {
            }
        }

        public static bool IsAvailable() {
            try {
                string v = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Log.Write("WebView2 运行时版本: " + v);
                return !string.IsNullOrEmpty(v);
            } catch (Exception ex) {
                Log.Write("WebView2 版本检测异常: " + ex.Message);
                return false;
            }
        }

        public static Task<CoreWebView2Environment> GetEnvironmentAsync() {
            lock (_envLock) {
                if (_envTask == null) _envTask = CreateEnvironmentAsync();
                return _envTask;
            }
        }

        static async Task<CoreWebView2Environment> CreateEnvironmentAsync() {
            try {
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", UserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync();
                Log.Write("WebView2 环境创建成功（默认发现）");
                return env;
            } catch (Exception firstError) {
                Log.Write("WebView2 环境创建失败: " + firstError.Message);
                string folder = FindRuntimeFolder();
                if (folder == null) throw;
                Log.Write("改用显式运行时目录: " + folder);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", UserDataFolder);
                Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", folder);
                var env = await CoreWebView2Environment.CreateAsync();
                Log.Write("WebView2 环境创建成功（显式目录）");
                return env;
            }
        }

        // 部分机器只注册了 32 位运行时视图，64 位进程默认发现不到运行时，
        // 这里直接扫描已安装的运行时目录作为兜底
        static string FindRuntimeFolder() {
            string[] roots = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft", "EdgeWebView", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft", "EdgeWebView", "Application")
            };
            string best = null;
            Version bestVersion = null;
            foreach (string root in roots) {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root)) {
                    Version v;
                    if (!Version.TryParse(Path.GetFileName(dir), out v)) continue;
                    if (bestVersion == null || v > bestVersion) {
                        bestVersion = v;
                        best = dir;
                    }
                }
            }
            Log.Write("FindRuntimeFolder 结果: " + (best ?? "未找到"));
            return best;
        }
    }
}




