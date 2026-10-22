using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenCap.Core;

namespace ScreenCap.Services
{
    public static class FfmpegLocator
    {
        public static string Find(string configuredPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                candidates.Add(Environment.ExpandEnvironmentVariables(configuredPath));
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(Log.DataDirectory, "ffmpeg.exe"));

            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathVar.Split(';'))
                {
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        candidates.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"));
                    }
                }
            }
            catch
            {
            }

            try
            {
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"));
            }
            catch
            {
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
            return null;
        }
    }

    public static class FfmpegExporter
    {
        public static async Task ExportAsync(
            string ffmpegPath,
            string input,
            double startSeconds,
            double durationSeconds,
            string output,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            Prepare(ffmpegPath, input, output, ref startSeconds, ref durationSeconds);
            string args = BuildVideoArguments(input, startSeconds, durationSeconds, output);
            await RunAsync(ffmpegPath, args, output, durationSeconds, progress, cancellationToken);
        }

        public static async Task ExportGifAsync(
            string ffmpegPath,
            string input,
            double startSeconds,
            double durationSeconds,
            string output,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            Prepare(ffmpegPath, input, output, ref startSeconds, ref durationSeconds);
            string args = BuildGifArguments(input, startSeconds, durationSeconds, output);
            await RunAsync(ffmpegPath, args, output, durationSeconds, progress, cancellationToken);
        }

        private static void Prepare(string ffmpegPath, string input, string output, ref double startSeconds, ref double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException("未找到 ffmpeg.exe");
            }
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("找不到录制文件", input);
            }
            if (startSeconds < 0) startSeconds = 0;
            if (durationSeconds <= 0.02) durationSeconds = 0.02;

            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            TryDelete(output);
        }

        private static async Task RunAsync(
            string ffmpegPath,
            string args,
            string output,
            double durationSeconds,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(output);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = string.IsNullOrEmpty(directory) ? AppDomain.CurrentDomain.BaseDirectory : directory
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("无法启动 ffmpeg。");
                }

                using (cancellationToken.Register(() => TryKill(process)))
                {
                    Task readProgress = ReadProgressAsync(process.StandardOutput, durationSeconds, progress, cancellationToken);
                    Task<string> readError = process.StandardError.ReadToEndAsync();

                    await readProgress;
                    await Task.Run(() => process.WaitForExit());
                    string error = await readError;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryDelete(output);
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (process.ExitCode != 0)
                    {
                        TryDelete(output);
                        throw new InvalidOperationException("ffmpeg 导出失败：" + Environment.NewLine + (string.IsNullOrWhiteSpace(error) ? "未知错误" : error.Trim()));
                    }

                    progress?.Report(1.0);
                }
            }
        }

        private static string BuildVideoArguments(string input, double start, double duration, string output)
        {
            string ss = start.ToString("0.###", CultureInfo.InvariantCulture);
            string t = duration.ToString("0.###", CultureInfo.InvariantCulture);
            return "-hide_banner -loglevel error -nostats -progress pipe:1 " +
                   $"-ss {ss} -i {Quote(input)} -t {t} " +
                   "-map 0:v:0 -map 0:a:0? " +
                   "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p " +
                   "-color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 " +
                   "-c:a aac -b:a 192k -movflags +faststart " +
                   "-y " + Quote(output);
        }

        private static string BuildGifArguments(string input, double start, double duration, string output)
        {
            string ss = start.ToString("0.###", CultureInfo.InvariantCulture);
            string t = duration.ToString("0.###", CultureInfo.InvariantCulture);
            const string filter =
                "fps=15," +
                "scale=w='min(960,iw)':h=-1:flags=lanczos," +
                "split[s0][s1];" +
                "[s0]palettegen=max_colors=256:stats_mode=full[p];" +
                "[s1][p]paletteuse=dither=sierra2_4a";

            return "-hide_banner -loglevel error -nostats -progress pipe:1 " +
                   $"-ss {ss} -i {Quote(input)} -t {t} " +
                   "-an " +
                   $"-vf \"{filter}\" " +
                   "-loop 0 " +
                   "-y " + Quote(output);
        }

        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0)
            {
                throw new ArgumentException("路径中不能包含双引号。", nameof(value));
            }
            return "\"" + value + "\"";
        }

        private static async Task ReadProgressAsync(StreamReader reader, double duration, IProgress<double> progress, CancellationToken token)
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                if (progress == null || !line.StartsWith("out_time=", StringComparison.Ordinal))
                {
                    continue;
                }

                string value = line.Substring("out_time=".Length).Trim();
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan time))
                {
                    double ratio = Math.Max(0, Math.Min(1, time.TotalSeconds / Math.Max(0.01, duration)));
                    progress.Report(ratio);
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
