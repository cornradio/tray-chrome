using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrayChrome
{
    public class GlobalHotKeyManager : IDisposable
    {
        // Windows API 常量
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_WIN = 0x0008;

        // Windows API 函数
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Window _window;
        private HwndSource _source;
        private int _hotKeyId = 9000;
        private bool _isRegistered = false;

        public event Action HotKeyPressed;

        public GlobalHotKeyManager(Window window)
        {
            _window = window;
        }

        public bool RegisterHotKey(uint modifiers, uint virtualKey, Action callback)
        {
            HotKeyPressed = callback;
            
            // 获取窗口句柄
            var helper = new WindowInteropHelper(_window);
            var handle = helper.Handle;
            
            if (handle == IntPtr.Zero)
            {
                // 如果窗口还没有句柄，等待窗口加载完成
                _window.SourceInitialized += (s, e) => RegisterHotKeyInternal(modifiers, virtualKey);
                return true;
            }
            
            return RegisterHotKeyInternal(modifiers, virtualKey);
        }

        private bool RegisterHotKeyInternal(uint modifiers, uint virtualKey)
        {
            var helper = new WindowInteropHelper(_window);
            var handle = helper.Handle;
            
            if (handle == IntPtr.Zero)
                return false;

            // 注册热键
            bool success = RegisterHotKey(handle, _hotKeyId, modifiers, virtualKey);
            
            if (success)
            {
                // 添加消息钩子
                _source = HwndSource.FromHwnd(handle);
                _source?.AddHook(HwndHook);
                _isRegistered = true;
            }
            
            return success;
        }

        public void UnregisterHotKey()
        {
            if (!_isRegistered || _window == null)
                return;

            var helper = new WindowInteropHelper(_window);
            var handle = helper.Handle;
            
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, _hotKeyId);
            }
            
            _source?.RemoveHook(HwndHook);
            _isRegistered = false;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hotKeyId)
            {
                HotKeyPressed?.Invoke();
                handled = true;
            }
            
            return IntPtr.Zero;
        }

        // 修饰键常量
        public static class Modifiers
        {
            public const uint Alt = MOD_ALT;
            public const uint Control = MOD_CONTROL;
            public const uint Shift = MOD_SHIFT;
            public const uint Win = MOD_WIN;
        }

        // 虚拟键码常量
        public static class VirtualKeys
        {
            public const uint X = 0x58;
            public const uint A = 0x41;
            public const uint B = 0x42;
            public const uint C = 0x43;
            public const uint D = 0x44;
            public const uint E = 0x45;
            public const uint F = 0x46;
            public const uint G = 0x47;
            public const uint H = 0x48;
            public const uint I = 0x49;
            public const uint J = 0x4A;
            public const uint K = 0x4B;
            public const uint L = 0x4C;
            public const uint M = 0x4D;
            public const uint N = 0x4E;
            public const uint O = 0x4F;
            public const uint P = 0x50;
            public const uint Q = 0x51;
            public const uint R = 0x52;
            public const uint S = 0x53;
            public const uint T = 0x54;
            public const uint U = 0x55;
            public const uint V = 0x56;
            public const uint W = 0x57;
            public const uint Y = 0x59;
            public const uint Z = 0x5A;
        }

        public void Dispose()
        {
            UnregisterHotKey();
        }
    }
}