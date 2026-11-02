using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
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

    }
}
