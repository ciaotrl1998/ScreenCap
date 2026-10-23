using System;
using System.IO;
using Newtonsoft.Json;

namespace ScreenCap.Core
{
    public sealed class AppSettings
    {
        public string Hotkey { get; set; } = "Alt+Shift+Q";
        public int Framerate { get; set; } = 60;
        public int Quality { get; set; } = 75;
        public bool ShowCursor { get; set; } = true;
        public bool RecordSystemAudio { get; set; } = true;
        public bool RecordGif { get; set; }
        public int GifFramerate { get; set; } = 15;
        public int GifQualityLevel { get; set; } = 8;
        public int GifMaxColors { get; set; } = 256;
        public bool GifDither { get; set; } = true;
        public int GifMaxWidth { get; set; } = 960;
        public string RecordingsFolder { get; set; } = "";
        public string ExportDirectory { get; set; } = "";
        public string FfmpegPath { get; set; } = "";

        [JsonIgnore]
        public string SettingsPath => Path.Combine(Log.DataDirectory, "settings.json");

        [JsonIgnore]
        public string ResolvedRecordingsFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(RecordingsFolder))
                {
                    return Environment.ExpandEnvironmentVariables(RecordingsFolder);
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenCap");
            }
        }

        [JsonIgnore]
        public string ResolvedExportDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ExportDirectory))
                {
                    try
                    {
                        string directory = Environment.ExpandEnvironmentVariables(ExportDirectory);
                        if (Directory.Exists(directory))
                        {
                            return directory;
                        }
                    }
                    catch
                    {
                    }
                }
                return ResolvedRecordingsFolder;
            }
        }

        [JsonIgnore]
        public string CacheFolder => Path.Combine(Log.DataDirectory, "cache");

        public static AppSettings Load()
        {
            try
            {
                var path = Path.Combine(Log.DataDirectory, "settings.json");
                if (File.Exists(path))
                {
                    var settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path));
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取设置失败", ex);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
            }
        }

        private void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Alt+Shift+Q";
            Framerate = Math.Max(15, Math.Min(120, Framerate));
            Quality = Math.Max(10, Math.Min(100, Quality));
            GifFramerate = Math.Max(5, Math.Min(30, GifFramerate));
            GifQualityLevel = Math.Max(0, Math.Min(10, GifQualityLevel));
            GifMaxColors = Math.Max(2, Math.Min(256, GifMaxColors));
            GifMaxWidth = Math.Max(160, Math.Min(2560, GifMaxWidth));
            ExportDirectory = ExportDirectory?.Trim() ?? "";
        }
    }
}
