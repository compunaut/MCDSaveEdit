using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DungeonTools.Save.File;
using MCDSaveEdit.Logic;
using MCDSaveEdit.Save.Models.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MCDSaveEditTests
{
    [TestClass]
    public class SaveSafetyTests
    {
        [TestMethod]
        public async Task PublicGameFixtureDecryptsAndPreservesPopulatedFieldsAcrossSave()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Blank.dat");
            using var input = File.OpenRead(fixture);
            Assert.IsTrue(SaveFileHandler.IsFileEncrypted(input));
            using var decrypted = await FileProcessHelper.Decrypt(input);
            using var original = await JsonDocument.ParseAsync(decrypted);
            decrypted.Position = 0;
            var profile = await ProfileParser.Read(decrypted);
            Assert.AreEqual(0L, profile.Xp);
            Assert.AreEqual(1L, profile.Version);
            Assert.AreEqual("steve", profile.Skin);
            Assert.AreEqual("C2BC12F8-4800-51B5-D9E3-6F9E2865F96D", profile.PlayerId);
            using var serialized = await ProfileParser.Write(profile);
            using var encrypted = await FileProcessHelper.Encrypt(serialized);
            Assert.IsTrue(SaveFileHandler.IsFileEncrypted(encrypted));
            using var reopenedJson = await FileProcessHelper.Decrypt(encrypted);
            var reopened = await ProfileParser.Read(reopenedJson);
            using var reopenedSerialized = await ProfileParser.Write(reopened);
            using var result = await JsonDocument.ParseAsync(reopenedSerialized);
            foreach (var property in original.RootElement.EnumerateObject())
            {
                // The serializer intentionally omits null-valued known properties.
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                Assert.IsTrue(result.RootElement.TryGetProperty(property.Name, out var actual), property.Name);
                Assert.IsTrue(JsonElement.DeepEquals(property.Value, actual), property.Name);
            }
        }

        [TestMethod]
        public async Task ParserPreservesQuotedBracesAndNestedUnknownFields()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"a}\\\"{b\",\"future\":{\"flag\":true},\"items\":[{\"futureItem\":42}]}{\"duplicate\":true}"));
            var profile = await ProfileParser.Read(input);
            Assert.AreEqual("a}\"{b", profile.Name);
            Assert.IsTrue(input.CanRead, "The parser does not own the caller's stream.");
            using var output = await ProfileParser.Write(profile);
            using var document = await JsonDocument.ParseAsync(output);
            Assert.IsTrue(document.RootElement.GetProperty("future").GetProperty("flag").GetBoolean());
            Assert.AreEqual(42, document.RootElement.GetProperty("items")[0].GetProperty("futureItem").GetInt32());
        }

        [TestMethod]
        public async Task UnknownFieldsSurviveNestedMerchantMapsAndItemCopies()
        {
            const string json = "{\"merchantData\":{\"newMerchant\":{\"futureMerchant\":7,\"slots\":{\"newSlot\":{\"futureSlot\":[true],\"item\":{\"futureItem\":{\"nested\":9},\"enchantments\":[{\"futureEnchant\":11}],\"armorproperties\":[{\"futureArmor\":13}]}}}}}}";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var profile = await ProfileParser.Read(input);
            var item = profile.MerchantData["newMerchant"].Slots["newSlot"].Item.Copy();
            Assert.AreEqual(9, item.AdditionalData["futureItem"].GetProperty("nested").GetInt32());
            Assert.AreEqual(11, item.Enchantments[0].AdditionalData["futureEnchant"].GetInt32());
            Assert.AreEqual(13, item.Armorproperties[0].AdditionalData["futureArmor"].GetInt32());
            using var output = await ProfileParser.Write(profile);
            using var document = await JsonDocument.ParseAsync(output);
            var merchant = document.RootElement.GetProperty("merchantData").GetProperty("newMerchant");
            Assert.AreEqual(7, merchant.GetProperty("futureMerchant").GetInt32());
            Assert.IsTrue(merchant.GetProperty("slots").GetProperty("newSlot").GetProperty("futureSlot")[0].GetBoolean());
        }

        [TestMethod]
        public async Task DuplicatePreservesAllSavedMetadataExceptPlacement()
        {
            const string json = """
                {"items":[{
                  "armorproperties":[{"id":"armor","rarity":"Rare","futureArmor":13}],
                  "cloned":true,"enchantments":[{"id":"enchant","investedPoints":12,"level":3,"futureEnchant":11}],
                  "equipmentSlot":"MeleeGear","gifted":true,"inventoryIndex":7,"modified":true,"markedNew":false,
                  "netheriteEnchant":{"id":"netherite","investedPoints":9,"level":2,"futureNetherite":{"flag":true}},
                  "power":123.5,"rarity":"Unique","timesmodified":7,"type":"Sword","upgraded":true,
                  "futureItem":{"nested":9}
                }]}
                """;
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var profile = await ProfileParser.Read(input);
            using var originalStream = await ProfileParser.Write(profile);
            using var original = await JsonDocument.ParseAsync(originalStream);
            var expected = System.Text.Json.Nodes.JsonNode.Parse(original.RootElement.GetProperty("items")[0].GetRawText())!.AsObject();
            expected.Remove("equipmentSlot");
            expected.Remove("inventoryIndex");
            var source = profile.Items[0];
            var copy = source.Copy();
            Assert.IsNull(copy.EquipmentSlot);
            Assert.IsNull(copy.InventoryIndex);
            Assert.AreNotSame(source.Enchantments, copy.Enchantments);
            Assert.AreNotSame(source.Enchantments[0], copy.Enchantments[0]);
            Assert.AreNotSame(source.NetheriteEnchant, copy.NetheriteEnchant);
            Assert.AreNotSame(source.AdditionalData, copy.AdditionalData);
            profile.Items = new[] { copy };
            using var duplicatedStream = await ProfileParser.Write(profile);
            using var duplicated = await JsonDocument.ParseAsync(duplicatedStream);
            using var expectedDocument = JsonDocument.Parse(expected.ToJsonString());
            Assert.IsTrue(JsonElement.DeepEquals(expectedDocument.RootElement, duplicated.RootElement.GetProperty("items")[0]));
            Assert.AreEqual(12L, source.Enchantments[0].InvestedPoints);
            Assert.AreEqual(7, source.TimesModified);
        }

        [TestMethod]
        public async Task ParserRejectsIncompleteObject()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"unterminated}"));
            await Assert.ThrowsExactlyAsync<JsonException>(async () => await ProfileParser.Read(input));
        }

        [TestMethod]
        public async Task EncryptionRoundTripsWithoutClosingCallerStreams()
        {
            byte[] expected = Encoding.UTF8.GetBytes("{\"name\":\"test\"}");
            using var input = new MemoryStream(expected);
            using var encrypted = await FileProcessHelper.Encrypt(input);
            Assert.IsTrue(input.CanRead);
            Assert.IsTrue(SaveFileHandler.IsFileEncrypted(encrypted));
            using var decrypted = await FileProcessHelper.Decrypt(encrypted);
            Assert.IsTrue(encrypted.CanRead);
            using var output = new MemoryStream();
            await decrypted.CopyToAsync(output);
            CollectionAssert.AreEqual(expected, output.ToArray());
        }

        [TestMethod]
        public async Task EncryptionMatchesIndependentKnownCiphertextAndGameHeader()
        {
            // Independently generated with OpenSSL AES-256-ECB, no padding.
            using var input = new MemoryStream(Encoding.ASCII.GetBytes("0123456789abcdef"));
            using var encrypted = await FileProcessHelper.Encrypt(input);
            using var output = new MemoryStream();
            await encrypted.CopyToAsync(output);
            CollectionAssert.AreEqual(Convert.FromHexString("4430303100000000BB10480A9C430F7BF85EC169A481F02E"), output.ToArray());
        }

        [TestMethod]
        public async Task ParserAcceptsUtf8BomAndRejectsRawControlsInStrings()
        {
            using var valid = new MemoryStream(new byte[] { 0xef, 0xbb, 0xbf, (byte)'{', (byte)'}' });
            Assert.IsNotNull(await ProfileParser.Read(valid));
            using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"a\nb\"}"));
            await Assert.ThrowsExactlyAsync<JsonException>(async () => await ProfileParser.Read(invalid));
        }

        [TestMethod]
        public void HeaderHandlesPartialReadsAndRejectsMalformedReservedBytes()
        {
            using var partial = new OneByteReadStream(Convert.FromHexString("443030310000000000"));
            Assert.IsTrue(SaveFileHandler.IsFileEncrypted(partial));
            Assert.AreEqual(8L, partial.Position);
            using var malformed = new MemoryStream(Convert.FromHexString("4430303101000000"));
            Assert.ThrowsExactly<InvalidDataException>(() => SaveFileHandler.IsFileEncrypted(malformed));
            Assert.AreEqual(0L, malformed.Position);
        }

        private sealed class OneByteReadStream : MemoryStream
        {
            public OneByteReadStream(byte[] bytes) : base(bytes) { }
            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
        }

        [TestMethod]
        public async Task AtomicSaveKeepsBackupAndRefusesCollision()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hero.dat");
                File.WriteAllText(path, "original");
                using var input = new MemoryStream(Encoding.UTF8.GetBytes("updated"));
                await AtomicFileWriter.WriteAsync(input, path, true);
                Assert.AreEqual("updated", File.ReadAllText(path));
                Assert.AreEqual("original", File.ReadAllText(Directory.GetFiles(directory, "*.bak").Single()));
                await Assert.ThrowsExactlyAsync<IOException>(() => AtomicFileWriter.WriteAsync(input, path, false));
                Assert.AreEqual("updated", File.ReadAllText(path));
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task ConversionUsesUniqueSiblingNamesWithoutOverwriting()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hero.json");
                File.WriteAllText(path, "{}");
                File.WriteAllText(Path.Combine(directory, "hero.dat"), "occupied");
                File.WriteAllText(Path.Combine(directory, "hero_1.dat"), "also occupied");
                await FileProcessHelper.ProcessFile(new FileInfo(path), false);
                Assert.AreEqual("occupied", File.ReadAllText(Path.Combine(directory, "hero.dat")));
                Assert.AreEqual("also occupied", File.ReadAllText(Path.Combine(directory, "hero_1.dat")));
                using var converted = File.OpenRead(Path.Combine(directory, "hero_2.dat"));
                Assert.IsTrue(SaveFileHandler.IsFileEncrypted(converted));
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task FailedStagingPreservesOriginalAndCleansTemporaryFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hero.dat");
                File.WriteAllText(path, "original");
                using var source = new FailingReadStream();
                await Assert.ThrowsExactlyAsync<IOException>(() => AtomicFileWriter.WriteAsync(source, path, true));
                Assert.AreEqual("original", File.ReadAllText(path));
                Assert.AreEqual(1, Directory.GetFiles(directory).Length);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task StaleSaveAndInjectedReplaceFailurePreserveExistingFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hero.dat");
                File.WriteAllText(path, "original");
                var expected = FileSnapshot.Capture(path);
                File.WriteAllText(path, "external progress");
                using var source = new MemoryStream(Encoding.UTF8.GetBytes("edited"));
                await Assert.ThrowsExactlyAsync<IOException>(() => AtomicFileWriter.WriteAsync(source, path, true, expected));
                Assert.AreEqual("external progress", File.ReadAllText(path));
                await Assert.ThrowsExactlyAsync<IOException>(() => AtomicFileWriter.WriteAsync(source, path, true, null,
                    (_, _, _) => throw new IOException("Simulated replacement failure")));
                Assert.AreEqual("external progress", File.ReadAllText(path));
                Assert.AreEqual(1, Directory.GetFiles(directory).Length);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task ParserRejectsMalformedUtf8AndNonObjectInput()
        {
            using var invalidUtf8 = new MemoryStream(new byte[] { 123, 34, 110, 97, 109, 101, 34, 58, 34, 0xff, 34, 125 });
            await Assert.ThrowsExactlyAsync<JsonException>(async () => await ProfileParser.Read(invalidUtf8));
            using var array = new MemoryStream(Encoding.UTF8.GetBytes("[]"));
            await Assert.ThrowsExactlyAsync<JsonException>(async () => await ProfileParser.Read(array));
        }

        [TestMethod]
        public async Task EncryptionRejectsOutputBeyondSupportedBound()
        {
            using var input = new RepeatingStream(ProfileParser.MaximumProfileBytes + 1L);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await AesEncryptionProvider.EncryptAsync(input));
        }

        private sealed class RepeatingStream : Stream
        {
            private long remaining;
            public RepeatingStream(long length) { remaining = length; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = (int)Math.Min(remaining, count);
                Array.Clear(buffer, offset, read);
                remaining -= read;
                return read;
            }
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
            {
                int read = (int)Math.Min(remaining, buffer.Length);
                buffer.Span.Slice(0, read).Clear();
                remaining -= read;
                return ValueTask.FromResult(read);
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class FailingReadStream : MemoryStream
        {
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
                => throw new IOException("Simulated input failure");
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
                => throw new IOException("Simulated input failure");
            public override Task CopyToAsync(Stream destination, int bufferSize, System.Threading.CancellationToken cancellationToken)
                => throw new IOException("Simulated input failure");
        }
    }
}
