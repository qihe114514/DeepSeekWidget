using System;
using System.Runtime.InteropServices;

namespace DeepSeekWidget {

    internal static class Win32 {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_ENTERSIZEMOVE = 0x0231;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int HTCAPTION = 2;
        public const int HTCLIENT = 1;
        public const int HTTRANSPARENT = -1;
        public const int MA_NOACTIVATE = 3;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr AddDllDirectory(string NewDirectory);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
