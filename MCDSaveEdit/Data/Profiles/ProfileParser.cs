using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MCDSaveEdit.Save.Models.Mapping;
#nullable enable

namespace MCDSaveEdit.Save.Models.Profiles
{
    public static class ProfileParser
    {
        public const int MaximumProfileBytes = 64 * 1024 * 1024;

        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions();

        static ProfileParser()
        {
            _options.Converters.Add(new AttributeBasedConverterFactory());
            _options.Converters.Add(new GuidConverterFactory());
            _options.Converters.Add(new JsonStringEnumConverter());
            //_options.Converters.Add(new TextDoubleJsonConverter());
            _options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            _options.WriteIndented = true;
            _options.AllowTrailingCommas = true;
        }

        public static async ValueTask<ProfileSaveFile?> Read(Stream stream)
        {
            // Console exports may append another object. Stop only at an unquoted
            // closing brace, and let the JSON parser validate the complete first value.
            using MemoryStream sanitized = new MemoryStream();
            int depth = 0;
            bool inString = false, escaped = false, started = false, complete = false;
            byte[] buffer = new byte[8192];
            int count, consumed = 0, bomPosition = 0;
            while (!complete && (count = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                for (int i = 0; i < count; i++)
                {
                    byte b = buffer[i];
                    if (++consumed > MaximumProfileBytes)
                        throw new InvalidDataException("Profile exceeds the supported size limit.");
                    if (!started)
                    {
                        if (consumed == 1 && b == 0xef) { bomPosition = 1; continue; }
                        if (bomPosition > 0 && bomPosition < 3)
                        {
                            if (b != (bomPosition == 1 ? 0xbb : 0xbf)) throw new JsonException("Invalid UTF-8 BOM.");
                            bomPosition++;
                            continue;
                        }
                        if (b == ' ' || b == '\r' || b == '\n' || b == '\t') continue;
                        if (b != '{') throw new JsonException("A profile must start with a JSON object.");
                        started = true;
                    }
                    sanitized.WriteByte(b);
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (b == '\\') escaped = true;
                        else if (b == '"') inString = false;
                    }
                    else if (b == '"') inString = true;
                    else if (b == '{') depth++;
                    else if (b == '}' && --depth == 0) { complete = true; break; }
                }
            }
            if (!complete) throw new JsonException("Incomplete profile JSON object.");
            // JsonElement can retain raw unknown values without decoding strings, so
            // validate all UTF-8 bytes before preserving any extension data.
            try { new UTF8Encoding(false, true).GetCharCount(sanitized.GetBuffer(), 0, (int)sanitized.Length); }
            catch (DecoderFallbackException error) { throw new JsonException("Profile contains malformed UTF-8.", error); }
            sanitized.Position = 0;
            ProfileSaveFile? obj = await JsonSerializer.DeserializeAsync<ProfileSaveFile>(sanitized, _options);
            //TODO: handle migration of Tower data from 1.15.1 to 1.16.2
            //if(obj?.MissionStatesMap?.TowerMissionContainer?.OldTowerMissionStateData != null && obj!.MissionStatesMap!.TowerMissionContainer!.OldTowerMissionStateData.Count > 0)
            //{
            //    var json = JsonSerializer.Serialize(obj!.MissionStatesMap!.TowerMissionContainer!.OldTowerMissionStateData);
            //    var oldTowerMissionStateData = JsonSerializer.Deserialize<TowerMissionState>(json);
            //    obj.MissionStatesMap.TowerMissionContainer = new TowerMissionContainer() { TowerMissionStates = new[] { oldTowerMissionStateData } };
            //    obj!.MissionStatesMap!.TowerMissionContainer!.OldTowerMissionStateData = null;
            //}
            return obj;
        }

        public static async ValueTask<Stream> Write(ProfileSaveFile settings)
        {
            Stream stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, settings, _options);
            stream.Position = 0;
            return stream;
        }
    }
}
