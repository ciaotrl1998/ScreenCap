using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenCap.Core;

namespace ScreenCap.Capture
{
    public partial class RegionFrameWindow : Window
    {
        private const double MarginDip = 6;
        private const double BorderDip = 2;
        private const double HitDip = 8;
        private const int MinSizePx = 32;

        private static readonly System.Windows.Media.Brush ReadyBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0x9B, 0xFF)));
        private static readonly System.Windows.Media.Brush RecordingBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x34, 0x4A)));

        private Rectangle _region;
        private MonitorInfo _monitor;
        private double _scale = 1.0;
        private bool _recording;
        private bool _resizing;
        private bool _windowPositioned;
        private HwndSource _source;
        private Edge _edge;
        private System.Windows.Point _dragStartScreen;
        private Rectangle _dragStartRegion;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        [Flags]
        private enum Edge
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8
        }

        public event Action<Rectangle> RegionChanged;

        public RegionFrameWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                try
                {
                    ScreenRecorderLib.Recorder.SetExcludeFromCapture(hwnd, true);
                }
                catch
                {
                }

                _source = HwndSource.FromHwnd(hwnd);
                _source?.AddHook(WndProc);
            };
            Closed += (s, e) =>
            {
                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source = null;
                }
            };
            DpiChanged += (s, e) =>
            {
                _scale = Math.Max(1.0, e.NewDpi.DpiScaleX);
                ApplyGeometry();
            };
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        public void ShowFor(Rectangle region, MonitorInfo monitor)
        {
            _region = region;
            _monitor = monitor;
            _scale = Math.Max(1.0, monitor.DpiPercent / 100.0);
            _windowPositioned = false;
            Show();
            ApplyGeometry();
            Opacity = 1;
        }

        public void UpdateRegion(Rectangle region)
        {
            _region = region;
            ApplyGeometry();
        }

        public void SetRecording(bool recording)
        {
            _recording = recording;
            Frame.BorderBrush = recording ? RecordingBrush : ReadyBrush;

            if (recording)
            {
                Shade.Visibility = Visibility.Collapsed;
                Background = System.Windows.Media.Brushes.Transparent;
                IsHitTestVisible = false;
                Cursor = Cursors.Arrow;
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    Native.MakeOverlay(hwnd, true);
                }
                catch
                {
                }
            }
        }

        private void ApplyGeometry()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || _region.IsEmpty || _monitor == null)
            {
                return;
            }

            if (!_windowPositioned)
            {
                Native.SetWindowRect(
                    hwnd,
                    _monitor.Bounds.X,
                    _monitor.Bounds.Y,
                    _monitor.Bounds.Width,
                    _monitor.Bounds.Height);
                _windowPositioned = true;
            }

            double localX = (_region.X - _monitor.Bounds.X) / _scale;
            double localY = (_region.Y - _monitor.Bounds.Y) / _scale;
            double width = _region.Width / _scale;
            double height = _region.Height / _scale;

            Canvas.SetLeft(Frame, localX - BorderDip);
            Canvas.SetTop(Frame, localY - BorderDip);
            Frame.Width = width + BorderDip * 2;
            Frame.Height = height + BorderDip * 2;
            Frame.BorderThickness = new Thickness(BorderDip);

            Canvas.SetLeft(Shade, localX - MarginDip);
            Canvas.SetTop(Shade, localY - MarginDip);
            Shade.Width = width + MarginDip * 2;
            Shade.Height = height + MarginDip * 2;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST)
            {
                return IntPtr.Zero;
            }

            handled = true;

            if (_recording || _region.IsEmpty)
            {
                return new IntPtr(HTTRANSPARENT);
            }

            int packed = lParam.ToInt32();
            int x = (short)(packed & 0xFFFF);
            int y = (short)((packed >> 16) & 0xFFFF);
            var screen = new System.Drawing.Point(x, y);

            if (GetEdge(screen) != Edge.None || _region.Contains(screen))
            {
                return new IntPtr(HTCLIENT);
            }

            return new IntPtr(HTTRANSPARENT);
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_recording)
            {
                return;
            }

            var cursor = Native.GetCursorPosition();
            _edge = GetEdge(cursor);
            if (_edge == Edge.None)
            {
                return;
            }

            _resizing = true;
            _dragStartScreen = new System.Windows.Point(cursor.X, cursor.Y);
            _dragStartRegion = _region;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_recording)
            {
                return;
            }

            var cursor = Native.GetCursorPosition();
            if (_resizing)
            {
                ResizeTo(cursor);
                e.Handled = true;
                return;
            }

            Cursor = CursorFor(GetEdge(cursor));
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_resizing)
            {
                return;
            }

            _resizing = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        private Edge GetEdge(System.Drawing.Point screen)
        {
            double t = HitDip * _scale;
            bool left = Math.Abs(screen.X - _region.Left) <= t;
            bool right = Math.Abs(screen.X - _region.Right) <= t;
            bool top = Math.Abs(screen.Y - _region.Top) <= t;
            bool bottom = Math.Abs(screen.Y - _region.Bottom) <= t;

            Edge edge = Edge.None;
            if (left && top) edge = Edge.Left | Edge.Top;
            else if (right && top) edge = Edge.Right | Edge.Top;
            else if (left && bottom) edge = Edge.Left | Edge.Bottom;
            else if (right && bottom) edge = Edge.Right | Edge.Bottom;
            else if (left) edge = Edge.Left;
            else if (right) edge = Edge.Right;
            else if (top) edge = Edge.Top;
            else if (bottom) edge = Edge.Bottom;
            return edge;
        }

        private static Cursor CursorFor(Edge edge)
        {
            switch (edge)
            {
                case Edge.Left:
                case Edge.Right:
                    return Cursors.SizeWE;
                case Edge.Top:
                case Edge.Bottom:
                    return Cursors.SizeNS;
                case Edge.Left | Edge.Top:
                case Edge.Right | Edge.Bottom:
                    return Cursors.SizeNWSE;
                case Edge.Right | Edge.Top:
                case Edge.Left | Edge.Bottom:
                    return Cursors.SizeNESW;
                default:
                    return Cursors.Arrow;
            }
        }

        private void ResizeTo(System.Drawing.Point cursor)
        {
            int dx = cursor.X - (int)_dragStartScreen.X;
            int dy = cursor.Y - (int)_dragStartScreen.Y;

            int left = _dragStartRegion.Left;
            int top = _dragStartRegion.Top;
            int right = _dragStartRegion.Right;
            int bottom = _dragStartRegion.Bottom;

            if ((_edge & Edge.Left) != 0)
            {
                left = Clamp(left + dx, _monitor.Bounds.Left, right - MinSizePx);
            }
            if ((_edge & Edge.Right) != 0)
            {
                right = Clamp(right + dx, left + MinSizePx, _monitor.Bounds.Right);
            }
            if ((_edge & Edge.Top) != 0)
            {
                top = Clamp(top + dy, _monitor.Bounds.Top, bottom - MinSizePx);
            }
            if ((_edge & Edge.Bottom) != 0)
            {
                bottom = Clamp(bottom + dy, top + MinSizePx, _monitor.Bounds.Bottom);
            }

            var region = Rectangle.FromLTRB(left, top, right, bottom);
            if (region == _region)
            {
                return;
            }

            _region = region;
            ApplyGeometry();
            RegionChanged?.Invoke(_region);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static System.Windows.Media.Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
