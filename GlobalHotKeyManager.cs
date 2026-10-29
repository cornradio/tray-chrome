using System;
using System.Collections.Generic;
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
            helper.EnsureHandle();
            return RegisterHotKeyInternal(modifiers, virtualKey);
        }

        private bool RegisterHotKeyInternal(uint modifiers, uint virtualKey)
        {
            var helper = new WindowInteropHelper(_window);
            var handle = helper.Handle;
            
            if (handle == IntPtr.Zero)
                return false;

            // 注册热键
            bool success = RegisterHotKey(handle, _hotKeyId, modifiers | 0x4000, virtualKey);
            
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

        public static string ParseHotkey(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            var names = new List<string>();
            string[] parts = text.Split('+');
            if (parts.Length < 2) throw new ArgumentException("快捷键需要至少一个修饰键和一个按键，例如 Ctrl + Alt + 1。");
            for (int i = 0; i < parts.Length - 1; i++)
            {
                uint modifier;
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": modifier = 2; break;
                    case "alt": modifier = 1; break;
                    case "shift": modifier = 4; break;
                    case "win": case "windows": modifier = 8; break;
                    default: throw new ArgumentException("修饰键仅支持 Ctrl、Alt、Shift、Win。");
                }
                if ((modifiers & modifier) != 0) throw new ArgumentException("快捷键不能重复使用同一修饰键。");
                modifiers |= modifier;
            }
            string key = parts[parts.Length - 1].Trim().ToUpperInvariant();
            if (key.Length == 1 && ((key[0] >= 'A' && key[0] <= 'Z') || (key[0] >= '0' && key[0] <= '9')))
                virtualKey = key[0];
            else if (key.StartsWith("F", StringComparison.Ordinal)
                && int.TryParse(key.Substring(1), out int number) && number >= 1 && number <= 24)
                virtualKey = (uint)(0x70 + number - 1);
            else throw new ArgumentException("按键仅支持 A-Z、0-9 或 F1-F24。");
            // 固定顺序，便于显示和检测重复组合。
            if ((modifiers & 2) != 0) names.Add("Ctrl");
            if ((modifiers & 1) != 0) names.Add("Alt");
            if ((modifiers & 4) != 0) names.Add("Shift");
            if ((modifiers & 8) != 0) names.Add("Win");
            names.Add(key);
            return string.Join(" + ", names);
        }
    }
}