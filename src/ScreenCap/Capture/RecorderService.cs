using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ScreenRecorderLib;
using ScreenCap.Core;
using ScreenCap.Services;

namespace ScreenCap.Capture
{
    public sealed class RecorderService : IDisposable
    {
        private readonly AppSettings _settings;
        private Recorder _recorder;
        private TaskCompletionSource<string> _completion;
        private Stopwatch _stopwatch;
        private bool _disposed;

        public event Action<string> Failed;

        public string OutputPath { get; private set; }
        public TimeSpan Elapsed => _stopwatch != null ? _stopwatch.Elapsed : TimeSpan.Zero;

        public RecorderService(AppSettings settings)
        {
            _settings = settings;
        }

        public void Start(Rectangle region, MonitorInfo monitor)
        {
            if (monitor == null)
            {
                throw new InvalidOperationException("没有找到选区所在的显示器。");
            }

            var displays = Recorder.GetDisplays();
            var recordableDisplay = displays.FirstOrDefault(d =>
                string.Equals(d.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                ?? displays.FirstOrDefault();
            if (recordableDisplay == null)
            {
                throw new InvalidOperationException("没有找到可录制的显示器。");
            }

            string folder = _settings.CacheFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"recording_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");
            OutputPath = path;

            int localX = region.X - monitor.Bounds.X;
            int localY = region.Y - monitor.Bounds.Y;
            int width = Math.Max(2, region.Width & ~1);
            int height = Math.Max(2, region.Height & ~1);

            bool useWgc = Environment.OSVersion.Version.Build >= 22621;
            var source = new DisplayRecordingSource(recordableDisplay)
            {
                RecorderApi = useWgc ? RecorderApi.WindowsGraphicsCapture : RecorderApi.DesktopDuplication,
                IsBorderRequired = false,
                IsCursorCaptureEnabled = _settings.ShowCursor
            };

            var options = RecorderOptions.Default;
            options.SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { source }
            };
            options.OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                SourceRect = new ScreenRect
                {
                    Left = localX,
                    Top = localY,
                    Right = localX + width,
                    Bottom = localY + height
                }
            };
            options.VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = Math.Max(15, Math.Min(120, _settings.Framerate)),
                Quality = Math.Max(10, Math.Min(100, _settings.Quality)),
                IsFixedFramerate = true,
                IsHardwareEncodingEnabled = true,
                IsLowLatencyEnabled = false,
                IsMp4FastStartEnabled = true,
                IsFragmentedMp4Enabled = false,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.Quality,
                    EncoderProfile = H264Profile.High
                }
            };
            options.AudioOptions = _settings.RecordSystemAudio
                ? new AudioOptions
                {
                    IsAudioEnabled = true,
                    Bitrate = AudioBitrate.bitrate_192kbps,
                    Channels = AudioChannels.Stereo,
                    AudioSources = new List<AudioSourceBase> { LoopbackAudioSource.Default }
                }
                : new AudioOptions { IsAudioEnabled = false };
            options.MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = _settings.ShowCursor,
                IsMouseClicksDetected = false
            };
            options.LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = Path.Combine(Log.DataDirectory, "recorder.log"),
                LogSeverityLevel = LogLevel.Info
            };

            Log.Info($"开始录制 {OutputPath} 区域 {localX},{localY} {width}x{height} api={(useWgc ? "WGC" : "DD")} fps={options.VideoEncoderOptions.Framerate} audio={_settings.RecordSystemAudio}");

            _recorder = Recorder.CreateRecorder(options);
            _completion = new TaskCompletionSource<string>();
            _stopwatch = Stopwatch.StartNew();

            _recorder.OnRecordingComplete += OnRecordingComplete;
            _recorder.OnRecordingFailed += OnRecordingFailed;
            _recorder.Record(OutputPath);
        }

        public async Task<string> StopAsync()
        {
            var recorder = _recorder;
            var completion = _completion;
            if (recorder == null || completion == null)
            {
                return null;
            }

            try
            {
                recorder.Stop();
            }
            catch (Exception ex)
            {
                Log.Error("调用 Stop 失败", ex);
                completion.TrySetResult(null);
            }

            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (finished == completion.Task)
            {
                _stopwatch?.Stop();
                return await completion.Task;
            }

            Log.Error("等待录制文件写入超时");
            return null;
        }

        private void OnRecordingComplete(object sender, RecordingCompleteEventArgs e)
        {
            _stopwatch?.Stop();
            Log.Info($"录制完成 {e.FilePath}");
            _completion?.TrySetResult(e.FilePath);
        }

        private void OnRecordingFailed(object sender, RecordingFailedEventArgs e)
        {
            _stopwatch?.Stop();
            Log.Error($"录制失败 {e.FilePath}: {e.Error}");
            _completion?.TrySetResult(null);
            Failed?.Invoke(e.Error);
        }

        public static void CleanupCacheFolder(string folder, string keepPath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return;
                }

                string keep = string.IsNullOrEmpty(keepPath) ? null : Path.GetFullPath(keepPath);
                foreach (var file in Directory.GetFiles(folder, "recording*.mp4"))
                {
                    try
                    {
                        if (keep != null && string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public static void ApplyColorMetadata(string path, AppSettings settings)
        {
            string temp = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                string ffmpeg = FfmpegLocator.Find(settings.FfmpegPath);
                if (ffmpeg == null)
                {
                    Log.Info("未找到 ffmpeg，跳过颜色标记");
                    return;
                }

                temp = path + ".tag.mp4";
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }

                string arguments =
                    "-hide_banner -loglevel error -y -i \"" + path + "\" -map 0 -c copy " +
                    "-bsf:v \"h264_metadata=video_full_range_flag=0:colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1\" " +
                    "-movflags +faststart \"" + temp + "\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null || !process.WaitForExit(20000) || process.ExitCode != 0)
                    {
                        Log.Error("写入颜色标记失败，保留原文件");
                        TryDeleteFile(temp);
                        return;
                    }
                }

                if (!File.Exists(temp) || new FileInfo(temp).Length < 1024)
                {
                    TryDeleteFile(temp);
                    return;
                }

                File.Delete(path);
                File.Move(temp, path);
                Log.Info("已写入颜色标记 limited/bt709: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("写入颜色标记异常", ex);
                if (!string.IsNullOrEmpty(temp))
                {
                    TryDeleteFile(temp);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                _recorder?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("释放录制器失败", ex);
            }
            _recorder = null;
        }
    }
}
