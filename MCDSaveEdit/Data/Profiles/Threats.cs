using System.Text.Json.Serialization;
using MCDSaveEdit.Save.Models.Enums;
#nullable enable

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class ThreatLevels
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("unlocked")]
        public string? Unlocked { get; set; } = null;
    }
}
