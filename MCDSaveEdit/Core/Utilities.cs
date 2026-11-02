using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MCDSaveEdit
{
    public static class Utilities
    {
        public static string prettyJson(string unPrettyJson)
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(unPrettyJson);
            var options = new JsonSerializerOptions() { WriteIndented = true };
            return JsonSerializer.Serialize(jsonElement, options);
        }

        public static Dictionary<T1, T2> concatMissingFrom<T1, T2>(this Dictionary<T1, T2> dictA, Dictionary<T1, T2> dictB)
        {
            return dictA.Concat(dictB.Where(kvp => !dictA.ContainsKey(kvp.Key))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

    }
}
