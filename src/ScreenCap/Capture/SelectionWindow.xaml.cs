using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Core;

namespace ScreenCap.Capture
{
    public partial class SelectionWindow : Window
    {
        private readonly MonitorInfo _monitor;
        private double _scale = 1.0;
        private bool _dragging;
        private System.Windows.Point _start;
        private System.Windows.Point _current;
        private bool _completed;
        private bool _dragStarted;
        private Rect _hoverRect = Rect.Empty;
        private Rect _hoverAtMouseDown = Rect.Empty;
        private System.Windows.Point _mouseDownPoint;
        private IntPtr _hoverHandle;
        private string _hoverTitle;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public event Action<Rectangle> Selected;
        public event Action Cancelled;

        public SelectionWindow(MonitorInfo monitor)
        {
            _monitor = monitor;
            InitializeComponent();

            CaptureBackdrop();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            DpiChanged += (s, e) =>
            {
                _scale = Math.Max(1.0, e.NewDpi.DpiScaleX);
                ApplyScale();
                ApplyPhysicalRect();
            };
            PreviewKeyDown += OnPreviewKeyDown;
            MouseRightButtonDown += (s, e) => { e.Handled = true; Cancel(); };

            new WindowInteropHelper(this).EnsureHandle();
            ApplyPhysicalRect();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            ApplyPhysicalRect();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scale = Math.Max(1.0, _monitor.DpiPercent / 100.0);
            ApplyPhysicalRect();
            ApplyScale();
            ResetSelection();
            Opacity = 1;
            Activate();
            Focus();
            UpdateHover();
        }

        private void CaptureBackdrop()
        {
            try
            {
                var bounds = _monitor.Bounds;
                using (var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(
                            bounds.Left,
                            bounds.Top,
                            0,
                            0,
                            new System.Drawing.Size(bounds.Width, bounds.Height),
                            System.Drawing.CopyPixelOperation.SourceCopy);
                    }

                    IntPtr hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        Backdrop.Source = source;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("截取框选背景失败", ex);
            }
        }

        private void ApplyPhysicalRect()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            Native.SetWindowRect(hwnd, _monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height);
        }

        private void ApplyScale()
        {
            double w = _monitor.Bounds.Width;
            double h = _monitor.Bounds.Height;
            Root.Width = w;
            Root.Height = h;
            Root.LayoutTransform = new ScaleTransform(1.0 / _scale, 1.0 / _scale);
            SelectionBorder.StrokeThickness = 2.0 * _scale;
            SizeBadge.CornerRadius = new CornerRadius(3 * _scale);
            SizeBadge.Padding = new Thickness(8 * _scale, 3 * _scale, 8 * _scale, 3 * _scale);
            SizeText.FontSize = 12 * _scale;
            UpdateShade(_hoverRect.IsEmpty ? GetSelectionRect() : _hoverRect, _hoverRect.IsEmpty ? null : _hoverTitle);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Cancel();
            }
        }

        public bool ContainsScreen(System.Drawing.Point screen)
        {
            return _monitor.Bounds.Contains(screen);
        }

        public System.Drawing.Rectangle MonitorBounds => _monitor.Bounds;

        public void ClearHoverExternal()
        {
            ClearHover();
        }

        public void ExternalBeginDrag(System.Drawing.Point screen)
        {
            if (_completed || _dragging)
            {
                return;
            }

            if (_hoverRect.IsEmpty)
            {
                UpdateHover();
            }

            _mouseDownPoint = ToLocalPoint(screen);
            _start = _mouseDownPoint;
            _current = _start;
            _hoverAtMouseDown = _hoverRect;
            _dragging = true;
            _dragStarted = false;
            HintBadge.Visibility = Visibility.Collapsed;
        }

        public void ExternalMoveDrag(System.Drawing.Point screen)
        {
            if (_completed || !_dragging)
            {
                return;
            }

            var point = ToLocalPoint(screen);
            if (!_dragStarted)
            {
                double moved = Math.Abs(point.X - _mouseDownPoint.X) + Math.Abs(point.Y - _mouseDownPoint.Y);
                if (moved > 4)
                {
                    _dragStarted = true;
                    _hoverRect = Rect.Empty;
                }
                else
                {
                    return;
                }
            }

            _current = point;
            UpdateShade(GetSelectionRect());
        }

        public void ExternalEndDrag(System.Drawing.Point screen)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (_dragStarted)
            {
                _current = ToLocalPoint(screen);
            }

            Rect? result = null;
            if (_dragStarted)
            {
                var rect = GetSelectionRect();
                if (rect.Width >= 16 && rect.Height >= 16)
                {
                    result = rect;
                }
            }
            else if (_hoverAtMouseDown.Width >= 4 && _hoverAtMouseDown.Height >= 4)
            {
                result = _hoverAtMouseDown;
            }

            if (result.HasValue)
            {
                Complete(result.Value);
            }
            else
            {
                ResetSelection();
                UpdateHover();
            }
        }

        private System.Windows.Point ToLocalPoint(System.Drawing.Point screen)
        {
            double x = screen.X - _monitor.Bounds.X;
            double y = screen.Y - _monitor.Bounds.Y;
            return new System.Windows.Point(
                Math.Max(0, Math.Min(_monitor.Bounds.Width, x)),
                Math.Max(0, Math.Min(_monitor.Bounds.Height, y)));
        }

        public void UpdateHover()
        {
            if (_completed || _dragging)
            {
                return;
            }

            try
            {
                var cursor = Native.GetCursorPosition();
                var last = CaptureHistory.LastRecordedRegion;
                if (last.HasValue && last.Value.Contains(cursor))
                {
                    int historyLocalX = last.Value.X - _monitor.Bounds.X;
                    int historyLocalY = last.Value.Y - _monitor.Bounds.Y;
                    double lx0 = Math.Max(0, historyLocalX);
                    double ly0 = Math.Max(0, historyLocalY);
                    double lx1 = Math.Min(_monitor.Bounds.Width, historyLocalX + last.Value.Width);
                    double ly1 = Math.Min(_monitor.Bounds.Height, historyLocalY + last.Value.Height);
                    if (lx1 - lx0 >= 4 && ly1 - ly0 >= 4)
                    {
                        var lastRect = new Rect(lx0, ly0, lx1 - lx0, ly1 - ly0);
                        if (lastRect != _hoverRect || !string.Equals(_hoverTitle, "上次录制区域", StringComparison.Ordinal))
                        {
                            _hoverRect = lastRect;
                            _hoverHandle = IntPtr.Zero;
                            _hoverTitle = "上次录制区域";
                            UpdateShade(lastRect, _hoverTitle);
                        }
                        return;
                    }
                }

                if (!Native.TryGetTopWindowAt(cursor.X, cursor.Y, out var bounds, out var handle, out var title))
                {
                    ClearHover();
                    return;
                }

                int localX = bounds.X - _monitor.Bounds.X;
                int localY = bounds.Y - _monitor.Bounds.Y;
                double x0 = Math.Max(0, localX);
                double y0 = Math.Max(0, localY);
                double x1 = Math.Min(_monitor.Bounds.Width, localX + bounds.Width);
                double y1 = Math.Min(_monitor.Bounds.Height, localY + bounds.Height);
                if (x1 - x0 < 4 || y1 - y0 < 4)
                {
                    ClearHover();
                    return;
                }

                var rect = new Rect(x0, y0, x1 - x0, y1 - y0);
                if (rect == _hoverRect && string.Equals(title, _hoverTitle, StringComparison.Ordinal))
                {
                    return;
                }

                _hoverRect = rect;
                _hoverHandle = handle;
                _hoverTitle = title;
                UpdateShade(rect, title);
            }
            catch
            {
                ClearHover();
            }
        }

        private void ClearHover()
        {
            _hoverRect = Rect.Empty;
            _hoverHandle = IntPtr.Zero;
            _hoverTitle = null;
            if (_dragging)
            {
                return;
            }
            ResetSelection();
        }

        private void Complete(Rect selection)
        {
            if (_completed)
            {
                return;
            }
            _completed = true;
            int x = _monitor.Bounds.X + (int)Math.Round(selection.X);
            int y = _monitor.Bounds.Y + (int)Math.Round(selection.Y);
            int w = (int)Math.Round(selection.Width);
            int h = (int)Math.Round(selection.Height);
            w -= w % 2;
            h -= h % 2;
            x = Math.Max(_monitor.Bounds.Left, Math.Min(x, _monitor.Bounds.Right - w));
            y = Math.Max(_monitor.Bounds.Top, Math.Min(y, _monitor.Bounds.Bottom - h));
            Selected?.Invoke(new Rectangle(x, y, Math.Max(2, w), Math.Max(2, h)));
        }

        private void Cancel()
        {
            if (_completed)
            {
                return;
            }
            _completed = true;
            Cancelled?.Invoke();
        }

        private System.Windows.Point ClampToMonitor(System.Windows.Point point)
        {
            double w = _monitor.Bounds.Width;
            double h = _monitor.Bounds.Height;
            return new System.Windows.Point(
                Math.Max(0, Math.Min(w, point.X)),
                Math.Max(0, Math.Min(h, point.Y)));
        }

        private Rect GetSelectionRect()
        {
            double x = Math.Min(_start.X, _current.X);
            double y = Math.Min(_start.Y, _current.Y);
            double w = Math.Abs(_current.X - _start.X);
            double h = Math.Abs(_current.Y - _start.Y);
            return new Rect(x, y, w, h);
        }

        private void ResetSelection()
        {
            _hoverRect = Rect.Empty;
            _hoverHandle = IntPtr.Zero;
            _hoverTitle = null;
            _start = new System.Windows.Point(-1, -1);
            _current = _start;
            UpdateShade(new Rect(0, 0, 0, 0));
            HintBadge.Visibility = Visibility.Visible;
            SizeBadge.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
        }

        private void UpdateShade(Rect selection, string title = null)
        {
            double w = _monitor.Bounds.Width;
            double h = _monitor.Bounds.Height;

            if (selection.Width < 1 || selection.Height < 1)
            {
                SetRect(ShadeTop, 0, 0, w, h);
                SetRect(ShadeBottom, 0, 0, 0, 0);
                SetRect(ShadeLeft, 0, 0, 0, 0);
                SetRect(ShadeRight, 0, 0, 0, 0);
                SelectionBorder.Visibility = Visibility.Collapsed;
                SizeBadge.Visibility = Visibility.Collapsed;
                return;
            }

            double x0 = Math.Floor(selection.X);
            double y0 = Math.Floor(selection.Y);
            double x1 = Math.Ceiling(selection.Right);
            double y1 = Math.Ceiling(selection.Bottom);

            SetRect(ShadeTop, 0, 0, w, y0);
            SetRect(ShadeBottom, 0, y1, w, Math.Max(0, h - y1));
            SetRect(ShadeLeft, 0, y0, x0, Math.Max(0, y1 - y0));
            SetRect(ShadeRight, x1, y0, Math.Max(0, w - x1), Math.Max(0, y1 - y0));

            Canvas.SetLeft(SelectionBorder, x0);
            Canvas.SetTop(SelectionBorder, y0);
            SelectionBorder.Width = Math.Max(1, x1 - x0);
            SelectionBorder.Height = Math.Max(1, y1 - y0);
            SelectionBorder.Visibility = Visibility.Visible;

            string size = $"{(int)(x1 - x0)} × {(int)(y1 - y0)}";
            SizeText.Text = string.IsNullOrWhiteSpace(title)
                ? size
                : Truncate(title, 40) + "   " + size;
            SizeBadge.Visibility = Visibility.Visible;
            SizeBadge.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = SizeBadge.DesiredSize.Width;
            double bh = SizeBadge.DesiredSize.Height;

            double bx = x1 + 7 * _scale;
            if (bx + bw > w)
            {
                bx = x0 - bw - 7 * _scale;
            }
            bx = Math.Max(0, bx);

            double by = y1 + 7 * _scale;
            if (by + bh > h)
            {
                by = y1 - bh - 7 * _scale;
            }
            by = Math.Max(0, by);

            Canvas.SetLeft(SizeBadge, bx);
            Canvas.SetTop(SizeBadge, by);
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }
            return text.Substring(0, maxLength - 1) + "…";
        }

        private static void SetRect(FrameworkElement element, double x, double y, double w, double h)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            element.Width = Math.Max(0, w);
            element.Height = Math.Max(0, h);
        }
    }
}
