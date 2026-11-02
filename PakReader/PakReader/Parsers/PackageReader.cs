using System;
using System.IO;
using System.Linq;
using PakReader.Parsers.Class;
using PakReader.Parsers.Objects;

namespace PakReader.Parsers
{
    public sealed class PackageReader
    {
        BinaryReader Loader { get; set; }
        int nestingDepth;
        int propertyCount;

        public FPackageFileSummary PackageFileSummary { get; }
        FNameEntrySerialized[] NameMap { get; }
        public FObjectImport[] ImportMap { get; }
        public FObjectExport[] ExportMap { get; }

        public IUExport[] DataExports { get; }
        public FName[] DataExportTypes { get; }

        public PackageReader(string path) : this(path + ".uasset", path + ".uexp", path + ".ubulk") { }
        public PackageReader(string uasset, string uexp, string ubulk) : this(File.OpenRead(uasset), File.OpenRead(uexp), File.Exists(ubulk) ? File.OpenRead(ubulk) : null) { }
        public PackageReader(Stream uasset, Stream uexp, Stream ubulk) : this(new BinaryReader(uasset), new BinaryReader(uexp), ubulk) { }

        PackageReader(BinaryReader uasset, BinaryReader uexp, Stream ubulk)
        {
            Loader = uasset;
            PackageFileSummary = new FPackageFileSummary(Loader);

            NameMap = SerializeNameMap();
            ImportMap = SerializeImportMap();
            ExportMap = SerializeExportMap();
            DataExports = new IUExport[ExportMap.Length];
            DataExportTypes = new FName[ExportMap.Length];
            Loader = uexp;
            for(int i = 0; i < ExportMap.Length; i++)
            {
                FObjectExport Export = ExportMap[i];
                {
                    FName ExportType;
                    if (Export.ClassIndex.IsNull)
                        ExportType = DataExportTypes[i] = ReadFName(); // check if this is true, I don't know if Fortnite ever uses this
                    else if (Export.ClassIndex.IsExport)
                        ExportType = DataExportTypes[i] = ExportMap[Export.ClassIndex.AsExport].SuperIndex.Resource.ObjectName;
                    else if (Export.ClassIndex.IsImport)
                        ExportType = DataExportTypes[i] = ImportMap[Export.ClassIndex.AsImport].ObjectName;
                    else
                        throw new FileLoadException("Can't get class name"); // Shouldn't reach this unless the laws of math have bent to MagmaReef's will

                    if (ExportType.String.Equals("BlueprintGeneratedClass")) continue;

                    long exportOffset = checked(Export.SerialOffset - PackageFileSummary.TotalHeaderSize);
                    ReaderExtensions.ValidateRange(Loader.BaseStream, exportOffset, Export.SerialSize);
                    var pos = Position = exportOffset;
                    using var exportScope = EnterScope(checked(exportOffset + Export.SerialSize));
                    DataExports[i] = ExportType.String switch
                    {
                        "Texture2D" => new UTexture2D(this, ubulk, ExportMap.Sum(e => e.SerialSize) + PackageFileSummary.TotalHeaderSize),
                        "CurveTable" => new UCurveTable(this),
                        "DataTable" => new UDataTable(this),
                        "FontFace" => new UFontFace(this, ubulk),
                        "SoundWave" => new USoundWave(this, ubulk, ExportMap.Sum(e => e.SerialSize) + PackageFileSummary.TotalHeaderSize),
                        "StringTable" => new UStringTable(this),
                        _ => new UObject(this),
                    };

#if DEBUG
                    if (pos + Export.SerialSize != Position)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExportType={ExportType.String}] Didn't read {Export.ObjectName} correctly (at {Position}, should be {pos + Export.SerialSize}, {pos + Export.SerialSize - Position} behind)");
                    }
#endif
                }
            }
            return;
        }

        FNameEntrySerialized[] SerializeNameMap()
        {
            ValidateTable(PackageFileSummary.NameCount, PackageFileSummary.NameOffset);
            if (PackageFileSummary.NameCount > 0)
            {
                Loader.BaseStream.Position = PackageFileSummary.NameOffset;

                var OutNameMap = new FNameEntrySerialized[PackageFileSummary.NameCount];
                for (int NameMapIdx = 0; NameMapIdx < PackageFileSummary.NameCount; ++NameMapIdx)
                {
                    // Read the name entry from the file.
                    OutNameMap[NameMapIdx] = new FNameEntrySerialized(Loader);
                }
                return OutNameMap;
            }
            return Array.Empty<FNameEntrySerialized>();
        }

        FObjectImport[] SerializeImportMap()
        {
            ValidateTable(PackageFileSummary.ImportCount, PackageFileSummary.ImportOffset);
            if (PackageFileSummary.ImportCount > 0)
            {
                Loader.BaseStream.Position = PackageFileSummary.ImportOffset;

                var OutImportMap = new FObjectImport[PackageFileSummary.ImportCount];
                for (int ImportMapIdx = 0; ImportMapIdx < PackageFileSummary.ImportCount; ++ImportMapIdx)
                {
                    OutImportMap[ImportMapIdx] = new FObjectImport(this);
                }
                return OutImportMap;
            }
            return Array.Empty<FObjectImport>();
        }

        FObjectExport[] SerializeExportMap()
        {
            ValidateTable(PackageFileSummary.ExportCount, PackageFileSummary.ExportOffset);
            if (PackageFileSummary.ExportCount > 0)
            {
                Loader.BaseStream.Position = PackageFileSummary.ExportOffset;

                var OutExportMap = new FObjectExport[PackageFileSummary.ExportCount];
                for (int ExportMapIdx = 0; ExportMapIdx < PackageFileSummary.ExportCount; ++ExportMapIdx)
                {
                    OutExportMap[ExportMapIdx] = new FObjectExport(this);
                }
                return OutExportMap;
            }
            return Array.Empty<FObjectExport>();
        }

        void ValidateTable(int count, int offset)
        {
            if (count < 0 || count > 1000000 || offset < 0 || offset > Loader.BaseStream.Length ||
                count > (Loader.BaseStream.Length - offset) / 4)
                throw new InvalidDataException("Invalid package table count or offset.");
            long saved = Position;
            Position = offset;
            Loader.ValidateCount(count, 4);
            Position = saved;
        }

        public int ValidateCount(long count, int minimumBytes = 1, int allocationBytes = 64)
            => Loader.ValidateCount(count, minimumBytes, allocationBytes);

        internal IDisposable EnterScope(long end)
        {
            if (Loader.BaseStream is not ParseStream)
                Loader = new BinaryReader(new ParseStream(Loader.BaseStream), System.Text.Encoding.UTF8, true);
            var stream = (ParseStream)Loader.BaseStream;
            if (end < Position || end > stream.Length || nestingDepth >= 64)
                throw new InvalidDataException("Invalid package payload boundary or nesting depth.");
            nestingDepth++;
            long start = stream.Start, previousEnd = stream.End;
            stream.Start = Position;
            stream.End = end;
            return new Scope(() => { stream.Start = start; stream.End = previousEnd; nestingDepth--; });
        }

        internal IDisposable EnterProperty(int size)
        {
            if (size < 0 || ++propertyCount > 1000000)
                throw new InvalidDataException("Invalid property size or excessive property count.");
            return EnterScope(checked(Position + size));
        }

        private sealed class Scope : IDisposable
        {
            Action? close;
            internal Scope(Action close) { this.close = close; }
            public void Dispose() { var action = close; close = null; action?.Invoke(); }
        }

        // All implicit BinaryReader consumers share these bounds, including bulk and
        // nested struct readers, so they cannot read into the next export/property.
        private sealed class ParseStream : Stream
        {
            readonly Stream source;
            internal long Start, End;
            internal ParseStream(Stream source) { this.source = source; End = source.Length; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => End;
            public override long Position
            {
                get => source.Position;
                set { if (value < Start || value > End) throw new InvalidDataException("Package seek exceeds its payload."); source.Position = value; }
            }
            public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, (int)Math.Min(count, End - Position));
            public override int Read(Span<byte> buffer) => source.Read(buffer[..(int)Math.Min(buffer.Length, End - Position)]);
            public override int ReadByte() => Position < End ? source.ReadByte() : -1;
            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? Position : End) + offset);
                return Position;
            }
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public FName ReadFName()
        {
            var NameIndex = Loader.ReadInt32();
            var Number = Loader.ReadInt32();

            // https://github.com/EpicGames/UnrealEngine/blob/bf95c2cbc703123e08ab54e3ceccdd47e48d224a/Engine/Source/Runtime/CoreUObject/Public/UObject/LinkerLoad.h#L821
            // Has some more complicated stuff related to name map pools etc. that seems unnecessary atm
            if (NameIndex >= 0 && NameIndex < NameMap.Length)
            {
                return new FName(NameMap[NameIndex], NameIndex, Number);
            }
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Bad Name Index: {NameIndex}/{NameMap.Length} - Loader Position: {Loader.BaseStream.Position}");
#endif
            return default;
        }


        public static implicit operator BinaryReader(PackageReader reader) => reader.Loader;

        public byte ReadByte() => Loader.ReadByte();
        public sbyte ReadSByte() => Loader.ReadSByte();
        public byte[] ReadBytes(int count) => Loader.ReadBytesExact(count);
        public string ReadFString() => Loader.ReadFString();
        public T[] ReadTArray<T>(Func<T> Getter) => Loader.ReadTArray(Getter);

        public short ReadInt16() => Loader.ReadInt16();
        public ushort ReadUInt16() => Loader.ReadUInt16();
        public int ReadInt32() => Loader.ReadInt32();
        public uint ReadUInt32() => Loader.ReadUInt32();
        public long ReadInt64() => Loader.ReadInt64();
        public ulong ReadUInt64() => Loader.ReadUInt64();
        public float ReadFloat() => Loader.ReadSingle();
        public double ReadDouble() => Loader.ReadDouble();

        public long Position { get => Loader.BaseStream.Position; set => Loader.BaseStream.Position = value; }
    }
}
