using System.Text.Json.Serialization;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public class MerchantSlot
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("item")]
        public Item Item { get; set; }

        [JsonPropertyName("priceMultiplier")]
        public double PriceMultiplier { get; set; }

        [JsonPropertyName("rebateFraction")]
        public double RebateFraction { get; set; }

        [JsonPropertyName("reserved")]
        public bool Reserved { get; set; }

    }
}
