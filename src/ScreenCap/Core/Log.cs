using System;
using System.IO;

namespace ScreenCap.Core
{
    public static class Log
    {
        private static readonly object Gate = new object();

        public static string DataDirectory
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCap");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string LogFilePath => Path.Combine(DataDirectory, "log.txt");

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception exception = null)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (Gate)
                {
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    if (exception != null)
                    {
                        line += Environment.NewLine + exception;
                    }
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
