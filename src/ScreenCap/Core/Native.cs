using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace ScreenCap.Core
{
    public static class Native
    {
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOZORDER = 0x0004;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static List<MonitorInfo> GetMonitors()
        {
            var result = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    int dpiPercent = 100;
                    try
                    {
                        if (GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                        {
                            dpiPercent = (int)Math.Round(dpiX * 100.0 / 96.0);
                        }
                    }
                    catch
                    {
                    }

                    result.Add(new MonitorInfo
                    {
                        Handle = hMonitor,
                        DeviceName = info.szDevice,
                        Bounds = new System.Drawing.Rectangle(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height),
                        WorkArea = new System.Drawing.Rectangle(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height),
                        IsPrimary = (info.dwFlags & 1) != 0,
                        DpiPercent = dpiPercent
                    });
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static System.Drawing.Point GetCursorPosition()
        {
            GetCursorPos(out POINT p);
            return new System.Drawing.Point(p.X, p.Y);
        }

        public static bool TryGetTopWindowAt(int screenX, int screenY, out System.Drawing.Rectangle bounds, out IntPtr handle, out string title)
        {
            var result = System.Drawing.Rectangle.Empty;
            IntPtr found = IntPtr.Zero;
            string foundTitle = string.Empty;
            uint currentProcessId = GetCurrentProcessId();

            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                        {
                            return true;
                        }

                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (processId == currentProcessId)
                        {
                            return true;
                        }

                        int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && !IsUnityWindow(hWnd))
                        {
                            return true;
                        }

                        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                        {
                            return true;
                        }

                        RECT rect;
                        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT)));
                        if (hr != 0 || rect.Width < 4 || rect.Height < 4)
                        {
                            if (!GetWindowRect(hWnd, out rect) || rect.Width < 4 || rect.Height < 4)
                            {
                                return true;
                            }
                        }

                        if (screenX < rect.Left || screenX >= rect.Right || screenY < rect.Top || screenY >= rect.Bottom)
                        {
                            return true;
                        }

                        result = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                        var text = new StringBuilder(256);
                        GetWindowText(hWnd, text, text.Capacity);
                        foundTitle = IsUnityWindow(hWnd) ? FriendlyPaneTitle(text.ToString()) : text.ToString();
                        found = hWnd;
                        return false;
                    }
                    catch
                    {
                        return true;
                    }
                }, IntPtr.Zero);
            }
            catch
            {
            }

            bounds = result;
            handle = found;
            title = foundTitle;

            if (found != IntPtr.Zero && TryGetUnityPane(found, screenX, screenY, out RECT paneRect, out string paneTitle))
            {
                bounds = new System.Drawing.Rectangle(paneRect.Left, paneRect.Top, paneRect.Width, paneRect.Height);
                title = paneTitle;
            }

            return found != IntPtr.Zero;
        }

        private static bool IsUnityWindow(IntPtr hWnd)
        {
            try
            {
                var className = new StringBuilder(128);
                GetClassName(hWnd, className, className.Capacity);
                if (className.ToString().StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                return text.ToString().StartsWith("UnityEditor.", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetUnityPane(IntPtr topLevelWindow, int screenX, int screenY, out RECT paneRect, out string paneTitle)
        {
            RECT bestRect = default;
            IntPtr bestHandle = IntPtr.Zero;
            long bestArea = long.MaxValue;

            try
            {
                EnumChildWindows(topLevelWindow, (hWnd, lParam) =>
                {
                    try
                    {
                        var className = new StringBuilder(128);
                        GetClassName(hWnd, className, className.Capacity);
                        if (!string.Equals(className.ToString(), "UnityGUIViewWndClass", StringComparison.Ordinal))
                        {
                            return true;
                        }

                        if (!IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out RECT rect))
                        {
                            return true;
                        }

                        if (rect.Width < 4 || rect.Height < 4)
                        {
                            return true;
                        }

                        if (screenX < rect.Left || screenX >= rect.Right || screenY < rect.Top || screenY >= rect.Bottom)
                        {
                            return true;
                        }

                        long area = (long)rect.Width * rect.Height;
                        if (area < bestArea)
                        {
                            bestArea = area;
                            bestRect = rect;
                            bestHandle = hWnd;
                        }
                        return true;
                    }
                    catch
                    {
                        return true;
                    }
                }, IntPtr.Zero);
            }
            catch
            {
            }

            if (bestHandle == IntPtr.Zero)
            {
                paneRect = default;
                paneTitle = null;
                return false;
            }

            var text = new StringBuilder(256);
            GetWindowText(bestHandle, text, text.Capacity);
            paneRect = bestRect;
            paneTitle = FriendlyPaneTitle(text.ToString());
            return true;
        }

        private static string FriendlyPaneTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return title;
            }

            string value = title.Replace("UnityEditor.", "");
            if (value.EndsWith("Window", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 6);
            }
            else if (value.EndsWith("View", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 4);
            }
            return value;
        }

        public static double GetDpiScale(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi > 0)
                    {
                        return dpi / 96.0;
                    }
                }
            }
            catch
            {
            }
            return 1.0;
        }

        public static void PositionWindow(IntPtr hwnd, int x, int y)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void FitWindowToWorkArea(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                RECT rect = GetWindowRectangle(hwnd);
                var windowRect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);

                MonitorInfo best = null;
                long bestArea = 0;
                foreach (var monitor in GetMonitors())
                {
                    var intersection = System.Drawing.Rectangle.Intersect(monitor.WorkArea, windowRect);
                    long area = (long)intersection.Width * intersection.Height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = monitor;
                    }
                }

                if (best == null)
                {
                    return;
                }

                int x = rect.Left;
                int y = rect.Top;
                if (rect.Right > best.WorkArea.Right)
                {
                    x -= rect.Right - best.WorkArea.Right;
                }
                if (rect.Bottom > best.WorkArea.Bottom)
                {
                    y -= rect.Bottom - best.WorkArea.Bottom;
                }
                x = Math.Max(best.WorkArea.Left, x);
                y = Math.Max(best.WorkArea.Top, y);

                if (x != rect.Left || y != rect.Top)
                {
                    PositionWindow(hwnd, x, y);
                }
            }
            catch
            {
            }
        }

        public static void SetWindowRect(IntPtr hwnd, int x, int y, int width, int height)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, Math.Max(1, width), Math.Max(1, height), SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static RECT GetWindowRectangle(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out RECT rect);
            return rect;
        }

        public static void MakeOverlay(IntPtr hwnd, bool clickThrough)
        {
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickThrough)
            {
                exStyle |= WS_EX_TRANSPARENT;
            }
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        public static bool ExcludeFromCapture(IntPtr hwnd)
        {
            try
            {
                return SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRegisterHotkey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
        {
            return RegisterHotKey(hwnd, id, modifiers, virtualKey);
        }

        public static void UnregisterHotkey(IntPtr hwnd, int id)
        {
            try
            {
                UnregisterHotKey(hwnd, id);
            }
            catch
            {
            }
        }
    }
}
