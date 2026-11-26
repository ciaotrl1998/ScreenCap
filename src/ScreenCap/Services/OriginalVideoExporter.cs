using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ScreenCap.Core;

namespace ScreenCap.Services
{
    internal static class OriginalVideoExporter
    {
        public static Task CopyAsync(string input, string output, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() => Copy(input, output, progress, cancellationToken), cancellationToken);
        }

        private static void Copy(string input, string output, IProgress<double> progress, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(output);
            Directory.CreateDirectory(directory);
            // Publish the copy only after it completes, so cancellation preserves an existing export.
            string temporaryPath = Path.Combine(directory, ".screencap-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var source = File.OpenRead(input))
                using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[128 * 1024];
                    long totalBytes = source.Length;
                    long bytesCopied = 0;
                    double lastProgress = 0;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = source.Read(buffer, 0, buffer.Length);
                        if (count == 0)
                        {
                            break;
                        }

                        target.Write(buffer, 0, count);
                        bytesCopied += count;
                        double ratio = (double)bytesCopied / totalBytes;
                        if (ratio - lastProgress >= 0.01 && ratio < 1)
                        {
                            lastProgress = ratio;
                            progress?.Report(ratio);
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(output))
                {
                    File.Replace(temporaryPath, output, null);
                }
                else
                {
                    File.Move(temporaryPath, output);
                }
                progress?.Report(1);
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex)
                {
                    Log.Error("清理导出临时文件失败", ex);
                }
            }
        }
    }
}
