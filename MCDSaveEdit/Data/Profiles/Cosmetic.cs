using System.Text.Json.Serialization;
#nullable enable

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class Cosmetic
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }
}
