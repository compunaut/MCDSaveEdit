using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public class MerchantDef
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("everInteracted")]
        public bool EverInteracted { get; set; }

        [JsonPropertyName("pricing")]
        public MerchantPricing Pricing { get; set; }

        [JsonPropertyName("quests")]
        public Dictionary<string, MerchantQuest> Quests { get; set; }

        [JsonPropertyName("slots")]
        public Dictionary<string, MerchantSlot> Slots { get; set; }
    }
}
