using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekWidget {

    // 负责把内嵌的 WebView2 SDK DLL 释放到本地目录并初始化环境
    public static class WebView2Host {
        static CoreWebView2Environment _env;
        static bool _loaderReady;
        static Version _bestVersion;

        public static string UserDataFolder {
            get {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "WebView2");
            }
        }

        static string BinDir {
            get {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "bin");
            }
        }

        // 每次启动清理上次释放的临时 DLL
        public static void CleanupBin() {
            try {
                if (Directory.Exists(BinDir)) Directory.Delete(BinDir, true);
            } catch {
            }
        }

        static void EnsureLoader() {
            if (_loaderReady) return;
            Log.Write("开始释放 WebView2Loader");
            string dir = BinDir;
            Directory.CreateDirectory(dir);
            string loaderPath = Path.Combine(dir, "WebView2Loader.dll");
            string resName = Environment.Is64BitProcess
                ? "DeepSeekWidget.Bin.WebView2Loader.x64.dll"
                : "DeepSeekWidget.Bin.WebView2Loader.x86.dll";
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName)) {
                if (st == null) throw new InvalidOperationException("缺少内置 WebView2Loader.dll");
                byte[] buf = new byte[st.Length];
                int off = 0;
                while (off < buf.Length) {
                    int n = st.Read(buf, off, buf.Length - off);
                    if (n <= 0) break;
                    off += n;
                }
                File.WriteAllBytes(loaderPath, buf);
            }
            try {
                Win32.SetDefaultDllDirectories(Win32.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                Win32.AddDllDirectory(dir);
            } catch {
            }
            Win32.LoadLibrary(loaderPath);
            _loaderReady = true;
            Log.Write("WebView2Loader 就绪");
        }

        public static bool IsAvailable() {
            try {
                EnsureLoader();
                string v = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Log.Write("WebView2 运行时版本: " + v);
                return !string.IsNullOrEmpty(v);
            } catch (Exception ex) {
                Log.Write("WebView2 版本检测异常: " + ex.Message);
                return false;
            }
        }

        public static async Task<CoreWebView2Environment> GetEnvironmentAsync() {
            if (_env != null) return _env;
            EnsureLoader();
            Exception createError = null;
            try {
                _env = await CoreWebView2Environment.CreateAsync(UserDataFolder);
                Log.Write("WebView2 环境创建成功（默认发现）");
            } catch (Exception ex) {
                createError = ex;
            }
            if (createError != null) {
                Log.Write("WebView2 环境创建失败: " + createError.Message);
                string folder = FindRuntimeFolder();
                if (folder == null) throw createError;
                Log.Write("改用显式运行时目录: " + folder);
                _env = await CoreWebView2Environment.CreateAsync(folder, UserDataFolder, null);
                Log.Write("WebView2 环境创建成功（显式目录）");
            }
            return _env;
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
            foreach (string root in roots) {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root)) {
                    Version v;
                    if (!Version.TryParse(Path.GetFileName(dir), out v)) continue;
                    if (_bestVersion == null || v > _bestVersion) {
                        _bestVersion = v;
                        best = dir;
                    }
                }
            }
            Log.Write("FindRuntimeFolder 结果: " + (best ?? "未找到"));
            return best;
        }
    }
}
