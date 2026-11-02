using System.Text.Json.Serialization;
#nullable enable

namespace MCDSaveEdit.Save.Models.Profiles
{
    public class Currency
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("count")]
        public ulong Count { get; set; } = 0;
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }
}
