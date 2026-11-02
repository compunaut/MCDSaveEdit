using System.IO.Compression;
using System;
using System.IO;

namespace PakReader.Parsers.Objects
{
    public class FPakEntry
    {
        const byte Flag_Encrypted = 0x01;
        const byte Flag_Deleted = 0x02;

        public bool Encrypted => (Flags & Flag_Encrypted) != 0;
        public bool Deleted => (Flags & Flag_Deleted) != 0;

        public readonly string PakFileName;
        public readonly string Name;
        public readonly long Offset;
        public readonly long Size;
        public readonly long UncompressedSize;
        public readonly FPakCompressedBlock[] CompressionBlocks;
        public readonly uint CompressionBlockSize;
        public readonly uint CompressionMethodIndex;
        public readonly byte Flags;

        public readonly int StructSize;

        internal FPakEntry(BinaryReader reader, EPakVersion Version, int SubVersion, bool caseSensitive, string pakName)
        {
            CompressionBlocks = null;
            CompressionBlockSize = 0;
            Flags = 0;

            PakFileName = pakName;
            string name = caseSensitive ? reader.ReadFString() : reader.ReadFString().ToLowerInvariant();
            Name = name.StartsWith("/") ? name.Substring(1) : name;

            var StartOffset = reader.BaseStream.Position;

            Offset = reader.ReadInt64();
            Size = reader.ReadInt64();
            UncompressedSize = reader.ReadInt64();
            if (Version < EPakVersion.FNAME_BASED_COMPRESSION_METHOD)
            {
                var LegacyCompressionMethod = reader.ReadInt32();
                if (LegacyCompressionMethod == (int)ECompressionFlags.COMPRESS_None)
                {
                    CompressionMethodIndex = 0;
                }
                else if ((LegacyCompressionMethod & (int)ECompressionFlags.COMPRESS_ZLIB) != 0)
                {
                    CompressionMethodIndex = 1;
                }
                else if ((LegacyCompressionMethod & (int)ECompressionFlags.COMPRESS_GZIP) != 0)
                {
                    CompressionMethodIndex = 2;
                }
                else if ((LegacyCompressionMethod & (int)ECompressionFlags.COMPRESS_Custom) != 0)
                {
                    CompressionMethodIndex = 3;
                }
                else
                {
                    // https://github.com/EpicGames/UnrealEngine/blob/8b6414ae4bca5f93b878afadcc41ab518b09984f/Engine/Source/Runtime/PakFile/Public/IPlatformFilePak.h#L441
                    throw new FileLoadException(@"Found an unknown compression type in pak file, will need to be supported for legacy files");
                }
            }
            else
            {
                if (Version == EPakVersion.FNAME_BASED_COMPRESSION_METHOD && SubVersion == 0)
                    CompressionMethodIndex = reader.ReadByte();
                else
                    CompressionMethodIndex = reader.ReadUInt32();
            }
            if (Version <= EPakVersion.INITIAL) reader.ReadInt64(); // Timestamp
            reader.ReadBytesExact(20); // Hash
            if (Version >= EPakVersion.COMPRESSION_ENCRYPTION)
            {
                if (CompressionMethodIndex != 0)
                {
                    CompressionBlocks = reader.ReadTArray(() => new FPakCompressedBlock(reader));
                }

                Flags = reader.ReadByte();
                CompressionBlockSize = reader.ReadUInt32();
            }

            // Used to seek ahead to the file data instead of parsing the entry again
            StructSize = (int)(reader.BaseStream.Position - StartOffset);
        }

        internal FPakEntry(BinaryReader reader, bool caseSensitive, string pakName)
        {
            CompressionBlocks = null;
            CompressionBlockSize = 0;
            Flags = 0;

            PakFileName = pakName;
            Name = caseSensitive ? reader.ReadFString() : reader.ReadFString().ToLowerInvariant();

            var StartOffset = reader.BaseStream.Position;

            Offset = reader.ReadInt64();
            Size = reader.ReadInt64();
            UncompressedSize = reader.ReadInt64();
            CompressionMethodIndex = reader.ReadUInt32();
            reader.ReadBytesExact(20); // Hash
            if (CompressionMethodIndex != 0)
            {
                CompressionBlocks = reader.ReadTArray(() => new FPakCompressedBlock(reader));
            }
            Flags = reader.ReadByte();
            CompressionBlockSize = reader.ReadUInt32();

            // Used to seek ahead to the file data instead of parsing the entry again
            StructSize = (int)(reader.BaseStream.Position - StartOffset);
        }

        internal FPakEntry(string pakName, string name, long offset, long size, long uncompressedSize, FPakCompressedBlock[] compressionBlocks, uint compressionBlockSize, uint compressionMethodIndex, byte flags)
        {
            PakFileName = pakName;
            Name = name;
            Offset = offset;
            Size = size;
            UncompressedSize = uncompressedSize;
            CompressionBlocks = compressionBlocks;
            CompressionBlockSize = compressionBlockSize;
            CompressionMethodIndex = compressionMethodIndex;
            Flags = flags;
            StructSize = (int)GetSize(EPakVersion.LATEST, compressionMethodIndex, compressionBlocks != null ? (uint)compressionBlocks.Length : 0);
        }

        public ArraySegment<byte>? GetData(Stream stream, byte[] key, string[] compressionMethods)
        {
            lock (stream)
            {
                if (Offset < 0 || Size < 0 || UncompressedSize < 0 || UncompressedSize > 256 * 1024 * 1024)
                    throw new InvalidDataException("Invalid or oversized PAK entry.");
                // Reject impossible source ranges before allocating the advertised output.
                if (CompressionMethodIndex == 0U)
                {
                    long sourceLength = Encrypted ? checked((Size + 15) / 16 * 16) : Size;
                    ReaderExtensions.ValidateRange(stream, checked(Offset + StructSize), sourceLength);
                }
                else
                {
                    if (CompressionBlocks == null || CompressionBlocks.Length == 0 || CompressionBlockSize == 0 ||
                        (UncompressedSize + CompressionBlockSize - 1) / CompressionBlockSize != CompressionBlocks.Length)
                        throw new InvalidDataException("Invalid compression block count.");
                    foreach (var block in CompressionBlocks)
                    {
                        if (block.CompressedStart < StructSize || block.CompressedEnd < block.CompressedStart)
                            throw new InvalidDataException("Invalid PAK compression block.");
                        long sourceLength = Encrypted ? checked((block.Size + 15) / 16 * 16) : block.Size;
                        ReaderExtensions.ValidateRange(stream, checked(Offset + block.CompressedStart), sourceLength);
                    }
                }
                var data = new byte[(int)UncompressedSize];
                if (CompressionMethodIndex == 0U)
                {
                    if (Size != UncompressedSize)
                        throw new InvalidDataException("Uncompressed PAK entry has inconsistent sizes.");
                    long storedSize = Encrypted ? checked((Size + 15) / 16 * 16) : Size;
                    long start = checked(Offset + StructSize);
                    ReaderExtensions.ValidateRange(stream, start, storedSize);
                    stream.Position = start;
                    if (Encrypted)
                    {
                        var encrypted = new byte[(int)storedSize];
                        stream.ReadExactly(encrypted);
                        Array.Copy(AESDecryptor.DecryptAES(encrypted, key), data, data.Length);
                    }
                    else stream.ReadExactly(data);
                }
                else Decompress(stream, key, compressionMethods, data);
                return new ArraySegment<byte>(data);
            }
        }

        private void Decompress(Stream stream, byte[] key, string[] compressionMethods, byte[] outData)
        {
            if (compressionMethods == null || CompressionMethodIndex > compressionMethods.Length ||
                CompressionBlocks == null || CompressionBlockSize == 0)
                throw new InvalidDataException("Invalid PAK compression metadata.");
            string compressionMethod = compressionMethods[CompressionMethodIndex - 1];
            int bytesRead = 0;
            foreach (var block in CompressionBlocks)
            {
                if (bytesRead >= outData.Length || block.CompressedStart < StructSize || block.CompressedEnd < block.CompressedStart)
                    throw new InvalidDataException("Invalid PAK compression block.");
                long storedSize = Encrypted ? checked((block.Size + 15) / 16 * 16) : block.Size;
                long start = checked(Offset + block.CompressedStart);
                ReaderExtensions.ValidateRange(stream, start, storedSize);
                stream.Position = start;
                byte[] buffer = new byte[(int)storedSize];
                stream.ReadExactly(buffer);
                if (Encrypted) buffer = AESDecryptor.DecryptAES(buffer, key);
                using var blockMs = new MemoryStream(buffer, 0, checked((int)block.Size), false);
                using Stream compressionStream = compressionMethod switch
                {
                    "Zlib" => new ZLibStream(blockMs, CompressionMode.Decompress),
                    "Gzip" => new GZipStream(blockMs, CompressionMode.Decompress),
                    _ => throw new NotSupportedException($"Unsupported compression ({compressionMethod})")
                };
                int expected = (int)Math.Min(CompressionBlockSize, outData.Length - bytesRead);
                compressionStream.ReadExactly(outData.AsSpan(bytesRead, expected));
                if (compressionStream.ReadByte() != -1)
                    throw new InvalidDataException("Decompressed PAK block exceeds its declared size.");
                bytesRead += expected;
            }
            if (bytesRead != outData.Length)
                throw new InvalidDataException("Incomplete PAK compression blocks.");
        }

        public static long GetSize(EPakVersion version, uint CompressionMethodIndex = 0, uint CompressionBlocksCount = 0)
        {
            long SerializedSize = sizeof(long) + sizeof(long) + sizeof(long) + 20;

            if (version >= EPakVersion.FNAME_BASED_COMPRESSION_METHOD)
            {
                SerializedSize += sizeof(uint);
            }
            else
            {
                SerializedSize += sizeof(int); // Old CompressedMethod var from pre-fname based compression methods
            }

            if (version >= EPakVersion.COMPRESSION_ENCRYPTION)
            {
                SerializedSize += sizeof(byte) + sizeof(uint);
                if (CompressionMethodIndex != 0)
                {
                    SerializedSize += sizeof(long) * 2 * CompressionBlocksCount + sizeof(int);
                }
            }
            if (version < EPakVersion.NO_TIMESTAMPS)
            {
                // Timestamp
                SerializedSize += sizeof(long);
            }
            return SerializedSize;
        }

        public FPakEntry Uexp = null;
        public FPakEntry Ubulk = null;

        public bool IsUE4Package() => Name.Substring(Name.LastIndexOf(".")).Equals(".uasset");
        public bool IsLocres() => Name.Substring(Name.LastIndexOf(".")).Equals(".locres");
        public bool IsUE4Map() => Name.Substring(Name.LastIndexOf(".")).Equals(".umap");
        public bool IsUE4Font() => Name.Substring(Name.LastIndexOf(".")).Equals(".ufont");

        public bool HasUexp() => Uexp != null;
        public bool HasUbulk() => Ubulk != null;

        public bool IsCompressed() => UncompressedSize != Size || CompressionMethodIndex != (int)ECompressionFlags.COMPRESS_None;
        public string GetExtension() => Name.Substring(Name.LastIndexOf("."));
        public string GetPathWithoutFile()
        {
            int stop = Name.LastIndexOf("/");
            if (stop <= -1)
                stop = 0;
            return Name.Substring(0, stop);
        }
        public string GetPathWithoutExtension() => Name.Substring(0, Name.LastIndexOf("."));
        public string GetNameWithExtension() => Name.Substring(Name.LastIndexOf("/") + 1);
        public string GetNameWithoutExtension()
        {
            int start = Name.LastIndexOf("/") + 1;
            int stop = Name.LastIndexOf(".") - start;
            return Name.Substring(start, stop);
        }

        public override string ToString() => Name;
    }
}
