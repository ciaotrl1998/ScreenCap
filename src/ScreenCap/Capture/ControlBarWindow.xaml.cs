using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScreenCap.Core;

namespace ScreenCap.Capture
{
    public partial class ControlBarWindow : Window
    {
        private const double SegmentWidth = 52;

        private static readonly Brush ActiveTextBrush = CreateBrush(Colors.White);
        private static readonly Brush InactiveTextBrush = CreateBrush(Color.FromRgb(0xB9, 0xBB, 0xC4));

        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _topmostTimer;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private System.Drawing.Rectangle _region;
        private MonitorInfo _monitor;
        private bool _avoidRegion;

        public event Action StartRequested;
        public event Action StopRequested;
        public event Action CancelRequested;

        public bool IsGif { get; private set; }
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public ControlBarWindow()
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
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => TimerText.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _topmostTimer.Tick += (s, e) => BringToTop();
            Closed += (s, e) =>
            {
                _timer.Stop();
                _topmostTimer.Stop();
            };
        }

        public void ShowReady(System.Drawing.Rectangle region, MonitorInfo monitor, bool isGif)
        {
            _topmostTimer.Start();
            _region = region;
            _monitor = monitor;
            _avoidRegion = false;
            SetFormat(isGif, false);
            ReadyPanel.Visibility = Visibility.Visible;
            RecordingPanel.Visibility = Visibility.Collapsed;
            Opacity = 0;
            Show();
            UpdateLayout();
            Place();
            Activate();
        }

        public void UpdateRegion(System.Drawing.Rectangle region)
        {
            _region = region;
            UpdateLayout();
            Place();
        }

        public void ShowRecording()
        {
            _topmostTimer.Start();
            _avoidRegion = true;
            ReadyPanel.Visibility = Visibility.Collapsed;
            RecordingPanel.Visibility = Visibility.Visible;
            StopButton.IsEnabled = true;
            DiscardButton.IsEnabled = true;
            TimerText.Text = "00:00:00";
            _stopwatch.Restart();
            _timer.Start();
            UpdateLayout();
            Place();
        }

        public void ShowSaving()
        {
            _avoidRegion = true;
            _timer.Stop();
            StopButton.IsEnabled = false;
            DiscardButton.IsEnabled = false;
            TimerText.Text = "保存中…";
            UpdateLayout();
            Place();
        }

        public void ShowFailed(string message)
        {
            _timer.Stop();
            StopButton.IsEnabled = false;
            DiscardButton.IsEnabled = false;
            TimerText.Text = string.IsNullOrEmpty(message) ? "失败" : message;
            UpdateLayout();
            Place();
        }

        private void SetFormat(bool isGif, bool animate)
        {
            IsGif = isGif;
            double target = isGif ? 0 : SegmentWidth;

            if (animate)
            {
                var animation = new DoubleAnimation
                {
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                FormatThumbTransform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
            else
            {
                FormatThumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
                FormatThumbTransform.X = target;
            }

            GifButton.Foreground = isGif ? ActiveTextBrush : InactiveTextBrush;
            VideoButton.Foreground = isGif ? InactiveTextBrush : ActiveTextBrush;
            GifButton.ToolTip = "录制为 GIF 动图（导出时按 15fps、最大宽度 960 生成）";
            VideoButton.ToolTip = "录制为 MP4 视频";
        }

        private void BringToTop()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            var rect = Native.GetWindowRectangle(hwnd);
            Native.PositionWindow(hwnd, rect.Left, rect.Top);
        }

        private void Place()
        {
            if (_monitor == null || _region.IsEmpty)
            {
                return;
            }
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var rect = Native.GetWindowRectangle(hwnd);
            int width = Math.Max(1, rect.Width);
            int height = Math.Max(1, rect.Height);

            int rightX = _region.Right - width;
            int bottomY = _region.Bottom + 10;
            int aboveY = _region.Top - height - 10;
            int sideY = _region.Bottom - height;

            if (TryPlace(hwnd, rightX, bottomY, width, height)) return;
            if (TryPlace(hwnd, rightX, aboveY, width, height)) return;
            if (TryPlace(hwnd, _region.Right + 10, sideY, width, height)) return;
            if (TryPlace(hwnd, _region.Left - width - 10, sideY, width, height)) return;
            if (TryPlace(hwnd, rightX, _monitor.WorkArea.Bottom + 2, width, height)) return;

            foreach (var monitor in Native.GetMonitors())
            {
                if (string.Equals(monitor.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int x;
                if (monitor.WorkArea.Left >= _monitor.WorkArea.Right)
                {
                    x = monitor.WorkArea.Left + 12;
                }
                else if (monitor.WorkArea.Right <= _monitor.WorkArea.Left)
                {
                    x = monitor.WorkArea.Right - width - 12;
                }
                else
                {
                    x = monitor.WorkArea.Right - width - 12;
                }

                int y = monitor.WorkArea.Bottom - height - 12;
                if (x >= monitor.WorkArea.Left && x + width <= monitor.WorkArea.Right && y >= monitor.WorkArea.Top)
                {
                    Native.PositionWindow(hwnd, x, y);
                    Opacity = 1;
                    return;
                }
            }

            if (!_avoidRegion)
            {
                Native.PositionWindow(
                    hwnd,
                    _monitor.WorkArea.Right - width - 12,
                    _monitor.WorkArea.Bottom - height - 12);
                Opacity = 1;
                return;
            }

            Opacity = 0;
        }

        private bool TryPlace(IntPtr hwnd, int x, int y, int width, int height)
        {
            var candidate = new System.Drawing.Rectangle(x, y, width, height);
            if (x < _monitor.Bounds.Left || y < _monitor.Bounds.Top ||
                candidate.Right > _monitor.Bounds.Right || candidate.Bottom > _monitor.Bounds.Bottom)
            {
                return false;
            }

            if (_avoidRegion && candidate.IntersectsWith(_region))
            {
                return false;
            }

            Native.PositionWindow(hwnd, x, y);
            Opacity = 1;
            return true;
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            StartRequested?.Invoke();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopRequested?.Invoke();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke();
        }

        private void Gif_Click(object sender, RoutedEventArgs e)
        {
            SetFormat(true, true);
        }

        private void Video_Click(object sender, RoutedEventArgs e)
        {
            SetFormat(false, true);
        }

        private static Brush CreateBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
