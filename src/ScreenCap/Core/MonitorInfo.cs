using System;
using System.Drawing;

namespace ScreenCap.Core
{
    public sealed class MonitorInfo
    {
        public IntPtr Handle { get; set; }
        public string DeviceName { get; set; }
        public Rectangle Bounds { get; set; }
        public Rectangle WorkArea { get; set; }
        public bool IsPrimary { get; set; }
        public int DpiPercent { get; set; }

        public static MonitorInfo FromPoint(System.Collections.Generic.IList<MonitorInfo> monitors, Point point)
        {
            MonitorInfo best = null;
            long bestArea = -1;
            foreach (var monitor in monitors)
            {
                var r = monitor.Bounds;
                int x = Math.Max(r.Left, Math.Min(point.X, r.Right));
                int y = Math.Max(r.Top, Math.Min(point.Y, r.Bottom));
                long area = (long)(x - r.Left) * (y - r.Top);
                if (r.Contains(point) && area > bestArea)
                {
                    best = monitor;
                    bestArea = area;
                }
            }
            return best ?? (monitors.Count > 0 ? monitors[0] : null);
        }
    }
}
