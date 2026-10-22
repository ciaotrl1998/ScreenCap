using System;
using System.Runtime.InteropServices;

namespace ScreenCap.Core
{
    public sealed class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_ESCAPE = 0x1B;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly HookProc _proc;
        private IntPtr _handle;

        public event Action EscapePressed;

        public KeyboardHook()
        {
            _proc = HookCallback;
            _handle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    int key = Marshal.ReadInt32(lParam);
                    if (key == VK_ESCAPE)
                    {
                        EscapePressed?.Invoke();
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
