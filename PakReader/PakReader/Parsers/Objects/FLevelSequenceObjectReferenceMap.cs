using System.Collections.Generic;

namespace PakReader.Parsers.Objects
{
    public readonly struct FLevelSequenceObjectReferenceMap : IUStruct
    {
        public readonly Dictionary<FGuid, FLevelSequenceLegacyObjectReference> Map;

        internal FLevelSequenceObjectReferenceMap(PackageReader reader)
        {
            int count = reader.ValidateCount(reader.ReadInt32(), 16, 128);
            Map = new Dictionary<FGuid, FLevelSequenceLegacyObjectReference>(count);
            for (int i = 0; i < count; i++)
            {
                Map[new FGuid(reader)] = new FLevelSequenceLegacyObjectReference(reader);
            }
        }
    }
}
