using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScreenCap.Core
{
    public static class CacheManager
    {
        private const int MaxRecordings = 5;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Active = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    string fullPath = Path.GetFullPath(path);
                    Active.TryGetValue(fullPath, out int count);
                    Active[fullPath] = count + 1;
                }
            }
            catch
            {
            }
        }

        public static void Unregister(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    string fullPath = Path.GetFullPath(path);
                    if (Active.TryGetValue(fullPath, out int count))
                    {
                        if (count > 1)
                        {
                            Active[fullPath] = count - 1;
                        }
                        else
                        {
                            Active.Remove(fullPath);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static bool IsActive(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                lock (Gate)
                {
                    return Active.ContainsKey(Path.GetFullPath(path));
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Cleanup(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return;
                }

                lock (Gate)
                {
                    var files = Directory.EnumerateFiles(folder, "recording*.mp4")
                        .Where(path => !path.EndsWith(".tag.mp4", StringComparison.OrdinalIgnoreCase))
                        .Select(path => new FileInfo(path))
                        .OrderBy(file => file.CreationTimeUtc)
                        .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Only retire recordings older than the newest five. If an old
                    // recording is in use, defer its removal until it is released.
                    foreach (var file in files.Take(Math.Max(0, files.Count - MaxRecordings)))
                    {
                        if (IsActive(file.FullName))
                        {
                            continue;
                        }

                        try
                        {
                            file.Delete();
                            Log.Info("淘汰最早的视频缓存（保留最近 5 条）: " + file.FullName);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
