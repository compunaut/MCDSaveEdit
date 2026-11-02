using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class MapUiState
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("panPosition")]
        public Dictionary<string, string> PanPosition { get; set; }

        [JsonPropertyName("selectedDifficulty")]
        public string SelectedDifficulty { get; set; }

        [JsonPropertyName("selectedMission")]
        public string SelectedMission { get; set; }

        [JsonPropertyName("selectedRealm")]
        public string SelectedRealm { get; set; }

        [JsonPropertyName("selectedThreatLevel")]
        public string SelectedThreatLevel { get; set; }
    }
}
