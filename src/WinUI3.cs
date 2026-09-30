using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace DeepSeekWidget {

    internal static class WinUI3 {
        static bool _ready;
        static XamlApp _app;
        static DispatcherQueueController _queue;

        public static bool IsReady { get { return _ready; } }

        public static void Initialize() {
            if (_ready) return;
            _queue = DispatcherQueueController.CreateOnCurrentThread();
            _app = new XamlApp();
            HookPreTranslate();
            _ready = true;
            Log.Write("WinUI 3 已初始化");
        }

        static void HookPreTranslate() {
            try {
                System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
            } catch (Exception ex) {
                Log.Write("WinUI 消息挂钩失败: " + ex.Message);
            }
        }

        static void OnThreadFilterMessage(ref System.Windows.Interop.MSG msg, ref bool handled) {
            try {
                var native = new NativeMsg {
                    hwnd = msg.hwnd,
                    message = (uint)msg.message,
                    wParam = msg.wParam,
                    lParam = msg.lParam,
                    time = msg.time,
                    pt_x = msg.pt_x,
                    pt_y = msg.pt_y
                };
                if (ContentPreTranslateMessage(ref native)) handled = true;
            } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeMsg {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("Microsoft.UI.Windowing.Core.dll")]
        static extern bool ContentPreTranslateMessage(ref NativeMsg message);
    }

    internal sealed class XamlApp : Application, IXamlMetadataProvider {
        readonly List<IXamlMetadataProvider> _providers = new List<IXamlMetadataProvider>();
        readonly WindowsXamlManager _manager;

        public XamlApp() {
            AddProvider(new XamlControlsXamlMetaDataProvider());
            _manager = WindowsXamlManager.InitializeForCurrentThread();
            try {
                Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
            } catch (Exception ex) {
                Log.Write("XamlControlsResources 加载失败: " + ex.Message);
            }
        }

        public void AddProvider(IXamlMetadataProvider provider) {
            _providers.Add(provider);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args) { }

        IXamlType IXamlMetadataProvider.GetXamlType(string fullName) {
            foreach (var provider in _providers) {
                var type = provider.GetXamlType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        IXamlType IXamlMetadataProvider.GetXamlType(Type type) {
            foreach (var provider in _providers) {
                var xamlType = provider.GetXamlType(type);
                if (xamlType != null) return xamlType;
            }
            return null;
        }

        XmlnsDefinition[] IXamlMetadataProvider.GetXmlnsDefinitions() {
            var definitions = new List<XmlnsDefinition>();
            foreach (var provider in _providers) definitions.AddRange(provider.GetXmlnsDefinitions());
            return definitions.ToArray();
        }
    }
}

