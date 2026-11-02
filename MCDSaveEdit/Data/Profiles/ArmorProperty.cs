using System;
using System.Text.Json.Serialization;
using MCDSaveEdit.Save.Models.Enums;

namespace MCDSaveEdit.Save.Models.Profiles
{
    public partial class Armorproperty
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> AdditionalData { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("rarity")]
        public Rarity Rarity { get; set; }
    }

    public partial class Armorproperty : ICloneable
    {
        public object Clone()
        {
            return Copy();
        }

        public Armorproperty Copy()
        {
            var copy = new Armorproperty();
            copy.Id = this.Id;
            copy.Rarity = this.Rarity;
            copy.AdditionalData = this.AdditionalData == null ? null : new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>(this.AdditionalData);
            return copy;
        }
    }
}
