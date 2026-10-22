using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScreenCap.Core;

namespace ScreenCap.Services
{
    public static class ClipboardService
    {
        public static string ClipboardDirectory => Path.Combine(Log.DataDirectory, "clipboard");

        public static void CopyFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("要复制的文件不存在。", path);
            }

            var files = new StringCollection { path };
            Exception lastError = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    Clipboard.SetFileDropList(files);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(60);
                }
            }
            throw new InvalidOperationException("写入剪贴板失败：" + lastError?.Message, lastError);
        }

        public static void CleanupOldFiles(TimeSpan maxAge)
        {
            try
            {
                string dir = ClipboardDirectory;
                if (!Directory.Exists(dir))
                {
                    return;
                }
                var threshold = DateTime.Now - maxAge;
                foreach (var file in Directory.GetFiles(dir, "*.mp4"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < threshold)
                        {
                            File.Delete(file);
                        }
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
    }
}
