using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ScreenCap.Core
{
    public sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private readonly HookProc _proc;
        private IntPtr _handle;

        public event Action<Point> Move;
        public event Action<Point> LeftDown;
        public event Action<Point> LeftUp;
        public event Action RightDown;

        public MouseHook()
        {
            _proc = HookCallback;
            _handle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var point = new Point(data.pt.X, data.pt.Y);
                int message = wParam.ToInt32();
                bool swallow = false;

                switch (message)
                {
                    case WM_MOUSEMOVE:
                        Move?.Invoke(point);
                        break;
                    case WM_LBUTTONDOWN:
                        LeftDown?.Invoke(point);
                        swallow = true;
                        break;
                    case WM_LBUTTONUP:
                        LeftUp?.Invoke(point);
                        swallow = true;
                        break;
                    case WM_RBUTTONDOWN:
                        RightDown?.Invoke();
                        swallow = true;
                        break;
                }

                return swallow ? new IntPtr(1) : CallNextHookEx(_handle, nCode, wParam, lParam);
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
