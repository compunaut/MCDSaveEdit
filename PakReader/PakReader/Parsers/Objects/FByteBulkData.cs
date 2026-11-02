using System.IO;

namespace PakReader.Parsers.Objects
{
    public readonly struct FByteBulkData : IUStruct
    {
        // Memory saving, we don't need this
        //uint BulkDataFlags;
        //long ElementCount;
        //long BulkDataOffsetInFile;
        //long BulkDataSizeOnDisk;

        public readonly byte[] Data;

        internal FByteBulkData(BinaryReader reader, Stream ubulk, long ubulkOffset)
        {
            var BulkDataFlags = reader.ReadUInt32();

            var ElementCount = reader.ReadInt32();
            _ = reader.ReadInt32(); //BulkDataSizeOnDisk
            var BulkDataOffsetInFile = reader.ReadInt64();

            if (ElementCount < 0 || ElementCount > 256 * 1024 * 1024)
                throw new InvalidDataException("Invalid bulk payload size.");
            Data = null;
            if ((BulkDataFlags & (uint)EBulkDataFlags.BULKDATA_ForceInlinePayload) != 0 && ElementCount > 0)
            {
                Data = reader.ReadBytesExact(ElementCount);
            }

            if ((BulkDataFlags & (uint)EBulkDataFlags.BULKDATA_PayloadInSeperateFile) != 0)
            {
                if (ubulk != null)
                {
                    long start = checked(BulkDataOffsetInFile + ubulkOffset);
                    ReaderExtensions.ValidateRange(ubulk, start, ElementCount);
                    ubulk.Position = start;
                    Data = new byte[ElementCount];
                    ubulk.ReadExactly(Data);
                }
                //else throw new FileLoadException("No ubulk specified for texture");
            }
        }
    }
}
