#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayChrome
{
    internal static class WindowZOrder
    {
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x20;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const long WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

        // 判断窗口是否被其他程序的可见窗口遮挡（自身进程的设置窗口、菜单等不算）
        public static bool IsCoveredByOtherWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (GetForegroundWindow() == hwnd) return false;
            if (!TryGetBounds(hwnd, out RECT self)) return false;

            uint selfProcess = (uint)Environment.ProcessId;
            for (IntPtr other = GetWindow(hwnd, GW_HWNDPREV); other != IntPtr.Zero; other = GetWindow(other, GW_HWNDPREV))
            {
                if (!IsWindowVisible(other) || IsIconic(other)) continue;
                GetWindowThreadProcessId(other, out uint processId);
                if (processId == selfProcess) continue;
                if (IsIgnored(other)) continue;
                if (!TryGetBounds(other, out RECT rect)) continue;
                if (rect.Left < self.Right && rect.Right > self.Left && rect.Top < self.Bottom && rect.Bottom > self.Top)
                    return true;
            }
            return false;
        }

        private static bool IsIgnored(IntPtr hwnd)
        {
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & (WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            string className = GetClass(hwnd);
            return className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW";
        }

        private static bool TryGetBounds(IntPtr hwnd, out RECT rect)
        {
            // 扩展边框范围不含 Win10/11 窗口四周不可见的调整大小边框，避免把相邻窗口误判为遮挡
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0
                && !GetWindowRect(hwnd, out rect))
                return false;
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }

        private static string GetClass(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            GetClassName(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }
    }
}
