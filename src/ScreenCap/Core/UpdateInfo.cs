using Newtonsoft.Json;

namespace ScreenCap.Core
{
    public sealed class UpdateInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; }
    }
}
