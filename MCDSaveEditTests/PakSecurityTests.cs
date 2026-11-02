using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using PakReader.Parsers;
using PakReader.Parsers.Class;
using PakReader.Parsers.PropertyTagData;
using System.Security.Cryptography;
using SkiaSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PakReader.Pak;
using PakReader.Parsers.Objects;

namespace MCDSaveEditTests
{
    [TestClass]
    public class PakSecurityTests
    {
        [TestMethod]
        public void TruncatedFooterFailsBeforeSeeking()
        {
            Assert.ThrowsExactly<InvalidDataException>(() => new PakFileReader("truncated", new MemoryStream(new byte[8])));
        }

        [TestMethod]
        public void TruncatedDxtIsRejectedBeforeNativeDecode()
        {
            var decoder = typeof(PakFileReader).Assembly.GetType("PakReader.TextureDecoder", true);
            var method = decoder.GetMethod("DecodeImage", BindingFlags.Public | BindingFlags.Static);
            var error = Assert.ThrowsExactly<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { new byte[7], 4, 4, 1, EPixelFormat.PF_DXT1 }));
            Assert.IsInstanceOfType(error.InnerException, typeof(InvalidDataException));
        }

        [TestMethod]
        public void TextureDimensionOverflowIsRejected()
        {
            var decoder = typeof(PakFileReader).Assembly.GetType("PakReader.TextureDecoder", true);
            var method = decoder.GetMethod("DecodeImage", BindingFlags.Public | BindingFlags.Static);
            var error = Assert.ThrowsExactly<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { Array.Empty<byte>(), int.MaxValue, int.MaxValue, 1, EPixelFormat.PF_B8G8R8A8 }));
            Assert.IsInstanceOfType(error.InnerException, typeof(InvalidDataException));
        }

        [TestMethod]
        public void EntryPastEndDoesNotReturnZeroPaddedData()
        {
            var constructor = typeof(FPakEntry).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(string), typeof(long), typeof(long), typeof(long),
                    typeof(FPakCompressedBlock[]), typeof(uint), typeof(uint), typeof(byte) }, null);
            var entry = (FPakEntry)constructor.Invoke(new object[] { "test", "a", 0L, 32L, 32L, null, 0U, 0U, (byte)0 });
            Assert.ThrowsExactly<InvalidDataException>(() => entry.GetData(new MemoryStream(new byte[60]), null, Array.Empty<string>()));
        }
        [TestMethod]
        public void TruncatedDecompressedBlockFailsInsteadOfPadding()
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
                zlib.Write(new byte[] { 1, 2, 3 });
            byte[] payload = compressed.ToArray();
            long headerSize = FPakEntry.GetSize(EPakVersion.LATEST, 1, 1);
            var blockCtor = typeof(FPakCompressedBlock).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(long), typeof(long) }, null);
            var block = (FPakCompressedBlock)blockCtor.Invoke(new object[] { headerSize, headerSize + payload.Length });
            var constructor = typeof(FPakEntry).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(string), typeof(long), typeof(long), typeof(long),
                    typeof(FPakCompressedBlock[]), typeof(uint), typeof(uint), typeof(byte) }, null);
            var entry = (FPakEntry)constructor.Invoke(new object[] { "test", "a", 0L, (long)payload.Length, 4L,
                new[] { block }, 4U, 1U, (byte)0 });
            using var stream = new MemoryStream();
            stream.Write(new byte[(int)headerSize]);
            stream.Write(payload);
            Assert.ThrowsExactly<EndOfStreamException>(() => entry.GetData(stream, null, new[] { "Zlib" }));
        }

        [TestMethod]
        public void NativeTextureOwnsPixelsAcrossManagedCollections()
        {
            var method = typeof(PakFileReader).Assembly.GetType("PakReader.TextureDecoder", true)
                .GetMethod("DecodeImage", BindingFlags.Public | BindingFlags.Static);
            for (int iteration = 0; iteration < 32; iteration++)
            {
                byte[] pixels = { 3, 2, 1, 255, 30, 20, 10, 255 };
                using var image = (SKImage)method.Invoke(null, new object[] { pixels, 2, 1, 1, EPixelFormat.PF_B8G8R8A8 });
                Array.Clear(pixels, 0, pixels.Length);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                using var copy = SKBitmap.FromImage(image);
                Assert.AreEqual(new SKColor(1, 2, 3, 255), copy.GetPixel(0, 0));
                Assert.AreEqual(new SKColor(10, 20, 30, 255), copy.GetPixel(1, 0));
            }
        }

        [TestMethod]
        public void WrongPakKeyDoesNotPreventLaterCorrectKey()
        {
            byte[] key = new byte[32];
            key[0] = 42;
            byte[] plaintext = new byte[16];
            plaintext[0] = 2; // ANSI FString length, including its terminator.
            plaintext[4] = (byte)'/'; // Mount point, followed by zero file count and padding.
            using var aes = Aes.Create();
            aes.Key = key;
            byte[] encrypted = aes.EncryptEcb(plaintext, PaddingMode.None);
            using var pakBytes = new MemoryStream();
            using (var writer = new BinaryWriter(pakBytes, System.Text.Encoding.UTF8, true))
            {
                writer.Write(encrypted);
                writer.Write(new byte[16]); // Encryption GUID.
                writer.Write((byte)1);
                writer.Write(0x5A6F12E1U);
                writer.Write((int)EPakVersion.ENCRYPTION_KEY_GUID);
                writer.Write(0L);
                writer.Write((long)encrypted.Length);
                writer.Write(SHA1.HashData(plaintext));
            }
            pakBytes.Position = 0;
            var reader = new PakFileReader("synthetic.pak", pakBytes);
            Assert.IsFalse(reader.TryReadIndex(new byte[32]));
            Assert.IsFalse(reader.Initialized);
            Assert.IsTrue(reader.TryReadIndex(key));
            Assert.IsTrue(reader.Initialized);
        }

        [TestMethod]
        public void UnencryptedPakInitializesWithoutAKey()
        {
            using var bytes = MinimalPak(null);
            var index = new PakIndex(new[] { bytes }, false, true, (PakFilter)null!);
            Assert.AreEqual(0, index.InitializedPakFileCount);
            Assert.AreEqual(1, index.UseKey((byte[])null!));
            Assert.AreEqual(index.PakFileCount, index.InitializedPakFileCount);
        }

        [TestMethod]
        public void MixedPakSetRemainsIncompleteUntilMatchingKeyInitializesEveryPak()
        {
            byte[] key = new byte[32]; key[0] = 42;
            using var plain = MinimalPak(null);
            using var encrypted = MinimalPak(key, nonzeroGuid: true);
            var index = new PakIndex(new[] { plain, encrypted }, false, true, (PakFilter)null!);
            Assert.AreEqual(1, index.UseKey(new byte[32]));
            Assert.AreEqual(1, index.InitializedPakFileCount);
            Assert.AreNotEqual(index.PakFileCount, index.InitializedPakFileCount);
            Assert.AreEqual(1, index.UseKey(key));
            Assert.AreEqual(index.PakFileCount, index.InitializedPakFileCount);
        }

        [TestMethod]
        public void SuppliedKeyInitializesNonzeroGuidPak()
        {
            byte[] key = new byte[32]; key[0] = 42;
            using var encrypted = MinimalPak(key, nonzeroGuid: true);
            var index = new PakIndex(new[] { encrypted }, false, true, (PakFilter)null!);
            Assert.AreEqual(0, index.UseKey(FGuid.Zero, key));
            Assert.AreEqual(1, index.UseKey(key));
            Assert.AreEqual(1, index.InitializedPakFileCount);
        }

        private static MemoryStream MinimalPak(byte[]? key, bool nonzeroGuid = false)
        {
            byte[] plaintext = new byte[16];
            plaintext[0] = 2; plaintext[4] = (byte)'/';
            byte[] payload = plaintext;
            if (key != null)
            {
                using var aes = Aes.Create(); aes.Key = key;
                payload = aes.EncryptEcb(plaintext, PaddingMode.None);
            }
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(payload);
                byte[] guid = new byte[16]; if (nonzeroGuid) guid[0] = 1;
                writer.Write(guid); writer.Write((byte)(key == null ? 0 : 1));
                writer.Write(0x5A6F12E1U); writer.Write((int)EPakVersion.ENCRYPTION_KEY_GUID);
                writer.Write(0L); writer.Write((long)payload.Length); writer.Write(SHA1.HashData(plaintext));
            }
            stream.Position = 0;
            return stream;
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(1000001)]
        public void InvalidGenerationCountIsRejectedAtCountBeforeReadingElements(int count)
        {
            // A legacy -4 summary reaches GenerationCount at byte 100. The large
            // case is just above the practical cap, avoiding an exhaustion payload.
            byte[] header = new byte[104];
            BitConverter.GetBytes(0x9E2A83C1U).CopyTo(header, 0);
            BitConverter.GetBytes(-4).CopyTo(header, 4);
            BitConverter.GetBytes(count).CopyTo(header, 100);
            using var stream = new OperationBoundedStream(header);
            using var reader = new BinaryReader(stream);
            var constructor = typeof(FPackageFileSummary).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(BinaryReader) }, null)!;
            var error = Assert.ThrowsExactly<TargetInvocationException>(() => constructor.Invoke(new object[] { reader }));
            Assert.IsInstanceOfType<InvalidDataException>(error.InnerException);
            Assert.AreEqual(104L, stream.Position, "No generation element should be read.");
        }

        [TestMethod]
        public void NegativePropertySizeFailsBeforePayloadReadOrRewind()
        {
            using var stream = new OperationBoundedStream(BuildIntObject(-25));
            var package = MakePackageReader(stream);
            Assert.ThrowsExactly<InvalidDataException>(() => new UObject(package, stream.Length));
            Assert.AreEqual(20L, stream.Position, "Reject the size before reading the rest of the tag.");
            Assert.AreEqual(0, stream.Rewinds);
        }

        [TestMethod]
        public void PropertyAdvertisedOutsideContainingBytesIsRejected()
        {
            using var stream = new OperationBoundedStream(BuildIntObject(1024));
            var package = MakePackageReader(stream);
            Assert.ThrowsExactly<InvalidDataException>(() => new UObject(package, stream.Length));
            Assert.AreEqual(25L, stream.Position, "Reject before decoding the payload.");
        }

        [TestMethod]
        public void PropertyCannotUseBytesBeyondDeclaredExportBoundary()
        {
            // The backing stream has a complete property, but the containing export
            // ends at its tag. Payload bytes belong to the following export.
            using var stream = new OperationBoundedStream(BuildIntObject(4));
            var package = MakePackageReader(stream);
            Assert.ThrowsExactly<InvalidDataException>(() => new UObject(package, 25L));
            Assert.AreEqual(25L, stream.Position);
        }

        [TestMethod]
        public void ShortPropertyCannotReadFollowingTagAsItsPayload()
        {
            using var stream = new OperationBoundedStream(BuildIntObject(1));
            var package = MakePackageReader(stream);
            Assert.ThrowsExactly<EndOfStreamException>(() => new UObject(package, stream.Length));
            Assert.IsTrue(stream.Position <= 26, "A one-byte payload cannot consume the following property bytes.");
        }

        [TestMethod]
        public void ValidIntPropertyAndNoneTerminatorRemainReadable()
        {
            using var stream = new OperationBoundedStream(BuildIntObject(4));
            var package = MakePackageReader(stream);
            var value = new UObject(package, stream.Length);
            Assert.AreEqual(1, value.Count);
            Assert.AreEqual(123, ((IntProperty)value["Field"]).GetValue());
            Assert.AreEqual(stream.Length, stream.Position);
        }

        [TestMethod]
        [DataRow("ArrayProperty", -1)]
        [DataRow("ArrayProperty", 1000001)]
        [DataRow("SetProperty", -1)]
        [DataRow("SetProperty", 1000001)]
        [DataRow("MapProperty", -1)]
        [DataRow("MapProperty", 1000001)]
        public void CollectionPropertyRejectsInvalidNestedCountBeforeAllocation(string type, int count)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                WriteName(writer, 0); // Field.
                WriteName(writer, 1); // Collection type.
                writer.Write(type == "ArrayProperty" ? 4 : 8); // Payload is just count metadata.
                writer.Write(0); // Array index.
                WriteName(writer, 2); // IntProperty inner/key type.
                if (type == "MapProperty") WriteName(writer, 2); // IntProperty value type.
                writer.Write((byte)0); // No property GUID.
                if (type != "ArrayProperty") writer.Write(0); // No removed keys.
                writer.Write(count);
                WriteName(writer, 3); // None terminator lies outside the collection payload.
                writer.Write(0);
            }
            using var stream = new OperationBoundedStream(bytes.ToArray());
            var package = MakePackageReader(stream, type);
            Assert.ThrowsExactly<InvalidDataException>(() => new UObject(package, stream.Length));
            Assert.IsTrue(stream.Position < stream.Length - 4, "The count must fail inside its property payload.");
        }

        private static byte[] BuildIntObject(int payloadSize)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                WriteName(writer, 0);
                WriteName(writer, 1);
                writer.Write(payloadSize);
                writer.Write(0);
                writer.Write((byte)0);
                writer.Write(123);
                WriteName(writer, 3);
                writer.Write(0); // No object GUID after None.
            }
            return bytes.ToArray();
        }

        private static void WriteName(BinaryWriter writer, int index)
        {
            writer.Write(index);
            writer.Write(0);
        }

        private static PackageReader MakePackageReader(Stream stream, string type = "IntProperty")
        {
            // Exercise real property readers without needing unrelated package tables.
            var package = (PackageReader)RuntimeHelpers.GetUninitializedObject(typeof(PackageReader));
            typeof(PackageReader).GetField("<Loader>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(package, new BinaryReader(stream, Encoding.UTF8, true));
            Type nameType = typeof(PackageReader).Assembly.GetType("PakReader.Parsers.Objects.FNameEntrySerialized", true)!;
            string[] labels = { "Field", type, "IntProperty", "None" };
            var names = Array.CreateInstance(nameType, labels.Length);
            for (int index = 0; index < labels.Length; index++)
            {
                using var nameBytes = new MemoryStream();
                using (var writer = new BinaryWriter(nameBytes, Encoding.UTF8, true))
                {
                    writer.Write(labels[index].Length + 1);
                    writer.Write(Encoding.UTF8.GetBytes(labels[index]));
                    writer.Write((byte)0);
                    writer.Write(0); // Name hashes.
                }
                nameBytes.Position = 0;
                using var reader = new BinaryReader(nameBytes);
                names.SetValue(Activator.CreateInstance(nameType, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { reader }, null), index);
            }
            typeof(PackageReader).GetField("<NameMap>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(package, names);
            return package;
        }

        private sealed class OperationBoundedStream : MemoryStream
        {
            private int operations;
            public int Rewinds { get; private set; }
            public OperationBoundedStream(byte[] bytes) : base(bytes) { }
            private void Step() { if (++operations > 256) throw new InvalidOperationException("Parser exceeded the test operation guard."); }
            public override long Position
            {
                get => base.Position;
                set { Step(); if (value < base.Position) Rewinds++; base.Position = value; }
            }
            public override int ReadByte() { Step(); return base.ReadByte(); }
            public override int Read(byte[] buffer, int offset, int count) { Step(); return base.Read(buffer, offset, count); }
            public override int Read(Span<byte> buffer) { Step(); return base.Read(buffer); }
        }

    }
}
