using System.Text.Json.Serialization;

namespace RITC
{
    public class Package
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("active")]
        public bool IsActive { get; set; }
    }
}

