using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenCap.UI
{
    public sealed class TimelineControl : FrameworkElement
    {
        private const double SidePad = 12;
        private const double TrackHeight = 24;
        private const double HandleWidth = 9;
        private const double MinGap = 0.08;
        private const double HandleHit = 9;

        private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12)));
        private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x28, 0x30)));
        private static readonly Brush RangeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x3D, 0xE5, 0x39, 0x4A)));
        private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00)));
        private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x4A)));
        private static readonly Brush PlayheadBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE9, 0xEE)));
        private static readonly Brush GripBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        private static readonly Pen GripPen = Freeze(new Pen(GripBrush, 2));

        private double _duration = 1;
        private double _inPoint;
        private double _outPoint = 1;
        private double _position;
        private DragMode _drag;

        private enum DragMode
        {
            None,
            In,
            Out,
            Playhead
        }

        public event Action RangeChanged;
        public event Action<double> SeekRequested;

        public TimelineControl()
        {
            Focusable = false;
            SnapsToDevicePixels = true;
        }

        public double Duration
        {
            get => _duration;
            set
            {
                _duration = Math.Max(0.01, value);
                _outPoint = Math.Min(_outPoint, _duration);
                _inPoint = Math.Min(_inPoint, Math.Max(0, _outPoint - MinGap));
                _position = Math.Max(_inPoint, Math.Min(_position, _outPoint));
                InvalidateVisual();
            }
        }

        public double InPoint
        {
            get => _inPoint;
            set
            {
                double v = Math.Max(0, Math.Min(value, _outPoint - MinGap));
                if (Math.Abs(v - _inPoint) < 0.0001) return;
                _inPoint = v;
                _position = Math.Max(_position, _inPoint);
                InvalidateVisual();
            }
        }

        public double OutPoint
        {
            get => _outPoint;
            set
            {
                double v = Math.Min(_duration, Math.Max(value, _inPoint + MinGap));
                if (Math.Abs(v - _outPoint) < 0.0001) return;
                _outPoint = v;
                _position = Math.Min(_position, _outPoint);
                InvalidateVisual();
            }
        }

        public double Position
        {
            get => _position;
            set
            {
                double v = Math.Max(_inPoint, Math.Min(value, _outPoint));
                if (Math.Abs(v - _position) < 0.0001) return;
                _position = v;
                InvalidateVisual();
            }
        }

        public void ResetRange(double duration)
        {
            _duration = Math.Max(0.01, duration);
            _inPoint = 0;
            _outPoint = _duration;
            _position = 0;
            InvalidateVisual();
        }

        private double TrackLeft => SidePad;
        private double TrackRight => Math.Max(SidePad + 1, ActualWidth - SidePad);
        private double TrackWidth => Math.Max(1, TrackRight - TrackLeft);

        private double TimeToX(double seconds)
        {
            double ratio = Math.Max(0, Math.Min(1, seconds / _duration));
            return TrackLeft + ratio * TrackWidth;
        }

        private double XToTime(double x)
        {
            double ratio = Math.Max(0, Math.Min(1, (x - TrackLeft) / TrackWidth));
            return ratio * _duration;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 || h <= 2)
            {
                return;
            }

            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            dc.DrawRoundedRectangle(PanelBrush, null, new Rect(0, 0, w, h), 6, 6);

            double trackY = (h - TrackHeight) / 2;
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(TrackLeft, trackY, TrackWidth, TrackHeight), 4, 4);

            double xIn = TimeToX(_inPoint);
            double xOut = TimeToX(_outPoint);
            double xPos = TimeToX(_position);

            dc.PushClip(new RectangleGeometry(new Rect(TrackLeft, trackY, TrackWidth, TrackHeight)));
            dc.DrawRectangle(DimBrush, null, new Rect(TrackLeft, trackY, Math.Max(0, xIn - TrackLeft), TrackHeight));
            dc.DrawRectangle(DimBrush, null, new Rect(xOut, trackY, Math.Max(0, TrackRight - xOut), TrackHeight));
            dc.DrawRectangle(RangeBrush, null, new Rect(xIn, trackY, Math.Max(0, xOut - xIn), TrackHeight));
            dc.Pop();

            DrawHandle(dc, xIn, h);
            DrawHandle(dc, xOut, h);

            dc.DrawRectangle(PlayheadBrush, null, new Rect(xPos - 1, trackY - 5, 2, TrackHeight + 10));
            dc.DrawRoundedRectangle(PlayheadBrush, null, new Rect(xPos - 4.5, h / 2 - 9, 9, 18), 2, 2);
            dc.DrawLine(GripPen, new Point(xPos, h / 2 - 4), new Point(xPos, h / 2 + 4));
        }

        private static void DrawHandle(DrawingContext dc, double x, double height)
        {
            var rect = new Rect(x - HandleWidth / 2, 4, HandleWidth, Math.Max(6, height - 8));
            dc.DrawRoundedRectangle(AccentBrush, null, rect, 2, 2);
            dc.DrawLine(GripPen, new Point(x, height / 2 - 6), new Point(x, height / 2 + 6));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_duration <= 0)
            {
                return;
            }

            var p = e.GetPosition(this);
            double dIn = Math.Abs(p.X - TimeToX(_inPoint));
            double dOut = Math.Abs(p.X - TimeToX(_outPoint));
            double dPos = Math.Abs(p.X - TimeToX(_position));
            double t = XToTime(p.X);

            if (dIn <= HandleHit && dIn <= dOut)
            {
                _drag = DragMode.In;
            }
            else if (dOut <= HandleHit)
            {
                _drag = DragMode.Out;
            }
            else
            {
                _drag = DragMode.Playhead;
                SetPlayheadFromX(p.X);
                SeekRequested?.Invoke(_position);
            }

            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag == DragMode.None || !IsMouseCaptured)
            {
                return;
            }

            var p = e.GetPosition(this);
            switch (_drag)
            {
                case DragMode.In:
                    _inPoint = Math.Max(0, Math.Min(XToTime(p.X), _outPoint - MinGap));
                    if (_position < _inPoint)
                    {
                        _position = _inPoint;
                    }
                    InvalidateVisual();
                    RangeChanged?.Invoke();
                    break;
                case DragMode.Out:
                    _outPoint = Math.Min(_duration, Math.Max(XToTime(p.X), _inPoint + MinGap));
                    if (_position > _outPoint)
                    {
                        _position = _outPoint;
                    }
                    InvalidateVisual();
                    RangeChanged?.Invoke();
                    break;
                case DragMode.Playhead:
                    SetPlayheadFromX(p.X);
                    SeekRequested?.Invoke(_position);
                    break;
            }
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drag == DragMode.None)
            {
                return;
            }

            var mode = _drag;
            _drag = DragMode.None;
            ReleaseMouseCapture();
            e.Handled = true;

            // 拖动入/出点时不移动当前帧，除非当前帧被区间挤出。
        }

        private void SetPlayheadFromX(double x)
        {
            _position = Math.Max(_inPoint, Math.Min(XToTime(x), _outPoint));
            InvalidateVisual();
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Pen Freeze(Pen pen)
        {
            pen.Freeze();
            return pen;
        }
    }
}
