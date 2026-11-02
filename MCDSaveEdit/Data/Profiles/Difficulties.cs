using MCDSaveEdit.Save.Models.Enums;
using System.Text.Json.Serialization;
#nullable enable

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class Difficulties
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("announced")]
        public DifficultyEnum? Announced { get; set; } = null;

        [JsonPropertyName("selected")]
        public DifficultyEnum? Selected { get; set; } = null;

        [JsonPropertyName("unlocked")]
        public DifficultyEnum Unlocked { get; set; } = DifficultyEnum.Difficulty_1;
    }
}
