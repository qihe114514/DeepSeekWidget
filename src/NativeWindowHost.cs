using System;
using System.Runtime.InteropServices;

namespace DeepSeekWidget {

    internal sealed class NativeWindowHost : IDisposable {

        delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        readonly string _className;
        readonly WndProcDelegate _proc;
        readonly Action<uint, IntPtr, IntPtr> _onMessage;

        public IntPtr Handle { get; private set; }

        public NativeWindowHost(string title, uint style, uint extendedStyle,
            int width, int height, Action<uint, IntPtr, IntPtr> onMessage) {
            _className = "DeepSeekWidget.Native." + Guid.NewGuid().ToString("N");
            _onMessage = onMessage;
            _proc = WndProc;

            var wc = new WNDCLASSEX {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = GetModuleHandle(null),
                lpszClassName = _className
            };
            if (RegisterClassEx(ref wc) == 0) {
                throw new InvalidOperationException("RegisterClassEx 失败: " + Marshal.GetLastWin32Error());
            }

            Handle = CreateWindowEx(extendedStyle, _className, title, style,
                0, 0, width, height, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (Handle == IntPtr.Zero) {
                throw new InvalidOperationException("CreateWindowEx 失败: " + Marshal.GetLastWin32Error());
            }
        }

        IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam) {
            try {
                if (_onMessage != null) _onMessage(message, wParam, lParam);
            } catch (Exception ex) {
                Log.Write("原生窗口消息异常: " + ex.Message);
            }
            return DefWindowProc(hWnd, message, wParam, lParam);
        }

        public void ShowNoActivate() {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
        }

        public void Hide() {
            ShowWindow(Handle, SW_HIDE);
        }

        public void Dispose() {
            if (Handle != IntPtr.Zero) {
                DestroyWindow(Handle);
                Handle = IntPtr.Zero;
            }
            try { UnregisterClass(_className, GetModuleHandle(null)); } catch { }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        const int SW_SHOWNOACTIVATE = 4;
        const int SW_HIDE = 0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool UnregisterClass(string className, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int width, int height,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string moduleName);
    }
}
