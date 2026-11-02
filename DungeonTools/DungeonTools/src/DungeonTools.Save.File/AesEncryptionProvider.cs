using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DungeonTools.Save.File {
    public static class AesEncryptionProvider {
        // The game format requires this fixed key and ECB/zero padding. Each operation
        // owns its algorithm and transform so concurrent conversions share no state.
        private static SymmetricAlgorithm CreateAlgorithm() {
            var algorithm = Aes.Create();
            algorithm.Mode = CipherMode.ECB;
            algorithm.Padding = PaddingMode.Zeros;
            algorithm.Key = new byte[] {
                0x5C, 0xEB, 0x9D, 0x0A, 0xEB, 0xB9, 0x5A, 0xC0, 0x27, 0x0B, 0x0A, 0xF6, 0x75, 0x3D, 0xFC, 0x0E,
                0xE3, 0xE6, 0x8B, 0xB6, 0x94, 0x79, 0x02, 0x0F, 0x24, 0x30, 0xE2, 0xEA, 0x00, 0x2B, 0xD4, 0xC9,
            };
            return algorithm;
        }

        public static async ValueTask<Stream> DecryptAsync(Stream encrypted) {
            using SymmetricAlgorithm algorithm = CreateAlgorithm();
            using ICryptoTransform transform = algorithm.CreateDecryptor();
            return await TransformAsync(encrypted, transform);
        }

        public static async ValueTask<Stream> EncryptAsync(Stream decrypted) {
            using SymmetricAlgorithm algorithm = CreateAlgorithm();
            using ICryptoTransform transform = algorithm.CreateEncryptor();
            return await TransformAsync(decrypted, transform);
        }

        private static async ValueTask<Stream> TransformAsync(Stream input, ICryptoTransform transform) {
            MemoryStream output = new MemoryStream();

            try {
                using (CryptoStream crypto = new CryptoStream(input, transform, CryptoStreamMode.Read, leaveOpen: true)) {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await crypto.ReadAsync(buffer, 0, buffer.Length)) != 0) {
                        if (output.Length + read > 64 * 1024 * 1024)
                            throw new InvalidDataException("Save exceeds the supported size limit.");
                        await output.WriteAsync(buffer, 0, read);
                    }
                }

                output.Seek(0, SeekOrigin.Begin);
                return output;
            } catch { output.Dispose(); throw; }
        }
    }
}
