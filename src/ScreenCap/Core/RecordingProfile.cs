using System;
using System.IO;
using Newtonsoft.Json;

namespace ScreenCap.Core
{
    internal sealed class RecordingProfile
    {
        [JsonConstructor]
        public RecordingProfile(int quality, bool systemAudio)
        {
            Quality = quality;
            SystemAudio = systemAudio;
        }

        [JsonProperty(Required = Required.Always)]
        public int Quality { get; }

        [JsonProperty(Required = Required.Always)]
        public bool SystemAudio { get; }

        public static RecordingProfile Load(string videoPath)
        {
            string path = MetadataPath(videoPath);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var profile = JsonConvert.DeserializeObject<RecordingProfile>(File.ReadAllText(path));
                return profile != null && profile.Quality >= 10 && profile.Quality <= 100 ? profile : null;
            }
            catch (Exception ex)
            {
                Log.Error("读取录制参数失败", ex);
                return null;
            }
        }

        public void Save(string videoPath)
        {
            try
            {
                File.WriteAllText(MetadataPath(videoPath), JsonConvert.SerializeObject(this));
            }
            catch (Exception ex)
            {
                Log.Error("保存录制参数失败", ex);
            }
        }

        public static string MetadataPath(string videoPath) => videoPath + ".capture.json";
    }
}
