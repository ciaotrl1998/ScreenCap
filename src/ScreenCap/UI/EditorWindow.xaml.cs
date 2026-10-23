using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ScreenCap.Core;
using ScreenCap.Services;
using Microsoft.Win32;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ScreenCap.UI
{
    public partial class EditorWindow : Window
    {
        private readonly string _sourcePath;
        private readonly string _baseName;
        private readonly AppSettings _settings;
        private readonly bool _isGif;
        private readonly DispatcherTimer _positionTimer;

        private LibVLC _libVlc;
        private VlcMediaPlayer _player;
        private VlcMedia _media;
        private CancellationTokenSource _exportCts;
        private ExportProgressWindow _busyWindow;
        private DispatcherTimer _loadTimeoutTimer;
        private string _posterPath;
        private string _lastOutputPath;
        private bool _userAdjustedRange;
        private bool _initialLengthApplied;
        private bool _initialized;
        private bool _ended;
        private bool _restarting;
        private bool _closing;
        private long? _pendingSeekMs;
        private long _lastPlayerMs = -1;
        private long _lastPlayerTimestamp;
        private double _interpolatedSeconds;

        public EditorWindow(string sourcePath, TimeSpan fallbackDuration, AppSettings settings, bool isGif)
        {
            _sourcePath = sourcePath;
            _settings = settings;
            _isGif = isGif;

            DateTime stamp = DateTime.Now;
            try
            {
                if (File.Exists(sourcePath))
                {
                    stamp = File.GetLastWriteTime(sourcePath);
                }
            }
            catch
            {
            }
            _baseName = "ScreenCap_" + stamp.ToString("yyyyMMdd_HHmmss");
            CacheManager.Register(_sourcePath);
            Log.Info($"打开剪辑窗: {_sourcePath}, 存在={File.Exists(_sourcePath)}, 格式={(_isGif ? "GIF" : "视频")}");

            InitializeComponent();
            FitToScreen();

            double initialDuration = fallbackDuration > TimeSpan.FromSeconds(0.5)
                ? fallbackDuration.TotalSeconds
                : 1.0;
            Timeline.Duration = initialDuration;
            Timeline.ResetRange(initialDuration);
            Timeline.RangeChanged += OnRangeChanged;
            Timeline.SeekRequested += Seek;

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _positionTimer.Tick += OnPositionTick;
            _positionTimer.Start();

            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;

            LoadPoster();
            UpdateTimeText();
            ApplyFormatUi();
            Dispatcher.BeginInvoke(new Action(() => Native.FitWindowToWorkArea(this)));
        }

        private void ApplyFormatUi()
        {
            string title = _isGif ? "编辑录屏 · GIF" : "编辑录屏";
            Title = title;
            TitleText.Text = title;
            ExportButton.Content = _isGif ? "导出 GIF" : "导出";
            ExportButton.ToolTip = _isGif ? ("导出 GIF：" + _settings.GifFramerate + "fps，" + _settings.GifMaxColors + " 色" + (_settings.GifDither ? "，抖动" : "，无抖动") + "，最大宽度 " + _settings.GifMaxWidth + "px") : "导出 MP4 视频";
            CopyButton.ToolTip = _isGif ? "将 GIF 作为文件复制到剪贴板" : "将剪辑后的 mp4 作为文件复制到剪贴板";
            StatusText.Text = _isGif
                ? "GIF 模式：拖动时间轴裁剪，空格播放"
                : "拖动时间轴两端滑块裁剪，空格播放";
        }

        private void FitToScreen()
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(1120, area.Width * 0.8));
            Height = Math.Max(MinHeight, Math.Min(820, area.Height * 0.88));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_sourcePath))
            {
                _initialized = true;
                Log.Error("剪辑源文件不存在: " + _sourcePath);
                ShowPreviewError("录制缓存文件已丢失，无法播放或导出。请重新录制。");
                return;
            }

            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                _libVlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
                _player = new VlcMediaPlayer(_libVlc);
                VideoView.MediaPlayer = _player;

                _player.LengthChanged += (s, args) => Dispatcher.BeginInvoke(new Action(UpdateDurationFromPlayer));
                _player.Playing += OnPlaying;
                _player.EndReached += (s, args) => Dispatcher.BeginInvoke(new Action(OnPlaybackEnded));
                _player.EncounteredError += (s, args) => Dispatcher.BeginInvoke(new Action(() =>
                    ShowPreviewError("无法预览该文件，但导出与复制仍然可用。")));

                _media = new VlcMedia(_libVlc, _sourcePath, FromType.FromPath);
                _player.Play(_media);
                _player.SetPause(true);
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log.Error("初始化视频预览失败", ex);
                _initialized = true;
                ShowPreviewError("视频预览不可用，但导出与复制仍然可用。\n" + ex.Message);
            }

            _loadTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _loadTimeoutTimer.Tick += (s, args) =>
            {
                _loadTimeoutTimer.Stop();
                if (Timeline.Duration <= 1.05 && PlayerLengthMilliseconds <= 250)
                {
                    ShowPreviewError("视频加载较慢，稍后可直接点播放；导出与复制仍然可用。");
                }
            };
            _loadTimeoutTimer.Start();

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private long PlayerLengthMilliseconds => _player?.Length ?? 0;

        private void OnPlaying(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_player == null || _closing)
                {
                    return;
                }

                _restarting = false;
                _ended = false;

                try
                {
                    if (_pendingSeekMs.HasValue)
                    {
                        long ms = _pendingSeekMs.Value;
                        _pendingSeekMs = null;
                        _player.Time = ms;
                        Timeline.Position = ms / 1000.0;
                        UpdateTimeText();
                    }
                }
                catch
                {
                }
            }));
        }

        private void OnPlaybackEnded()
        {
            if (_closing || _restarting)
            {
                return;
            }

            _ended = true;
            _lastPlayerMs = -1;
            Timeline.Position = Timeline.OutPoint;
            UpdateTimeText();
            PlayButton.Content = "▶";
        }

        private void UpdateDurationFromPlayer()
        {
            if (_player == null || _closing)
            {
                return;
            }

            long length = _player.Length;
            if (length <= 250)
            {
                return;
            }

            _loadTimeoutTimer?.Stop();
            double seconds = length / 1000.0;
            if (!_userAdjustedRange && !_initialLengthApplied)
            {
                double position = Timeline.Position;
                Timeline.Duration = seconds;
                Timeline.InPoint = 0;
                Timeline.OutPoint = seconds;
                Timeline.Position = Math.Min(position, seconds);
                _initialLengthApplied = true;
                UpdateTimeText();
            }
            else if (seconds > Timeline.Duration)
            {
                Timeline.Duration = Math.Max(seconds, Timeline.OutPoint);
            }
        }

        private void OnRangeChanged()
        {
            _userAdjustedRange = true;
            if (_player != null && _player.IsPlaying)
            {
                _player.SetPause(true);
            }
            UpdateTimeText();
        }

        private void OnPositionTick(object sender, EventArgs e)
        {
            if (_closing || _player == null)
            {
                return;
            }

            bool playing = _player.IsPlaying;
            PlayButton.Content = playing ? "❚❚" : "▶";

            if (!_initialLengthApplied && _player.Length > 250)
            {
                UpdateDurationFromPlayer();
            }

            if (!playing)
            {
                _lastPlayerMs = -1;
                return;
            }

            long time = _player.Time;
            if (time < 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (time != _lastPlayerMs)
            {
                _lastPlayerMs = time;
                _lastPlayerTimestamp = now;
                _interpolatedSeconds = time / 1000.0;
            }
            else
            {
                double elapsed = (now - _lastPlayerTimestamp) / (double)Stopwatch.Frequency;
                _interpolatedSeconds = time / 1000.0 + Math.Min(1.2, elapsed);
            }

            double actualSeconds = time / 1000.0;
            double seconds = Math.Min(_interpolatedSeconds, Timeline.OutPoint);
            Timeline.Position = seconds;
            UpdateTimeText();

            if (actualSeconds >= Timeline.OutPoint - 0.03)
            {
                _player.SetPause(true);
                _lastPlayerMs = -1;
                Timeline.Position = Timeline.OutPoint;
                UpdateTimeText();
            }
        }

        private void UpdateTimeText()
        {
            TimeText.Text = Format(Timeline.Position) + " / " + Format(Timeline.Duration);
        }

        private static string Format(double seconds)
        {
            return TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff");
        }

        private void TogglePlay()
        {
            if (!_initialized || _player == null)
            {
                return;
            }

            if (_player.IsPlaying)
            {
                _player.Pause();
                return;
            }

            if (_restarting)
            {
                return;
            }

            if (_ended)
            {
                double start = Timeline.Position < Timeline.OutPoint - 0.05 ? Timeline.Position : Timeline.InPoint;
                _ = RestartPlayerAsync(start);
                return;
            }

            if (Timeline.Position >= Timeline.OutPoint - 0.1)
            {
                _ = RestartPlayerAsync(Timeline.InPoint);
                return;
            }

            if (_player.Time >= 0 && Math.Abs(_player.Time / 1000.0 - Timeline.Position) > 0.2)
            {
                _ = RestartPlayerAsync(Timeline.Position);
                return;
            }

            HidePoster();
            _lastPlayerMs = -1;
            _player.Play();
        }

        private async Task RestartPlayerAsync(double start)
        {
            var player = _player;
            if (player == null || _closing)
            {
                return;
            }

            double target = Math.Max(Timeline.InPoint, Math.Min(start, Timeline.OutPoint));
            _restarting = true;
            _ended = false;
            _lastPlayerMs = -1;
            _interpolatedSeconds = target;
            Timeline.Position = target;
            UpdateTimeText();
            HidePoster();

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        player.Stop();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("停止播放失败", ex);
                    }
                });
            }
            catch
            {
            }

            if (_closing || _player != player)
            {
                return;
            }

            _pendingSeekMs = (long)Math.Round(target * 1000);
            try
            {
                bool played = _media != null ? player.Play(_media) : player.Play();
                if (!played)
                {
                    _restarting = false;
                    _pendingSeekMs = null;
                    ShowPreviewError("无法重新播放该文件。");
                }
            }
            catch (Exception ex)
            {
                _restarting = false;
                _pendingSeekMs = null;
                Log.Error("重新播放失败", ex);
            }
        }

        private void Seek(double seconds)
        {
            double target = Math.Max(Timeline.InPoint, Math.Min(seconds, Timeline.OutPoint));
            Timeline.Position = target;
            HidePoster();
            _lastPlayerMs = -1;
            _interpolatedSeconds = target;

            if (_initialized && !_closing)
            {
                if (_ended)
                {
                    _ended = false;
                }
                try
                {
                    _player.Time = (long)Math.Round(target * 1000);
                }
                catch (Exception ex)
                {
                    Log.Error("Seek 失败", ex);
                }
            }
            UpdateTimeText();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            TogglePlay();
        }

        private void LoadPoster()
        {
            try
            {
                string ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
                if (ffmpeg == null)
                {
                    return;
                }

                string folder = Path.Combine(Path.GetTempPath(), "ScreenCap");
                Directory.CreateDirectory(folder);
                _posterPath = Path.Combine(folder, "poster_" + Guid.NewGuid().ToString("N") + ".png");
                string posterPath = _posterPath;
                string source = _sourcePath;

                Task.Run(() =>
                {
                    try
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = ffmpeg,
                            Arguments = "-hide_banner -loglevel error -y -ss 0 -i \"" + source + "\" -frames:v 1 \"" + posterPath + "\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var process = Process.Start(info))
                        {
                            process?.WaitForExit(8000);
                        }
                    }
                    catch
                    {
                    }
                }).ContinueWith(t =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!File.Exists(posterPath))
                            {
                                return;
                            }
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(posterPath);
                            bitmap.EndInit();
                            bitmap.Freeze();
                            PosterImage.Source = bitmap;
                        }
                        catch
                        {
                        }
                    }));
                });
            }
            catch
            {
            }
        }

        private void HidePoster()
        {
            PreviewCover.Visibility = Visibility.Collapsed;
        }

        private void ShowPreviewError(string message)
        {
            PreviewError.Text = message;
            PreviewError.Visibility = Visibility.Visible;
        }

        private bool IsBusy => BusyOverlay.Visibility == Visibility.Visible;

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            if (!SourceAvailable())
            {
                return;
            }

            if (!ValidateRange())
            {
                return;
            }

            string ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
            if (ffmpeg == null)
            {
                ShowFfmpegMissing();
                return;
            }

            string target = ChooseExportPath();
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            double start = Timeline.InPoint;
            double length = Timeline.OutPoint - Timeline.InPoint;
            bool ok = await RunExporterAsync(
                ffmpeg,
                _sourcePath,
                start,
                length,
                target,
                _isGif ? "正在导出 GIF…" : "正在导出…",
                _isGif);
            if (ok)
            {
                _lastOutputPath = target;
                StatusText.Text = "已导出 " + Path.GetFileName(target);
                OpenFolderButton.Visibility = Visibility.Visible;
            }
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            if (!SourceAvailable())
            {
                return;
            }

            if (!ValidateRange())
            {
                return;
            }

            Directory.CreateDirectory(ClipboardService.ClipboardDirectory);
            string extension = _isGif ? ".gif" : ".mp4";
            string path = Path.Combine(
                ClipboardService.ClipboardDirectory,
                _baseName + "_" + DateTime.Now.ToString("HHmmss") + extension);

            bool fullRange = Timeline.InPoint <= 0.02 && Timeline.OutPoint >= Timeline.Duration - 0.02;
            if (!_isGif && fullRange)
            {
                try
                {
                    File.Copy(_sourcePath, path, true);
                }
                catch (Exception ex)
                {
                    Log.Error("复制缓存文件失败", ex);
                    MessageBox.Show(this, "复制文件失败：\n" + ex.Message, "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                string ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
                if (ffmpeg == null)
                {
                    ShowFfmpegMissing();
                    return;
                }

                double start = Timeline.InPoint;
                double length = Timeline.OutPoint - Timeline.InPoint;
                if (!await RunExporterAsync(
                        ffmpeg,
                        _sourcePath,
                        start,
                        length,
                        path,
                        _isGif ? "正在准备 GIF…" : "正在准备复制…",
                        _isGif))
                {
                    return;
                }
            }

            try
            {
                ClipboardService.CopyFile(path);
                _lastOutputPath = path;
                StatusText.Text = "已复制视频文件，可直接粘贴到聊天或邮件中";
                OpenFolderButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Error("复制到剪贴板失败", ex);
                MessageBox.Show(this, "复制到剪贴板失败：\n" + ex.Message, "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool SourceAvailable()
        {
            if (File.Exists(_sourcePath))
            {
                return true;
            }

            MessageBox.Show(this, "录制缓存文件已被清理，无法继续导出。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private bool ValidateRange()
        {
            if (Timeline.OutPoint - Timeline.InPoint < 0.05)
            {
                MessageBox.Show(this, "选区太短，请至少保留 0.05 秒。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        private async Task<bool> RunExporterAsync(string ffmpeg, string input, double start, double duration, string output, string title, bool gif)
        {
            _exportCts = new CancellationTokenSource();
            var startedAt = Stopwatch.StartNew();
            ShowBusy(title);

            try
            {
                var progress = new Progress<double>(SetBusyProgress);
                if (gif)
                {
                    await FfmpegExporter.ExportGifAsync(ffmpeg, input, start, duration, output, _isGif ? _settings.GifFramerate : 15, _settings.GifMaxColors, _settings.GifDither, _settings.GifMaxWidth, progress, _exportCts.Token);
                }
                else
                {
                    await FfmpegExporter.ExportAsync(ffmpeg, input, start, duration, output, progress, _exportCts.Token);
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "已取消导出";
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("导出失败", ex);
                MessageBox.Show(this, "导出失败：\n" + ex.Message, "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                long remaining = 350 - startedAt.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    try
                    {
                        await Task.Delay((int)remaining);
                    }
                    catch
                    {
                    }
                }
                _exportCts.Dispose();
                _exportCts = null;
                HideBusy();
            }
        }

        private string ChooseExportPath()
        {
            string extension = _isGif ? ".gif" : ".mp4";
            string directory = _settings.ResolvedExportDirectory;
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                directory = _settings.ResolvedRecordingsFolder;
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch
                {
                    directory = "";
                }
            }

            var dialog = new SaveFileDialog
            {
                Title = _isGif ? "导出 GIF" : "导出视频",
                Filter = _isGif ? "GIF 动图 (*.gif)|*.gif" : "MP4 视频 (*.mp4)|*.mp4",
                DefaultExt = extension,
                AddExtension = true,
                OverwritePrompt = true,
                FileName = _baseName + extension,
                InitialDirectory = directory
            };

            if (dialog.ShowDialog(this) != true)
            {
                return null;
            }

            string target = dialog.FileName;
            try
            {
                string chosen = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(chosen))
                {
                    _settings.ExportDirectory = chosen;
                    _settings.Save();
                }
            }
            catch
            {
            }
            return target;
        }

        private void ShowFfmpegMissing()
        {
            MessageBox.Show(this,
                "未找到 ffmpeg.exe，导出和复制需要它。\n\n" +
                "方式一：把 ffmpeg.exe 放到程序目录的 tools 文件夹下。\n" +
                "方式二：在 PowerShell 执行 winget install Gyan.FFmpeg。\n" +
                "方式三：运行项目里的 tools\\get-ffmpeg.ps1 自动下载。",
                "ScreenCap",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void ShowBusy(string title)
        {
            try
            {
                if (_player != null && _player.IsPlaying)
                {
                    _player.SetPause(true);
                }
            }
            catch
            {
            }

            _busyWindow?.Close();
            _busyWindow = new ExportProgressWindow { Owner = this };
            _busyWindow.CancelClicked += CancelExport;
            _busyWindow.Show();
            _busyWindow.SetTitle(title);
            _busyWindow.SetProgress(0);
            _busyWindow.SetCancelEnabled(true);
        }

        private void SetBusyProgress(double ratio)
        {
            _busyWindow?.SetProgress(ratio);
        }

        private void HideBusy()
        {
            if (_busyWindow == null)
            {
                return;
            }
            try
            {
                _busyWindow.Close();
            }
            catch
            {
            }
            _busyWindow = null;
        }

        private void CancelExport_Click(object sender, RoutedEventArgs e)
        {
            CancelExport();
        }

        private void CancelExport()
        {
            _busyWindow?.SetTitle("正在取消…");
            _busyWindow?.SetCancelEnabled(false);
            try
            {
                _exportCts?.Cancel();
            }
            catch
            {
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = _lastOutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = _settings.ResolvedRecordingsFolder;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error("打开文件夹失败", ex);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    TogglePlay();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Step(-(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 1.0 / Math.Max(1, _settings.Framerate)));
                    e.Handled = true;
                    break;
                case Key.Right:
                    Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 1.0 / Math.Max(1, _settings.Framerate));
                    e.Handled = true;
                    break;
                case Key.I:
                    Timeline.InPoint = Timeline.Position;
                    _userAdjustedRange = true;
                    UpdateTimeText();
                    e.Handled = true;
                    break;
                case Key.O:
                    Timeline.OutPoint = Timeline.Position;
                    _userAdjustedRange = true;
                    UpdateTimeText();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void Step(double seconds)
        {
            Seek(Timeline.Position + seconds);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closing = true;
            _positionTimer.Stop();
            _loadTimeoutTimer?.Stop();

            try
            {
                _exportCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                VideoView.MediaPlayer = null;
            }
            catch
            {
            }

            var player = _player;
            var media = _media;
            var lib = _libVlc;
            _player = null;
            _media = null;
            _libVlc = null;

            Task.Run(() =>
            {
                try
                {
                    player?.Stop();
                }
                catch
                {
                }
                try
                {
                    player?.Dispose();
                }
                catch
                {
                }
                try
                {
                    media?.Dispose();
                }
                catch
                {
                }
                try
                {
                    lib?.Dispose();
                }
                catch
                {
                }
            });

            DeleteCachedSource();
            DeletePoster();
            CacheManager.Unregister(_sourcePath);
        }

        private void DeleteCachedSource()
        {
            try
            {
                string cacheDirectory = Path.GetFullPath(_settings.CacheFolder);
                string source = Path.GetFullPath(_sourcePath);
                if (!source.StartsWith(cacheDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (!File.Exists(source))
                        {
                            return;
                        }
                        File.Delete(source);
                        Log.Info("删除缓存(关闭剪辑窗): " + source);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(60);
                    }
                }
            }
            catch
            {
            }
        }

        private void DeletePoster()
        {
            try
            {
                if (!string.IsNullOrEmpty(_posterPath) && File.Exists(_posterPath))
                {
                    File.Delete(_posterPath);
                }
            }
            catch
            {
            }
        }
    }
}
