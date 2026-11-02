using System.Text.Json.Serialization;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class UiHintsExpired
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("hintType")]
        public string HintType { get; set; }
    }
}
