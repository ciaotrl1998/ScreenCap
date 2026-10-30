using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ScreenCap.Core;
using ScreenCap.UI;

namespace ScreenCap.Capture
{
    public sealed class CaptureController : IDisposable
    {
        private enum State
        {
            Idle,
            Selecting,
            Ready,
            Recording,
            Stopping
        }

        private readonly AppSettings _settings;
        private readonly Func<string, string> _applyHotkey;
        private State _state = State.Idle;
        private RegionSelector _selector;
        private ControlBarWindow _bar;
        private RegionFrameWindow _frame;
        private RecorderService _recorder;
        private Rectangle _region;
        private MonitorInfo _monitor;
        private bool _recordGif;

        public bool IsBusy => _state != State.Idle;
        public bool IsRecording => _state == State.Recording;

        public CaptureController(AppSettings settings, Func<string, string> applyHotkey = null)
        {
            _settings = settings;
            _applyHotkey = applyHotkey;
        }

        public async void Toggle()
        {
            try
            {
                switch (_state)
                {
                    case State.Selecting:
                        _selector?.Cancel();
                        return;
                    case State.Ready:
                        CloseBar();
                        CloseFrame();
                        _state = State.Idle;
                        return;
                    case State.Recording:
                        await StopAsync(false);
                        return;
                    case State.Stopping:
                        return;
                    default:
                        await BeginSelectionAsync();
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("录制流程异常", ex);
                ResetToIdle();
            }
        }

        private async Task BeginSelectionAsync()
        {
            _state = State.Selecting;
            _selector = new RegionSelector();
            _selector.Begin();
            var rectangle = await _selector.Result;
            _selector = null;

            if (rectangle == null)
            {
                _state = State.Idle;
                return;
            }

            var monitors = Native.GetMonitors();
            var center = new System.Drawing.Point(rectangle.Value.Left + rectangle.Value.Width / 2, rectangle.Value.Top + rectangle.Value.Height / 2);
            _monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(center))
                       ?? monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(rectangle.Value))
                       ?? monitors.FirstOrDefault();
            _region = rectangle.Value;

            _frame = new RegionFrameWindow();
            _frame.RegionChanged += OnFrameRegionChanged;
            _frame.ShowFor(_region, _monitor);

            _bar = new ControlBarWindow();
            _bar.StartRequested += OnStartRequested;
            _bar.StopRequested += OnStopRequested;
            _bar.CancelRequested += OnCancelRequested;
            _state = State.Ready;
            _bar.ShowReady(_region, _monitor, _settings.RecordGif);
        }

        private void OnFrameRegionChanged(Rectangle region)
        {
            _region = region;
            _bar?.UpdateRegion(region);
        }

        private void OnStartRequested()
        {
            if (_state != State.Ready)
            {
                return;
            }

            _state = State.Recording;
            _recordGif = _bar?.IsGif ?? _settings.RecordGif;
            _settings.RecordGif = _recordGif;
            _settings.Save();
            CaptureHistory.LastRecordedRegion = _region;
            _bar?.ShowRecording();
            _frame?.SetRecording(true);

            _recorder = new RecorderService(_settings);
            _recorder.Failed += OnRecorderFailed;

            try
            {
                _recorder.Start(_region, _monitor);
            }
            catch (Exception ex)
            {
                Log.Error("启动录制失败", ex);
                _recorder.Dispose();
                _recorder = null;
                CloseFrame();
                _bar?.ShowFailed("启动失败");
                _state = State.Idle;
                MessageBox.Show(
                    "无法启动录制：\n" + ex.Message +
                    "\n\n请确认系统为 Windows 10 1903 或更高版本，并已安装 Microsoft Visual C++ 2015-2022 运行库。",
                    "ScreenCap",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                CloseBar();
            }
        }

        private void OnStopRequested()
        {
            _ = StopAsync(false);
        }

        private void OnCancelRequested()
        {
            if (_state == State.Recording)
            {
                var result = MessageBox.Show("放弃当前录制？", "ScreenCap", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _ = StopAsync(true);
                }
                return;
            }

            CloseBar();
            CloseFrame();
            _state = State.Idle;
        }

        private async Task StopAsync(bool discard)
        {
            if (_state != State.Recording || _recorder == null)
            {
                return;
            }

            _state = State.Stopping;
            var elapsed = _recorder.Elapsed;
            _bar?.ShowSaving();

            string path = null;
            try
            {
                path = await _recorder.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Error("停止录制异常", ex);
            }

            _recorder.Failed -= OnRecorderFailed;
            _recorder.Dispose();
            _recorder = null;

            if (!discard && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    await Task.Run(() => RecorderService.ApplyColorMetadata(path, _settings));
                }
                catch (Exception ex)
                {
                    Log.Error("颜色标记失败", ex);
                }
            }

            CloseFrame();
            _bar?.Close();
            _bar = null;
            _state = State.Idle;

            if (discard)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    TryDelete(path);
                }
                return;
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 1024)
            {
                new EditorWindow(path, elapsed, _settings, _recordGif, _applyHotkey).Show();
            }
            else
            {
                MessageBox.Show(
                    "录制没有生成有效文件，请查看日志：\n" + Log.LogFilePath,
                    "ScreenCap",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnRecorderFailed(string message)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (_state != State.Recording)
                {
                    return;
                }

                Log.Error("录制失败回调: " + message);
                _bar?.ShowFailed("录制失败");
                await Task.Delay(900);

                _recorder?.Dispose();
                _recorder = null;
                CloseFrame();
                _bar?.Close();
                _bar = null;
                _state = State.Idle;

                MessageBox.Show("录制失败：\n" + message, "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            }));
        }

        private void CloseBar()
        {
            if (_bar != null)
            {
                _bar.StartRequested -= OnStartRequested;
                _bar.StopRequested -= OnStopRequested;
                _bar.CancelRequested -= OnCancelRequested;
                _bar.Close();
                _bar = null;
            }
        }

        private void CloseFrame()
        {
            if (_frame != null)
            {
                _frame.RegionChanged -= OnFrameRegionChanged;
                _frame.Close();
                _frame = null;
            }
        }

        private void ResetToIdle()
        {
            _selector?.Cancel();
            _selector = null;
            try
            {
                _recorder?.Dispose();
            }
            catch
            {
            }
            _recorder = null;
            CloseFrame();
            CloseBar();
            _state = State.Idle;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log.Error("删除录制文件失败", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_recorder != null && _state == State.Recording)
                {
                    _recorder.StopAsync().Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
            }

            try
            {
                _recorder?.Dispose();
            }
            catch
            {
            }
            _recorder = null;

            CloseBar();
            CloseFrame();
            _state = State.Idle;
        }
    }
}
