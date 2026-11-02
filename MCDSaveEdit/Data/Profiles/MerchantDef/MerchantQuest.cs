using System.Text.Json.Serialization;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public class MerchantQuest
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("dynamicQuestState")]
        public object DynamicQuestState { get; set; }

        [JsonPropertyName("questState")]
        public object QuestState { get; set; }

    }
}
