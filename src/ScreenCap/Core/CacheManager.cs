using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenCap.Core
{
    public static class CacheManager
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    Active.Add(Path.GetFullPath(path));
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
                    Active.Remove(Path.GetFullPath(path));
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
                    return Active.Contains(Path.GetFullPath(path));
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

                foreach (var file in Directory.GetFiles(folder, "recording*.mp4"))
                {
                    if (IsActive(file))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        Log.Info("清理缓存: " + file);
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
